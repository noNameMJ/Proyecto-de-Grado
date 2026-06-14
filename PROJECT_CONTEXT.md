# Contexto del Proyecto (PROJECT_CONTEXT.md)

Este documento ha sido generado a partir de una auditoría exhaustiva del código fuente del repositorio, reemplazando las asunciones previas con evidencia verificable.

## Fase 1: Auditoría del README existente

| Afirmación Original | Estado | Evidencia / Notas |
|--------------------|--------|-------------------|
| *El proyecto emplea Arquitectura Limpia estricta (.Domain, .Data, .AppCore, .Desktop)* | **CONTRADICHA POR EL CÓDIGO** | `Geomatica.Desktop` consume directamente los repositorios de `Geomatica.Data` (ej. `IProyectoRepository`). `Geomatica.AppCore` existe pero no orquesta la aplicación principal. |
| *Uso de Entity Framework en Capa de Datos* | **PARCIALMENTE VERIFICADA** | Hay referencia a `Npgsql.EntityFrameworkCore.PostgreSQL` en `Geomatica.Data.csproj`, pero repositorios críticos (`ProyectoRepository.cs`, `MunicipioRepository.cs`) utilizan ADO.NET puro con consultas SQL en texto (ej. `new NpgsqlCommand(sql, con)`). No se hallaron DbContext. |
| *El ViewModel se comunica con AppCore* | **CONTRADICHA POR EL CÓDIGO** | `MapaViewModel` importa `Geomatica.Data.Repositories` de forma directa y recibe repositorios vía DI, esquivando AppCore. |
| *WPF, MVVM y CommunityToolkit.Mvvm* | **VERIFICADA** | Uso prevalente en `Geomatica.Desktop\ViewModels` (ej. `MapaViewModel`, `MainViewModel`). |
| *ArcGIS Runtime WPF v300 y GDAL 3.12.1* | **VERIFICADA** | Declarados en `Geomatica.Desktop.csproj` y usados intensivamente. |
| *Inyección de Dependencias (IoC)* | **VERIFICADA** | Configurada en `App.xaml.cs` (ServiceCollection). |

## Fase 2: Inventario Real del Proyecto

### Proyectos (.csproj)
1. **Geomatica.Domain**: Modelos anémicos abstractos y una interfaz `IProyectoRepository` que **no** se usa en la aplicación principal.
2. **Geomatica.Data**: Implementación real de acceso a datos (`ProyectoRepository`, `MunicipioRepository`). Contiene sus propios DTOs (ej. `ProyectoDto`, `ProyectoDetalleDto`) y su propia interfaz `IProyectoRepository` que sobrescribe lógicamente a la del Domain.
3. **Geomatica.AppCore**: Contiene `BuscarProyectosUseCase`, pero no está siendo invocado ni registrado en la Inyección de Dependencias general de la UI.
4. **Geomatica.Desktop**: Interfaz rica WPF, configurador de servicios, visualización de mapas.

### Dependencias Observadas (NuGet)
* **WPF/UI**: `CommunityToolkit.Mvvm`, `Microsoft.Web.WebView2`
* **GIS**: `Esri.ArcGISRuntime.WPF`, `Esri.ArcGISRuntime.Toolkit.WPF`, `GDAL`, `NetTopologySuite`
* **Datos**: `Npgsql`
* **Configuración**: `Microsoft.Extensions.Configuration.*`, `Microsoft.Extensions.DependencyInjection`

### Capas Arquitectónicas Reales
La arquitectura real observada es un patrón **Modelo-Vista-ViewModel (MVVM) con acceso directo a Repositorios**, acoplado fuertemente a SQL puro vía Npgsql y a las librerías GIS. La separación en proyectos N-Tier es puramente estructural, debido a que `Geomatica.Desktop` referencia a `Geomatica.Data` directamente.

## Fase 3: Flujo Geospacial (GIS Flow)

El flujo GIS está centralizado en dos focos principales:

1. **Renderizado y Gestión de Capas (ArcGIS Runtime)**:
   * **Componente principal**: `MapaViewModel.cs` y `MapaView.xaml`.
   * **Lógica observada**: Se instancia un `MapView`; se cargan capas vectoriales y raster directamente desde rutas en disco.
   * **Tipos de recursos cargados**:
	 - `.kml` / `.kmz` -> instanciados mediante `KmlDataset` y `KmlLayer`.
	 - Raster (varios formatos) -> instanciado mediante `Raster` y `RasterLayer`. Funcionalidad condicionada fuertemente a `LoadStatusChanged`.
   * **Flujo**: El usuario agrega una capa en el mapa, el sistema efectúa carga asíncrona (`LoadAsync()`) e invoca `ZoomCapaAsync()` si tiene éxito o manda mensajes de error vía `RasterDiagnostics` si falla.
   * **Consumo de Servicios Geoespaciales**: La aplicación soporta OAuth 2.0 y consumo del `ArcGISPortal`, solicitando un token o challenge. Evidenciado en `App.xaml.cs` y `OAuthAuthorizeHandler.cs`.

