using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OSGeo.GDAL;

namespace Geomatica.Desktop.GDAL
{
    public static class GdalRasterConverter
    {
        private static readonly string CacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Geomatica",
            "RasterCache");

        static GdalRasterConverter()
        {
            if (!Directory.Exists(CacheFolder))
            {
                Directory.CreateDirectory(CacheFolder);
            }

            Gdal.AllRegister();
        }

        public static string GetRasterPathToLoad(string originalTiffPath)
        {
            if (string.IsNullOrEmpty(originalTiffPath) || !File.Exists(originalTiffPath))
            {
                return originalTiffPath;
            }

            var extension = Path.GetExtension(originalTiffPath).ToLowerInvariant();
            if (extension != ".tif" && extension != ".tiff")
            {
                return originalTiffPath;
            }

            var sidecars = GetSidecarFiles(originalTiffPath);
            if (sidecars.Count == 0)
            {
                return originalTiffPath;
            }

            var cacheFile = GetCacheFilePath(originalTiffPath);

            if (IsCacheUpToDate(originalTiffPath, sidecars, cacheFile))
            {
                return cacheFile;
            }

            ConvertToGeoTiff(originalTiffPath, sidecars, cacheFile);

            return cacheFile;
        }

        private static List<string> GetSidecarFiles(string tiffPath)
        {
            var sidecars = new List<string>();
            var directory = Path.GetDirectoryName(tiffPath)!;
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(tiffPath);

            var extensions = new[] { ".tfw", ".prj", ".tif.aux.xml" };
            foreach (var ext in extensions)
            {
                var sidecarPath = Path.Combine(directory, fileNameWithoutExt + ext);
                if (File.Exists(sidecarPath))
                {
                    sidecars.Add(sidecarPath);
                }
            }

            return sidecars;
        }

