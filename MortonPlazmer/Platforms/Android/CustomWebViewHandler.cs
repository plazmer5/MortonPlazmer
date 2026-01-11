#nullable disable

using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Android.Provider;
using Android.Webkit;
using Java.Interop;

using Microsoft.Maui.Handlers;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using AndroidEnvironment = Android.OS.Environment;
using AndroidGraphics = Android.Graphics;
using AndroidUri = Android.Net.Uri;
using AndroidUtil = Android.Util;
using AndroidWebView = Android.Webkit.WebView;
using AndroidApp = Android.App.Application;

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
                var nm = (NotificationManager)ctx.GetSystemService(Context.NotificationService);
                nm.CreateNotificationChannel(channel);
            }

            _initialized = true;
        }

        private static Notification.Builder CreateBuilder(Context ctx)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                return new Notification.Builder(ctx, CHANNEL_ID);

            return new Notification.Builder(ctx);
        }

        private static int _nid;

        public static int ShowProgress(Context ctx, string name)
        {
            Init(ctx);
            int id = Interlocked.Increment(ref _nid);
            var b = CreateBuilder(ctx)
                .SetContentTitle("Загрузка файла")
                .SetContentText(name)
                .SetSmallIcon(Resource.Drawable.material_ic_menu_arrow_down_black_24dp)
                .SetOngoing(true)
                .SetProgress(0, 0, true);
            NotificationManager.FromContext(ctx).Notify(id, b.Build());
            return id;
        }

        public static void Complete(Context ctx, int id, string name, AndroidUri uri)
        {
            var i = new Intent(Intent.ActionView);
            i.SetData(uri);
            i.SetFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);

            var flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                flags |= PendingIntentFlags.Immutable;

            var pi = PendingIntent.GetActivity(ctx, 0, i, flags);
            var b = CreateBuilder(ctx)
                .SetContentTitle("Загрузка завершена")
                .SetContentText(name)
                .SetSmallIcon(Resource.Drawable.material_ic_menu_arrow_down_black_24dp)
                .SetContentIntent(pi)
                .SetAutoCancel(true);
            NotificationManager.FromContext(ctx).Notify(id, b.Build());
        }

        public static void Error(Context ctx, int id, string msg)
        {
            var b = CreateBuilder(ctx)
                .SetContentTitle("Ошибка загрузки")
                .SetContentText(msg)
                .SetSmallIcon(Resource.Drawable.material_ic_menu_arrow_down_black_24dp)
                .SetAutoCancel(true);
            NotificationManager.FromContext(ctx).Notify(id, b.Build());
        }
    }

    // =====================================================
    // CUSTOM WEBVIEW HANDLER
    // =====================================================
    public class CustomWebViewHandler : WebViewHandler
    {
        protected override void ConnectHandler(AndroidWebView wv)
        {
            base.ConnectHandler(wv);

            var s = wv.Settings;
            s.JavaScriptEnabled = true;
            s.DomStorageEnabled = true;
            s.DatabaseEnabled = true;
            s.AllowFileAccess = true;
            s.AllowContentAccess = true;
            s.UseWideViewPort = true;
            s.LoadWithOverviewMode = true;
            s.BuiltInZoomControls = true;
            s.DisplayZoomControls = false;

            // Кэш
            bool isLineageOS = Build.Manufacturer?.Equals("lineage", System.StringComparison.OrdinalIgnoreCase) == true
                               || Build.Display?.ToLowerInvariant().Contains("lineage", StringComparison.InvariantCultureIgnoreCase) == true;

            if (Build.VERSION.SdkInt < BuildVersionCodes.R || isLineageOS)
            {
                s.SetAppCacheEnabled(true);
                s.SetAppCachePath(wv.Context.CacheDir.AbsolutePath);
            }

            s.CacheMode = CacheModes.CacheElseNetwork; // оффлайн поддержка

            if (VirtualView is Controls.CustomWebView cv &&
                !string.IsNullOrEmpty(cv.UserAgent))
            {
                s.UserAgentString = cv.UserAgent;
            }

            wv.SetBackgroundColor(AndroidGraphics.Color.Black);
            wv.AddJavascriptInterface(new BlobJsBridge(wv.Context), "AndroidBlob");

            // WebViewClient с оффлайн-поддержкой
            wv.SetWebViewClient(new OfflineWebViewClient(wv));
            wv.SetDownloadListener(new QueueDownloadListener(wv.Context));

            // авто-очистка старого кэша (файлы старше 7 дней)
            LocalCache.CleanOldCache(7);

            DL.I("CustomWebViewHandler connected with offline cache enabled");
        }
    }

    // =====================================================
    // OFFLINE WEBVIEW CLIENT
    // =====================================================
    internal class OfflineWebViewClient(AndroidWebView wv) : WebViewClient
    {
        private readonly AndroidWebView _wv = wv;
        private readonly Context _ctx = wv.Context;

        public override void OnReceivedError(AndroidWebView view, IWebResourceRequest request, WebResourceError error)
        {
            view.Settings.CacheMode = IsNetworkAvailable() ? CacheModes.Default : CacheModes.CacheElseNetwork;
            base.OnReceivedError(view, request, error);
        }

        public override WebResourceResponse ShouldInterceptRequest(AndroidWebView view, IWebResourceRequest request)
        {
            var url = request.Url.ToString();
            if (LocalCache.TryGet(url) is (var stream, var mime))
            {
                return new WebResourceResponse(mime, "UTF-8", stream);
            }

            return base.ShouldInterceptRequest(view, request);
        }

        private bool IsNetworkAvailable()
        {
            var cm = (ConnectivityManager)_ctx.GetSystemService(Context.ConnectivityService);
            var network = cm.ActiveNetworkInfo;
            return network != null && network.IsConnected;
        }
    }

    // =====================================================
    // LOCAL CACHE HELPER
    // =====================================================
    internal static class LocalCache
    {
        private static readonly string CacheDir = Path.Combine(AndroidApp.Context.CacheDir.AbsolutePath, "webview");

        public static (Stream Stream, string MimeType)? TryGet(string url)
        {
            try
            {
                var file = Path.Combine(CacheDir, System.Uri.EscapeDataString(url));
                if (!File.Exists(file)) return null;

                File.SetLastAccessTimeUtc(file, System.DateTime.UtcNow);

                string mime = MimeTypeMap.Singleton.GetMimeTypeFromExtension(Path.GetExtension(file)) ?? "application/octet-stream";
                return (File.OpenRead(file), mime);
            }
            catch
            {
                return null;
            }
        }

        public static void Save(string url, byte[] data)
        {
            try
            {
                if (!Directory.Exists(CacheDir))
                    Directory.CreateDirectory(CacheDir);

                var file = Path.Combine(CacheDir, System.Uri.EscapeDataString(url));
                File.WriteAllBytes(file, data);
                File.SetLastAccessTimeUtc(file, System.DateTime.UtcNow);
            }
            catch { DL.E("CleanOldCache job failed."); }
        }
        public static void CleanOldCache(int days = 7)
        {
            try
            {
                if (!Directory.Exists(CacheDir))
                {
                    Directory.CreateDirectory(CacheDir); // создаём папку, если её нет
                    return; // папка новая, нечего чистить
                }

                var files = Directory.GetFiles(CacheDir);
                var threshold = DateTime.UtcNow.AddDays(-days);

                foreach (var file in files)
                {
                    try
                    {
                        if (File.GetLastAccessTimeUtc(file) < threshold)
                            File.Delete(file);
                    }
                    catch { /* Игнорируем ошибки удаления */ }
                }
            }
            catch (Exception ex)
            {
                DL.E("CleanOldCache job failed: " + ex);
            }
        }
    }

    // =====================================================
    // BLOB JS BRIDGE
    // =====================================================
    internal class BlobJsBridge(Context ctx) : Java.Lang.Object
    {
        private readonly Context _ctx = ctx;

        [JavascriptInterface, Export("save")]
        public void Save(string base64, string mime)
        {
            DownloadQueue.Enqueue(async () =>
            {
                int nid = DownloadNotifications.ShowProgress(_ctx, "blob");
                try
                {
                    var bytes = System.Convert.FromBase64String(base64);
                    var uri = await MediaStoreWriter.WriteBytes(_ctx, bytes, mime);
                    DownloadNotifications.Complete(_ctx, nid, "blob", uri);
                }
                catch (System.Exception ex)
                {
                    DownloadNotifications.Error(_ctx, nid, ex.Message);
                }
            });
        }

        [JavascriptInterface, Export("error")]
        public static void Error(string msg)
        {
            DL.E("Blob JS error: " + msg);
        }
    }

    // =====================================================
    // DOWNLOAD QUEUE
    // =====================================================
    internal static class DownloadQueue
    {
        private static readonly Queue<System.Func<System.Threading.Tasks.Task>> _queue = new();
        private static bool _running;

        public static void Enqueue(System.Func<System.Threading.Tasks.Task> job)
        {
            lock (_queue)
            {
                _queue.Enqueue(job);
                if (_running) return;
                _running = true;
            }

            _ = System.Threading.Tasks.Task.Run(Process);
        }

        private static async System.Threading.Tasks.Task Process()
        {
            while (true)
            {
                System.Func<System.Threading.Tasks.Task> job;

                lock (_queue)
                {
                    if (_queue.Count == 0)
                    {
                        _running = false;
                        return;
                    }
                    job = _queue.Dequeue();
                }

                try
                {
                    await job();
                }
                catch (System.Exception ex)
                {
                    DL.E("DownloadQueue job failed: " + ex);
                }
            }
        }
    }

    // =====================================================
    // MEDIASTORE WRITER
    // =====================================================
    internal static class MediaStoreWriter
    {
        public static async System.Threading.Tasks.Task<AndroidUri> WriteFromUrl(
            Context ctx,
            string url,
            string ua,
            string name,
            string mime)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(ua);
            var data = await client.GetByteArrayAsync(url);

            LocalCache.Save(url, data);

            return await WriteBytes(ctx, data, mime, name);
        }

        public static async System.Threading.Tasks.Task<AndroidUri> WriteBytes(
            Context ctx,
            byte[] data,
            string mime,
            string fileName = null)
        {
            string ext = MimeTypeMap.Singleton.GetExtensionFromMimeType(mime) ?? "bin";
            fileName ??= $"file_{System.DateTime.Now:yyyyMMdd_HHmmss}.{ext}";

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                var r = ctx.ContentResolver;
                var v = new ContentValues();
                v.Put(MediaStore.IMediaColumns.DisplayName, fileName);
                v.Put(MediaStore.IMediaColumns.MimeType, mime);
                v.Put(MediaStore.IMediaColumns.RelativePath, AndroidEnvironment.DirectoryDownloads);
                v.Put(MediaStore.IMediaColumns.IsPending, 1);

                var uri = r.Insert(MediaStore.Downloads.ExternalContentUri, v);
                using (var o = r.OpenOutputStream(uri))
                    await o.WriteAsync(data);

                var u = new ContentValues();
                u.Put(MediaStore.IMediaColumns.IsPending, 0);
                r.Update(uri, u, null, null);
                return uri;
            }
            else
            {
                var dir = AndroidEnvironment.GetExternalStoragePublicDirectory(AndroidEnvironment.DirectoryDownloads);
                if (!dir.Exists()) dir.Mkdirs();

                var file = new Java.IO.File(dir, fileName);
                using (var fs = new FileStream(file.AbsolutePath, FileMode.Create))
                    await fs.WriteAsync(data);

                return AndroidUri.FromFile(file);
            }
        }
    }

    // =====================================================
    // DOWNLOAD LISTENER
    // =====================================================
    internal class QueueDownloadListener(Context ctx) : Java.Lang.Object, IDownloadListener
    {
        private readonly Context _ctx = ctx;

        public void OnDownloadStart(string url, string ua, string cd, string mime, long len)
        {
            if (string.IsNullOrEmpty(mime))
                mime = "application/octet-stream";

            string name = URLUtil.GuessFileName(url, cd, mime);

            DownloadQueue.Enqueue(async () =>
            {
                int nid = DownloadNotifications.ShowProgress(_ctx, name);
                try
                {
                    var uri = await MediaStoreWriter.WriteFromUrl(_ctx, url, ua, name, mime);
                    DownloadNotifications.Complete(_ctx, nid, name, uri);
                }
                catch (System.Exception ex)
                {
                    DownloadNotifications.Error(_ctx, nid, ex.Message);
                }
            });
        }
    }
}

#nullable restore
