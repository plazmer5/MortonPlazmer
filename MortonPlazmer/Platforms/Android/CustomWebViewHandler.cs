#nullable disable

using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Android.Webkit;
using Android.Widget;
using Microsoft.Maui.Handlers;
using MortonPlazmer.Controls;
using Android.Util;
using System;
using System.IO;
using System.Threading;

using AndroidUtil = Android.Util;
using AndroidEnvironment = Android.OS.Environment;
using AndroidWebView = Android.Webkit.WebView;
using Uri1 = Android.Net.Uri;
using AndroidVersion = Android.OS.Build;

namespace MortonPlazmer.Platforms.Android
{
    public class CustomWebViewHandler : WebViewHandler
    {
        protected override void ConnectHandler(AndroidWebView platformView)
        {
            base.ConnectHandler(platformView);

            var s = platformView.Settings;
            s.JavaScriptEnabled = true;
            s.DomStorageEnabled = true;
            s.AllowFileAccess = true;
            s.AllowContentAccess = true;

            platformView.SetDownloadListener(
                new TwoStepDownloadListener(platformView.Context));
        }
    }

    // =====================================================
    // DownloadListener (временный файл + прогресс)
    // =====================================================
    internal class TwoStepDownloadListener :
        Java.Lang.Object, IDownloadListener
    {
        private readonly Context _context;

        public TwoStepDownloadListener(Context context)
        {
            _context = context;
        }

        public void OnDownloadStart(
        string url,
        string userAgent,
        string contentDisposition,
        string mimeType,
        long contentLength)
        {
            if (string.IsNullOrEmpty(url) || url.StartsWith("blob:"))
                return;

            var activity = ActivityHelper.GetActivity(_context);
            if (activity == null)
                return;

            string fileName =
                URLUtil.GuessFileName(url, contentDisposition, mimeType);

            activity.RunOnUiThread(() =>
            {
                new AlertDialog.Builder(activity)
                    .SetTitle("Скачать файл")
                    .SetMessage($"Скачать файл:\n{fileName}?")
                    .SetPositiveButton("Скачать", (_, __) =>
                    {
                        StartTempDownload(
                            url, userAgent, fileName, mimeType);
                    })
                    .SetNegativeButton("Отмена", (_, __) => { })
                    .Show();
            });
        }


        private void StartTempDownload(
            string url,
            string userAgent,
            string fileName,
            string mimeType)
        {
            var request =
                new DownloadManager.Request(Uri1.Parse(url));

            request.AddRequestHeader("User-Agent", userAgent);
            request.SetTitle(fileName);
            request.SetNotificationVisibility(
                DownloadVisibility.VisibleNotifyCompleted);

            request.SetDestinationInExternalFilesDir(
                _context,
                AndroidEnvironment.DirectoryDownloads,
                fileName);

            var manager =
                (DownloadManager)_context.GetSystemService(
                    Context.DownloadService);

            long downloadId = manager.Enqueue(request);

            // ПРОГРЕСС
            TrackProgress(_context, downloadId);

            // ЗАВЕРШЕНИЕ
            DownloadCompletionReceiver.Register(
                _context,
                downloadId,
                fileName,
                mimeType);
        }

        // =================================================
        // Прогресс загрузки
        // =================================================
        private void TrackProgress(Context context, long downloadId)
        {
            var manager =
                (DownloadManager)context.GetSystemService(
                    Context.DownloadService);

            new Thread(() =>
            {
                bool downloading = true;

                while (downloading)
                {
                    var query = new DownloadManager.Query();
                    query.SetFilterById(downloadId);

                    using ICursor cursor =
                        manager.InvokeQuery(query);

                    if (!cursor.MoveToFirst())
                        break;

                    int downloaded =
                        cursor.GetInt(
                            cursor.GetColumnIndex(
                                DownloadManager.ColumnBytesDownloadedSoFar));

                    int total =
                        cursor.GetInt(
                            cursor.GetColumnIndex(
                                DownloadManager.ColumnTotalSizeBytes));

                    if (total > 0)
                    {
                        int progress =
                            (int)(downloaded * 100L / total);

                        AndroidUtil.Log.Debug(
                            "DOWNLOAD",
                            $"Progress: {progress}%");
                    }

                    int status =
                        cursor.GetInt(
                            cursor.GetColumnIndex(
                                DownloadManager.ColumnStatus));

                    if (status == (int)DownloadStatus.Successful ||
                        status == (int)DownloadStatus.Failed)
                        downloading = false;

                    Thread.Sleep(500);
                }
            }).Start();
        }

    }

