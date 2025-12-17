#nullable disable

using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using MortonPlazmer.Controls;
using System.IO;
using Windows.Storage.Pickers;
using WinRT.Interop;

using MauiWindow = Microsoft.Maui.Controls.Window;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace MortonPlazmer.Platforms.Windows
{
    public class CustomWebViewHandler : WebViewHandler
    {
        protected override async void ConnectHandler(WebView2 platformView)
        {
            base.ConnectHandler(platformView);

            await platformView.EnsureCoreWebView2Async();

            // 🔴 1. Запрещаем открытие новых окон
            platformView.CoreWebView2.NewWindowRequested +=
                (s, e) =>
                {
                    // Открываем ссылку в ТОМ ЖЕ WebView
                    s.Navigate(e.Uri);
                    e.Handled = true;
                };

            // 🔴 2. Перехват скачивания
            platformView.CoreWebView2.DownloadStarting += OnDownloadStarting;
        }


        private async void OnDownloadStarting(
            CoreWebView2 sender,
            CoreWebView2DownloadStartingEventArgs e)
        {
            // 🔒 ПОЛНЫЙ КОНТРОЛЬ — БЕРЁМ DEFERRAL
            var deferral = e.GetDeferral();

            try
            {
                // ⛔ останавливаем автоматическое скачивание
                e.Handled = true;

                string fileName = Path.GetFileName(
                    e.ResultFilePath ??
                    e.DownloadOperation.Uri);

                bool confirmed = await ShowConfirmDialogAsync(fileName);
                if (!confirmed)
                {
                    e.Cancel = true;
                    return;
                }

                var picker = new FileSavePicker();
                picker.SuggestedFileName = fileName;
                picker.FileTypeChoices.Add(
                    "Файл",
                    new[] { Path.GetExtension(fileName) });

                MauiWindow mauiWindow =
                    Microsoft.Maui.Controls.Application.Current.Windows[0];

                WinUIWindow winuiWindow =
                    mauiWindow.Handler.PlatformView as WinUIWindow;

                if (winuiWindow == null)
                {
                    e.Cancel = true;
                    return;
                }

                InitializeWithWindow.Initialize(
                    picker,
                    WindowNative.GetWindowHandle(winuiWindow));

                var file = await picker.PickSaveFileAsync();
                if (file == null)
                {
                    e.Cancel = true;
                    return;
                }

                // ✅ РАЗРЕШАЕМ ЗАГРУЗКУ
                e.ResultFilePath = file.Path;
                e.Handled = false;
            }
            finally
            {
                // ▶ WebView2 может продолжить работу
                deferral.Complete();
            }
        }

        private static async System.Threading.Tasks.Task<bool>
            ShowConfirmDialogAsync(string fileName)
        {
            MauiWindow mauiWindow =
                Microsoft.Maui.Controls.Application.Current.Windows[0];

            WinUIWindow winuiWindow =
                mauiWindow.Handler.PlatformView as WinUIWindow;

            if (winuiWindow == null)
                return false;

            var dialog = new ContentDialog
            {
                Title = "Скачать файл",
                Content = $"Скачать файл:\n{fileName}?",
                PrimaryButtonText = "Скачать",
                SecondaryButtonText = "Отмена",
                XamlRoot = winuiWindow.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
    }
}

#nullable restore
