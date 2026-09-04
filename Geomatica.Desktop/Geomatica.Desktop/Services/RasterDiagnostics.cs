using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Geomatica.Desktop.Services
{
    public static class RasterDiagnostics
    {
        public static void Log(string message)
        {
            AppLogger.Info($"[Raster] {message}");
        }

        public static void LogException(string context, Exception? ex)
        {
            if (ex == null)
            {
                AppLogger.Info($"[Raster] {context}: <no exception>");
                return;
            }

            AppLogger.Error($"[Raster] {context}", ex);
        }

        public static void LogDispatcher(string context)
        {
            var app = Application.Current;
            var hasApp = app != null;
            var checkAccess = app?.Dispatcher.CheckAccess() == true;
            Log($"{context}: hasApplication={hasApp}; dispatcherCheckAccess={checkAccess}");
        }

        public static void LogFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                Log($"File path={path}; exists={info.Exists}; bytes={(info.Exists ? info.Length : 0)}; lastWriteUtc={(info.Exists ? info.LastWriteTimeUtc : null)}");

                if (!info.Exists) return;

                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    Log("File lock probe: opened Read/FileShare.ReadWrite successfully");
                }
                catch (IOException ex)
                {
                    LogException("File lock probe warning", ex);
                }
                catch (Exception ex)
                {
                    LogException("File lock probe failed", ex);
                }
            }
            catch (Exception ex)
            {
                LogException("File diagnostics failed", ex);
            }
        }

        public static void LogRasterMetadata(
            string path,
            long bytes,
            string rasterStatus,
            string layerStatus,
            string? rasterExtent,
            string? layerExtent,
            string? rasterSpatialReference,
            string? layerSpatialReference,
            string? rasterSpatialReferenceId,
            string? layerSpatialReferenceId)
        {
            Log("Raster metadata " +
                $"path={path}; bytes={bytes}; rasterStatus={rasterStatus}; layerStatus={layerStatus}; " +
                $"rasterExtent={rasterExtent ?? "<null>"}; layerExtent={layerExtent ?? "<null>"}; " +
                $"rasterSR={rasterSpatialReference ?? "<null>"}; layerSR={layerSpatialReference ?? "<null>"}; " +
                $"rasterSRId={rasterSpatialReferenceId ?? "<null>"}; layerSRId={layerSpatialReferenceId ?? "<null>"}");
        }

        public static void LogArcGisLayerError(string context, string? layerName, string? status, Exception? error)
        {
            if (error != null)
            {
                AppLogger.Error($"[Raster Layer Error] {context}: layer={layerName ?? "<unknown>"}; status={status ?? "<unknown>"}", error);
            }
            else
            {
                Log($"{context}: layer={layerName ?? "<unknown>"}; status={status ?? "<unknown>"}; error=<null>");
            }
        }

        public static void LogPix4DProduct(string path, string? productType, IReadOnlyList<string> sidecars)
        {
            var isPix4D = !string.IsNullOrWhiteSpace(path)
                && (path.Contains("pix4d", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("orthomosaic", StringComparison.OrdinalIgnoreCase)
                    || sidecars.Any(s => s.EndsWith(".pox", StringComparison.OrdinalIgnoreCase)
                                      || s.EndsWith(".points", StringComparison.OrdinalIgnoreCase)));

            Log($"Pix4D product hint path={path}; detected={isPix4D}; productType={productType ?? "<unknown>"}; sidecars={string.Join(",", sidecars.Select(Path.GetFileName))}");
        }

        public static void LogRasterInfo(string path, string? rasterInfoName, string? extent, string? spatialReference)
        {
            Log($"RasterInfo path={path}; name={rasterInfoName ?? "<unknown>"}; extent={extent ?? "<null>"}; spatialReference={spatialReference ?? "<null>"}");
        }
    }
}
