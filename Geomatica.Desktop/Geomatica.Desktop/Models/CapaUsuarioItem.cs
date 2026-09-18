using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Rasters;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;

namespace Geomatica.Desktop.Models
{
    public partial class CapaUsuarioItem : ObservableObject, IDisposable
    {
        private bool _disposed;

        public string Nombre { get; set; } = "";
        public string RutaCompleta { get; set; } = "";
        public string TipoIcono { get; set; } = "🗺️";
        public string TipoTexto { get; set; } = "Capa Ráster";
        public Layer? Capa { get; set; }
        public Envelope? ExtentParaZoom { get; set; }

        /// <summary>
        /// Referencia al contenedor GeoPackage en caso de que la capa provenga de un archivo .gpkg.
        /// Permite invocar Close() explícitamente al eliminar la capa para liberar los descriptores del archivo SQLite.
        /// </summary>
        public GeoPackage? ContenedorGeoPackage { get; set; }

        // Elementos y propiedades 3D propios de esta capa (para nubes LAS/LAZ y SLPK)
        public GraphicsOverlay? OverlayPuntos3D { get; set; }
        public GraphicsOverlay? OverlayGuia3D { get; set; }
        public List<(MapPoint PtWgs84, System.Drawing.Color Color)>? PuntosMuestreados3D { get; set; }
        public double CentroZ { get; set; }
        public double RadioMetros { get; set; } = 150.0;
        public string CrsNombre { get; set; } = "";
        public string InfoDetalle3D { get; set; } = "";

        [ObservableProperty] private double offsetZ3D = 0.0;
        [ObservableProperty] private double tamanoPunto3D = 4.5;

        [ObservableProperty]
        private bool isVisible = true;

        public Action<bool>? OnVisibilityChangedAction { get; set; }
        public Action<double>? OnOpacityChangedAction { get; set; }

        partial void OnIsVisibleChanged(bool value)
        {
            if (Capa != null)
            {
                Capa.IsVisible = value;
            }
            if (OverlayPuntos3D != null)
            {
                OverlayPuntos3D.IsVisible = value;
            }
            if (OverlayGuia3D != null)
            {
                OverlayGuia3D.IsVisible = value;
            }
            OnVisibilityChangedAction?.Invoke(value);
        }

        [ObservableProperty]
        private double opacidad = 1.0;

        partial void OnOpacidadChanged(double value)
        {
            if (Capa != null)
            {
                Capa.Opacity = value;
            }
            if (OverlayPuntos3D != null)
            {
                OverlayPuntos3D.Opacity = value;
            }
            if (OverlayGuia3D != null)
            {
                OverlayGuia3D.Opacity = value;
            }
            OnOpacityChangedAction?.Invoke(value);
        }

        public void ReconstruirPuntos()
        {
            if (OverlayPuntos3D == null || PuntosMuestreados3D == null || PuntosMuestreados3D.Count == 0) return;

            OverlayPuntos3D.Graphics.Clear();
            var graphics = new List<Graphic>(PuntosMuestreados3D.Count);
            double tamano = TamanoPunto3D;
            double offset = OffsetZ3D;

            foreach (var item in PuntosMuestreados3D)
            {
                var basePt = item.PtWgs84;
                var ptConOffset = (offset != 0.0)
                    ? new MapPoint(basePt.X, basePt.Y, basePt.Z + offset, SpatialReferences.Wgs84)
                    : basePt;

                var symbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, item.Color, tamano);
                graphics.Add(new Graphic(ptConOffset, symbol));
            }

            OverlayPuntos3D.Graphics.AddRange(graphics);
        }

        public IRelayCommand? QuitarCommand { get; set; }
        public IRelayCommand? ZoomCommand { get; set; }

        /// <summary>
        /// Libera explícitamente recursos gráficos, colecciones en memoria, buffers 3D y conexiones de archivo.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 1. Limpieza de overlays y colecciones gráficas 3D
                if (OverlayPuntos3D != null)
                {
                    OverlayPuntos3D.Graphics.Clear();
                    OverlayPuntos3D = null;
                }
                if (OverlayGuia3D != null)
                {
                    OverlayGuia3D.Graphics.Clear();
                    OverlayGuia3D = null;
                }

                // 2. Liberar buffers de puntos 3D muestreados (hasta 75,000 tuplas en memoria)
                if (PuntosMuestreados3D != null)
                {
                    PuntosMuestreados3D.Clear();
                    PuntosMuestreados3D = null;
                }

                // 3. Cerrar contenedor GeoPackage para liberar archivo SQLite en disco
                try
                {
                    ContenedorGeoPackage?.Close();
                    ContenedorGeoPackage = null;
                }
                catch { }

                // 4. Liberar capa y rásteres asociados si implementan IDisposable
                try
                {
                    if (Capa is RasterLayer rl && rl.Raster is IDisposable dispRaster)
                    {
                        dispRaster.Dispose();
                    }
                    if (Capa is FeatureLayer fl && fl.FeatureTable is IDisposable dispTable)
                    {
                        dispTable.Dispose();
                    }
                    if (Capa is IDisposable dispLayer)
                    {
                        dispLayer.Dispose();
                    }
                }
                catch { }
                Capa = null;

                // 5. Desvincular eventos y callbacks
                OnVisibilityChangedAction = null;
                OnOpacityChangedAction = null;
                QuitarCommand = null;
                ZoomCommand = null;
            }

            _disposed = true;
        }
    }
}

