using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;

namespace Geomatica.Desktop.Services
{
    /// <summary>
    /// Resultado estructurado del procesamiento en segundo plano de una nube de puntos LiDAR.
    /// Contiene geometrías ya transformadas a WGS84 listas para renderizar en el hilo de UI sin bloquearlo.
    /// </summary>
    public class LidarCloudProcessingResult
    {
        public string RutaCompleta { get; set; } = string.Empty;
        public string NombreArchivo { get; set; } = string.Empty;
        public ulong TotalPoints { get; set; }
        public int SampledPointsCount { get; set; }
        public string CrsNombre { get; set; } = "Desconocido";
        public bool EsCoordenadasLocales { get; set; }
        public bool HasRgbColors { get; set; }

        public double MinZ { get; set; }
        public double MaxZ { get; set; }
        public double AlturaRango { get; set; }
        public double AnchoMetros { get; set; }
        public double LargoMetros { get; set; }
        public double RadioMetros { get; set; }
        public double CentroZWgs84 { get; set; }

        public Envelope EnvelopeWgs84 { get; set; } = null!;
        public Polygon FootprintWgs84 { get; set; } = null!;
        public MapPoint CenterWgs84 { get; set; } = null!;
        public double TamanoPuntoRecomendado { get; set; } = 4.5;

        public List<(MapPoint PtWgs84, System.Drawing.Color Color)> PuntosMuestreadosWgs84 { get; set; } = new();
        public string InfoDetalle3D { get; set; } = string.Empty;

        public bool EsComprimidoLaz { get; set; }
        public string? MensajeDiagnosticoLaz { get; set; }
    }

    /// <summary>
    /// Trabajador en segundo plano para muestreo, lectura y proyección geodésica de nubes LiDAR (LAS/LAZ).
    /// Desacopla todas las operaciones pesadas de cómputo y transformación geométrica del hilo de UI.
    /// </summary>
    public static class LidarBackgroundWorker
    {
        public static async Task<LidarCloudProcessingResult> ProcesarNubeLidarAsync(
            string path,
            (double lon, double lat, double alt)? anclajeLocal = null,
            int maxPointsToSample = 75_000,
            IProgress<(int porcentaje, string detalle)>? progress = null)
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"El archivo LiDAR no existe: {path}");

                var result = new LidarCloudProcessingResult
                {
                    RutaCompleta = path,
                    NombreArchivo = Path.GetFileName(path)
                };

                // 1. Fase de lectura y muestreo binario (progreso 0% - 60%)
                progress?.Report((5, $"Leyendo encabezado LiDAR: {result.NombreArchivo}..."));

                var progressLectura = new Progress<(int pct, string det)>(p =>
                {
                    // Escalar progreso de lectura entre 5% y 60%
                    int escalado = 5 + (int)(p.pct * 0.55);
                    progress?.Report((escalado, p.det));
                });

                var cloud = LasFileReader.Read(path, maxPointsToSample, progressLectura);

                result.TotalPoints = cloud.TotalPoints;
                result.SampledPointsCount = cloud.SampledPointsCount;
                result.CrsNombre = cloud.CrsNombre;
                result.EsCoordenadasLocales = cloud.EsCoordenadasLocales;
                result.HasRgbColors = cloud.HasRgbColors;
                result.MinZ = cloud.MinZ;
                result.MaxZ = cloud.MaxZ;
                result.AlturaRango = cloud.AlturaRango;
                result.AnchoMetros = cloud.AnchoMetros;
                result.LargoMetros = cloud.LargoMetros;

                if (cloud.SampledPointsCount == 0)
                {
                    return result;
                }

                // 2. Fase de transformación y proyección de coordenadas en segundo plano (progreso 60% - 90%)
                progress?.Report((65, $"Proyectando {cloud.SampledPointsCount:N0} puntos geodésicos en segundo plano..."));

                double radioMetros = cloud.RadioAproximadoMetros;
                Envelope envelopeWgs84;
                Polygon footprintWgs84;
                MapPoint centerWgs84;
                double centroZWgs84;

                var cachedList = new List<(MapPoint PtWgs84, System.Drawing.Color Color)>(cloud.SampledPointsCount);