    // =====================================================
    // BroadcastReceiver: копирование в MediaStore + авто-открытие
    // =====================================================
    [BroadcastReceiver(Enabled = true, Exported = false)]
    internal class DownloadCompletionReceiver : BroadcastReceiver
    {
        private static long _downloadId;
        private static string _fileName;
        private static string _mimeType;

        public static void Register(
            Context context,
            long downloadId,
            string fileName,
            string mimeType)
        {
            _downloadId = downloadId;
            _fileName = fileName;
            _mimeType = mimeType;

            context.RegisterReceiver(
                new DownloadCompletionReceiver(),
                new IntentFilter(
                    DownloadManager.ActionDownloadComplete));
        }

        public override void OnReceive(Context context, Intent intent)
        {
            long id =
                intent.GetLongExtra(
                    DownloadManager.ExtraDownloadId, -1);

            if (id != _downloadId)
                return;

            var manager =
                (DownloadManager)context.GetSystemService(
                    Context.DownloadService);

            var sourceUri =
                manager.GetUriForDownloadedFile(id);

            if (sourceUri == null)
                return;

            var targetUri =
                CopyToMediaStore(context, sourceUri);

            if (targetUri != null)
                OpenFile(context, targetUri);
        }

        private Uri1 CopyToMediaStore(
            Context context,
            Uri1 sourceUri)
        {
            try
            {
                var values = new ContentValues();
                values.Put(
                    MediaStore.MediaColumns.DisplayName,
                    _fileName);
                values.Put(
                    MediaStore.MediaColumns.MimeType,
                    _mimeType);
                values.Put(
                    MediaStore.MediaColumns.RelativePath,
                    AndroidEnvironment.DirectoryDownloads);
                values.Put(
                    MediaStore.MediaColumns.IsPending, 1);

                var resolver = context.ContentResolver;

                var targetUri =
                    resolver.Insert(
                        MediaStore.Downloads.ExternalContentUri,
                        values);

                using var input =
                    resolver.OpenInputStream(sourceUri);
                using var output =
                    resolver.OpenOutputStream(targetUri);

                input.CopyTo(output);

                values.Clear();
                values.Put(
                    MediaStore.MediaColumns.IsPending, 0);
                resolver.Update(targetUri, values, null, null);

                return targetUri;
            }
            catch (Exception ex)
            {
                Toast.MakeText(
                    context,
                    $"Ошибка копирования: {ex.Message}",
                    ToastLength.Long).Show();

                return null;
            }
        }

        private static void OpenFile(
            Context context,
            Uri1 fileUri)
        {
            try
            {
                var intent =
                    new Intent(Intent.ActionView);

                intent.SetDataAndType(fileUri, _mimeType);
                intent.SetFlags(
                    ActivityFlags.NewTask |
                    ActivityFlags.GrantReadUriPermission);

                context.StartActivity(intent);
            }
            catch
            {
                Toast.MakeText(
                    context,
                    "Не удалось открыть файл",
                    ToastLength.Short).Show();
            }
        }
    }
    internal static class ActivityHelper
    {
        public static Activity GetActivity(Context context)
        {
            while (context is ContextWrapper wrapper)
            {
                if (wrapper is Activity activity)
                    return activity;

                context = wrapper.BaseContext;
            }

            return null;
        }
    }
}

#nullable restore
