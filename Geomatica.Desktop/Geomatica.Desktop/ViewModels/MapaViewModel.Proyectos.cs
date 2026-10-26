using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Geomatica.Domain.Entities;
using Geomatica.Desktop.Services;
using Geomatica.Infrastructure.Gis.Services;
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

    private void RemoverCapasBaseSistema()
    {
        RemoverCapaMunicipios();
        RemoverCapaProyectos();
        RemoverCapaSubproyectos();
    }

    private void RemoverCapaMunicipios()
    {
        if (Map == null) return;
        if (_layerMunicipios != null)
        {
            Map.OperationalLayers.Remove(_layerMunicipios);
            _layerMunicipios = null;
        }
    }

    private void RemoverCapaProyectos()
    {
        if (Map == null) return;
        if (_layerProyectos != null)
        {
            Map.OperationalLayers.Remove(_layerProyectos);
            _layerProyectos = null;
        }
    }

    private void RemoverCapaSubproyectos()
    {
        if (Map == null) return;
        if (_layerSubproyectos != null)
        {
            Map.OperationalLayers.Remove(_layerSubproyectos);
            _layerSubproyectos = null;
        }
    }

    /// <summary>
    /// Invalida el caché de municipios y la capa de proyectos para reflejar datos nuevos.
    /// Llamar después de crear/eliminar un proyecto.
    /// </summary>
    public void InvalidarCache()
    {
        _cachedMunicipios = null;
        _cachedGeometries = null;
        _oidToProjectId.Clear();
        _ultimosSubproyectosPorProyecto = new Dictionary<int, IReadOnlyList<SubproyectoDto>>();
        _todosSubproyectosActuales = Array.Empty<SubproyectoDto>();
        RemoverCapasBaseSistema();
        _layerProyectos = null;
        _layerMunicipios = null;
        _layerSubproyectos = null;
        _updateGeneration++;
        OverlayProyectosEtiquetas.Graphics.Clear();
        OverlayResaltadoProyecto.Graphics.Clear();
        OverlaySubproyectos.Graphics.Clear();
    }

    /// <summary>
    /// Realiza la búsqueda geográfica y por filtros de proyectos y actualiza las capas del mapa.
    /// </summary>
    public async Task<IReadOnlyList<ProyectoGeomatico>> BuscarConFiltrosGeograficosAsync(CancellationToken ct = default)
    {
        string? dptoCodigo = null;
        string? mpioCodigo = null;

        if (Filtros.AreaInteres is FiltrosViewModel.MunicipioItem muni && !string.IsNullOrEmpty(muni.Codigo))
        {
            mpioCodigo = muni.Codigo;
        }
        else if (Filtros.SelectedDepartamento is FiltrosViewModel.DepartamentoItem dept && !string.IsNullOrEmpty(dept.Codigo))
        {
            dptoCodigo = dept.Codigo;
        }

        return await BuscarYActualizarCapasAsync(
            Filtros.PalabraClave,
            Filtros.Desde,
            Filtros.Hasta,
            dptoCodigo,
            mpioCodigo,
            minX: null,
            minY: null,
            maxX: null,
            maxY: null,
            ct);
    }

    /// <summary>
    /// Actualiza las capas del mapa con los proyectos filtrados.
    /// Llamar desde MapaView después de obtener los resultados filtrados.
    /// </summary>
    /// <summary>
    /// Actualiza de forma inmediata y segura para el hilo de interfaz los resultados y contadores en el panel de filtros.
    /// </summary>
    private IReadOnlyList<SubproyectoDto> _todosSubproyectosActuales = Array.Empty<SubproyectoDto>();
    private IReadOnlyDictionary<int, IReadOnlyList<SubproyectoDto>> _ultimosSubproyectosPorProyecto = new Dictionary<int, IReadOnlyList<SubproyectoDto>>();

    public IReadOnlyDictionary<int, IReadOnlyList<SubproyectoDto>> UltimosSubproyectosPorProyecto => _ultimosSubproyectosPorProyecto;

    public void ActualizarResultadosFiltros(IReadOnlyList<ProyectoGeomatico> proyectos)
    {
        ActualizarResultadosFiltros(proyectos, _ultimosSubproyectosPorProyecto);
    }

    public void ActualizarResultadosFiltros(
        IReadOnlyList<ProyectoGeomatico> proyectos,
        IReadOnlyDictionary<int, IReadOnlyList<SubproyectoDto>>? subproyectosPorProyecto)
    {
        if (subproyectosPorProyecto != null)
        {
            _ultimosSubproyectosPorProyecto = subproyectosPorProyecto;
            var todosSubproyectos = subproyectosPorProyecto.Values.SelectMany(x => x).ToList();
            MostrarSubproyectosEnMapa(todosSubproyectos);
        }

        void Actualizar()
        {
            var expandidos = Filtros.ResultadosLista
                .OfType<FiltrosViewModel.ProyectoItem>()
                .Where(x => x.EstaExpandido)
                .Select(x => x.Id)
                .ToHashSet();

            var subsFuente = subproyectosPorProyecto ?? _ultimosSubproyectosPorProyecto;

            Filtros.ResultadosLista.Clear();
            Filtros.ResultadosResumen.Clear();

            foreach (var p in proyectos)
            {
                IReadOnlyList<SubproyectoDto>? subs = null;
                subsFuente?.TryGetValue(p.Id, out subs);

                var item = new FiltrosViewModel.ProyectoItem(
                    p.Id, p.Titulo, p.Longitud, p.Latitud, p.RutaArchivos,
                    p.Fecha, p.PalabrasClave, p.Responsable,
                    p.MinX, p.MinY, p.MaxX, p.MaxY,
                    subproyectos: subs);

                if (expandidos.Contains(p.Id))
                {
                    item.EstaExpandido = true;
                }

                Filtros.ResultadosLista.Add(item);
            }

            string textoConteo = proyectos.Count == 1 ? "1 proyecto encontrado" : $"{proyectos.Count} proyectos encontrados";
            Filtros.ResultadosResumen.Add(textoConteo);

            Filtros.NotificarResultadosCargados();
        }

        if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            System.Windows.Application.Current.Dispatcher.Invoke(Actualizar);
        }
        else
        {
            Actualizar();
        }
    }

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
        
        // Obtener en lote los subproyectos asociados a todos los proyectos encontrados
        var ids = proyectos.Select(p => p.Id).ToList();
        IReadOnlyDictionary<int, IReadOnlyList<SubproyectoDto>> subproyectosPorProyecto;
        try
        {
            subproyectosPorProyecto = await _proyectos.ObtenerSubproyectosPorProyectosAsync(ids, ct);
        }
        catch (Exception exSubs)
        {
            System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Advertencia al obtener subproyectos en lote: {exSubs.Message}");
            subproyectosPorProyecto = new Dictionary<int, IReadOnlyList<SubproyectoDto>>();
        }

        // Notificar y poblar la lista de proyectos en la barra lateral con sus subproyectos anidados
        ActualizarResultadosFiltros(proyectos, subproyectosPorProyecto);

        if (ct.IsCancellationRequested) return proyectos;

        // Visualizar inmediatamente todos los subproyectos en el mapa principal
        var todosSubproyectos = subproyectosPorProyecto.Values.SelectMany(x => x).ToList();
        MostrarSubproyectosEnMapa(todosSubproyectos);

        await ActualizarCapasConFiltroAsync(proyectos);
        return proyectos;
    }

    public async Task ActualizarCapasConFiltroAsync(IReadOnlyList<ProyectoGeomatico> proyectosFiltrados)
    {
        if (Map == null) return;

        var gen = ++_updateGeneration;

        try
        {
            // 1. Crear y presentar la capa de proyectos DE INMEDIATO para visibilidad instantánea (<50ms)
            var layerProy = await CrearCapaProyectosDesdeListaAsync(proyectosFiltrados);
            if (gen != _updateGeneration) return;

            RemoverCapaProyectos();
            RemoverCapaSubproyectos();
            if (layerProy != null)
            {
                int insertProyIdx = _layerMunicipios != null && Map.OperationalLayers.Contains(_layerMunicipios) ? 1 : 0;
                Map.OperationalLayers.Insert(Math.Min(insertProyIdx, Map.OperationalLayers.Count), layerProy);
                _layerProyectos = layerProy;
                _ = layerProy.LoadAsync();
            }

            await ActualizarCapaSubproyectosAsync(_todosSubproyectosActuales);

            AppLogger.Info($"[MapaViewModel] Capa de proyectos actualizada en el mapa ({proyectosFiltrados.Count} proyectos).");

            // 2. Obtener municipios de los proyectos filtrados
            var ids = proyectosFiltrados.Select(p => p.Id).ToList();
            var codigosMuni = ids.Count > 0
                ? await _proyectos.ObtenerCodigosMunicipioAsync(ids)
                : (IReadOnlyList<string>)Array.Empty<string>();
            if (gen != _updateGeneration) return;
            _ultimosCodigosMunicipio = codigosMuni;

            // 3. Poblar caché de geometrías de municipios bajo demanda solo para los municipios requeridos
            if (_cachedMunicipios == null)
            {
                _cachedMunicipios = new List<MunicipioGeoJsonDto>();
            }

            var codigosFaltantes = codigosMuni
                .Where(c => _cachedMunicipios.All(m => !string.Equals(m.Codigo, c, StringComparison.OrdinalIgnoreCase)))
                .Distinct()
                .ToList();

            if (codigosFaltantes.Count > 0)
            {
                var nuevosMunis = await _municipios.PorCodigosGeoJsonAsync(codigosFaltantes);
                if (nuevosMunis.Count > 0)
                {
                    _cachedMunicipios = _cachedMunicipios.Concat(nuevosMunis).ToList();
                }
            }
            if (gen != _updateGeneration) return;

            if (_cachedGeometries == null)
            {
                _cachedGeometries = new Dictionary<string, Geometry>();
            }

            var munisParaGeometrias = _cachedMunicipios.Where(m => !_cachedGeometries.ContainsKey(m.Codigo)).ToList();
            if (munisParaGeometrias.Count > 0)
            {
                await Task.Run(() =>
                {
                    var tempParsed = new System.Collections.Concurrent.ConcurrentDictionary<string, Geometry>();
                    Parallel.ForEach(munisParaGeometrias, m =>
                    {
                        if (string.IsNullOrEmpty(m.GeoJson)) return;
                        try
                        {
                            var geom = ParseGeoJson(m.GeoJson);
                            if (geom != null) tempParsed[m.Codigo] = geom;
                        }
                        catch { }
                    });

                    foreach (var kvp in tempParsed)
                    {
                        _cachedGeometries[kvp.Key] = kvp.Value;
                    }
                });
            }
            if (gen != _updateGeneration) return;

            // 4. Crear capa de municipios solo con los del filtro
            var filteredMuni = _cachedMunicipios.Where(m => codigosMuni.Contains(m.Codigo)).ToList();
            var layerMuni = await CrearCapaMunicipiosFiltradaAsync(filteredMuni);
            if (gen != _updateGeneration) return;

            // 5. Agregar capa de municipios en el fondo del mapa (índice 0, debajo de proyectos)
            RemoverCapaMunicipios();
            if (layerMuni != null)
            {
                Map.OperationalLayers.Insert(0, layerMuni);
                _layerMunicipios = layerMuni;
                _ = layerMuni.LoadAsync();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("[MapaViewModel] Error actualizando capas con filtro", ex);
        }
    }

    /// <summary>
    /// Devuelve el extent (Envelope) de los municipios que contienen proyectos del último filtro aplicado.
    /// Prioriza los municipios correspondientes al departamento o municipio seleccionado en el filtro geográfico activo.
    /// </summary>
    public Envelope? ObtenerExtentMunicipiosFiltrados()
    {
        if (_cachedGeometries == null || _ultimosCodigosMunicipio == null || _ultimosCodigosMunicipio.Count == 0)
            return null;

        double xmin = double.MaxValue, ymin = double.MaxValue;
        double xmax = double.MinValue, ymax = double.MinValue;
        bool any = false;

        string? dptoFiltro = Filtros?.SelectedDepartamento?.Codigo;
        bool tieneFiltroDpto = !string.IsNullOrWhiteSpace(dptoFiltro);

        string? mpioFiltro = (Filtros?.AreaInteres as FiltrosViewModel.MunicipioItem)?.Codigo;
        bool tieneFiltroMpio = !string.IsNullOrWhiteSpace(mpioFiltro);

        // 1. Si hay filtro geográfico activo (departamento o municipio), priorizar los municipios que coinciden con dicho filtro
        if (tieneFiltroMpio || tieneFiltroDpto)
        {
            foreach (var codigo in _ultimosCodigosMunicipio)
            {
                if (tieneFiltroMpio && !string.Equals(codigo, mpioFiltro, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (tieneFiltroDpto && !codigo.StartsWith(dptoFiltro!, StringComparison.OrdinalIgnoreCase))
                    continue;

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
        }

        // 2. Si no hay filtro específico o ninguna geometría coincidió, calcular la envolvente con todos los municipios de los proyectos
        if (!any)
        {
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
        }

        return any ? new Envelope(xmin, ymin, xmax, ymax, SpatialReferences.Wgs84) : null;
    }

    private async Task<Layer?> CrearCapaProyectosDesdeListaAsync(IReadOnlyList<ProyectoGeomatico> items)
    {
        var fields = new List<Field>
        {
            OID("oid"),
            Int("id_proyecto"),
            Str("codigo", 30),
            Str("titulo", 200),
            Str("ruta_archivos", 1024)
        };
        var table = new FeatureCollectionTable(fields, GeometryType.Point, SpatialReferences.Wgs84);

        _oidToProjectId.Clear();
        OverlayProyectosEtiquetas.Graphics.Clear();
        OverlayResaltadoProyecto.Graphics.Clear();

        var features = new List<Feature>();
        int oid = 1;
        foreach (var p in items)
        {
            if (p.Longitud == 0 && p.Latitud == 0) continue;
            var currentOid = oid++;
            _oidToProjectId[currentOid] = p.Id;
            var codigo = $"#PROY-{p.Id:D4}";

            var attrs = new Dictionary<string, object?>
            {
                ["oid"] = currentOid,
                ["id_proyecto"] = p.Id,
                ["codigo"] = codigo,
                ["titulo"] = p.Titulo,
                ["ruta_archivos"] = string.IsNullOrWhiteSpace(p.RutaArchivos) ? null : p.RutaArchivos
            };
            var geom = new MapPoint(p.Longitud, p.Latitud, SpatialReferences.Wgs84);
            features.Add(table.CreateFeature(attrs, geom));

            // Agregar etiqueta textual visible en el mapa con halo blanco de alta legibilidad
            var etiqueta = ProyectoStylingHelper.CrearEtiquetaGraphic(geom, codigo, p.Titulo);
            OverlayProyectosEtiquetas.Graphics.Add(etiqueta);
        }

        if (features.Count == 0) return null;
        await table.AddFeaturesAsync(features);

        // Asignar el icono característico unificado para todos los proyectos (Pin UIS con alta visibilidad)
        table.Renderer = await ProyectoStylingHelper.CrearRendererProyectosAsync();

        var collection = new FeatureCollection(new[] { table });
        var layer = new FeatureCollectionLayer(collection)
        {
            Name = "Proyectos"
        };
        return layer;
    }

    /// <summary>
    /// Resalta interactivamente el proyecto seleccionado en el mapa con un halo luminoso,
    /// su polígono de huella/cobertura geográfica (si tiene extensión válida) y centra la vista suavemente.
    /// </summary>
    public async Task ResaltarProyectoEnMapaAsync(FiltrosViewModel.ProyectoItem? proyecto)
    {
        OverlayResaltadoProyecto.Graphics.Clear();
        if (proyecto == null || (proyecto.Lon == 0 && proyecto.Lat == 0))
        {
            LimpiarSubproyectos();
            return;
        }

        var pt = new MapPoint(proyecto.Lon, proyecto.Lat, SpatialReferences.Wgs84);
        Envelope? extentGeom = null;

        if (proyecto.TieneExtentValido)
        {
            extentGeom = new Envelope(proyecto.MinX, proyecto.MinY, proyecto.MaxX, proyecto.MaxY, SpatialReferences.Wgs84);
        }

        var graphics = ProyectoStylingHelper.CrearResaltadoGraphics(pt, extentGeom);
        foreach (var g in graphics)
        {
            OverlayResaltadoProyecto.Graphics.Add(g);
        }

        if (_ownerMapView != null)
        {
            try
            {
                // Si el proyecto tiene una extensión geográfica amplia (> ~500 metros), encuadrar el polígono completo
                if (extentGeom != null && extentGeom.Width > 0.005 && extentGeom.Height > 0.005)
                {
                    await _ownerMapView.SetViewpointGeometryAsync(extentGeom, 50.0);
                }
                else
                {
                    await _ownerMapView.SetViewpointCenterAsync(pt, 25_000);
                }
            }
            catch { }
        }

        // Cargar y graficar subproyectos (embarcaderos / sitios físicos) si existen
        if (proyecto.Id > 0)
        {
            try
            {
                var subproyectos = await _proyectos.ObtenerSubproyectosAsync(proyecto.Id);
                MostrarSubproyectosEnMapa(subproyectos, actualizarListaGlobal: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Error al cargar subproyectos para resaltado: {ex.Message}");
            }
        }
        else
        {
            LimpiarSubproyectos();
        }
    }

    /// <summary>
    /// Limpia los gráficos de sitios / embarcaderos en el mapa.
    /// </summary>
    public void LimpiarSubproyectos()
    {
        if (_todosSubproyectosActuales != null && _todosSubproyectosActuales.Count > 0)
        {
            MostrarSubproyectosEnMapa(_todosSubproyectosActuales, actualizarListaGlobal: false);
            return;
        }
        OverlaySubproyectos.Graphics.Clear();
    }

    private void _legacyLimpiarSubproyectos()
    {
        OverlaySubproyectos.Graphics.Clear();
    }

    /// <summary>
    /// Muestra los marcadores y etiquetas de subproyectos (ej. embarcaderos) en el overlay correspondiente.
    /// </summary>
    public void MostrarSubproyectosEnMapa(IReadOnlyList<SubproyectoDto>? subproyectos, bool actualizarListaGlobal = true)
    {
        if (actualizarListaGlobal)
        {
            _todosSubproyectosActuales = subproyectos ?? Array.Empty<SubproyectoDto>();
        }
        OverlaySubproyectos.Graphics.Clear();
        if (subproyectos == null || subproyectos.Count == 0) return;

        foreach (var sub in subproyectos)
        {
            if (sub.Lon == 0 && sub.Lat == 0) continue;

            var pt = new MapPoint(sub.Lon, sub.Lat, SpatialReferences.Wgs84);
            var graphics = ProyectoStylingHelper.CrearSubproyectoGraphics(
                pt,
                sub.IdSubproyecto,
                sub.Nombre,
                sub.Subcarpeta,
                sub.MunicipioNombre,
                sub.Descripcion,
                sub.IdProyecto);

            foreach (var g in graphics)
            {
                OverlaySubproyectos.Graphics.Add(g);
            }
        }

        _ = ActualizarCapaSubproyectosAsync(_todosSubproyectosActuales);
    }

    /// <summary>
    /// Centra y hace zoom de alta precisión (1:10,000) a un subproyecto puntual (embarcadero).
    /// </summary>
    public async Task CentrarEnSubproyectoAsync(double lon, double lat, string nombre)
    {
        if (lon == 0 && lat == 0) return;
        var pt = new MapPoint(lon, lat, SpatialReferences.Wgs84);
        if (_ownerMapView != null)
        {
            try
            {
                await _ownerMapView.SetViewpointCenterAsync(pt, 10_000);
            }
            catch { }
        }
    }

    public void AsegurarSubproyectoVisibleEnMapa(SubproyectoDto? sub)
    {
        if (sub == null) return;

        bool yaEstaVisible = OverlaySubproyectos.Graphics.Any(g =>
            g.Attributes.TryGetValue("id_subproyecto", out var val) &&
            val is int id && id == sub.IdSubproyecto);

        if (!yaEstaVisible)
        {
            if (_todosSubproyectosActuales != null && _todosSubproyectosActuales.Count > 0)
            {
                MostrarSubproyectosEnMapa(_todosSubproyectosActuales, actualizarListaGlobal: false);
            }
            else if (sub.Lon != 0 || sub.Lat != 0)
            {
                var pt = new MapPoint(sub.Lon, sub.Lat, SpatialReferences.Wgs84);
                var graphics = ProyectoStylingHelper.CrearSubproyectoGraphics(
                    pt, sub.IdSubproyecto, sub.Nombre, sub.Subcarpeta, sub.MunicipioNombre, sub.Descripcion, sub.IdProyecto);
                foreach (var g in graphics)
                    OverlaySubproyectos.Graphics.Add(g);
            }
        }
    }

    private async Task<Layer?> CrearCapaSubproyectosAsync(IReadOnlyList<SubproyectoDto> items)
    {
        if (items == null || items.Count == 0) return null;

        var validItems = items.Where(s => s.Lon != 0 || s.Lat != 0).ToList();
        if (validItems.Count == 0) return null;

        var fields = new List<Field>
        {
            OID("oid"),
            Int("id_subproyecto"),
            Int("id_proyecto"),
            Str("nombre", 200),
            Str("subcarpeta", 300),
            Str("municipio", 200),
            Str("descripcion", 500)
        };
        var table = new FeatureCollectionTable(fields, GeometryType.Point, SpatialReferences.Wgs84);

        var features = new List<Feature>();
        int oid = 1;
        foreach (var s in validItems)
        {
            var attrs = new Dictionary<string, object?>
            {
                ["oid"] = oid++,
                ["id_subproyecto"] = s.IdSubproyecto,
                ["id_proyecto"] = s.IdProyecto,
                ["nombre"] = s.Nombre,
                ["subcarpeta"] = s.Subcarpeta,
                ["municipio"] = s.MunicipioNombre ?? string.Empty,
                ["descripcion"] = s.Descripcion ?? string.Empty
            };
            var geom = new MapPoint(s.Lon, s.Lat, SpatialReferences.Wgs84);
            features.Add(table.CreateFeature(attrs, geom));
        }

        await table.AddFeaturesAsync(features);

        var markerSymbol = new SimpleMarkerSymbol(
            SimpleMarkerSymbolStyle.Diamond,
            System.Drawing.Color.FromArgb(2, 132, 199),
            12.0)
        {
            Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.White, 2.0)
        };
        table.Renderer = new SimpleRenderer(markerSymbol);

        var collection = new FeatureCollection(new[] { table });
        var layer = new FeatureCollectionLayer(collection)
        {
            Name = "Subproyectos"
        };
        return layer;
    }

    public async Task ActualizarCapaSubproyectosAsync(IReadOnlyList<SubproyectoDto>? subproyectos)
    {
        if (Map == null) return;

        try
        {
            var layer = await CrearCapaSubproyectosAsync(subproyectos ?? Array.Empty<SubproyectoDto>());

            void AplicarEnMapa()
            {
                RemoverCapaSubproyectos();
                if (layer != null)
                {
                    int insertIdx = Map.OperationalLayers.Count;
                    if (_layerProyectos != null && Map.OperationalLayers.Contains(_layerProyectos))
                    {
                        insertIdx = Map.OperationalLayers.IndexOf(_layerProyectos) + 1;
                    }
                    Map.OperationalLayers.Insert(Math.Min(insertIdx, Map.OperationalLayers.Count), layer);
                    _layerSubproyectos = layer;
                    _ = layer.LoadAsync();
                }
            }

            if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(AplicarEnMapa);
            }
            else
            {
                AplicarEnMapa();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("[MapaViewModel] Error al actualizar capa operacional de subproyectos", ex);
        }
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

