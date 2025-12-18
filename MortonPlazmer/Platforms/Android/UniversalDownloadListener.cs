#nullable disable
using Android.App;
using Android.Content;
using Android.OS;
using Android.Webkit;
using Android.Widget;
using System.Diagnostics;
using AndroidEnvironment = Android.OS.Environment;
using AndroidUri = Android.Net.Uri;

namespace MortonPlazmer.Platforms.Android
{
    public class UniversalDownloadListener : Java.Lang.Object, IDownloadListener
    {
        private readonly MainActivity _activity;
        private string _pendingDownloadUrl;
        private string _pendingDownloadUserAgent;
        private string _pendingDownloadMimeType;

        private const int REQUEST_CODE_CREATE_FILE = 1001;

        public UniversalDownloadListener(MainActivity activity)
        {
            _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        }

        public void OnDownloadStart(string url, string userAgent, string contentDisposition, string mimeType, long contentLength)
        {
            if (string.IsNullOrEmpty(url) || url.StartsWith("blob:"))
                return;

            string fileName = URLUtil.GuessFileName(url, contentDisposition, mimeType);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat)
            {
                var intent = new Intent(Intent.ActionCreateDocument);
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType(mimeType ?? "application/octet-stream");
                intent.PutExtra(Intent.ExtraTitle, fileName);

                _pendingDownloadUrl = url;
                _pendingDownloadUserAgent = userAgent;
                _pendingDownloadMimeType = mimeType;

                _activity.StartActivityForResult(intent, REQUEST_CODE_CREATE_FILE);
            }
            else
            {
                DownloadLegacy(url, userAgent, fileName);
            }
        }

        public void OnFileSelected(AndroidUri uri)
        {
            if (uri == null || string.IsNullOrEmpty(_pendingDownloadUrl))
                return;

            var request = new DownloadManager.Request(AndroidUri.Parse(_pendingDownloadUrl));
            request.AddRequestHeader("User-Agent", _pendingDownloadUserAgent ?? "");
            request.SetNotificationVisibility(DownloadVisibility.VisibleNotifyCompleted);
            request.SetDestinationUri(uri);

            var manager = (DownloadManager)_activity.GetSystemService(Context.DownloadService);
            manager.Enqueue(request);

            Toast.MakeText(_activity, "Загрузка началась", ToastLength.Short).Show();

            _pendingDownloadUrl = null;
            _pendingDownloadUserAgent = null;
            _pendingDownloadMimeType = null;
        }

        private void DownloadLegacy(string url, string userAgent, string fileName)
        {
            try
            {
                var request = new DownloadManager.Request(AndroidUri.Parse(url));
                request.AddRequestHeader("User-Agent", userAgent ?? "");
                request.SetNotificationVisibility(DownloadVisibility.VisibleNotifyCompleted);
                request.SetTitle(fileName);
                request.SetDestinationInExternalPublicDir(AndroidEnvironment.DirectoryDownloads, fileName);

                var manager = (DownloadManager)_activity.GetSystemService(Context.DownloadService);
                manager.Enqueue(request);

                Toast.MakeText(_activity, "Загрузка началась", ToastLength.Short).Show();
            }
            catch (Exception ex)
            {
                Toast.MakeText(_activity, $"Ошибка: {ex.Message}", ToastLength.Long).Show();
            }
        }
    }
}
#nullable restore
