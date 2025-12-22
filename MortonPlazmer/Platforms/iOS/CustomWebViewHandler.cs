using Microsoft.Maui.Handlers;
using WebKit;
using UIKit;
using Foundation;
using CoreGraphics;
using System;
using System.IO;
using System.Net.Http;

namespace MortonPlazmer.Platforms.iOS
{
    // =====================================================
    // MAUI WebView Handler (ТОЛЬКО создание WKWebView)
    // =====================================================
    public class CustomWebViewHandler : WebViewHandler
    {
        protected override WKWebView CreatePlatformView()
        {
            var pagePreferences = new WKWebpagePreferences
            {
                AllowsContentJavaScript = true
            };

            var config = new WKWebViewConfiguration
            {
                DefaultWebpagePreferences = pagePreferences,
                AllowsInlineMediaPlayback = true,
                WebsiteDataStore = WKWebsiteDataStore.DefaultDataStore,
                UserContentController = new WKUserContentController()
            };

            return new WKWebView(CGRect.Empty, config);
        }

        protected override void ConnectHandler(WKWebView nativeView)
        {
            base.ConnectHandler(nativeView);

            nativeView.ScrollView.BouncesZoom = true;
            nativeView.ScrollView.MaximumZoomScale = 5f;
            nativeView.ScrollView.MinimumZoomScale = 1f;
            nativeView.ScrollView.PinchGestureRecognizer.Enabled = true;

            nativeView.AllowsBackForwardNavigationGestures = true;

            nativeView.NavigationDelegate =
                new IOSWebViewNavigationDelegate(nativeView);

            nativeView.UIDelegate = new WKUIDelegate();
        }
    }

    // =====================================================
    // Navigation + Download logic
    // =====================================================
    internal class IOSWebViewNavigationDelegate : WKNavigationDelegate
    {
        private readonly WKWebView _webView;

        private readonly string[] _downloadableExt =
        {
            ".pdf", ".zip", ".apk", ".doc", ".docx", ".xls", ".xlsx"
        };

        public IOSWebViewNavigationDelegate(WKWebView webView)
        {
            _webView = webView;
        }

        // -------------------------------------------------
        // Navigation decision
        // -------------------------------------------------
        public override void DecidePolicy(
            WKWebView webView,
            WKNavigationAction navigationAction,
            Action<WKNavigationActionPolicy> decisionHandler)
        {
            var url = navigationAction?.Request?.Url?.AbsoluteString;

            if (string.IsNullOrEmpty(url))
            {
                decisionHandler(WKNavigationActionPolicy.Allow);
                return;
            }

            bool isBlob =
                url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase);

            bool isFile =
                IsDownloadable(url);

            if (isBlob || isFile)
            {
                ShowDownloadDialog(url, () =>
                {
                    if (isBlob)
                        HandleBlob(url);
                    else
                        DownloadFile(url);
                });

                decisionHandler(WKNavigationActionPolicy.Cancel);
                return;
            }

            decisionHandler(WKNavigationActionPolicy.Allow);
        }

        private bool IsDownloadable(string url)
        {
            foreach (var ext in _downloadableExt)
                if (url.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // -------------------------------------------------
        // Dialog
        // -------------------------------------------------
        private void ShowDownloadDialog(string url, Action confirmed)
        {
            string fileName = Path.GetFileName(url) ?? "file";

            var alert = UIAlertController.Create(
                "Скачать файл?",
                $"Имя: {fileName}",
                UIAlertControllerStyle.Alert);

            alert.AddAction(
                UIAlertAction.Create(
                    "Отмена",
                    UIAlertActionStyle.Cancel,
                    null));

            alert.AddAction(
                UIAlertAction.Create(
                    "Скачать",
                    UIAlertActionStyle.Default,
                    _ => confirmed?.Invoke()));

            UIApplication.SharedApplication
                .KeyWindow?
                .RootViewController?
                .InvokeOnMainThread(() =>
                {
                    UIApplication.SharedApplication
                        .KeyWindow
                        .RootViewController
                        .PresentViewController(alert, true, null);
                });
        }

        // -------------------------------------------------
        // Direct download (URL)
        // -------------------------------------------------
        private async void DownloadFile(string url)
        {
            try
            {
                using var client = new HttpClient();
                var bytes = await client.GetByteArrayAsync(url);

                string name =
                    Path.GetFileName(url) ??
                    $"file_{DateTime.Now:yyyyMMdd_HHmmss}";

                SaveAndPreview(
                    bytes,
                    name,
                    GetMime(name));
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        // -------------------------------------------------
        // Blob handling
        // -------------------------------------------------
        private async void HandleBlob(string blobUrl)
        {
            try
            {
                string js = $@"
                    (async function(){{
                        const r = await fetch('{blobUrl}');
                        const b = await r.blob();
                        const fr = new FileReader();
                        return await new Promise(res => {{
                            fr.onloadend = () => res(fr.result);
                            fr.readAsDataURL(b);
                        }});
                    }})();
                ";

                var result =
                    await _webView.EvaluateJavaScriptAsync(js);

                if (result is not NSString ns)
                {
                    ShowError("Ошибка чтения blob");
                    return;
                }

                string data = ns.ToString();
                int comma = data.IndexOf(',');

                if (comma < 0)
                {
                    ShowError("Некорректные blob данные");
                    return;
                }

                string base64 = data[(comma + 1)..];
                byte[] bytes = Convert.FromBase64String(base64);

                string mime = ExtractMime(data);
                string ext = GetExtension(mime);

                SaveAndPreview(
                    bytes,
                    $"blob_{DateTime.Now:yyyyMMdd_HHmmss}{ext}",
                    mime);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        // -------------------------------------------------
        // Save + preview
        // -------------------------------------------------
        private void SaveAndPreview(byte[] bytes, string name, string mime)
        {
            try
            {
                string dir =
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.MyDocuments),
                        "..", "Library", "Downloads");

                Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, name);
                File.WriteAllBytes(path, bytes);

                var url = NSUrl.FromFilename(path);
                var controller =
                    UIDocumentInteractionController.FromUrl(url);

                controller.Uti = mime;
                controller.PresentPreview(true);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        // -------------------------------------------------
        // Helpers
        // -------------------------------------------------
        private string ExtractMime(string dataUrl)
        {
            if (dataUrl.StartsWith("data:"))
            {
                int semi = dataUrl.IndexOf(';');
                if (semi > 5)
                    return dataUrl[5..semi];
            }
            return "application/octet-stream";
        }

        private string GetExtension(string mime) =>
            mime switch
            {
                "application/pdf" => ".pdf",
                "application/zip" => ".zip",
                "application/msword" => ".doc",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
                "application/vnd.ms-excel" => ".xls",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
                _ => ".bin"
            };

        private string GetMime(string file) =>
            Path.GetExtension(file).ToLower() switch
            {
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream"
            };

        private void ShowError(string msg)
        {
            var alert =
                UIAlertController.Create(
                    "Ошибка",
                    msg ?? "Неизвестная ошибка",
                    UIAlertControllerStyle.Alert);

            alert.AddAction(
                UIAlertAction.Create(
                    "OK",
                    UIAlertActionStyle.Default,
                    null));

            UIApplication.SharedApplication
                .KeyWindow?
                .RootViewController?
                .PresentViewController(alert, true, null);
        }
    }
}
