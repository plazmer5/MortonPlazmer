using WebKit;
using UIKit;
using Foundation;
using System;
using System.IO;
using System.Net.Http;

namespace MortonPlazmer.Platforms.iOS
{
    internal class IOSWebViewNavigationDelegate : WKNavigationDelegate
    {
        private readonly WKWebView _webView;

        private readonly string[] _downloadableExt =
        {
            ".pdf", ".zip", ".apk", ".doc", ".docx", ".xls", ".xlsx"
        };

        public IOSWebViewNavigationDelegate()
        {
        }

        public IOSWebViewNavigationDelegate(WKWebView webView)
        {
            _webView = webView;
        }

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

            bool isBlob = url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase);
            bool isFile = IsDownloadable(url);

            if (isBlob || isFile)
            {
                //ShowDownloadDialog(url, () =>
                //{
                //if (isBlob)
                //HandleBlob(url);
                //    else
                //DownloadFile(url);
                //});

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

        // остальной код (DownloadFile, HandleBlob, SaveAndPreview, helpers)
        // ОСТАЁТСЯ БЕЗ ИЗМЕНЕНИЙ
    }
}
