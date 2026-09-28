using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Esri.ArcGISRuntime.Geometry;

namespace Geomatica.Desktop.Services
{
    public class LasPoint3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public ushort Intensity { get; set; }
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public byte Classification { get; set; }
    }

    public class LasCloudData
    {
        public string FilePath { get; set; } = string.Empty;
        public string Version { get; set; } = "1.2";
        public ulong TotalPoints { get; set; }
        public int SampledPointsCount => Points.Count;
        public byte PointFormat { get; set; }
        public bool HasRgbColors { get; set; }

        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double MinY { get; set; }
        public double MaxY { get; set; }
        public double MinZ { get; set; }
        public double MaxZ { get; set; }

        public SpatialReference? SpatialReference { get; set; }
        public string CrsNombre { get; set; } = "Desconocido";
        public bool EsCoordenadasLocales { get; set; }
        public List<LasPoint3D> Points { get; } = new();

        public double CentroX => (MinX + MaxX) / 2.0;
        public double CentroY => (MinY + MaxY) / 2.0;
        public double CentroZ => (MinZ + MaxZ) / 2.0;
        public double AlturaRango => Math.Max(0.01, MaxZ - MinZ);

        public double AnchoMetros => Math.Max(0.01, MaxX - MinX);
        public double LargoMetros => Math.Max(0.01, MaxY - MinY);
        public double RadioAproximadoMetros => Math.Sqrt(AnchoMetros * AnchoMetros + LargoMetros * LargoMetros + AlturaRango * AlturaRango) / 2.0;
    }

    public static class LasFileReader
    {
        public static LasCloudData Read(string filePath, int maxPointsToSample = 80_000, IProgress<(int porcentaje, string detalle)>? progress = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"El archivo LAS no existe: {filePath}");

            var cloud = new LasCloudData { FilePath = filePath };

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
            using var reader = new BinaryReader(fs, Encoding.ASCII);

            // 1. Validar firma 'LASF'
            var sig = new string(reader.ReadChars(4));
            if (sig != "LASF")
            {
                throw new InvalidDataException("El archivo no es un archivo LAS válido (firma 'LASF' ausente).");
            }

            // Version en byte 24
            fs.Seek(24, SeekOrigin.Begin);
            byte verMajor = reader.ReadByte();
            byte verMinor = reader.ReadByte();
            cloud.Version = $"{verMajor}.{verMinor}";

            // Header Size (offset 94) y Offset to Point Data (offset 96)
            fs.Seek(94, SeekOrigin.Begin);
            ushort headerSize = reader.ReadUInt16();
            uint offsetToPoints = reader.ReadUInt32();
            uint numVlr = reader.ReadUInt32();
            byte pointFormat = reader.ReadByte();
            ushort pointRecordLength = reader.ReadUInt16();
            uint legacyNumPoints = reader.ReadUInt32();

            cloud.PointFormat = pointFormat;
            cloud.HasRgbColors = pointFormat is 2 or 3 or 7 or 8 or 10;

            // Escala y Desfases (offset 131)
            fs.Seek(131, SeekOrigin.Begin);
            double scaleX = reader.ReadDouble();
            double scaleY = reader.ReadDouble();
            double scaleZ = reader.ReadDouble();
            double offsetX = reader.ReadDouble();
            double offsetY = reader.ReadDouble();
            double offsetZ = reader.ReadDouble();

            // Extents (offset 179)
            double maxX = reader.ReadDouble();
            double minX = reader.ReadDouble();
            double maxY = reader.ReadDouble();
            double minY = reader.ReadDouble();
            double maxZ = reader.ReadDouble();
            double minZ = reader.ReadDouble();

            cloud.MinX = minX;
            cloud.MaxX = maxX;
            cloud.MinY = minY;
            cloud.MaxY = maxY;
            cloud.MinZ = minZ;
            cloud.MaxZ = maxZ;

            ulong totalPoints = legacyNumPoints;

            // En LAS 1.4, si legacy es 0 o header >= 375, leer número de puntos de 64 bits en byte 247
            if (verMinor >= 4 && headerSize >= 375)
            {
                fs.Seek(247, SeekOrigin.Begin);
                ulong points64 = reader.ReadUInt64();
                if (points64 > 0)
                {
                    totalPoints = points64;
                }
            }

            cloud.TotalPoints = totalPoints;

            // Detectar CRS (desde VLRs internos del LAS, archivo .prj asociado o análisis de coordenadas)
            var (sr, crsDesc, esLocal) = DetectarSpatialReference(filePath, reader, fs, headerSize, numVlr, offsetToPoints, minX, maxX, minY, maxY);
            cloud.SpatialReference = sr;
            cloud.CrsNombre = crsDesc;
            cloud.EsCoordenadasLocales = esLocal;

            if (totalPoints == 0 || pointRecordLength == 0)
            {
                return cloud;
            }

            // Calcular paso de submuestreo para no saturar memoria ni GPU
            int step = 1;
            if (totalPoints > (ulong)maxPointsToSample)
            {
                step = (int)Math.Ceiling((double)totalPoints / maxPointsToSample);
            }

            fs.Seek(offsetToPoints, SeekOrigin.Begin);

            var zRange = Math.Max(1.0, maxZ - minZ);
            byte[] recordBuffer = new byte[pointRecordLength];

            int pointsRead = 0;
            int sampledCount = 0;
            int ultimoPorcentajeReportado = -1;
            long dataLength = Math.Max(1, fs.Length - offsetToPoints);

            progress?.Report((5, $"Analizando encabezado LAS (Total: {totalPoints:N0} puntos)..."));

            while (fs.Position < fs.Length && sampledCount < maxPointsToSample)
            {
                int bytesRead = fs.Read(recordBuffer, 0, pointRecordLength);
                if (bytesRead < pointRecordLength) break;

                if (pointsRead % step == 0)
                {
                    // Decodificar X, Y, Z
                    int rawX = BitConverter.ToInt32(recordBuffer, 0);
                    int rawY = BitConverter.ToInt32(recordBuffer, 4);
                    int rawZ = BitConverter.ToInt32(recordBuffer, 8);

                    double realX = rawX * scaleX + offsetX;
                    double realY = rawY * scaleY + offsetY;
                    double realZ = rawZ * scaleZ + offsetZ;

                    ushort intensity = BitConverter.ToUInt16(recordBuffer, 12);
                    byte classification = recordBuffer.Length > 15 ? recordBuffer[15] : (byte)0;

                    byte r = 0, g = 0, b = 0;

                    if (cloud.HasRgbColors)
                    {
                        // En formato 2 y 3, RGB está en los últimos 6 bytes del registro
                        int rgbOffset = pointFormat switch
                        {
                            2 => 20,
                            3 => 28,
                            7 => 30,
                            8 => 30,
                            _ => pointRecordLength - 6
                        };

                        if (rgbOffset + 6 <= recordBuffer.Length)
                        {
                            ushort rawR = BitConverter.ToUInt16(recordBuffer, rgbOffset);
                            ushort rawG = BitConverter.ToUInt16(recordBuffer, rgbOffset + 2);
                            ushort rawB = BitConverter.ToUInt16(recordBuffer, rgbOffset + 4);

                            // Normalizar 16-bit a 8-bit
                            r = (byte)(rawR > 255 ? rawR >> 8 : rawR);
                            g = (byte)(rawG > 255 ? rawG >> 8 : rawG);
                            b = (byte)(rawB > 255 ? rawB >> 8 : rawB);
                        }
                    }

                    // Si no tiene RGB o el color leído es negro puro (0,0,0), aplicar rampa hipsométrica por Z
                    if (!cloud.HasRgbColors || (r == 0 && g == 0 && b == 0))
                    {
                        var t = Math.Clamp((realZ - minZ) / zRange, 0.0, 1.0);
                        (r, g, b) = ObtenerColorRampaElevacion(t);
                    }

                    cloud.Points.Add(new LasPoint3D
                    {
                        X = realX,
                        Y = realY,
                        Z = realZ,
                        Intensity = intensity,
                        Classification = classification,
                        R = r,
                        G = g,
                        B = b
                    });

                    sampledCount++;
                }

                pointsRead++;

                if (progress != null && (pointsRead % 10_000 == 0 || sampledCount == maxPointsToSample))
                {
                    double ratio = Math.Clamp((double)(fs.Position - offsetToPoints) / dataLength, 0.0, 1.0);
                    int pct = 5 + (int)(ratio * 90.0); // Rango 5% a 95%
                    if (pct != ultimoPorcentajeReportado)
                    {
                        ultimoPorcentajeReportado = pct;
                        progress.Report((pct, $"{sampledCount:N0} puntos procesados ({pct}%)"));
                    }
                }
            }

            progress?.Report((100, $"Nube de puntos cargada ({sampledCount:N0} puntos)."));
            return cloud;
        }

        private static (byte R, byte G, byte B) ObtenerColorRampaElevacion(double t)
        {
            // Rampa hipsométrica: Azul (valle) -> Cian -> Verde -> Amarillo -> Rojo (cima)
            if (t < 0.25)
            {
                double f = t / 0.25;
                return (0, (byte)(f * 200), (byte)(220 - f * 40));
            }
            if (t < 0.5)
            {
                double f = (t - 0.25) / 0.25;
                return ((byte)(f * 50), (byte)(200 + f * 55), (byte)(180 - f * 180));
            }
            if (t < 0.75)
            {
                double f = (t - 0.5) / 0.25;
                return ((byte)(50 + f * 205), (byte)(255 - f * 40), 0);
            }
            else
            {
                double f = (t - 0.75) / 0.25;
                return (255, (byte)(215 - f * 180), (byte)(f * 60));
            }
        }

        private static (SpatialReference? sr, string nombre, bool esLocal) DetectarSpatialReference(
            string filePath, 
            BinaryReader reader, 
            Stream fs, 
            ushort headerSize, 
            uint numVlr, 
            uint offsetToPoints, 
            double minX, double maxX, double minY, double maxY)
        {
            double cx = (minX + maxX) / 2.0;
            double cy = (minY + maxY) / 2.0;

            // En Colombia:
            // - Geográficas WGS84: Longitud en [-85.0 .. -66.0], Latitud en [-4.5 .. 13.5].
            // - Proyectadas (MAGNA Origen Nacional, Bogotá, UTM): Coordenadas X > 100,000 m.
            // Si |cx| < 50,000 y |cy| < 50,000 y NO está en el recuadro geográfico de Colombia:
            // es DEFINITIVAMENTE un escáner en coordenadas locales (metros desde el instrumento).
            bool estaEnRangoGeograficoColombia = (cx >= -85.0 && cx <= -66.0 && cy >= -4.5 && cy <= 13.5);
            bool esRangoLocalPequeno = Math.Abs(cx) < 50_000.0 && Math.Abs(cy) < 50_000.0 && !estaEnRangoGeograficoColombia;

            if (esRangoLocalPequeno)
            {
                return (null, "Coordenadas Locales (Escáner / TLS)", true);
            }

            // 1. Intentar extraer CRS exacto de los VLRs internos del LAS (GeoKeyDirectory o WKT)
            var (vlrSr, vlrNombre) = LeerVlrsSpatialReference(reader, fs, headerSize, numVlr, offsetToPoints);
            if (vlrSr != null)
            {
                // Si el VLR dice WGS84 o grados, pero las coordenadas no son grados de Colombia
                if ((vlrSr.Wkid == 4326 || vlrSr.IsGeographic) && !estaEnRangoGeograficoColombia)
                {
                    return (null, "Coordenadas Locales (Escáner / TLS)", true);
                }
                return (vlrSr, vlrNombre, false);
            }

            // 2. Revisar archivo sidecar .prj (mismo nombre que el LAS)
            var dir = Path.GetDirectoryName(filePath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                var prjPath = Path.Combine(dir, nameWithoutExt + ".prj");
                if (File.Exists(prjPath))
                {
                    try
                    {
                        var wkt = File.ReadAllText(prjPath).Trim();
                        var prjSr = ParsearWktTexto(wkt);
                        if (prjSr != null)
                        {
                            // Si el PRJ dice que es geográfico o MAGNA 3D (como EPSG:4997), pero las coordenadas no son grados de Colombia
                            if ((prjSr.Wkid == 4326 || prjSr.Wkid == 4997 || prjSr.IsGeographic) && !estaEnRangoGeograficoColombia)
                            {
                                return (null, "Coordenadas Locales (Escáner / TLS)", true);
                            }
                            return (prjSr, prjSr.Wkid != 0 ? $"EPSG:{prjSr.Wkid} (de archivo .prj)" : "CRS de archivo .prj", false);
                        }
                    }
                    catch { }
                }
            }

            // 3. Heurística inteligente según coordenadas de Colombia
            if (estaEnRangoGeograficoColombia)
            {
                return (SpatialReferences.Wgs84, "WGS 84 (EPSG:4326) [Geográfico]", false);
            }

            // Origen Nacional Colombia (EPSG:9377): X ~ 4,000,000 - 6,000,000; Y ~ 1,000,000 - 3,500,000
            if (cx > 3_500_000 && cx < 6_500_000 && cy > 800_000 && cy < 3_800_000)
            {
                return (SpatialReference.Create(9377), "MAGNA-SIRGAS Origen Nacional (EPSG:9377)", false);
            }

            // MAGNA-SIRGAS Colombia Bogotá (EPSG:3116): X ~ 900,000 - 1,250,000; Y ~ 700,000 - 1,500,000
            if (cx >= 880_000 && cx <= 1_250_000 && cy >= 650_000 && cy <= 1_550_000)
            {
                return (SpatialReference.Create(3116), "MAGNA-SIRGAS Bogotá (EPSG:3116)", false);
            }

            // UTM Zone 18N (EPSG:32618): X ~ 100,000 - 880,000; Y ~ 0 - 1,500,000 (Gran parte de Colombia Central y Oriental)
            if (cx > 100_000 && cx < 880_000 && cy >= 0 && cy < 1_600_000)
            {
                return (SpatialReference.Create(32618), "WGS 84 / UTM Zone 18N (EPSG:32618)", false);
            }

            // UTM Zone 17N (EPSG:32617): Colombia Occidental / Chocó
            if (cx > 100_000 && cx < 500_000 && cy >= 0 && cy < 1_200_000)
            {
                return (SpatialReference.Create(32617), "WGS 84 / UTM Zone 17N (EPSG:32617)", false);
            }

            // Si las coordenadas son menores a 100,000 m y no coinciden con ningún CRS proyectado:
            return (null, "Coordenadas Locales (Escáner / TLS)", true);
        }

        private static (SpatialReference? sr, string nombre) LeerVlrsSpatialReference(
            BinaryReader reader, 
            Stream fs, 
            ushort headerSize, 
            uint numVlr, 
            uint offsetToPoints)
        {
            if (numVlr == 0 || headerSize >= offsetToPoints) return (null, "");

            try
            {
                fs.Seek(headerSize, SeekOrigin.Begin);

                for (int i = 0; i < numVlr; i++)
                {
                    if (fs.Position + 54 > offsetToPoints || fs.Position + 54 > fs.Length) break;

                    ushort reserved = reader.ReadUInt16();
                    byte[] userIdBytes = reader.ReadBytes(16);
                    ushort recordId = reader.ReadUInt16();
                    ushort recordLength = reader.ReadUInt16();
                    byte[] descBytes = reader.ReadBytes(32);

                    string userId = Encoding.ASCII.GetString(userIdBytes).Trim('\0', ' ');

                    if (fs.Position + recordLength > fs.Length) break;
                    byte[] data = reader.ReadBytes(recordLength);

                    // 1. GeoKeyDirectoryTag (Record ID 34735)
                    if (recordId == 34735 && (userId.Contains("LASF_Projection", StringComparison.OrdinalIgnoreCase) || userId.Contains("liblas", StringComparison.OrdinalIgnoreCase)))
                    {
                        var sr = ParsearGeoKeyDirectory(data);
                        if (sr != null)
                        {
                            return (sr, sr.Wkid != 0 ? $"EPSG:{sr.Wkid} (VLR GeoKey)" : "VLR GeoKey");
                        }
                    }

                    // 2. WKT Coordinate System (Record ID 2112 o 2111)
                    if ((recordId == 2112 || recordId == 2111 || recordId == 34737) && 
                        (userId.Contains("LASF_Projection", StringComparison.OrdinalIgnoreCase) || userId.Contains("liblas", StringComparison.OrdinalIgnoreCase)))
                    {
                        var sr = ParsearWkt(data);
                        if (sr != null)
                        {
                            return (sr, sr.Wkid != 0 ? $"EPSG:{sr.Wkid} (VLR WKT)" : "VLR WKT");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LasFileReader] Error analizando VLRs: {ex.Message}");
            }

            return (null, "");
        }

        private static SpatialReference? ParsearGeoKeyDirectory(byte[] data)
        {
            if (data.Length < 8) return null;
            int numKeys = BitConverter.ToUInt16(data, 6);
            int offset = 8;

            SpatialReference? geographicSr = null;

            for (int k = 0; k < numKeys; k++)
            {
                if (offset + 8 > data.Length) break;

                ushort keyId = BitConverter.ToUInt16(data, offset);
                ushort tiffTagLocation = BitConverter.ToUInt16(data, offset + 2);
                ushort count = BitConverter.ToUInt16(data, offset + 4);
                ushort valueOffset = BitConverter.ToUInt16(data, offset + 6);
                offset += 8;

                // ProjectedCSTypeGeoKey = 3072
                if (keyId == 3072 && tiffTagLocation == 0 && valueOffset > 0)
                {
                    try
                    {
                        var sr = SpatialReference.Create(valueOffset);
                        if (sr != null) return sr;
                    }
                    catch { }
                }

                // GeographicTypeGeoKey = 2048
                if (keyId == 2048 && tiffTagLocation == 0 && valueOffset > 0)
                {
                    try
                    {
                        geographicSr = SpatialReference.Create(valueOffset);
                    }
                    catch { }
                }
            }

            return geographicSr;
        }

        private static SpatialReference? ParsearWkt(byte[] data)
        {
            try
            {
                string text = Encoding.UTF8.GetString(data).Trim('\0', ' ', '\r', '\n');
                return ParsearWktTexto(text);
            }
            catch { }
            return null;
        }

        private static SpatialReference? ParsearWktTexto(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            try
            {
                // Buscar código EPSG en el texto: AUTHORITY["EPSG","32618"] o ID["EPSG",32618]
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    text, 
                    @"(?:AUTHORITY|ID)\[""EPSG""\s*,\s*""?(\d+)""?\]", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // El último suele ser el código proyectado (si está anidado)
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    if (int.TryParse(matches[i].Groups[1].Value, out int epsg) && epsg > 0)
                    {
                        try
                        {
                            var sr = SpatialReference.Create(epsg);
                            if (sr != null) return sr;
                        }
                        catch { }
                    }
                }

                // Intentar construir directamente desde el texto WKT
                var srWkt = SpatialReference.Create(text);
                if (srWkt != null) return srWkt;
            }
            catch { }

            return null;
        }
    }
}
