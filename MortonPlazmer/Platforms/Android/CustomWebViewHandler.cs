#nullable disable

using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Webkit;
using Android.Widget;
using Java.Interop;
using Microsoft.Maui.Handlers;
using System.Collections.Concurrent;

using AndroidEnvironment = Android.OS.Environment;
using AndroidUtil = Android.Util;
using AndroidWebView = Android.Webkit.WebView;
using AndroidUri = Android.Net.Uri;
using AndroidGraphics = Android.Graphics;

namespace MortonPlazmer.Platforms.Android
{
    // =====================================================
    // LOG
    // =====================================================
    internal static class DL
    {
        public const string TAG = "WEBVIEW-DOWNLOAD";
        public static void I(string m) => AndroidUtil.Log.Info(TAG, m);
        public static void E(string m) => AndroidUtil.Log.Error(TAG, m);
    }

    // =====================================================
    // NOTIFICATIONS
    // =====================================================
    internal static class DownloadNotifications
    {
        private const string CHANNEL_ID = "downloads";
        private static bool _initialized;

        public static void Init(Context ctx)
        {
            if (_initialized) return;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(
                    CHANNEL_ID,
                    "Загрузки",
                    NotificationImportance.Default)
                {
                    Description = "Загрузка файлов"
                };

                var nm =
                    (NotificationManager)ctx.GetSystemService(
                        Context.NotificationService);

                nm.CreateNotificationChannel(channel);
            }

            _initialized = true;
        }

        public static int ShowProgress(Context ctx, string name)
        {
            Init(ctx);

            int id = name.GetHashCode();

            var n = new Notification.Builder(ctx, CHANNEL_ID)
                .SetContentTitle("Загрузка файла")
                .SetContentText(name)
                .SetSmallIcon(Resource.Drawable.material_ic_menu_arrow_down_black_24dp)
                .SetOngoing(true)
                .SetProgress(0, 0, true)
                .Build();

            NotificationManager.FromContext(ctx).Notify(id, n);
            return id;
        }

        public static void Complete(Context ctx, int id, string name, AndroidUri uri)
        {
            var i = new Intent(Intent.ActionView);
            i.SetData(uri);
            i.SetFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);

            var pi = PendingIntent.GetActivity(
                ctx, 0, i,
                PendingIntentFlags.UpdateCurrent |
                PendingIntentFlags.Immutable);

            var n = new Notification.Builder(ctx, CHANNEL_ID)
                .SetContentTitle("Загрузка завершена")
                .SetContentText(name)
                .SetSmallIcon(Resource.Drawable.material_ic_menu_arrow_down_black_24dp)
                .SetContentIntent(pi)
                .SetAutoCancel(true)
                .Build();

