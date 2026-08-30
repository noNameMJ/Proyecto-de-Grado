using System;
using System.Windows.Input;
using System.Windows.Media;

namespace Geomatica.Desktop.Models
{
    public enum ToastType
    {
        Success,
        Info,
        Warning,
        Error
    }

    public class ToastNotificationItem
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Titulo { get; set; } = string.Empty;
        public string Mensaje { get; set; } = string.Empty;
        public ToastType Tipo { get; set; } = ToastType.Info;
        public string Icono { get; set; } = "ℹ️";
        public Brush AccentBrush { get; set; } = Brushes.DodgerBlue;
        public Brush BackgroundBrush { get; set; } = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        public ICommand? CerrarCommand { get; set; }
        public DateTime Timestamp { get; } = DateTime.Now;
    }
}
