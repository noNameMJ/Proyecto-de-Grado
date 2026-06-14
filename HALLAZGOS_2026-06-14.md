# Hallazgos confirmados — Sesión 2026-06-14

Investigación de la falla al cargar TIFFs raster en el mapa de
`Geomatica.Desktop`. Sesión: 2026-06-14. Todos los hallazgos tienen
evidencia reproducible (logs, `gdalinfo`, `grep`, inspección de archivos).

## Hallazgos confirmados

### 1. `gdal_wrap.dll` no existe en la máquina

- Búsqueda global con `find` por nombre `gdal_wrap*` en todo el proyecto
  `D:\Repositorios\Proyecto-de-Grado\`: **0 resultados**.
- Inspección del paquete NuGet `GDAL 3.12.1` en
  `C:\Users\marbin.arevalo\.nuget\packages\gdal\3.12.1\lib\netstandard2.0\`
  contiene **solo** los 4 bindings administrados (`gdal_csharp.dll`,
  `gdalconst_csharp.dll`, `ogr_csharp.dll`, `osr_csharp.dll`). **Cero
  DLLs nativas**.
- Inspección del output
  `bin\Debug\net8.0-windows10.0.19041.0\runtimes\win-x64\native\`
  contiene **4 DLLs, todas de ArcGIS Runtime / WebView2** (ninguna de
  GDAL): `runtimecore.dll`, `RuntimeCoreNet300_0.dll`,
  `RuntimeCoreNet300_0.WPF.dll`, `WebView2Loader.dll`.
- PATH del proceso: ningún segmento contiene GDAL, QGIS u OSGeo.
- Conclusión: el paquete NuGet `GDAL` 3.12.1 (de OSGeo en nuget.org)
  **no incluye binarios nativos**; la app nunca tomó las DLLs de
  ningún otro origen (no usa `GDAL.Native`, no copia desde OSGeo4W, no
  las agrega al PATH).

### 2. La excepción observada es una cascada de `TypeInitializationException`

Stack trace literal del log de la app (carga de
`D:\Pruebas\aislamiento\Mompos_2026_mosaic_group1.tif`):

```
Excepción producida: 'System.TypeInitializationException' en Geomatica.Desktop.dll
System.TypeInitializationException: The type initializer for
  'Geomatica.Desktop.GDAL.GdalRasterConverter' threw an exception.
 ---> System.TypeInitializationException: The type initializer for
   'OSGeo.GDAL.GdalPINVOKE' threw an exception.
  ---> System.TypeInitializationException: The type initializer for
    'SWIGExceptionHelper' threw an exception.
   ---> System.DllNotFoundException: Unable to load DLL 'gdal_wrap'
     or one of its dependencies (0x8007007E)
     at OSGeo.GDAL.GdalPINVOKE.SWIGExceptionHelper
         .SWIGRegisterExceptionCallbacks_Gdal(...)
     at OSGeo.GDAL.GdalPINVOKE.SWIGExceptionHelper..cctor()
   --- End inner ---
   at OSGeo.GDAL.GdalPINVOKE..cctor()
 --- End inner ---
 at OSGeo.GDAL.GdalPINVOKE.AllRegister()
 at OSGeo.GDAL.Gdal.AllRegister()
 at Geomatica.Desktop.GDAL.GdalRasterConverter..cctor()
   in ...GdalRasterConverter.cs:line 23
--- End inner ---
 at Geomatica.Desktop.GDAL.GdalRasterConverter.GetRasterPathToLoad(...)
   in ...GdalRasterConverter.cs:line 27
 at Geomatica.Desktop.ViewModels.MapaViewModel.CrearRasterLayerValidadoAsync(...)
   in ...MapaViewModel.cs:line 210