        private static string GetCacheFilePath(string tiffPath)
        {
            var hash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(tiffPath.ToUpperInvariant()));
            var hashHex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return Path.Combine(CacheFolder, $"raster_{hashHex}.tif");
        }

        private static bool IsCacheUpToDate(string tiffPath, List<string> sidecars, string cacheFile)
        {
            if (!File.Exists(cacheFile))
            {
                return false;
            }

            var cacheTime = File.GetLastWriteTimeUtc(cacheFile);
            var tiffTime = File.GetLastWriteTimeUtc(tiffPath);

            if (tiffTime > cacheTime)
            {
                return false;
            }

            foreach (var sidecar in sidecars)
            {
                var sidecarTime = File.GetLastWriteTimeUtc(sidecar);
                if (sidecarTime > cacheTime)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ConvertToGeoTiff(string tiffPath, List<string> sidecars, string outputPath)
        {
            using var srcDataset = Gdal.Open(tiffPath, Access.GA_ReadOnly);
            if (srcDataset == null)
            {
                throw new IOException($"Could not open the source TIFF file: {tiffPath}");
            }

            int width = srcDataset.RasterXSize;
            int height = srcDataset.RasterYSize;
            int bands = srcDataset.RasterCount;

            using var firstBand = srcDataset.GetRasterBand(1);
            DataType dataType = firstBand.DataType;

            double[]? geoTransform = null;
            string? projectionWkt = null;

            var tfwPath = Path.Combine(Path.GetDirectoryName(tiffPath)!,
                Path.GetFileNameWithoutExtension(tiffPath) + ".tfw");
            if (File.Exists(tfwPath))
            {
                geoTransform = ReadWorldFile(tfwPath);
            }

            var prjPath = Path.Combine(Path.GetDirectoryName(tiffPath)!,
                Path.GetFileNameWithoutExtension(tiffPath) + ".prj");
            if (File.Exists(prjPath))
            {
                projectionWkt = File.ReadAllText(prjPath);
            }

            if (geoTransform == null || projectionWkt == null)
            {
                throw new IOException("Missing .tfw or .prj file required for georeference conversion.");
            }

            var driver = Gdal.GetDriverByName("GTiff");
            if (driver == null)
            {
                throw new IOException("GTiff driver not available.");
            }

            var options = new List<string> { "COMPRESS=LZW", "TILED=YES", "BIGTIFF=YES" };

            using var dstDataset = driver.Create(outputPath, width, height, bands, dataType, options.ToArray());
            if (dstDataset == null)
            {
                throw new IOException($"Could not create the destination TIFF file: {outputPath}");
            }

            dstDataset.SetGeoTransform(geoTransform);
            dstDataset.SetProjection(projectionWkt);

            for (int i = 1; i <= bands; i++)
            {
                using var srcBand = srcDataset.GetRasterBand(i);
                using var dstBand = dstDataset.GetRasterBand(i);

                dstBand.SetColorInterpretation(srcBand.GetColorInterpretation());
                double noDataValue;
                int hasNoData;
                srcBand.GetNoDataValue(out noDataValue, out hasNoData);
                if (hasNoData != 0)
                {
                    dstBand.SetNoDataValue(noDataValue);
                }

                int blockWidth;
                int blockHeight;
                srcBand.GetBlockSize(out blockWidth, out blockHeight);
                if (blockWidth <= 0)
                {
                    blockWidth = width;
                }

                if (blockHeight <= 0)
                {
                    blockHeight = height;
                }

                switch (dataType)
                {
                    case DataType.GDT_Byte:
                        CopyBandBlocks(srcBand, dstBand, width, height, blockWidth, blockHeight, new byte[blockWidth * blockHeight]);
                        break;
                    case DataType.GDT_Int16:
                        CopyBandBlocks(srcBand, dstBand, width, height, blockWidth, blockHeight, new short[blockWidth * blockHeight]);
                        break;
                    case DataType.GDT_Int32:
                        CopyBandBlocks(srcBand, dstBand, width, height, blockWidth, blockHeight, new int[blockWidth * blockHeight]);
                        break;
                    case DataType.GDT_Float32:
                        CopyBandBlocks(srcBand, dstBand, width, height, blockWidth, blockHeight, new float[blockWidth * blockHeight]);
                        break;
                    case DataType.GDT_Float64:
                        CopyBandBlocks(srcBand, dstBand, width, height, blockWidth, blockHeight, new double[blockWidth * blockHeight]);
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported GDAL data type: {dataType}");
                }
            }

            dstDataset.FlushCache();
        }

        private static double[]? ReadWorldFile(string tfwPath)
        {
            try
            {
                var lines = File.ReadAllLines(tfwPath);
                if (lines.Length < 6)
                {
                    return null;
                }

                var transform = new double[6];
                transform[0] = double.Parse(lines[0], CultureInfo.InvariantCulture);
                transform[1] = double.Parse(lines[1], CultureInfo.InvariantCulture);
                transform[2] = double.Parse(lines[2], CultureInfo.InvariantCulture);
                transform[3] = double.Parse(lines[3], CultureInfo.InvariantCulture);
                transform[4] = double.Parse(lines[4], CultureInfo.InvariantCulture);
                transform[5] = double.Parse(lines[5], CultureInfo.InvariantCulture);

                double pixelSizeX = transform[0];
                double pixelSizeY = transform[3];

                double adjX = transform[4] - 0.5 * pixelSizeX - 0.5 * transform[1];
                double adjY = transform[5] - 0.5 * pixelSizeY - 0.5 * transform[2];

                transform[0] = pixelSizeX;
                transform[1] = transform[1];
                transform[2] = transform[2];
                transform[3] = pixelSizeY;
                transform[4] = adjX;
                transform[5] = adjY;

                return transform;
            }
            catch
            {
                return null;
            }
        }

        private static void CopyBandBlocks<T>(Band srcBand, Band dstBand, int width, int height, int blockWidth, int blockHeight, T[] buffer)
            where T : struct
        {
            for (int blockY = 0; blockY < height; blockY += blockHeight)
            {
                int currentBlockHeight = Math.Min(blockHeight, height - blockY);
                for (int blockX = 0; blockX < width; blockX += blockWidth)
                {
                    int currentBlockWidth = Math.Min(blockWidth, width - blockX);
                    var err = ReadBlock(srcBand, blockX, blockY, currentBlockWidth, currentBlockHeight, buffer);
                    if (err != CPLErr.CE_None)
                    {
                        throw new IOException($"Error reading raster block at ({blockX},{blockY})");
                    }

                    err = WriteBlock(dstBand, blockX, blockY, currentBlockWidth, currentBlockHeight, buffer);
                    if (err != CPLErr.CE_None)
                    {
                        throw new IOException($"Error writing raster block at ({blockX},{blockY})");
                    }
                }
            }
        }

        private static CPLErr ReadBlock<T>(Band band, int x, int y, int xSize, int ySize, T[] buffer)
            where T : struct
        {
            if (buffer is byte[] byteBuffer)
            {
                return band.ReadRaster(x, y, xSize, ySize, byteBuffer, xSize, ySize, 0, 0);
            }

            if (buffer is short[] shortBuffer)
            {
                return band.ReadRaster(x, y, xSize, ySize, shortBuffer, xSize, ySize, 0, 0);
            }

            if (buffer is int[] intBuffer)
            {
                return band.ReadRaster(x, y, xSize, ySize, intBuffer, xSize, ySize, 0, 0);
            }

            if (buffer is float[] floatBuffer)
            {
                return band.ReadRaster(x, y, xSize, ySize, floatBuffer, xSize, ySize, 0, 0);
            }

            if (buffer is double[] doubleBuffer)
            {
                return band.ReadRaster(x, y, xSize, ySize, doubleBuffer, xSize, ySize, 0, 0);
            }

            throw new NotSupportedException($"Unsupported buffer type: {typeof(T)}");
        }

        private static CPLErr WriteBlock<T>(Band band, int x, int y, int xSize, int ySize, T[] buffer)
            where T : struct
        {
            if (buffer is byte[] byteBuffer)
            {
                return band.WriteRaster(x, y, xSize, ySize, byteBuffer, xSize, ySize, 0, 0);
            }

            if (buffer is short[] shortBuffer)
            {
                return band.WriteRaster(x, y, xSize, ySize, shortBuffer, xSize, ySize, 0, 0);
            }

            if (buffer is int[] intBuffer)
            {
                return band.WriteRaster(x, y, xSize, ySize, intBuffer, xSize, ySize, 0, 0);
            }

            if (buffer is float[] floatBuffer)
            {
                return band.WriteRaster(x, y, xSize, ySize, floatBuffer, xSize, ySize, 0, 0);
            }

            if (buffer is double[] doubleBuffer)
            {
                return band.WriteRaster(x, y, xSize, ySize, doubleBuffer, xSize, ySize, 0, 0);
            }

            throw new NotSupportedException($"Unsupported buffer type: {typeof(T)}");
        }
    }
}
