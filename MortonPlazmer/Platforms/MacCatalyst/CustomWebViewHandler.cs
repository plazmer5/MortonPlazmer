#nullable disable

using WebKit;
using Microsoft.Maui.Handlers;
using MortonPlazmer.Controls;
using Foundation;
using CoreGraphics;

namespace MortonPlazmer.Platforms.MacCatalyst
{
    public class CustomWebViewHandler : WebViewHandler
    {
        protected override WKWebView CreatePlatformView()
        {
            // Используем WKWebPagePreferences вместо устаревших WKPreferences
            var pagePreferences = new WKWebpagePreferences
            {
                AllowsContentJavaScript = true // включаем JS безопасным современным способом
            };

            var config = new WKWebViewConfiguration
            {
                DefaultWebpagePreferences = pagePreferences,
                AllowsInlineMediaPlayback = true,
                WebsiteDataStore = WKWebsiteDataStore.DefaultDataStore,
                UserContentController = new WKUserContentController()
            };

            // Добавление viewport meta через JS
            var js = @"
                var meta = document.createElement('meta');
                meta.name = 'viewport';
                meta.content = 'width=device-width, initial-scale=1.0, maximum-scale=5.0, user-scalable=yes';
                document.getElementsByTagName('head')[0].appendChild(meta);
            ";
            var userScript = new WKUserScript(new NSString(js), WKUserScriptInjectionTime.AtDocumentEnd, true);
            config.UserContentController.AddUserScript(userScript);

            return new WKWebView(CGRect.Empty, config);
        }

        protected override void ConnectHandler(WKWebView platformView)
        {
            base.ConnectHandler(platformView);

            // Настройка масштабирования (аналог UseWideViewPort)
            platformView.ScrollView.BouncesZoom = true;
            platformView.ScrollView.MaximumZoomScale = 5f;
            platformView.ScrollView.MinimumZoomScale = 1f;
            platformView.ScrollView.ZoomScale = 1f;
            platformView.ScrollView.PinchGestureRecognizer.Enabled = true;

            // Разрешение навигации жестами (свайп назад/вперед)
            platformView.AllowsBackForwardNavigationGestures = true;
        }
    }
}

#nullable restore
