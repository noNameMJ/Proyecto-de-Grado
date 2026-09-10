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
        public List<LasPoint3D> Points { get; } = new();

        public double CentroX => (MinX + MaxX) / 2.0;
        public double CentroY => (MinY + MaxY) / 2.0;
        public double CentroZ => (MinZ + MaxZ) / 2.0;
        public double AlturaRango => Math.Max(1.0, MaxZ - MinZ);
    }

    public static class LasFileReader
    {
        public static LasCloudData Read(string filePath, int maxPointsToSample = 80_000)
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

            // Detectar CRS (de archivo .prj asociado o por rangos de coordenadas colombianas)
            cloud.SpatialReference = DetectarSpatialReference(filePath, minX, maxX, minY, maxY);

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
            }

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

        private static SpatialReference DetectarSpatialReference(string filePath, double minX, double maxX, double minY, double maxY)
        {
            // 1. Revisar archivo sidecar .prj
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
                        if (wkt.Contains("3116") || wkt.Contains("MAGNA-SIRGAS / Colombia Bogota", StringComparison.OrdinalIgnoreCase))
                            return SpatialReference.Create(3116);
                        if (wkt.Contains("9377") || wkt.Contains("MAGNA-SIRGAS / Origen-Nacional", StringComparison.OrdinalIgnoreCase))
                            return SpatialReference.Create(9377);
                        if (wkt.Contains("4326") || wkt.Contains("WGS 84", StringComparison.OrdinalIgnoreCase))
                            return SpatialReferences.Wgs84;
                        if (wkt.Contains("32618") || wkt.Contains("UTM zone 18N", StringComparison.OrdinalIgnoreCase))
                            return SpatialReference.Create(32618);
                        if (wkt.Contains("32617") || wkt.Contains("UTM zone 17N", StringComparison.OrdinalIgnoreCase))
                            return SpatialReference.Create(32617);
                        if (wkt.Contains("32619") || wkt.Contains("UTM zone 19N", StringComparison.OrdinalIgnoreCase))
                            return SpatialReference.Create(32619);

                        var sr = SpatialReference.Create(wkt);
                        if (sr != null) return sr;
                    }
                    catch { }
                }
            }

            // 2. Heurística por coordenadas comunes en Colombia
            double cx = (minX + maxX) / 2.0;
            double cy = (minY + maxY) / 2.0;

            // Grados WGS84: Longitud entre -85° y -65°, Latitud entre -5° y 15°
            if (cx >= -85.0 && cx <= -65.0 && cy >= -5.0 && cy <= 15.0)
            {
                return SpatialReferences.Wgs84;
            }

            // Origen Nacional Colombia (EPSG:9377): X ~ 4,000,000 - 6,000,000; Y ~ 1,000,000 - 3,000,000
            if (cx > 3_000_000 && cx < 7_000_000 && cy > 800_000 && cy < 4_000_000)
            {
                return SpatialReference.Create(9377);
            }

            // MAGNA-SIRGAS Colombia Bogotá (EPSG:3116): X ~ 700,000 - 1,300,000; Y ~ 600,000 - 1,700,000
            if (cx > 500_000 && cx < 1_500_000 && cy > 400_000 && cy < 1_900_000)
            {
                return SpatialReference.Create(3116);
            }

            // UTM Zone 18N (EPSG:32618): X ~ 200,000 - 850,000; Y ~ 0 - 1,500,000
            if (cx > 100_000 && cx < 900_000 && cy >= 0 && cy < 1_600_000)
            {
                return SpatialReference.Create(32618);
            }

            // Default fallback para Colombia: MAGNA Bogotá (EPSG:3116)
            return SpatialReference.Create(3116);
        }
    }
}
