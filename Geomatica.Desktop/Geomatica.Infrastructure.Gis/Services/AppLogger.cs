using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Geomatica.Desktop.Services
{
    public static class AppLogger
    {
        private static readonly string LogDirectory;
        private static readonly Channel<string> LogChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        private static readonly Task BackgroundWriterTask;
        private static readonly CancellationTokenSource Cts = new();

        static AppLogger()
        {
            try
            {
                LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }
            }
            catch
            {
                LogDirectory = Path.GetTempPath();
            }

            BackgroundWriterTask = Task.Run(ProcessLogQueueAsync);
            Info("=== Iniciando aplicación Geomática UIS Desktop ===");
        }

        private static async Task ProcessLogQueueAsync()
        {
            var reader = LogChannel.Reader;
            while (await reader.WaitToReadAsync(Cts.Token))
            {
                while (reader.TryRead(out var logMessage))
                {
                    try
                    {
                        var filePath = Path.Combine(LogDirectory, $"geomatica-{DateTime.Now:yyyyMMdd}.log");
                        await File.AppendAllTextAsync(filePath, logMessage + Environment.NewLine, Encoding.UTF8);
                    }
                    catch
                    {
                        // Fallback silencioso para no interferir con la aplicación
                    }
                }
            }
        }

        private static void Write(string level, string message, Exception? ex = null)
        {
            var threadId = Environment.CurrentManagedThreadId;
            var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var sb = new StringBuilder();
            sb.Append($"[{time}] [{threadId:D3}] [{level}] {message}");

            if (ex != null)
            {
                sb.AppendLine();
                sb.Append($"  Excepción: {ex.GetType().FullName}: {ex.Message}");
                sb.AppendLine();
                sb.Append($"  StackTrace: {ex.StackTrace}");
            }

            var entry = sb.ToString();
            System.Diagnostics.Debug.WriteLine(entry);
            LogChannel.Writer.TryWrite(entry);
        }

        public static void Info(string message) => Write("INFO ", message);
        public static void Warn(string message, Exception? ex = null) => Write("WARN ", message, ex);
        public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);
        public static void Debug(string message) => Write("DEBUG", message);
    }
}
