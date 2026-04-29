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
    public class OAuthAuthorizeHandler : IOAuthAuthorizeHandler
    {
        private Window? _authWindow;
         private TaskCompletionSource<IDictionary<string, string>>? _tcs;
         private Uri? _redirectUrl;
         private Uri? _authorizeUrl;

         public Task<IDictionary<string, string>> AuthorizeAsync(Uri serviceUri, Uri authorizeUri, Uri callbackUri)
         {
             if (_tcs != null && !_tcs.Task.IsCompleted)
                 throw new Exception("Task in progress");

             _tcs = new TaskCompletionSource<IDictionary<string, string>>();

             _authorizeUrl = authorizeUri;
             _redirectUrl = callbackUri;

             Dispatcher dispatcher = Application.Current.Dispatcher;
             if (dispatcher.CheckAccess())
             {
                 AuthorizeOnUIThread(_authorizeUrl);
             }
             else
             {
                 dispatcher.BeginInvoke(new Action(() => AuthorizeOnUIThread(_authorizeUrl)));
             }

             return _tcs.Task;
         }

         private void AuthorizeOnUIThread(Uri authorizeUri)
         {
             WebView2 webBrowser = new WebView2() { MinWidth = 500, MinHeight = 500 };
             webBrowser.NavigationStarting += WebBrowserOnNavigationStarting;

             _authWindow = new Window
             {
                 Content = webBrowser,
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
                 await webBrowser.EnsureCoreWebView2Async();
                 webBrowser.CoreWebView2.Navigate(authorizeUri.AbsoluteUri);
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

             if (_tcs != null && !_tcs.Task.IsCompleted)
             {
                 _tcs.SetCanceled();
             }

             _authWindow = null;
         }

         private void WebBrowserOnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
         {
             const string portalApprovalMarker = "/oauth2/approval";

             Uri uri = new Uri(e.Uri);

             if (sender == null || uri == null || string.IsNullOrEmpty(uri.AbsoluteUri) || _redirectUrl == null)
                 return;

             bool isRedirected = _redirectUrl.IsBaseOf(uri) ||
                 (_redirectUrl.AbsoluteUri.Contains(portalApprovalMarker) && uri.AbsoluteUri.Contains(portalApprovalMarker));

             if (isRedirected)
             {
                 e.Cancel = true;
                 IDictionary<string, string> authResponse = DecodeParameters(uri);

                 _tcs?.SetResult(authResponse);

                 if (_authWindow != null)
                 {
                     _authWindow.Close();
                 }
             }
         }

         private static IDictionary<string, string> DecodeParameters(Uri uri)
         {
             string answer = "";

             if (!string.IsNullOrEmpty(uri.Fragment))
             {
                 answer = uri.Fragment.Substring(1);
             }
             else
             {
                 if (!string.IsNullOrEmpty(uri.Query))
                 {
                     answer = uri.Query.Substring(1);
                 }
             }

             Dictionary<string, string> keyValueDictionary = new Dictionary<string, string>();
             string[] keysAndValues = answer.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
             foreach (string kvString in keysAndValues)
             {
                 string[] pair = kvString.Split('=');
                 string key = pair[0];
                 string value = string.Empty;
                 if (pair.Length > 1)
                 {
                     value = Uri.UnescapeDataString(pair[1]);
                 }

                 keyValueDictionary.Add(key, value);
             }

             return keyValueDictionary;
         }
    }
}