using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Geomatica.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Geomatica.Desktop.ViewModels;

public partial class MapaViewModel
{
    // Fallback para crear campos cuando no existen los helpers CreateXxx
    private static Field OID(string name)
        => Field.FromJson($"{{\"name\":\"{name}\",\"type\":\"esriFieldTypeOID\",\"alias\":\"{name}\"}}")!;

    private static Field Int(string name, string? alias = null)
        => Field.FromJson($"{{\"name\":\"{name}\",\"type\":\"esriFieldTypeInteger\",\"alias\":\"{alias ?? name}\"}}")!;

    private static Field Str(string name, int length, string? alias = null)
        => Field.FromJson($"{{\"name\":\"{name}\",\"type\":\"esriFieldTypeString\",\"alias\":\"{alias ?? name}\",\"length\":{length}}}")!;

    /// <summary>
    /// Busca el id de proyecto correspondiente al OID de un feature en la capa de proyectos.
    /// </summary>
    public int? BuscarIdProyectoPorOid(long oid)
        => _oidToProjectId.TryGetValue(oid, out var id) ? id : null;

    /// <summary>
    /// Invalida el caché de municipios y la capa de proyectos para reflejar datos nuevos.
    /// Llamar después de crear/eliminar un proyecto.
    /// </summary>
    public void InvalidarCache()
    {
        _cachedMunicipios = null;
        _cachedGeometries = null;
        _oidToProjectId.Clear();
        _layerProyectos = null;
        _layerMunicipios = null;
        _updateGeneration++;
        Map?.OperationalLayers.Clear();
    }

    /// <summary>
    /// Realiza la búsqueda geográfica y por filtros de proyectos y actualiza las capas del mapa.
    /// </summary>
    public async Task<IReadOnlyList<ProyectoGeomatico>> BuscarConFiltrosGeograficosAsync(CancellationToken ct = default)
    {
        string? dptoCodigo = null;
        string? mpioCodigo = null;
        double? minX = null;
        double? minY = null;
        double? maxX = null;
        double? maxY = null;

        if (Filtros.AreaInteres is FiltrosViewModel.MunicipioItem muni && !string.IsNullOrEmpty(muni.Codigo))
        {
            mpioCodigo = muni.Codigo;
            var extent = await _municipios.ExtentPorMunicipiosAsync(new[] { muni.Codigo });
            if (extent != null)
            {
                minX = extent.West;
                minY = extent.South;
                maxX = extent.East;
                maxY = extent.North;
            }
        }
        else if (Filtros.SelectedDepartamento is FiltrosViewModel.DepartamentoItem dept && !string.IsNullOrEmpty(dept.Codigo))
        {
            dptoCodigo = dept.Codigo;
            var extent = await _municipios.ExtentPorDepartamentoAsync(dept.Codigo);
            if (extent != null)
            {
                minX = extent.West;
                minY = extent.South;
                maxX = extent.East;
                maxY = extent.North;
            }
        }

        return await BuscarYActualizarCapasAsync(
            Filtros.PalabraClave,
            Filtros.Desde,
            Filtros.Hasta,
            dptoCodigo,
            mpioCodigo,
            minX,
            minY,
            maxX,
            maxY,
            ct);
    }

    /// <summary>
    /// Actualiza las capas del mapa con los proyectos filtrados.
    /// Llamar desde MapaView después de obtener los resultados filtrados.
    /// </summary>
    public async Task<IReadOnlyList<ProyectoGeomatico>> BuscarYActualizarCapasAsync(
        string? texto,
        DateTime? desde,
        DateTime? hasta,
        string? dptoCodigo = null,
        string? mpioCodigo = null,
        double? minX = null,
        double? minY = null,
        double? maxX = null,
        double? maxY = null,
        CancellationToken ct = default)
    {
        var proyectos = await _buscarProyectos.EjecutarAsync(texto, desde, hasta, dptoCodigo, mpioCodigo, minX, minY, maxX, maxY, ct);
        await ActualizarCapasConFiltroAsync(proyectos);
        return proyectos;
    }

