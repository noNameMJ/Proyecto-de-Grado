using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Esri.ArcGISRuntime.Security;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Geomatica.Desktop
{
    public class OAuthAuthorizeHandler : IOAuthHandler
    {
        private Window? _authWindow;
        private WebView2? _webBrowser; // Almacenado a nivel de clase para poder hacer Dispose
        private TaskCompletionSource<IDictionary<string, string>>? _tcs;
        private Uri? _redirectUrl;

        public Task<IDictionary<string, string>> LoginAsync(OAuthLoginParameters parameters)
        {
            if (_tcs != null && !_tcs.Task.IsCompleted)
                throw new Exception("Task in progress");

            _tcs = new TaskCompletionSource<IDictionary<string, string>>();
            _redirectUrl = parameters.RedirectUri;

            Dispatcher dispatcher = Application.Current.Dispatcher;
            if (dispatcher.CheckAccess())
            {
                AuthorizeOnUIThread(parameters.AuthorizeUri);
            }
            else
            {
                dispatcher.BeginInvoke(new Action(() => AuthorizeOnUIThread(parameters.AuthorizeUri)));
            }

            return _tcs.Task;
        }

        private void AuthorizeOnUIThread(Uri authorizeUri)
        {
            _webBrowser = new WebView2() { MinWidth = 500, MinHeight = 500 };
            _webBrowser.NavigationStarting += WebBrowserOnNavigationStarting;

            _authWindow = new Window
            {
                Content = _webBrowser,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = "Iniciar sesión en ArcGIS"
            };

            if (Application.Current != null && Application.Current.MainWindow != null)
            {
                _authWindow.Owner = Application.Current.MainWindow;
            }

            _authWindow.Loaded += async (s, e) =>
            {
                // MEJORA 1: Configurar un entorno seguro para la caché de WebView2 en AppData
                string userDataFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GeomaticaDesktop",
                    "WebView2Cache");

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await _webBrowser.EnsureCoreWebView2Async(env);

                _webBrowser.CoreWebView2.Navigate(authorizeUri.AbsoluteUri);
            };

            _authWindow.Closed += OnWindowClosed;
            _authWindow.ShowDialog();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (_authWindow != null && _authWindow.Owner != null)
            {
                _authWindow.Owner.Focus();
            }

            // MEJORA 3: Usar TrySetCanceled para evitar excepciones de condición de carrera
            _tcs?.TrySetCanceled();

            // MEJORA 2: Liberar recursos de Edge explícitamente
            if (_webBrowser != null)
            {
                _webBrowser.NavigationStarting -= WebBrowserOnNavigationStarting;
                _webBrowser.Dispose();
                _webBrowser = null;
            }

            _authWindow = null;
        }

        private void WebBrowserOnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            const string portalApprovalMarker = "/oauth2/approval";
            Uri uri = new Uri(e.Uri);

            if (uri == null || string.IsNullOrEmpty(uri.AbsoluteUri) || _redirectUrl == null)
                return;

            // MEJORA 4: StartsWith es más robusto que IsBaseOf para Custom URI Schemes (ej. my-geomatica-app://)
            bool isRedirected = uri.AbsoluteUri.StartsWith(_redirectUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase) ||
                (_redirectUrl.AbsoluteUri.Contains(portalApprovalMarker) && uri.AbsoluteUri.Contains(portalApprovalMarker));

            if (isRedirected)
            {
                e.Cancel = true; // Detiene la navegación en el WebView2

                IDictionary<string, string> authResponse = DecodeParameters(uri);

                // MEJORA 5: Validar si Esri devolvió un error (ej. el usuario canceló en la UI web)
                if (authResponse.ContainsKey("error"))
                {
                    _tcs?.TrySetException(new Exception($"OAuth Error: {authResponse["error"]}"));
                }
                else
                {
                    _tcs?.TrySetResult(authResponse);
                }

                _authWindow?.Close();
            }
        }

        private static IDictionary<string, string> DecodeParameters(Uri uri)
        {
            string answer = "";

            if (!string.IsNullOrEmpty(uri.Fragment))
            {
                answer = uri.Fragment.Substring(1);
            }
            else if (!string.IsNullOrEmpty(uri.Query))
            {
                answer = uri.Query.Substring(1);
            }

            Dictionary<string, string> keyValueDictionary = new Dictionary<string, string>();
            string[] keysAndValues = answer.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string kvString in keysAndValues)
            {
                string[] pair = kvString.Split('=');
                string key = pair[0];
                string value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
                keyValueDictionary.Add(key, value);
            }

            return keyValueDictionary;
        }
    }
}