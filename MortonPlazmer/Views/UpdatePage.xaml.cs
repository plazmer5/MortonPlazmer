using MortonPlazmer.Models;
using MortonPlazmer.Services;
using System.Linq;

namespace MortonPlazmer.Views;

public partial class UpdatePage : ContentPage
{
    private readonly UpdateInfo _info;
    private readonly DownloadService _download = new();

#if ANDROID
    private string? _pendingApkPath;
    private bool _waitingForInstallPermission;
#endif

    public UpdatePage(UpdateInfo info)
    {
        InitializeComponent();
        _info = info;
    }

    private async void OnUpdateClicked(object sender, EventArgs e)
    {
        string? url = GetPlatformUrl();
        if (string.IsNullOrEmpty(url))
            return;

        var progress = new Progress<double>(value =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Progress.Progress = value;
                StatusLabel.Text = $"{(int)(value * 100)}%";
            });
        });

        var file = await _download.DownloadFileAsync(
            url,
#if ANDROID
            "update.apk",
#elif WINDOWS
            "update.msix",
#else
            "update.bin",
#endif
            progress
        );

        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            StatusLabel.Text = "Ошибка: файл не найден";
            return;
        }

#if ANDROID
        StatusLabel.Text = "Подготовка установки...";

        if (!ApkInstallPermissionService.HasInstallPermission())
        {
            _pendingApkPath = file;
            _waitingForInstallPermission = true;

            StatusLabel.Text = "Разрешите установку приложений";
            ApkInstallPermissionService.OpenInstallSettings();
            return;
        }

        TryInstallApk(file);

#elif WINDOWS
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = true
        });

#else
        await Launcher.OpenAsync(new OpenFileRequest
        {
            File = new ReadOnlyFile(file)
        });
#endif
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        if (Application.Current?.Windows.Count > 0)
        {
            Application.Current.Windows[0].Page = new AppShell();
        }
    }

    private string? GetPlatformUrl()
    {
#if ANDROID
        return _info.AndroidUrl;
#elif WINDOWS
        return _info.WindowsUrl;
#else
        return null;
#endif
    }

#if ANDROID

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // MAUI-правильный способ отслеживания возврата в приложение
        if (Application.Current?.Windows.FirstOrDefault() is Window window)
        {
            window.Activated += OnWindowActivated;
        }
    }

    protected override void OnDisappearing()
    {
        if (Application.Current?.Windows.FirstOrDefault() is Window window)
        {
            window.Activated -= OnWindowActivated;
        }

        base.OnDisappearing();
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (!_waitingForInstallPermission)
            return;

        _waitingForInstallPermission = false;

        if (!ApkInstallPermissionService.HasInstallPermission())
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusLabel.Text = "Разрешение не выдано";
            });
            return;
        }

        if (!string.IsNullOrEmpty(_pendingApkPath) && File.Exists(_pendingApkPath))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TryInstallApk(_pendingApkPath);
            });
        }
    }

    private void TryInstallApk(string filePath)
    {
        try
        {
            var context = Android.App.Application.Context;
            var file = new Java.IO.File(filePath);

            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                context,
                context.PackageName + ".fileprovider",
                file
            );

            var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
            intent.SetDataAndType(uri, "application/vnd.android.package-archive");
            intent.SetFlags(Android.Content.ActivityFlags.NewTask);
            intent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission);

            context.StartActivity(intent);

            StatusLabel.Text = "Запуск установщика...";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Ошибка установки: {ex.Message}";
        }
    }

#endif
}