            NotificationManager.FromContext(ctx).Notify(id, n);
        }

        public static void Error(Context ctx, int id, string msg)
        {
            var n = new Notification.Builder(ctx, CHANNEL_ID)
                .SetContentTitle("Ошибка загрузки")
                .SetContentText(msg)
                .SetSmallIcon(Resource.Drawable.material_ic_menu_arrow_down_black_24dp)
                .SetAutoCancel(true)
                .Build();

            NotificationManager.FromContext(ctx).Notify(id, n);
        }
    }

    // =====================================================
    // HANDLER
    // =====================================================
    public class CustomWebViewHandler : WebViewHandler
    {
        protected override void ConnectHandler(AndroidWebView wv)
        {
            base.ConnectHandler(wv);

            var s = wv.Settings;
            s.JavaScriptEnabled = true;
            s.DomStorageEnabled = true;
            s.AllowFileAccess = true;
            s.AllowContentAccess = true;
            s.UseWideViewPort = true;
            s.LoadWithOverviewMode = true;
            s.BuiltInZoomControls = true;
            s.DisplayZoomControls = false;

            if (VirtualView is Controls.CustomWebView cv &&
                !string.IsNullOrEmpty(cv.UserAgent))
            {
                s.UserAgentString = cv.UserAgent;
            }

            wv.SetBackgroundColor(AndroidGraphics.Color.Black);

            wv.AddJavascriptInterface(
                new BlobJsBridge(wv.Context),
                "AndroidBlob");

            wv.SetWebViewClient(new BlobAwareClient(wv));
            wv.SetDownloadListener(new QueueDownloadListener(wv.Context));

            DL.I("CustomWebViewHandler connected");
        }
    }

    // =====================================================
    // WEBVIEW CLIENT
    // =====================================================
    internal class BlobAwareClient : WebViewClient
    {
        private readonly AndroidWebView _wv;
        private readonly Context _ctx;

        public BlobAwareClient(AndroidWebView wv)
        {
            _wv = wv;
            _ctx = wv.Context;
        }

        public override bool ShouldOverrideUrlLoading(
            AndroidWebView view,
            IWebResourceRequest request)
        {
            var url = request?.Url?.ToString();
            if (string.IsNullOrEmpty(url))
                return false;

            bool isFile =
                url.StartsWith("blob:") ||
                url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".doc", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("static.wixstatic.com");

            if (!isFile)
                return false;

            ShowConfirm(url, () =>
            {
                if (url.StartsWith("blob:"))
                    InjectBlobJS(url);
                else
                    view.LoadUrl(url);
            });

            return true;
        }

        private void ShowConfirm(string url, Action ok)
        {
            string name = System.IO.Path.GetFileName(url);

            new AlertDialog.Builder(_ctx)
                .SetTitle("Скачать файл?")
                .SetMessage($"Имя: {name}\nФайл будет сохранён в Загрузки.")
                .SetPositiveButton("Скачать", (_, __) => ok())
                .SetNegativeButton("Отмена", (_, __) => { })
                .Show();
        }

        private void InjectBlobJS(string blobUrl)
        {
            string js = $@"
(async function() {{
  try {{
    const r = await fetch('{blobUrl}');
    const b = await r.blob();
    const reader = new FileReader();
    reader.onloadend = function() {{
      AndroidBlob.save(
        reader.result.split(',')[1],
        b.type || 'application/octet-stream',
        b.size
      );
    }};
    reader.readAsDataURL(b);
  }} catch(e) {{
    AndroidBlob.error(e.toString());
  }}
}})();";

            _wv.Post(() => _wv.EvaluateJavascript(js, null));
        }
    }

    // =====================================================
    // QUEUE DOWNLOAD LISTENER
    // =====================================================
    internal class QueueDownloadListener :
        Java.Lang.Object, IDownloadListener
    {
        private readonly Context _ctx;

        public QueueDownloadListener(Context ctx) => _ctx = ctx;

        public void OnDownloadStart(
            string url, string ua, string cd, string mime, long len)
        {
            if (string.IsNullOrEmpty(mime))
                mime = "application/octet-stream";

            string name = URLUtil.GuessFileName(url, cd, mime);

            DownloadQueue.Enqueue(async () =>
            {
                int nid = DownloadNotifications.ShowProgress(_ctx, name);
                try
                {
                    var uri = await MediaStoreWriter.WriteFromUrl(
                        _ctx, url, ua, name, mime);

                    DownloadNotifications.Complete(_ctx, nid, name, uri);
                }
                catch (Exception ex)
                {
                    DownloadNotifications.Error(_ctx, nid, ex.Message);
                }
            });
        }
    }

    // =====================================================
    // JS → ANDROID (BLOB)
    // =====================================================
    internal class BlobJsBridge : Java.Lang.Object
    {
        private readonly Context _ctx;
        public BlobJsBridge(Context ctx) => _ctx = ctx;

        [JavascriptInterface, Export("save")]
        public void Save(string base64, string mime, long size)
        {
            DownloadQueue.Enqueue(async () =>
            {
                int nid = DownloadNotifications.ShowProgress(_ctx, "blob");

                try
                {
                    var bytes = Convert.FromBase64String(base64);
                    var uri = await MediaStoreWriter.WriteBytes(
                        _ctx, bytes, mime);

                    DownloadNotifications.Complete(_ctx, nid, "blob", uri);
                }
                catch (Exception ex)
                {
                    DownloadNotifications.Error(_ctx, nid, ex.Message);
                }
            });
        }

        [JavascriptInterface, Export("error")]
        public void Error(string msg)
        {
            DL.E("Blob JS error: " + msg);
        }
    }

    // =====================================================
    // DOWNLOAD QUEUE
    // =====================================================
    internal static class DownloadQueue
    {
        private static readonly Queue<Func<Task>> _queue = new();
        private static bool _running;

        public static void Enqueue(Func<Task> job)
        {
            lock (_queue)
            {
                _queue.Enqueue(job);
                if (_running) return;
                _running = true;
            }

            _ = Task.Run(Process);
        }

        private static async Task Process()
        {
            while (true)
            {
                Func<Task> job;

                lock (_queue)
                {
                    if (_queue.Count == 0)
                    {
                        _running = false;
                        return;
                    }
                    job = _queue.Dequeue();
                }

                await job();
            }
        }
    }

    // =====================================================
    // MEDIASTORE WRITER
    // =====================================================
    internal static class MediaStoreWriter
    {
        public static async Task<AndroidUri> WriteFromUrl(
            Context ctx,
            string url,
            string ua,
            string name,
            string mime)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(ua);

            var data = await client.GetByteArrayAsync(url);
            return await WriteBytes(ctx, data, mime, name);
        }

        public static async Task<AndroidUri> WriteBytes(
            Context ctx,
            byte[] data,
            string mime,
            string fileName = null)
        {
            string ext =
                MimeTypeMap.Singleton.GetExtensionFromMimeType(mime) ?? "bin";

            fileName ??=
                $"file_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";

            var r = ctx.ContentResolver;

            var v = new ContentValues();
            v.Put(MediaStore.IMediaColumns.DisplayName, fileName);
            v.Put(MediaStore.IMediaColumns.MimeType, mime);
            v.Put(MediaStore.IMediaColumns.RelativePath,
                AndroidEnvironment.DirectoryDownloads);
            v.Put(MediaStore.IMediaColumns.IsPending, 1);

            var uri =
                r.Insert(MediaStore.Downloads.ExternalContentUri, v);

            using (var o = r.OpenOutputStream(uri))
                await o.WriteAsync(data, 0, data.Length);

            var u = new ContentValues();
            u.Put(MediaStore.IMediaColumns.IsPending, 0);
            r.Update(uri, u, null, null);

            return uri;
        }
    }
}

#nullable restore
