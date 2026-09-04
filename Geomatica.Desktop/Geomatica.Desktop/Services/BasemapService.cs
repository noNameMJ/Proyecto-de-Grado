using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Portal;
using Esri.ArcGISRuntime.Rasters;
using Esri.ArcGISRuntime.Tasks.Offline;
using Geomatica.Desktop.Models;

namespace Geomatica.Desktop.Services
{
    public class BasemapService
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

        public static string GetOfflineStorageDirectory()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Geomatica",
                "Basemaps");

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            return folder;
        }

        public static string GetBundledTopographicPath()
        {
            var appDir = AppContext.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(appDir, "Assets", "Basemaps", "topographic_offline.vtpk"),
                Path.Combine(appDir, "Assets", "Basemaps", "basemap_offline.vtpk"),
                Path.GetFullPath(Path.Combine(appDir, "..", "..", "..", "Assets", "Basemaps", "topographic_offline.vtpk")),
                Path.GetFullPath(Path.Combine(appDir, "..", "..", "..", "Assets", "Basemaps", "basemap_offline.vtpk"))
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }

            return candidates[0];
        }

        public static string GetOfflinePathForBasemap(string id)
        {
            if (id == "topographic")
            {
                var bundled = GetBundledTopographicPath();
                if (File.Exists(bundled)) return bundled;
            }

            var storage = GetOfflineStorageDirectory();
            var vtpkPath = Path.Combine(storage, $"{id}_offline.vtpk");
            if (File.Exists(vtpkPath)) return vtpkPath;

            var tpkxPath = Path.Combine(storage, $"{id}_offline.tpkx");
            if (File.Exists(tpkxPath)) return tpkxPath;

            return vtpkPath;
        }

        public static bool IsOfflineAvailableFor(string id)
        {
            var path = GetOfflinePathForBasemap(id);
            return File.Exists(path);
        }

        public IReadOnlyList<BasemapOption> ObtenerMapasBaseDisponibles()
        {
            var topoPath = GetBundledTopographicPath();
            var topoOfflineExists = File.Exists(topoPath);

            return new List<BasemapOption>
            {
                new()
                {
                    Id = "topographic",
                    Nombre = "Topográfico",
                    Icono = "⛰️",
                    Descripcion = "Relieve, curvas de nivel y toponimia oficial de Esri (Offline predeterminado)",
                    Style = BasemapStyle.ArcGISTopographic,
                    IsDefaultOffline = true,
                    IsOfflineAvailable = topoOfflineExists,
                    OfflinePackagePath = topoPath,
                    IsHabilitado = true,
                    EstadoTexto = "Disponible Offline"
                },
                new()
                {
                    Id = "imagery",
                    Nombre = "Satélite",
                    Icono = "🛰️",
                    Descripcion = "Imágenes satelitales mundiales de alta resolución de Esri (Requiere internet)",
                    Style = BasemapStyle.ArcGISImageryStandard,
                    IsDefaultOffline = false,
                    IsOfflineAvailable = false,
                    IsHabilitado = true,
                    EstadoTexto = "En línea"
                },
                new()
                {
                    Id = "imagery_labels",
                    Nombre = "Híbrido",
                    Icono = "🗺️",
                    Descripcion = "Satélite oficial de Esri con nombres de vías, límites y etiquetas (Requiere internet)",
                    Style = BasemapStyle.ArcGISImagery,
                    IsDefaultOffline = false,
                    IsOfflineAvailable = false,
                    IsHabilitado = true,
                    EstadoTexto = "En línea"
                },
                new()
                {
                    Id = "streets",
                    Nombre = "Calles",
                    Icono = "🚗",
                    Descripcion = "Red vial detallada y zonas urbanas oficiales de Esri (Requiere internet)",
                    Style = BasemapStyle.ArcGISStreets,
                    IsDefaultOffline = false,
                    IsOfflineAvailable = false,
                    IsHabilitado = true,
                    EstadoTexto = "En línea"
                },
                new()
                {
                    Id = "navigation",
                    Nombre = "Navegación",
                    Icono = "🧭",
                    Descripcion = "Cartografía vial optimizada para navegación de Esri (Requiere internet)",
                    Style = BasemapStyle.ArcGISNavigation,
                    IsDefaultOffline = false,
                    IsOfflineAvailable = false,
                    IsHabilitado = true,
                    EstadoTexto = "En línea"
                },
                new()
                {
                    Id = "light_gray",
                    Nombre = "Gris Claro",
                    Icono = "🌐",
                    Descripcion = "Lienzo neutro claro oficial de Esri para destacar capas (Requiere internet)",
                    Style = BasemapStyle.ArcGISLightGray,
                    IsDefaultOffline = false,
                    IsOfflineAvailable = false,
                    IsHabilitado = true,
                    EstadoTexto = "En línea"
                },
                new()
                {
                    Id = "dark_gray",
                    Nombre = "Gris Oscuro",
                    Icono = "🌑",
                    Descripcion = "Lienzo neutro oscuro de alto contraste oficial de Esri (Requiere internet)",
                    Style = BasemapStyle.ArcGISDarkGray,
                    IsDefaultOffline = false,
                    IsOfflineAvailable = false,
                    IsHabilitado = true,
                    EstadoTexto = "En línea"
                }
            };
        }

        public static bool ComprobarConexionInternet()
        {
            if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                return false;

            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = ping.Send("8.8.8.8", 1200);
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch
            {
                try
                {
                    using var client = new System.Net.Sockets.TcpClient();
                    var result = client.BeginConnect("8.8.8.8", 53, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(1200));
                    if (!success) return false;
                    client.EndConnect(result);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static Layer CrearCapaDesdePaqueteLocal(string path, string nombre)
        {
            bool esVector = false;
            if (path.EndsWith(".vtpk", StringComparison.OrdinalIgnoreCase))
            {
                esVector = true;
            }
            else if (path.EndsWith(".tpkx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tpk", StringComparison.OrdinalIgnoreCase))
            {
                esVector = false;
            }
            else
            {
                try
                {
                    using var archive = System.IO.Compression.ZipFile.OpenRead(path);
                    esVector = archive.Entries.Any(e => 
                        e.FullName.StartsWith("p12/resources/styles", StringComparison.OrdinalIgnoreCase) || 
                        e.FullName.EndsWith(".pbf", StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"No se pudo inspeccionar el archivo de {path}: {ex.Message}");
                    esVector = false;
                }
            }

            if (esVector)
            {
                AppLogger.Info($"Cargando como VectorTileCache: {path}");
                var cache = new VectorTileCache(path);

                // Detectar si existe una carpeta de recursos de estilo ItemResourceCache al lado del paquete
                var dir = Path.GetDirectoryName(path) ?? "";
                var baseName = Path.GetFileNameWithoutExtension(path);
                var styleDir1 = Path.Combine(dir, $"{baseName}_style");
                var idWithoutOffline = baseName.Replace("_offline", "");
                var styleDir2 = Path.Combine(dir, $"{idWithoutOffline}_style");

                string? styleDir = Directory.Exists(styleDir1) ? styleDir1 : (Directory.Exists(styleDir2) ? styleDir2 : null);

                if (styleDir != null)
                {
                    try
                    {
                        AppLogger.Info($"Aplicando ItemResourceCache oficial Esri desde {styleDir}");
                        var resCache = new ItemResourceCache(styleDir);
                        return new ArcGISVectorTiledLayer(cache, resCache)
                        {
                            Name = $"Offline {nombre}",
                            ShowInLegend = false
                        };
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"No se pudo inicializar ItemResourceCache desde {styleDir}: {ex.Message}");
                    }
                }

                return new ArcGISVectorTiledLayer(cache)
                {
                    Name = $"Offline {nombre}",
                    ShowInLegend = false
                };
            }
            else
            {
                AppLogger.Info($"Cargando como TileCache (Raster): {path}");
                var cache = new TileCache(path);
                return new ArcGISTiledLayer(cache)
                {
                    Name = $"Offline {nombre}",
                    ShowInLegend = false
                };
            }
        }

        private static Layer? ObtenerCapaHillshadeComplementaria(string vtpkPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(vtpkPath) ?? "";
                var storage = GetOfflineStorageDirectory();
                var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "Basemaps");

                var candidates = new[]
                {
                    Path.Combine(dir, "topographic_hillshade.tpkx"),
                    Path.Combine(storage, "topographic_hillshade.tpkx"),
                    Path.Combine(bundled, "topographic_hillshade.tpkx")
                };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        AppLogger.Info($"Capa de sombreado de relieve (Hillshade) encontrada en: {candidate}");
                        return new ArcGISTiledLayer(new TileCache(candidate))
                        {
                            Name = "Offline Hillshade",
                            ShowInLegend = false
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"No se pudo cargar capa hillshade complementaria: {ex.Message}");
            }

            return null;
        }

        public Basemap CrearBasemap(BasemapOption option, bool forzarOffline = false)
        {
            bool usarOffline = forzarOffline || !ComprobarConexionInternet();

            if (usarOffline)
            {
                // Único mapa offline: Topográfico predeterminado
                var fallbackPath = GetBundledTopographicPath();
                if (File.Exists(fallbackPath))
                {
                    try
                    {
                        AppLogger.Info($"Cargando mapa base Topográfico offline predeterminado: {fallbackPath}");
                        var layer = CrearCapaDesdePaqueteLocal(fallbackPath, "Topográfico Predeterminado");
                        var hillshadeLayer = ObtenerCapaHillshadeComplementaria(fallbackPath);
                        if (hillshadeLayer != null)
                        {
                            return new Basemap(new Layer[] { hillshadeLayer, layer });
                        }
                        return new Basemap(layer);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Error al cargar mapa base Topográfico offline predeterminado", ex);
                    }
                }
            }

            // Modo en línea
            if (option.Style.HasValue)
            {
                return new Basemap(option.Style.Value);
            }

            return new Basemap(BasemapStyle.ArcGISTopographic);
        }
    }
}