    public async Task ActualizarCapasConFiltroAsync(IReadOnlyList<ProyectoGeomatico> proyectosFiltrados)
    {
        if (Map == null) return;

        var gen = ++_updateGeneration;

        try
        {
            // Limpiar todas las capas operacionales para evitar capas huérfanas
            Map.OperationalLayers.Clear();
            _layerProyectos = null;
            _layerMunicipios = null;

            // 1. Crear capa de proyectos
            var layerProy = await CrearCapaProyectosDesdeListaAsync(proyectosFiltrados);
            if (gen != _updateGeneration) return;

            // 2. Obtener municipios de los proyectos filtrados
            var ids = proyectosFiltrados.Select(p => p.Id).ToList();
            var codigosMuni = ids.Count > 0
                ? await _proyectos.ObtenerCodigosMunicipioAsync(ids)
                : (IReadOnlyList<string>)Array.Empty<string>();
            if (gen != _updateGeneration) return;
            _ultimosCodigosMunicipio = codigosMuni;

            // 3. Asegurar que el caché de geometrías esté poblado
            if (_cachedMunicipios == null)
            {
                var todosCodigosConProyecto = await _proyectos.ObtenerTodosCodigosMunicipioAsync();
                _cachedMunicipios = todosCodigosConProyecto.Count > 0
                    ? await _municipios.PorCodigosGeoJsonAsync(todosCodigosConProyecto)
                    : (IReadOnlyList<MunicipioGeoJsonDto>)Array.Empty<MunicipioGeoJsonDto>();
            }
            if (gen != _updateGeneration) return;

            if (_cachedGeometries == null)
            {
                var munis = _cachedMunicipios;
                _cachedGeometries = await Task.Run(() =>
                {
                    var dict = new Dictionary<string, Geometry>(munis.Count);
                    foreach (var m in munis)
                    {
                        if (string.IsNullOrEmpty(m.GeoJson)) continue;
                        try
                        {
                            var geom = ParseGeoJson(m.GeoJson);
                            if (geom != null) dict[m.Codigo] = geom;
                        }
                        catch { }
                    }
                    return dict;
                });
            }
            if (gen != _updateGeneration) return;

            // 4. Crear capa de municipios solo con los del filtro
            var filteredMuni = _cachedMunicipios.Where(m => codigosMuni.Contains(m.Codigo));
            var layerMuni = await CrearCapaMunicipiosFiltradaAsync(filteredMuni);
            if (gen != _updateGeneration) return;

            // 5. Agregar capas al mapa (municipios abajo, proyectos arriba)
            Map.OperationalLayers.Clear();
            if (layerMuni != null)
            {
                Map.OperationalLayers.Add(layerMuni);
                await layerMuni.LoadAsync();
            }
            if (gen != _updateGeneration) return;
            if (layerProy != null)
            {
                Map.OperationalLayers.Add(layerProy);
                await layerProy.LoadAsync();
            }

            _layerMunicipios = layerMuni;
            _layerProyectos = layerProy;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Error actualizando capas con filtro: {ex}");
        }
    }

    /// <summary>
    /// Devuelve el extent (Envelope) de los municipios que contienen proyectos del último filtro aplicado.
    /// </summary>
    public Envelope? ObtenerExtentMunicipiosFiltrados()
    {
        if (_cachedGeometries == null || _ultimosCodigosMunicipio == null || _ultimosCodigosMunicipio.Count == 0)
            return null;

        double xmin = double.MaxValue, ymin = double.MaxValue;
        double xmax = double.MinValue, ymax = double.MinValue;
        bool any = false;
        foreach (var codigo in _ultimosCodigosMunicipio)
        {
            if (_cachedGeometries.TryGetValue(codigo, out var geom))
            {
                var ext = geom.Extent;
                if (ext != null)
                {
                    xmin = Math.Min(xmin, ext.XMin);
                    ymin = Math.Min(ymin, ext.YMin);
                    xmax = Math.Max(xmax, ext.XMax);
                    ymax = Math.Max(ymax, ext.YMax);
                    any = true;
                }
            }
        }
        return any ? new Envelope(xmin, ymin, xmax, ymax, SpatialReferences.Wgs84) : null;
    }