                if (cloud.EsCoordenadasLocales)
                {
                    var anchor = anclajeLocal ?? (-74.08175, 4.60971, 2600.0); // Bogotá por defecto
                    double anchorLon = anchor.lon;
                    double anchorLat = anchor.lat;
                    double anchorAlt = anchor.alt;

                    double cosLat = Math.Cos(anchorLat * Math.PI / 180.0);
                    double metersPerDegLat = 111_320.0;
                    double metersPerDegLon = 111_320.0 * (cosLat > 0.01 ? cosLat : 1.0);
                    double zOffsetBase = (cloud.MinZ < 0) ? Math.Abs(cloud.MinZ) + 0.5 : 0.5;

                    double minLon = anchorLon + (cloud.MinX / metersPerDegLon);
                    double maxLon = anchorLon + (cloud.MaxX / metersPerDegLon);
                    double minLat = anchorLat + (cloud.MinY / metersPerDegLat);
                    double maxLat = anchorLat + (cloud.MaxY / metersPerDegLat);

                    envelopeWgs84 = new Envelope(minLon, minLat, maxLon, maxLat, SpatialReferences.Wgs84);
                    centroZWgs84 = anchorAlt + zOffsetBase + cloud.CentroZ;

                    var polyPoints = new PointCollection(SpatialReferences.Wgs84)
                    {
                        new MapPoint(minLon, minLat, SpatialReferences.Wgs84),
                        new MapPoint(maxLon, minLat, SpatialReferences.Wgs84),
                        new MapPoint(maxLon, maxLat, SpatialReferences.Wgs84),
                        new MapPoint(minLon, maxLat, SpatialReferences.Wgs84),
                        new MapPoint(minLon, minLat, SpatialReferences.Wgs84)
                    };
                    footprintWgs84 = new Polygon(polyPoints, SpatialReferences.Wgs84);

                    double centerLon = anchorLon + (cloud.CentroX / metersPerDegLon);
                    double centerLat = anchorLat + (cloud.CentroY / metersPerDegLat);
                    centerWgs84 = new MapPoint(centerLon, centerLat, SpatialReferences.Wgs84);

                    int procesados = 0;
                    foreach (var pt in cloud.Points)
                    {
                        double ptLon = anchorLon + (pt.X / metersPerDegLon);
                        double ptLat = anchorLat + (pt.Y / metersPerDegLat);
                        double ptAlt = anchorAlt + zOffsetBase + pt.Z;

                        var wgs84Point = new MapPoint(ptLon, ptLat, ptAlt, SpatialReferences.Wgs84);
                        var color = System.Drawing.Color.FromArgb(240, pt.R, pt.G, pt.B);
                        cachedList.Add((wgs84Point, color));

                        procesados++;
                        if (procesados % 25_000 == 0)
                        {
                            int pct = 65 + (int)((double)procesados / cloud.SampledPointsCount * 25.0);
                            progress?.Report((pct, $"Calculando proyección local ({procesados:N0}/{cloud.SampledPointsCount:N0})..."));
                        }
                    }
                }
                else
                {
                    var targetSr = cloud.SpatialReference ?? SpatialReferences.Wgs84;

                    if (targetSr.Wkid == 4326 || (cloud.MinX >= -180 && cloud.MaxX <= 180 && cloud.MinY >= -90 && cloud.MaxY <= 90))
                    {
                        double latRad = cloud.CentroY * Math.PI / 180.0;
                        double dx = (cloud.MaxX - cloud.MinX) * 111_320.0 * Math.Cos(latRad);
                        double dy = (cloud.MaxY - cloud.MinY) * 111_320.0;
                        radioMetros = Math.Sqrt(dx * dx + dy * dy + cloud.AlturaRango * cloud.AlturaRango) / 2.0;
                    }

                    var envelopeOriginal = new Envelope(cloud.MinX, cloud.MinY, cloud.MaxX, cloud.MaxY, targetSr);
                    envelopeWgs84 = (targetSr.Wkid == 4326)
                        ? envelopeOriginal
                        : GeometryEngine.Project(envelopeOriginal, SpatialReferences.Wgs84) as Envelope ?? envelopeOriginal;

                    double zOffsetRelativo = 0.0;
                    if (anclajeLocal.HasValue && anclajeLocal.Value.alt > 500.0 && cloud.MaxZ < (anclajeLocal.Value.alt - 150.0) && cloud.MaxZ < 500.0)
                    {
                        // Altura de vuelo relativa AGL sobre terreno de montaña: elevar para posar sobre la superficie
                        zOffsetRelativo = (anclajeLocal.Value.alt - cloud.MinZ) + 1.0;
                    }

                    centroZWgs84 = cloud.CentroZ + zOffsetRelativo;

                    var polyPoints = new PointCollection(targetSr)
                    {
                        new MapPoint(cloud.MinX, cloud.MinY, targetSr),
                        new MapPoint(cloud.MaxX, cloud.MinY, targetSr),
                        new MapPoint(cloud.MaxX, cloud.MaxY, targetSr),
                        new MapPoint(cloud.MinX, cloud.MaxY, targetSr),
                        new MapPoint(cloud.MinX, cloud.MinY, targetSr)
                    };
                    var polyOriginal = new Polygon(polyPoints, targetSr);
                    footprintWgs84 = (targetSr.Wkid == 4326)
                        ? polyOriginal
                        : GeometryEngine.Project(polyOriginal, SpatialReferences.Wgs84) as Polygon ?? polyOriginal;

                    var centerPoint = new MapPoint(cloud.CentroX, cloud.CentroY, targetSr);
                    centerWgs84 = (targetSr.Wkid == 4326)
                        ? centerPoint
                        : GeometryEngine.Project(centerPoint, SpatialReferences.Wgs84) as MapPoint ?? centerPoint;

                    bool esDirectamenteWgs84 = (targetSr.Wkid == 4326);
                    int procesados = 0;

                    foreach (var pt in cloud.Points)
                    {
                        var mapPoint = new MapPoint(pt.X, pt.Y, pt.Z + zOffsetRelativo, targetSr);
                        var wgs84Point = esDirectamenteWgs84
                            ? mapPoint
                            : GeometryEngine.Project(mapPoint, SpatialReferences.Wgs84) as MapPoint ?? mapPoint;

                        var color = System.Drawing.Color.FromArgb(240, pt.R, pt.G, pt.B);
                        cachedList.Add((wgs84Point, color));

                        procesados++;
                        if (procesados % 25_000 == 0)
                        {
                            int pct = 65 + (int)((double)procesados / cloud.SampledPointsCount * 25.0);
                            progress?.Report((pct, $"Proyectando geometrías ({procesados:N0}/{cloud.SampledPointsCount:N0})..."));
                        }
                    }
                }

