# Mapa Arquitectónico (ARCHITECTURE_MAP.md)

Este documento detalla las dependencias y relaciones entre los distintos componentes clave de la aplicación `Geomatica.Desktop`.

## ViewModels

### MainViewModel
- **Servicios Inyectados**: `mapFactory` (`Func<MapaViewModel>`), `filesFactory` (`Func<ArchivosViewModel>`), `createFactory` (`Func<..., CrearProyectoViewModel>`), `editFactory` (`Func<..., EditarProyectoViewModel>`)
- **Repositorios Consumidos**: Ninguno directo.
- **Clases GIS Utilizadas**: Ninguna directa.
- **Ventanas/Vistas Asociadas**: `MainWindow.xaml`

### MapaViewModel
- **Servicios Inyectados**: `FiltrosViewModel`, `ArchivosViewModel`
- **Repositorios Consumidos**: `IProyectoRepository`, `IMunicipioRepository`
- **Clases GIS Utilizadas**: `Esri.ArcGISRuntime.Mapping.*` (`Map`, `RasterLayer`, `KmlLayer`), `Esri.ArcGISRuntime.Geometry.*` (`Envelope`, `Geometry`), `Esri.ArcGISRuntime.Data.*` (`FeatureTable`, `QueryParameters`), `Esri.ArcGISRuntime.Rasters.Raster`
- **Ventanas/Vistas Asociadas**: `MapaView.xaml`

### ArchivosViewModel
- **Servicios Inyectados**: `FiltrosViewModel`, `ProyectoArchivosService`
- **Repositorios Consumidos**: Ninguno directo (depende de `ProyectoArchivosService`).
- **Clases GIS Utilizadas**: Ninguna.
- **Ventanas/Vistas Asociadas**: `ArchivosView.xaml`

### FiltrosViewModel
- **Servicios Inyectados**: Ninguno (accede a Repositorio).
- **Repositorios Consumidos**: `IMunicipioRepository`
- **Clases GIS Utilizadas**: Ninguna.
- **Ventanas/Vistas Asociadas**: `BarraFiltrosView.xaml`

### CrearProyectoViewModel
- **Servicios Inyectados**: `ProyectoArchivosService`, Acciones de Navegación (`Action navigateBack`, `Action onProyectoCreado`)
- **Repositorios Consumidos**: `IProyectoRepository`, `IMunicipioRepository`
- **Clases GIS Utilizadas**: Ninguna directa (manejada a nivel texto/WKT hacia el repo).
- **Ventanas/Vistas Asociadas**: `CrearProyectoView.xaml`

### EditarProyectoViewModel
- **Servicios Inyectados**: Acciones de Navegación, DTO Inyectado en Constructor (`ProyectoDetalleDto`)
- **Repositorios Consumidos**: `IProyectoRepository`, `IMunicipioRepository`
- **Clases GIS Utilizadas**: Ninguna directa.
- **Ventanas/Vistas Asociadas**: `EditarProyectoView.xaml`

### FichaProyectoViewModel
- **Servicios Inyectados**: Acciones de Navegación, DTO Inyectado (`ProyectoDetalleDto`)
- **Repositorios Consumidos**: Ninguno.
- **Clases GIS Utilizadas**: Ninguna.
- **Ventanas/Vistas Asociadas**: `FichaProyectoView.xaml`

---

## Repositorios (Geomatica.Data)

### ProyectoRepository
- **Interfaces Implementadas**: `IProyectoRepository` (Hospedada localmente en la propia capa, ignora la interfaz de la capa Domain).
- **DTOs Utilizados**: `ProyectoDto`, `ProyectoDetalleDto`
- **Tablas/Vistas Consultadas**: `geovisor.proyecto`, `geovisor.proyecto_municipio`, `geovisor.vw_proyecto_departamento`, `geovisor.municipio`
- **Dependencias PostGIS**: Uso de funciones espaciales SQL directamente en los strings de consulta: `ST_AsGeoJSON`, `ST_X()`, `ST_Y()`, `ST_GeomFromText()`.

### MunicipioRepository
- **Interfaces Implementadas**: `IMunicipioRepository`
- **DTOs Utilizados**: `MunicipioGeoJsonDto`, `MunicipioDto`, `DepartamentoDto`, `EnvelopeDto`
- **Tablas/Vistas Consultadas**: `geovisor.municipio`, `geovisor.departamento`
- **Dependencias PostGIS**: Uso de funciones espaciales espaciales `ST_AsGeoJSON`, `ST_Extent()`, `ST_XMin()`, `ST_YMin()`, `ST_XMax()`, `ST_YMax()`.

---

## Diagrama de Arquitectura y Dependencias

```text
MainWindow
 └─ MainViewModel
	 │
	 ├─ MapaViewModel (Vista: MapaView.xaml)
	 │   ├─ FiltrosViewModel
	 │   ├─ ArchivosViewModel
	 │   │
	 │   ├─ GIS dependencies
	 │   │   ├─ ArcGIS Runtime (Map, Layers, TPKX, KML)
	 │   │
	 │   └─ Repositories
	 │       ├─ ProyectoRepository
	 │       └─ MunicipioRepository
	 │
	 ├─ FiltrosViewModel (Vista: BarraFiltrosView.xaml)
	 │   └─ MunicipioRepository
	 │
	 ├─ ArchivosViewModel (Vista: ArchivosView.xaml)
	 │   ├─ FiltrosViewModel
	 │   └─ ProyectoArchivosService
	 │
	 ├─ CrearProyectoViewModel (Vista: CrearProyectoView.xaml)
	 │   ├─ ProyectoArchivosService
	 │   ├─ ProyectoRepository
	 │   └─ MunicipioRepository
	 │
	 ├─ EditarProyectoViewModel (Vista: EditarProyectoView.xaml)
	 │   ├─ ProyectoRepository
	 │   └─ MunicipioRepository
	 │
	 └─ FichaProyectoViewModel (Vista: FichaProyectoView.xaml)
		 └─ Modelos/Datos provistos estáticos (ProyectoDetalleDto)
```
