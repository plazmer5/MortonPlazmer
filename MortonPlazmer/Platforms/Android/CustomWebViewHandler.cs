using Android.App;
using Android.Webkit;
using Microsoft.Maui.Handlers;
using MortonPlazmer.Controls;
using WebView = Android.Webkit.WebView;

namespace MortonPlazmer.Platforms.Android
{
    public class CustomWebViewHandler000 : WebViewHandler
    {
        private UniversalDownloadListener? _downloadListener;

        protected override void ConnectHandler(WebView platformView)
        {
            base.ConnectHandler(platformView);

            var settings = platformView.Settings;
            settings.JavaScriptEnabled = true;
            settings.DomStorageEnabled = true;
            settings.AllowFileAccess = true;
            settings.AllowContentAccess = true;
            settings.UseWideViewPort = true;
            settings.LoadWithOverviewMode = true;
            settings.BuiltInZoomControls = true;

            // Передаем Activity напрямую из Context
            var activity = platformView.Context as Activity;
            if (activity != null)
            {
                _downloadListener = new UniversalDownloadListener(activity);
                platformView.SetDownloadListener(_downloadListener);
            }
        }

        public UniversalDownloadListener? GetDownloadListener() => _downloadListener;
    }
}
