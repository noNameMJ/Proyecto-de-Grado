# Geomatica Desktop

Aplicación WPF/.NET 8 para gestionar proyectos geográficos y visualizarlos con ArcGIS Maps SDK for .NET. Este README es la documentación vigente del repositorio; los documentos Markdown anteriores fueron eliminados porque mezclaban arquitectura ideal, GDAL obsoleto y hallazgos ya superados.

## Arquitectura Real

La solución principal está en `Geomatica.Desktop/Geomatica.sln`. La aplicación ejecutable es `Geomatica.Desktop/Geomatica.Desktop/Geomatica.Desktop.csproj`.

Proyectos reales:

| Proyecto | Rol real |
|---|---|
| `Geomatica.Domain` | Modelos e interfaces de dominio, con uso limitado en la app actual. |
| `Geomatica.Data` | Acceso real a PostgreSQL/PostGIS mediante repositorios y SQL/Npgsql. |
| `Geomatica.AppCore` | Casos de uso parciales; no es el orquestador principal de la UI. |
| `Geomatica.Desktop` | WPF/MVVM, DI, autenticación ArcGIS, `MapView`, ViewModels y carga de capas. |

La arquitectura efectiva no es Clean Architecture estricta: `Geomatica.Desktop` referencia directamente `Geomatica.Data` y recibe repositorios por DI.

## Flujo MVVM Principal

```text
View WPF
  -> ViewModel
  -> Repository / Service
  -> PostgreSQL/PostGIS o sistema de archivos
  -> ViewModel
  -> Binding / MapView
```

Flujo de archivos hacia mapa:

```text
ArchivosView
  -> ArchivosViewModel.AbrirEnMapaSolicitado(path)
  -> MapaViewModel.CargarCapaAdicionalAsync(path)
  -> Raster / FeatureLayer / KmlLayer / etc.
  -> Map.OperationalLayers.Add(layer)
  -> MapView renderiza y reporta LayerViewStateChanged
```

## ArcGIS Maps SDK for .NET

La integración ArcGIS está concentrada en:

| Archivo | Responsabilidad |
|---|---|
| `Geomatica.Desktop/App.xaml.cs` | Inicialización, DI y configuración de ArcGIS/auth. |
| `Geomatica.Desktop/ViewModels/MapaViewModel.cs` | Crea `Map`, carga capas, valida rasters y controla zoom. |
| `Geomatica.Desktop/Views/MapaView.xaml.cs` | Adjunta el `MapView`, escucha `LayerViewStateChanged` y reporta errores de render. |
| `Geomatica.Desktop/Services/RasterDiagnostics.cs` | Logging de carga raster, sidecars, `LoadStatus`, errores y metadata espacial. |

Patrón oficial Esri para archivo raster local:

```csharp
Raster raster = new Raster(pathToRaster);
RasterLayer layer = new RasterLayer(raster);
map.OperationalLayers.Add(layer);
await layer.LoadAsync();
await mapView.SetViewpointGeometryAsync(layer.FullExtent);
```

Referencias oficiales:

- https://developers.arcgis.com/net/layers/add-raster-data/#add-a-raster-using-a-raster-layer
- https://developers.arcgis.com/net/wpf/sample-code/raster-layer-file/

Nota crítica de Esri: si el raster tiene spatial reference desconocido o inválido, ArcGIS Runtime no puede reproyectarlo al vuelo. Una capa raster sin spatial reference solo se puede mostrar correctamente en un mapa sin spatial reference y queda en el origen `(0,0)`.

## Flujo Raster TIFF Actual

Entrada esperada:

```text
D:\Pruebas\3_dsm_ortho\2_mosaic\
  Mompos_2026_mosaic_group1.tif
  Mompos_2026_mosaic_group1.tfw
  Mompos_2026_mosaic_group1.prj
```

Flujo implementado:

```text
Seleccionar TIFF
  -> detectar sidecars .tfw/.prj/.aux.xml
  -> new Raster(path)
  -> Raster.LoadAsync()
  -> new RasterLayer(raster)
  -> RasterLayer.LoadAsync()
  -> registrar RasterInfo/FullExtent/SpatialReference
  -> validar FullExtent + SpatialReference
  -> si es válido: agregar al mapa y hacer zoom
  -> si no es válido: rechazar con mensaje técnico y log
```