    private async Task<Layer?> CrearCapaProyectosDesdeListaAsync(IReadOnlyList<ProyectoGeomatico> items)
    {
        var fields = new List<Field>
        {
            OID("oid"),
            Int("id_proyecto"),
            Str("titulo", 200),
            Str("ruta_archivos", 1024)
        };
        var table = new FeatureCollectionTable(fields, GeometryType.Point, SpatialReferences.Wgs84);

        _oidToProjectId.Clear();
        var features = new List<Feature>();
        int oid = 1;
        foreach (var p in items)
        {
            if (p.Longitud == 0 && p.Latitud == 0) continue;
            var currentOid = oid++;
            _oidToProjectId[currentOid] = p.Id;
            var attrs = new Dictionary<string, object?>
            {
                ["oid"] = currentOid,
                ["id_proyecto"] = p.Id,
                ["titulo"] = p.Titulo,
                ["ruta_archivos"] = string.IsNullOrWhiteSpace(p.RutaArchivos) ? null : p.RutaArchivos
            };
            var geom = new MapPoint(p.Longitud, p.Latitud, SpatialReferences.Wgs84);
            features.Add(table.CreateFeature(attrs, geom));
        }

        if (features.Count == 0) return null;
        await table.AddFeaturesAsync(features);

        var marker = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.OrangeRed, 9)
        {
            Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.White, 1.5)
        };
        table.Renderer = new SimpleRenderer(marker);

        var collection = new FeatureCollection(new[] { table });
        var layer = new FeatureCollectionLayer(collection)
        {
            Name = "Proyectos"
        };
        return layer;
    }

    private async Task<Layer?> CrearCapaMunicipiosFiltradaAsync(IEnumerable<MunicipioGeoJsonDto> municipios)
    {
        var fields = new List<Field>
        {
            OID("oid"),
            Str("mpio_cdpmp", 5),
            Str("mpio_cnmbr", 200)
        };
        var table = new FeatureCollectionTable(fields, GeometryType.Polygon, SpatialReferences.Wgs84);

        int oid = 1;
        var features = new List<Feature>();
        foreach (var m in municipios)
        {
            if (!_cachedGeometries!.TryGetValue(m.Codigo, out var geom)) continue;

            var attrs = new Dictionary<string, object?>
            {
                ["oid"] = oid++,
                ["mpio_cdpmp"] = m.Codigo,
                ["mpio_cnmbr"] = m.Nombre
            };
            features.Add(table.CreateFeature(attrs, geom));
        }

        if (features.Count == 0) return null;
        await table.AddFeaturesAsync(features);

        table.Renderer = new SimpleRenderer(
            new SimpleFillSymbol(SimpleFillSymbolStyle.Solid,
                System.Drawing.Color.FromArgb(40, 33, 150, 243),
                new SimpleLineSymbol(SimpleLineSymbolStyle.Solid,
                    System.Drawing.Color.FromArgb(180, 33, 150, 243), 1.5f)));

        var collection = new FeatureCollection(new[] { table });
        var layer = new FeatureCollectionLayer(collection)
        {
            Name = "Municipios"
        };
        return layer;
    }

    /// <summary>
    /// Parses a GeoJSON geometry string (Polygon/MultiPolygon) into an ArcGIS Geometry.
    /// Geometry.FromJson() expects Esri JSON, not GeoJSON, so we parse coordinates manually.
    /// </summary>
    public static Geometry? ParseGeoJson(string geoJson)
    {
        using var doc = JsonDocument.Parse(geoJson);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();
        var coordinates = root.GetProperty("coordinates");

        if (type is not ("Polygon" or "MultiPolygon")) return null;

        var builder = new PolygonBuilder(SpatialReferences.Wgs84);

        // MultiPolygon: [polygon, polygon, ...] where polygon = [ring, ring, ...]
        // Polygon: [ring, ring, ...] where ring = [[lon, lat], ...]
        if (type == "Polygon")
        {
            foreach (var ring in coordinates.EnumerateArray())
            {
                var numPoints = ring.GetArrayLength();
                var points = new MapPoint[numPoints];
                int pointIndex = 0;

                foreach (var point in ring.EnumerateArray())
                {
                    points[pointIndex++] = new MapPoint(point[0].GetDouble(), point[1].GetDouble(), SpatialReferences.Wgs84);
                }
                builder.AddPart(points);
            }
        }
        else
        {
            foreach (var polygon in coordinates.EnumerateArray())
            {
                foreach (var ring in polygon.EnumerateArray())
                {
                    var numPoints = ring.GetArrayLength();
                    var points = new MapPoint[numPoints];
                    int pointIndex = 0;

                    foreach (var point in ring.EnumerateArray())
                    {
                        points[pointIndex++] = new MapPoint(point[0].GetDouble(), point[1].GetDouble(), SpatialReferences.Wgs84);
                    }
                    builder.AddPart(points);
                }
            }
        }

        return builder.ToGeometry();
    }
}

