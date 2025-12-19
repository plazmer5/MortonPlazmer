using Microsoft.Maui.Handlers;
using WebKit;
using Foundation;

namespace MortonPlazmer.Platforms.iOS
{
    public class CustomWebViewHandler : WebViewHandler
    {
        protected override WKWebView CreatePlatformView()
        {
            var config = new WKWebViewConfiguration
            {
                Preferences = new WKPreferences
                {
                    JavaScriptEnabled = true
                }
            };

            var webView = new WKWebView(
                CoreGraphics.CGRect.Empty,
                config);

            return webView;
        }

        protected override void ConnectHandler(WKWebView platformView)
        {
            base.ConnectHandler(platformView);

            platformView.NavigationDelegate =
                new IOSWebViewNavigationDelegate();
        }
    }
}