                // 3. Fase de estructuración final (progreso 90% - 100%)
                progress?.Report((92, "Estructurando visualización 3D..."));

                radioMetros = Math.Max(0.4, radioMetros);
                double tamanoPunto = (radioMetros < 15.0) ? 5.5 : (radioMetros < 100.0 ? 5.0 : 4.5);

                result.RadioMetros = radioMetros;
                result.EnvelopeWgs84 = envelopeWgs84;
                result.FootprintWgs84 = footprintWgs84;
                result.CenterWgs84 = centerWgs84;
                result.CentroZWgs84 = centroZWgs84;
                result.TamanoPuntoRecomendado = tamanoPunto;
                result.PuntosMuestreadosWgs84 = cachedList;

                result.InfoDetalle3D = $"Archivo: {cloud.TotalPoints:N0} pts (muestra: {cloud.SampledPointsCount:N0})\n" +
                                      $"CRS: {cloud.CrsNombre}\n" +
                                      $"Dim: {cloud.AnchoMetros:F1}m × {cloud.LargoMetros:F1}m (R: {cloud.RadioAproximadoMetros:F1}m)\n" +
                                      $"Elevación Z: {cloud.MinZ:F2} m a {cloud.MaxZ:F2} m (Δ {cloud.AlturaRango:F2} m)\n" +
                                      $"Colores: {(cloud.HasRgbColors ? "RGB Fotogramétrico" : "Rampa Hipsométrica")}" +
                                      (cloud.EsCoordenadasLocales ? "\n(Anclado en entorno 3D local 1:1 en metros)" : "");

                progress?.Report((100, $"Nube preparada ({cachedList.Count:N0} puntos)."));
                return result;
            });
        }
    }
}

