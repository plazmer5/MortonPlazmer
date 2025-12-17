using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

#if ANDROID
using Android.OS;
#endif

#if WINDOWS
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
#endif

namespace MortonPlazmer
{
    public partial class MainPage : ContentPage
    {
#if WINDOWS
        private FrameworkElement? _rootElement;
        private bool _altPressed;
#endif
        public MainPage()
        {
            InitializeComponent();

#if ANDROID
            RequestPermissions();
            Loaded += OnPageLoadedAndroid;
#endif

#if WINDOWS
            Loaded += OnPageLoadedWindows;
#endif
        }

#if ANDROID
        private async void RequestPermissions()
        {
            if (DeviceInfo.Version.Major < 13)
            {
                await Permissions.RequestAsync<Permissions.StorageRead>();
                await Permissions.RequestAsync<Permissions.StorageWrite>();
            }
        }
#endif

        #region Navigation

        protected override bool OnBackButtonPressed()
        {
            if (MyWebView.CanGoBack)
            {
                MyWebView.GoBack();
                return true;
            }

            return base.OnBackButtonPressed();
        }

        private void OnSwipeLeft(object sender, SwipedEventArgs e)
        {
            if (MyWebView.CanGoBack)
                MyWebView.GoBack();
        }

        #endregion

        #region Page Loaded / Age Check

#if ANDROID
        private async void OnPageLoadedAndroid(object? sender, EventArgs e)
        {
            await CheckAgeAsync();
        }
#endif

#if WINDOWS
        private async void OnPageLoadedWindows(object? sender, EventArgs e)
        {
            await CheckAgeAsync();
            RegisterKeyboardHandlers();
        }
#endif
        private async Task CheckAgeAsync()
        {
            bool isAdult = Preferences.Get("IsAdult", false);

            if (!isAdult)
                await AskAgeAsync();

            if (Preferences.Get("IsAdult", false))
            {
                MyWebView.IsVisible = true;

                // КРИТИЧНО для Windows
                MyWebView.Source = new UrlWebViewSource
                {
                    Url = "https://mortonplazmer.wixsite.com/plazmerdi/"
                };
            }
        }
        private async Task AskAgeAsync()
        {
            if (!Preferences.Get("IsAdult", false))
            {
                bool result = await DisplayAlertAsync(
                    "Возрастное ограничение 21+",
                    "Вам уже есть 21+ лет?",
                    "Да",
                    "Нет");

                Preferences.Set("IsAdult", result);
                if (!result)
                {
#if ANDROID
        Process.KillProcess(Process.MyPid());
#elif WINDOWS
        Microsoft.Maui.Controls.Application.Current?.Quit();
#endif
                }
            }

        }

        #endregion

#if WINDOWS
        #region Keyboard Navigation (Windows)

        private void RegisterKeyboardHandlers()
{
    var mauiWindow = App.Current?.Windows.FirstOrDefault();
    if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
        return;

    _rootElement = nativeWindow.Content as FrameworkElement;
    if (_rootElement is null)
        return;

    // Привязка к событиям
    _rootElement.KeyDown += Root_KeyDown;
    _rootElement.KeyUp += Root_KeyUp;
}

// Методы должны быть в том же классе
private void Root_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
{
    if (!MyWebView?.CanGoBack == true)
        return;

    if (e.Key == Windows.System.VirtualKey.Back || e.Key == Windows.System.VirtualKey.GoBack)
    {
        if (MyWebView != null && MyWebView.CanGoBack)
        {
            MyWebView.GoBack();
            e.Handled = true;
            return;
        }      
    }

    if (e.Key == Windows.System.VirtualKey.Menu)
    {
        _altPressed = true;
        return;
    }

    if (_altPressed && e.Key == Windows.System.VirtualKey.Left)
    {
        
        if (MyWebView != null && MyWebView.CanGoBack)
        {
            MyWebView.GoBack();
            e.Handled = true;
        }
    }
}

private void Root_KeyUp(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
{
    if (e.Key == Windows.System.VirtualKey.Menu)
        _altPressed = false;
}


        #endregion
#endif
    }
}
