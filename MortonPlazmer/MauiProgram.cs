using MortonPlazmer.Controls;
using Microsoft.Extensions.Logging;
#if ANDROID
using MortonPlazmer.Platforms.Android;
#elif IOS
    using MortonPlazmer.Platforms.iOS;
#elif WINDOWS
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using MortonPlazmer.Platforms.Windows;
#endif
namespace MortonPlazmer
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });
            builder.ConfigureMauiHandlers(handlers =>
            {
#if ANDROID
    handlers.AddHandler(typeof(CustomWebView), typeof(CustomWebViewHandler000));
#elif IOS
                handlers.AddHandler(typeof(CustomWebView), typeof(CustomWebViewHandler));
#elif WINDOWS        
                handlers.AddHandler(typeof(CustomWebView), typeof(CustomWebViewHandler));
#endif

            });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