```

- Punto único de fallo: el constructor estático de `GdalRasterConverter`
  (línea 23, `Gdal.AllRegister()`) revienta al primer uso de cualquier
  raster.
- Cascada: `SWIGExceptionHelper` → `GdalPINVOKE` → `Gdal.AllRegister()` →
  `GdalRasterConverter` (todos son `cctor` estáticos).
- `0x8007007E` = "módulo no encontrado" + dependencias insatisfechas.
- Conclusión: la app **nunca ha podido ejecutar `GdalRasterConverter` en
  este equipo**. No falla por el archivo: falla por la DLL nativa
  faltante.

### 3. Los archivos `.tif` son GeoTIFFs válidos y se abren con GDAL/QGIS

Inspección con `gdalinfo` (ruta
`C:\Program Files\QGIS 3.34.9\bin\gdalinfo.exe`) sobre los dos archivos
de `D:\Pruebas\3_dsm_ortho\2_mosaic\`:

| Campo | `Mompos_2026_mosaic_group1.tif` | `Mompos_2026_transparent_mosaic_group1.tif` |
|---|---|---|
| Driver | GTiff/GeoTIFF | GTiff/GeoTIFF |
| Tamaño en disco | 660.36 MB (692 440 210 B) | 762.66 MB (799 705 444 B) |
| Dimensiones | 25 750 × 27 193 | 25 750 × 27 193 |
| SRS | EPSG:3116 MAGNA-SIRGAS / Bogota zone | EPSG:3116 MAGNA-SIRGAS / Bogota zone |
| Origin | (962335.058, 1513161.819) | (962335.058, 1513161.819) |
| Pixel Size | 0.02207 m × -0.02207 m (~2.2 cm/píxel) | 0.02207 m × -0.02207 m |
| Bandas | 3 (RGB, Byte) | 4 (RGBA, Byte) + `Mask Flags: PER_DATASET ALPHA` |
| BLOCKSIZE | 25750×1 (tiras de 1 fila) | 25750×1 (tiras de 1 fila) |
| Sidecars | `.tfw` (76 B) y `.prj` (705 B) | `.tfw`, `.prj`, `.tif.aux.xml` (10 106 B) |

- Ambos son **GeoTIFFs con `EPSG:3116` ya incrustado**. Los sidecars son
  redundantes, no necesarios.
- Ambos abren perfectamente con GDAL/QGIS. Conclusión: el problema no es
  el archivo, es la app.

### 4. `GdalRasterConverter` no aporta valor al flujo actual

Análisis de `GdalRasterConverter.cs` (342 líneas) + única llamada en
`MapaViewModel.cs:210`:

- Solo aplica para `.tif`/`.tiff` con sidecars **y** caché vencido.
- Su única utilidad real era forzar LZW + tileado + BigTIFF en un
  reempaquetado. Para los archivos de Pix4D (ya GeoTIFF, ya comprimidos
  de fábrica), esa operación es trabajo inútil.
- ArcGIS Runtime v300 abre GeoTIFFs directo y, además, lee `.tfw`/`.prj`
  adyacentes nativamente: no necesita reescritura.
- Eliminar el conversor **no cambia el comportamiento observable** para
  los archivos actuales.

### 5. Único call site de `GdalRasterConverter`

`grep -n GdalRasterConverter` en todo el repo (excluyendo `.md`):

```
Geomatica.Desktop/Geomatica.Desktop/ViewModels/MapaViewModel.cs:210
Geomatica.Desktop/Geomatica.Desktop/GDAL/GdalRasterConverter.cs:    (definición)
```

- Un solo punto de uso en el código de la aplicación.
- `using Geomatica.Desktop.GDAL;` solo en `MapaViewModel.cs:16`.
- `OSGeo.GDAL` solo importado por `GdalRasterConverter.cs:5`.
- No hay tests, ni proyectos Benchmark, ni `InternalsVisibleTo` que
  toquen el conversor.

### 6. La carpeta de caché está vacía

`%LocalAppData%\Geomatica\RasterCache\`
(`C:\Users\marbin.arevalo\AppData\Local\Geomatica\RasterCache`):
existe, pero **vacía**. La app nunca completó una conversión. Descarta
la hipótesis de "caché corrupto previo" en esta máquina.

### 7. La prueba de aislamiento falló por la DLL, no por el archivo

Al copiar `Mompos_2026_mosaic_group1.tif` a `D:\Pruebas\aislamiento\`
**sin** los sidecars, el conversor debería haber tomado la rama de
"return originalTiffPath" (línea 42). No lo hizo porque la
inicialización de la clase (`cctor` línea 23) revienta **antes** de
evaluar ninguna rama. Confirmado por el stack trace: la excepción
proviene de `..cctor()`, no de `GetRasterPathToLoad`.

## Decisiones tomadas

1. **Eliminación completa de `GdalRasterConverter`** (decisión del
   usuario). Se descartó la opción de solo neutralizar el `cctor` o
   agregar `GDAL.Native` porque el conversor no aporta valor al flujo
   actual.
2. **Eliminación de los `.md` obsoletos** (`GDAL_CONTEXT.md` y
   `GdalRasterConverter_Analysis.md`) por estar 100% dedicados al
   conversor eliminado.
3. **Actualización de los `.md` que mezclan info vigente y deprecada**
   (`PROJECT_CONTEXT.md` y `ARCHITECTURE_MAP.md`) para reflejar la
   nueva realidad.
4. **Mínimo cambio en `MapaViewModel.cs`**: solo se removieron 6 líneas
   (el `using`, la llamada, el flag, el log duplicado) y se cambió
   `new Raster(rasterPathToLoad)` a `new Raster(path)`. No se refactorizó
   nada más.
5. **Se conservó `D:\Pruebas\aislamiento\Mompos_2026_mosaic_group1.tif`**
   (660 MB) para que el usuario verifique manualmente que la app ya
   carga el TIFF directo.
6. **No se recompiló el proyecto** (queda para la sesión de Visual
   Studio del usuario).

## Cambios aplicados

| Archivo | Cambio |
|---|---|
| `Geomatica.Desktop/ViewModels/MapaViewModel.cs` | Eliminado `using Geomatica.Desktop.GDAL;` (línea 16). Eliminada la llamada a `GdalRasterConverter.GetRasterPathToLoad`, el flag `isConverted` y el log duplicado. `new Raster(rasterPathToLoad)` → `new Raster(path)`. |
| `Geomatica.Desktop/GDAL/GdalRasterConverter.cs` | Eliminado. |
| `Geomatica.Desktop/GDAL/` (carpeta) | Eliminada. |
| `Geomatica.Desktop.csproj` | Eliminado `<PackageReference Include="GDAL" Version="3.12.1" />`. |
| `Geomatica.sln` | Sin cambios (no referenciaba el archivo). |
| `PROJECT_CONTEXT.md` | Quitado el bullet de GDAL en §Fase 3 (reemplazado por nota de estado). Quitada la fila de la tabla en §Fase 5. |
| `ARCHITECTURE_MAP.md` | Quitado `GdalRasterConverter` de la lista de clases GIS de `MapaViewModel` y del bloque del diagrama. |
| `GDAL_CONTEXT.md` | Eliminado. |
| `GdalRasterConverter_Analysis.md` | Eliminado. |

## Siguiente acción pendiente

**Del lado del usuario (bloqueante para validar el fix):**

1. **Compilar y restaurar paquetes**:
   - En Visual Studio: `Build → Clean Solution` y luego `Rebuild
     Solution`. Si VS no detecta el cambio en el `.csproj`, ejecutar
     `dotnet restore` manualmente.
   - Esperado: build limpio, sin referencias rotas a
     `Geomatica.Desktop.GDAL` ni a `OSGeo.GDAL`. La lista de paquetes
     NuGet ya no debe incluir `GDAL 3.12.1`.
2. **Probar carga del TIFF problema**:
   - En la app, agregar capa raster desde
     `D:\Pruebas\aislamiento\Mompos_2026_mosaic_group1.tif`.
   - Esperado: `Raster.LoadStatus` y `RasterLayer.LoadStatus` llegan a
     `Loaded`, la imagen se ve en el mapa en `EPSG:3116`, sin
     `TypeInitializationException` y sin el `MessageBox` "Error al
     cargar en mapa".
3. **Probar el mosaico transparente** (opcional, recomendado):
   - Copiar `Mompos_2026_transparent_mosaic_group1.tif` a
     `D:\Pruebas\aislamiento\` y agregarlo.
   - Esperado: carga análoga, con la banda Alpha respetada.
4. **Sanity check de no-regresión**:
   - Cargar un `.kml` o `.kmz` y un servicio REST de ArcGIS Online.
   - Esperado: comportamiento idéntico al de antes del cambio.

**Si algo falla al compilar o al cargar:**

- Pegar el error literal y el stack trace.
- En particular, si reaparece un `TypeInitializationException`, pegar
  la cadena completa de inner exceptions (suele ser informativa sobre
  qué DLL falta o qué versión de .NET targeting está mal).

**Limpieza opcional (cuando el usuario quiera):**

- Borrar `D:\Pruebas\aislamiento\` y sus ~660 MB.
- Borrar el caché `%LocalAppData%\Geomatica\RasterCache\` (vacío, pero
  por si acaso).
- Considerar un commit con un mensaje claro, ej.:
  `Remove GdalRasterConverter and GDAL NuGet dependency (raster load fix)`.
