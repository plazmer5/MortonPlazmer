using MortonPlazmer.Models;
using MortonPlazmer.Services;
using MortonPlazmer.Views;
using Microsoft.Maui.ApplicationModel;

namespace MortonPlazmer;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new ContentPage
        {
            Content = new ActivityIndicator
            {
                IsRunning = true,
                VerticalOptions = LayoutOptions.Center
            }
        });

        _ = InitAsync(window);

        return window;
    }

    private async Task InitAsync(Window window)
    {
        var service = new UpdateService();
        var update = await service.GetAsync();

        if (update == null)
        {
            window.Page = new AppShell();
            return;
        }

        var current = AppInfo.VersionString;

        if (service.IsUpdateAvailable(current, update.Version!))
        {
            if (update.ForceUpdate)
            {
                window.Page = new UpdatePage(update);
                return;
            }

            window.Page = new AppShell();

            bool go = await window.Page.DisplayAlertAsync(
                "Обновление",
                update.Message ?? "Доступна новая версия",
                "Обновить",
                "Позже"
            );

#if ANDROID
            if (go && !string.IsNullOrEmpty(update.AndroidUrl))
                await Launcher.OpenAsync(update.AndroidUrl);

#elif WINDOWS
            if (go && !string.IsNullOrEmpty(update.WindowsUrl))
                await Launcher.OpenAsync(update.WindowsUrl);
#endif

            return;
        }

        window.Page = new AppShell();
    }
}