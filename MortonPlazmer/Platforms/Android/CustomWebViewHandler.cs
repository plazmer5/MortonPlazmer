#nullable disable

using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Webkit;
using Android.Widget;
using Java.Interop;
using Microsoft.Maui.Handlers;

using AndroidEnvironment = Android.OS.Environment;
using AndroidUtil = Android.Util;
using AndroidWebView = Android.Webkit.WebView;
using AndroidUri = Android.Net.Uri;
using AndroidGraphics = Android.Graphics;
namespace MortonPlazmer.Platforms.Android
{
    internal static class DL
    {
        public const string TAG = "WEBVIEW-DOWNLOAD";
        public static void I(string m) => AndroidUtil.Log.Info(TAG, m);
        public static void E(string m) => AndroidUtil.Log.Error(TAG, m);
    }

    // =====================================================
    // WebView Handler
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
            wv.SetBackgroundColor(
                AndroidGraphics.Color.Black
            );
            wv.AddJavascriptInterface(
                new BlobJsBridge(wv.Context),
                "AndroidBlob");

            wv.SetWebViewClient(new BlobAwareClient(wv));
            wv.SetDownloadListener(new DownloadListener(wv.Context));

            DL.I("CustomWebViewHandler connected");
        }
    }

    // =====================================================
    // WebViewClient — WIX FIX + CONFIRMATION DIALOG
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
                url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".doc", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("static.wixstatic.com");

            // 1️⃣ blob
            if (url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
            {
                ShowDownloadConfirmDialog(url, () =>
                {
                    InjectBlobJS(url);
                });
                return true;
            }

            // 2️⃣ обычный файл (PDF, DOCX и т.п.)
            if (isFile)
            {
                ShowDownloadConfirmDialog(url, () =>
                {
                    view.LoadUrl(url); // ✅ запускаем DownloadListener
                });
                return true;
            }

            return false;
        }


        // -------------------------------------------------
        // Диалог подтверждения
        // -------------------------------------------------
        private void ShowDownloadConfirmDialog(string url, Action onConfirm)
        {
            string file = System.IO.Path.GetFileName(url);

            new AlertDialog.Builder(_ctx)
                .SetTitle("Скачать файл?")
                .SetMessage($"Имя: {file}\nФайл будет сохранён в папку Загрузки.")
                .SetCancelable(true)
                .SetNegativeButton("Отмена", (s, e) => { })
                .SetPositiveButton("Скачать", (s, e) => onConfirm?.Invoke())
                .Show();
        }

        // -------------------------------------------------
        // Запуск blob-скачивания
        // -------------------------------------------------
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
                                b.type,
                                b.size);
                        }};
                        reader.readAsDataURL(b);
                    }} catch(e) {{
                        AndroidBlob.error(e.toString());
                    }}
                }})();
            ";

            _wv.Post(() => _wv.EvaluateJavascript(js, null));
        }
    }

    // =====================================================
    // JS → Android (blob)
    // =====================================================
    internal class BlobJsBridge : Java.Lang.Object
    {
        private readonly Context _ctx;

        public BlobJsBridge(Context ctx) => _ctx = ctx;

        [JavascriptInterface]
        [Export("save")]
        public void Save(string base64, string mime, long size)
        {
            try
            {
                if (!StorageCheck.HasSpace(_ctx, size))
                    throw new IOException("Недостаточно места");

                var bytes = Convert.FromBase64String(base64);

                string ext =
                    MimeTypeMap.Singleton.GetExtensionFromMimeType(mime);

                string name =
                    $"blob_{DateTime.Now:yyyyMMdd_HHmmss}" +
                    (string.IsNullOrEmpty(ext) ? "" : "." + ext);

                string temp =
                    Path.Combine(
                        _ctx.GetExternalFilesDir(
                            AndroidEnvironment.DirectoryDownloads).AbsolutePath,
                        name);

                File.WriteAllBytes(temp, bytes);

                DownloadFinalizer.Finalize(
                    _ctx, temp, name, mime);
            }
            catch (Exception ex)
            {
                DL.E(ex.ToString());
                Toast.MakeText(_ctx, ex.Message, ToastLength.Long).Show();
            }
        }

        [JavascriptInterface]
        [Export("error")]
        public void Error(string msg)
        {
            DL.E("Blob JS error: " + msg);
        }
    }

    // =====================================================
    // URL DownloadListener
    // =====================================================
    internal class DownloadListener :
        Java.Lang.Object, IDownloadListener
    {
        private readonly Context _ctx;

        public DownloadListener(Context ctx)
        {
            _ctx = ctx;
        }

        public void OnDownloadStart(string url, string ua, string cd, string mime, long len)
        {
            try
            {
                if (string.IsNullOrEmpty(mime))
                    mime = "application/octet-stream";

                if (len <= 0)
                    len = 1;

                string name = URLUtil.GuessFileName(url, cd, mime);

                var req = new DownloadManager.Request(AndroidUri.Parse(url));
                req.AddRequestHeader("User-Agent", ua);
                req.SetMimeType(mime);
                req.SetTitle(name);
                req.SetNotificationVisibility(DownloadVisibility.VisibleNotifyCompleted);
                req.SetDestinationInExternalFilesDir(_ctx, AndroidEnvironment.DirectoryDownloads, name);

                var dm = (DownloadManager)_ctx.GetSystemService(Context.DownloadService);
                long id = dm.Enqueue(req);

                DownloadReceiver.Register(_ctx.ApplicationContext, id, name, mime);
            }
            catch (Exception ex)
            {
                Toast.MakeText(_ctx, "Ошибка загрузки: " + ex.Message, ToastLength.Long).Show();
                DL.E(ex.ToString());
            }
        }

        // =====================================================
        // Download completion
        // =====================================================
        [BroadcastReceiver(Enabled = true, Exported = false)]
        internal class DownloadReceiver : BroadcastReceiver
        {
            private static long _id;
            private static string _name;
            private static string _mime;

            public static void Register(
    Context ctx,
    long id,
    string name,
    string mime)
            {
                _id = id;
                _name = name;
                _mime = mime;

                var filter =
                    new IntentFilter(
                        DownloadManager.ActionDownloadComplete);

                var receiver = new DownloadReceiver();

                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                {
                    ctx.RegisterReceiver(
                        receiver,
                        filter,
                        ReceiverFlags.NotExported);
                }
                else
                {
                    ctx.RegisterReceiver(
                        receiver,
                        filter);
                }

                DL.I($"Receiver registered for download id={id}");
            }


            public override void OnReceive(Context ctx, Intent intent)
            {
                long id =
                    intent.GetLongExtra(
                        DownloadManager.ExtraDownloadId, -1);

                if (id != _id)
                    return;

                string temp =
                    Path.Combine(
                        ctx.GetExternalFilesDir(
                            AndroidEnvironment.DirectoryDownloads).AbsolutePath,
                        _name);

                DownloadFinalizer.Finalize(
                    ctx, temp, _name, _mime);
            }
        }
    }

    // =====================================================
    // FINAL SAVE (Android 9–16)
    // =====================================================
    internal static class DownloadFinalizer
    {
        public static void Finalize(
            Context ctx,
            string tempPath,
            string fileName,
            string mime)
        {
            if (!File.Exists(tempPath))
                return;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                var r = ctx.ContentResolver;

                var v = new ContentValues();
                v.Put(MediaStore.IMediaColumns.DisplayName, fileName);
                v.Put(MediaStore.IMediaColumns.MimeType, mime);
                v.Put(MediaStore.IMediaColumns.RelativePath, AndroidEnvironment.DirectoryDownloads);
                v.Put(MediaStore.IMediaColumns.IsPending, 1);

                var uri = r.Insert(MediaStore.Downloads.ExternalContentUri, v);

                using (var i = File.OpenRead(tempPath))
                using (var o = r.OpenOutputStream(uri))
                    i.CopyTo(o);

                var u = new ContentValues();
                u.Put(MediaStore.IMediaColumns.IsPending, 0);
                r.Update(uri, u, null, null);

                File.Delete(tempPath);
                OpenFile(ctx, uri, mime);
            }
            else
            {
                var dir = AndroidEnvironment.GetExternalStoragePublicDirectory(
                    AndroidEnvironment.DirectoryDownloads);

                if (!dir.Exists())
                    dir.Mkdirs();

                var dst = Path.Combine(dir.AbsolutePath, fileName);
                File.Copy(tempPath, dst, true);
                File.Delete(tempPath);

                OpenFile(ctx, AndroidUri.FromFile(new Java.IO.File(dst)), mime);
            }
        }

        private static void OpenFile(Context ctx, AndroidUri uri, string mime)
        {
            try
            {
                var i = new Intent(Intent.ActionView);
                i.SetDataAndType(uri, mime);
                i.SetFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
                ctx.StartActivity(i);
            }
            catch { }
        }
    }

    // =====================================================
    // Utils
    // =====================================================
    internal static class StorageCheck
    {
        public static bool HasSpace(Context ctx, long needBytes)
        {
            try
            {
                var dir = ctx.GetExternalFilesDir(AndroidEnvironment.DirectoryDownloads);
                var stat = new StatFs(dir.AbsolutePath);
                return stat.AvailableBytes > needBytes;
            }
            catch
            {
                return true;
            }
        }
    }
}

#nullable restore