2. **Procesamiento de Bajo Nivel Raster (GDAL)**:
   * **Componente principal**: ~~`GdalRasterConverter.cs`~~ (eliminado).
   * **Lógica observada**: Anteriormente se hacía manipulación directa de matrices de píxeles (`ReadRaster`/`WriteRaster`), extracción del Geotransform, y copiado de bloques binarios asíncronos (`CopyBandBlocks`) para tratar imágenes que ArcGIS no manejaba bien de forma nativa o repeticiones de reproyección.
   * **Estado actual**: el conversor y la dependencia del paquete `GDAL` 3.12.1 fueron eliminados. ArcGIS Runtime v300 abre los GeoTIFFs (incluidos los que traen sidecars `.tfw`/`.prj`) de forma nativa, por lo que la lógica de reempaquetado no aporta valor al flujo actual.

> *Nota: NO SE ENCONTRÓ EVIDENCIA SUFICIENTE de reproyección de primitivas vectoriales por GDAL; se utiliza GDAL para operaciones sobre rasters, y ArcGIS Runtime para vectores.*

## Fase 4: Inyección de Dependencias (DI)

* **Punto de Composición:** Método `OnStartup` en `App.xaml.cs`.
* **Motor:** `Microsoft.Extensions.DependencyInjection.ServiceCollection`.
* **Registros Observados:**
  * **AddSingleton:**
	* `IProyectoRepository` mapeado a `ProyectoRepository(connectionString)`
	* `IMunicipioRepository` mapeado a `MunicipioRepository(connectionString)`
	* `ProyectoArchivosService`
	* `FiltrosViewModel`
	* `MapaViewModel` (Carga en Lazy)
	* `MainViewModel`, `MainWindow`
	* Factorías personalizadas: `Func<..., CrearProyectoViewModel>` y `Func<..., EditarProyectoViewModel>`.
  * **AddTransient:**
	* `ArchivosViewModel`

## Fase 5: Componentes Críticos

| Componente | Responsabilidad | Evidencia |
| ---------- | --------------- | --------- |
| `App.xaml.cs` (Desktop) | Punto de Inicio, Configuración, DI, Verificación de persistencia Postgres de esquemas base (geovisor), e inicio de Autenticación Esri (OAuth). | `App.xaml.cs` líneas 1-200. |
| `ProyectoRepository.cs` (Data) | Concentra las consultas SQL directas para búsqueda e ingreso de Proyectos en Postgres. Maneja datos en tuplas DTO específicas. | `Geomatica.Data\Repositories\ProyectoRepository.cs` |
| `MunicipioRepository.cs` (Data) | Carga catálogos de municipios/departamentos y efectúa proyecciones básicas `ST_Extent` sobre PostGIS. | `Geomatica.Data\Repositories\MunicipioRepository.cs` |
| `MapaViewModel.cs` (Desktop) | Coordinación de capas de ArcGIS, control del componente `MapView`, renderizado. | `Geomatica.Desktop\ViewModels\MapaViewModel.cs` |

## Observaciones de Deuda Técnica / Local

1. **Uso de SQL puro sin ORM**: Pese a existir .Domain, `.Data` opera con Entity Framework ausente o ignorado, escribiendo comandos `NpgsqlCommand` usando strings dinámicos con literales SQL.
2. **Duplicación de Interfaces**: Existe `IProyectoRepository` en `Domain.Interfaces.Repositories` interactuando con `ProyectoGeomatico` (Entidad de dominio), pero el único implementado de verdad es `IProyectoRepository` definido explícitamente en el inicio de `Geomatica.Data\Repositories\ProyectoRepository.cs` con tipos DTO nativos del repositorio.
3. **Acoplamiento UI-Data**: La aplicación de escritorio referencia directamente servicios de datos esquivando por completo `AppCore` o `Domain`.
4. **Configuración DB y GIS Cíclica**: El chequeo de base de datos Postgres está "hardcodeado" para evaluar que contenga el schema de base de datos `geovisor` en el propio hilo gráfico (UI).
