using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Geomatica.Desktop.Models;

namespace Geomatica.Desktop.Services
{
    public interface INotificationService
    {
        ObservableCollection<ToastNotificationItem> Notifications { get; }
        void Show(string mensaje, string? titulo = null, ToastType tipo = ToastType.Info, int durationSeconds = 4);
        void ShowSuccess(string mensaje, string? titulo = null, int durationSeconds = 4);
        void ShowInfo(string mensaje, string? titulo = null, int durationSeconds = 4);
        void ShowWarning(string mensaje, string? titulo = null, int durationSeconds = 5);
        void ShowError(string mensaje, string? titulo = null, int durationSeconds = 6);
        void Remove(Guid id);
        void Clear();
    }

    public class NotificationService : INotificationService
    {
        public ObservableCollection<ToastNotificationItem> Notifications { get; } = new();

        private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0, 102, 51));      // UIS Green
        private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(25, 118, 210));       // Info Blue
        private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(212, 160, 23));    // UIS Gold
        private static readonly SolidColorBrush DangerBrush = new(Color.FromRgb(211, 47, 47));      // Red

        static NotificationService()
        {
            SuccessBrush.Freeze();
            InfoBrush.Freeze();
            WarningBrush.Freeze();
            DangerBrush.Freeze();
        }

        public void Show(string mensaje, string? titulo = null, ToastType tipo = ToastType.Info, int durationSeconds = 4)
        {
            if (Application.Current == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                var (icono, defaultTitulo, brush) = tipo switch
                {
                    ToastType.Success => ("✓", "Operación exitosa", SuccessBrush),
                    ToastType.Info => ("ℹ", "Información", InfoBrush),
                    ToastType.Warning => ("⚠", "Atención", WarningBrush),
                    ToastType.Error => ("✕", "Error", DangerBrush),
                    _ => ("ℹ", "Notificación", InfoBrush)
                };

                var item = new ToastNotificationItem
                {
                    Mensaje = mensaje,
                    Titulo = string.IsNullOrWhiteSpace(titulo) ? defaultTitulo : titulo,
                    Tipo = tipo,
                    Icono = icono,
                    AccentBrush = brush
                };

                item.CerrarCommand = new RelayCommand(() => Remove(item.Id));

                // Máximo 5 notificaciones simultáneas para no saturar la pantalla
                if (Notifications.Count >= 5)
                {
                    Notifications.RemoveAt(0);
                }

                Notifications.Add(item);

                // Auto-cierre
                if (durationSeconds > 0)
                {
                    var timer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(durationSeconds)
                    };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        Remove(item.Id);
                    };
                    timer.Start();
                }
            });
        }

        public void ShowSuccess(string mensaje, string? titulo = null, int durationSeconds = 4)
            => Show(mensaje, titulo, ToastType.Success, durationSeconds);

        public void ShowInfo(string mensaje, string? titulo = null, int durationSeconds = 4)
            => Show(mensaje, titulo, ToastType.Info, durationSeconds);

        public void ShowWarning(string mensaje, string? titulo = null, int durationSeconds = 5)
            => Show(mensaje, titulo, ToastType.Warning, durationSeconds);

        public void ShowError(string mensaje, string? titulo = null, int durationSeconds = 6)
            => Show(mensaje, titulo, ToastType.Error, durationSeconds);

        public void Remove(Guid id)
        {
            if (Application.Current == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = Notifications.FirstOrDefault(n => n.Id == id);
                if (item != null)
                {
                    Notifications.Remove(item);
                }
            });
        }

        public void Clear()
        {
            if (Application.Current == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                Notifications.Clear();
            });
        }
    }
}
