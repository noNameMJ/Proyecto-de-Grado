using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

namespace Geomatica.Desktop.Services
{
    public static class RasterDiagnostics
    {
        private static readonly object Gate = new();
        private static readonly string LogPath = Path.Combine(
            AppContext.BaseDirectory,
            "raster-diagnostics.log");

        public static void Log(string message)
        {
            var line = $"{DateTimeOffset.Now:O} [T{Thread.CurrentThread.ManagedThreadId}] {message}";
            Debug.WriteLine(line);
            lock (Gate)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }

        public static void LogException(string context, Exception? ex)
        {
            if (ex == null)
            {
                Log($"{context}: <no exception>");
                return;
            }

            Log($"{context}: {ex}");
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
            Log($"{context}: layer={layerName ?? "<unknown>"}; status={status ?? "<unknown>"}; error={(error?.ToString() ?? "<null>")}");
        }
    }
}