La validación existe porque `LoadStatus=Loaded` no garantiza que la capa sea espacialmente usable. Para un TIFF que ArcGIS carga sin `SpatialReference`, el extent queda en coordenadas de píxel, por ejemplo:

```text
Envelope[XMin=-0.5, YMin=-27192.5, XMax=25749.5, YMax=0.5]
```

Ese extent no representa coordenadas MAGNA-SIRGAS ni WGS84; agregarlo al mapa con basemap geográfico produce un resultado invisible o incorrecto.

## Verificación Ejecutada

Prueba automática ejecutada contra:

```text
D:\Pruebas\3_dsm_ortho\2_mosaic\Mompos_2026_mosaic_group1.tif
```

Resultado de ArcGIS Runtime 300.0.0:

| Campo | Valor |
|---|---|
| `Raster.LoadStatus` | `Loaded` |
| `Raster.LoadError` | `null` |
| `RasterInfo.SpatialReference` | `null` |
| `RasterInfo.Extent` | `Envelope[XMin=-0.5, YMin=-27192.5, XMax=25749.5, YMax=0.5]` |
| `RasterLayer.LoadStatus` | `Loaded` |
| `RasterLayer.LoadError` | `null` |
| `RasterLayer.SpatialReference` | `null` |
| `RasterLayer.FullExtent` | `Envelope[XMin=-0.5, YMin=-27192.5, XMax=25749.5, YMax=0.5, WkText=]` |

Sidecars leídos:

`Mompos_2026_mosaic_group1.tfw`

```text
0.022070000000
0
0
-0.022070000000
962335.058190000127
1513161.819270000095
```

`Mompos_2026_mosaic_group1.prj`

```text
PROJCS["MAGNA-SIRGAS / Colombia Bogota zone", ... AUTHORITY["EPSG","3116"]]
```

Conclusión: el archivo tiene sidecars con transform y EPSG:3116, pero ArcGIS Runtime WPF no los está aplicando para este TIFF local. El error cometido en documentación anterior fue afirmar que ArcGIS Runtime abría correctamente estos GeoTIFF/sidecars de forma nativa. La ejecución real demuestra que los carga como imagen sin sistema de coordenadas.

## Corrección Vigente

La app ya no agrega al mapa rasters TIFF que ArcGIS carga sin spatial reference. En lugar de dejar una capa invisible o mal ubicada:

- registra archivo, sidecars, `LoadStatus`, `LoadError`, `RasterInfo`, `FullExtent` y SR;
- rechaza rasters cuyo `FullExtent` sea nulo, inválido o sin `SpatialReference`;
- evita `SetViewpointGeometryAsync` sobre extents de píxel;
- reporta errores de `LayerViewStateChanged`;
- escribe diagnóstico en `raster-diagnostics.log` junto al ejecutable.

## Solución Correcta Para Estos TIFF

Para visualizar `Mompos_2026_mosaic_group1.tif` en esta app, no basta con `.tfw` + `.prj` si ArcGIS Runtime devuelve `SpatialReference=null`.

Opciones correctas:

1. Reprocesar con GDAL/QGIS/ArcGIS Pro a GeoTIFF/COG con CRS embebido que ArcGIS Runtime reconozca al cargar.
2. Reproyectar a EPSG:4326 si se requiere una ruta local simple; en pruebas previas del proyecto, el GeoTIFF reproyectado a EPSG:4326 sí reportó `SpatialReference[Wkid=4326]`.
3. Para mosaicos grandes, usar COG con tiling y overviews internas.
4. Para producción con muchos raster o raster pesados, publicar como ArcGIS ImageServer o mobile mosaic dataset.

No hay dependencia GDAL activa en la aplicación WPF. Cualquier pipeline GDAL debe tratarse como preprocesamiento externo o como un servicio/herramienta explícita, no como parte implícita de `RasterLayer`.

## Build

Comando recomendado para validar la app principal:

```powershell
dotnet build .\Geomatica.Desktop\Geomatica.Desktop\Geomatica.Desktop.csproj
```

La solución completa puede contener referencias históricas a proyectos de benchmark no presentes localmente; para validar la app, compilar el `.csproj` de `Geomatica.Desktop`.
