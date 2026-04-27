#nullable disable

using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Provider;
using Android.Webkit;
using Android.Widget;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Java.Interop;
using Java.Nio.FileNio.Attributes;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Handlers;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AndroidEnvironment = Android.OS.Environment;
using AndroidResource = Android.Resource;
using AndroidUri = Android.Net.Uri;
using AndroidUtil = Android.Util;
using AndroidWebView = Android.Webkit.WebView;
using AndroidWidget = Android.Widget;
using JavaIO = Java.IO;
using AndroidNet = Android.Net;
using AndroidGraphics = Android.Graphics;

namespace MortonPlazmer.Platforms.Android
{
    // =====================================================
    // LOG
    // =====================================================
    internal static class CustomWebViewHandlerLog
    {
        public const string TAG = "CustomWebViewHandlerLog-ENGINE";
        public static void I(string m) => AndroidUtil.Log.Info(TAG, m);
        public static void E(string m) => AndroidUtil.Log.Error(TAG, m);
    }

    // =====================================================
    // HTTP CORE (SINGLETON)
    // =====================================================
    internal static class HttpCore
    {
        public static readonly HttpClient Client = new HttpClient()
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
    }

    // =====================================================
    // CACHE CORE
    // =====================================================
    internal static class CacheCore
    {
        private static string Dir(Context ctx)
            => Path.Combine(ctx.CacheDir.AbsolutePath, "web_cache");

        public static string Key(string url)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(url));
            return Convert.ToHexString(hash);
        }

        public static string GetPath(Context ctx, string url)
            => Path.Combine(Dir(ctx), Key(url));

        public static bool Exists(Context ctx, string url)
            => File.Exists(GetPath(ctx, url));

        public static Stream Open(Context ctx, string url)
            => File.OpenRead(GetPath(ctx, url));

        public static void Save(Context ctx, string url, byte[] data)
        {
            Directory.CreateDirectory(Dir(ctx));
            File.WriteAllBytes(GetPath(ctx, url), data);
        }
    }

    // =====================================================
    // STORAGE ENGINE (ANDROID 9–16 SAFE)
    // =====================================================
#pragma warning disable CS0168
    internal static class StorageEngine
    {
        public static async Task<AndroidNet.Uri> Save(
            Context ctx,
            byte[] data,
            string mime,
            string name)
        {
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                {
#pragma warning disable CA1416

                    var values = new ContentValues();
                    values.Put(MediaStore.IMediaColumns.DisplayName, name);
                    values.Put(MediaStore.IMediaColumns.MimeType, mime);
                    values.Put(MediaStore.IMediaColumns.RelativePath,
                        AndroidEnvironment.DirectoryDownloads);

                    var uri = ctx.ContentResolver.Insert(
                        MediaStore.Downloads.ExternalContentUri,
                        values);

                    if (uri == null)
                        throw new Exception("MediaStore returned null URI");

                    using var stream = ctx.ContentResolver.OpenOutputStream(uri);

                    if (stream == null)
                        throw new Exception("OpenOutputStream returned null");

                    await stream.WriteAsync(data, 0, data.Length);

                    return uri;
                }
                else
                {
                    var dir = AndroidEnvironment.GetExternalStoragePublicDirectory(
                        AndroidEnvironment.DirectoryDownloads);

                    if (!dir.Exists()) dir.Mkdirs();

                    var file = new Java.IO.File(dir, name);

                    await File.WriteAllBytesAsync(file.AbsolutePath, data);

                    var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                        ctx,
                        ctx.PackageName + ".fileprovider",
                        file);

                    return uri;
                }
            }
            catch (Exception ex)
            {
#if DEBUG
    AndroidUtil.Log.Error("StorageEngine", ex.ToString());
#endif

                AndroidWidget.Toast.MakeText(
                    ctx,
                    "Ошибка загрузки файла",
                    AndroidWidget.ToastLength.Long
                )?.Show();

                throw;
            }
        }
    }

    // =====================================================
    // DOWNLOAD ENGINE
    // =====================================================
    internal static class DownloadEngine
    {
        public static async Task<AndroidUri> Download(
            Context ctx,
            string url,
            string mime,
            string name)
        {
            try
            {
                if (CacheCore.Exists(ctx, url))
                {
                    using var cached = CacheCore.Open(ctx, url);

                    using var ms = new MemoryStream();
                    await cached.CopyToAsync(ms);

                    return await StorageEngine.Save(ctx, ms.ToArray(), mime, name);
                }

                var response = await HttpCore.Client.GetAsync(url);
                var bytes = await response.Content.ReadAsByteArrayAsync();

                CacheCore.Save(ctx, url, bytes);

                return await StorageEngine.Save(ctx, bytes, mime, name);
            }
            catch (Exception ex)
            {
                CustomWebViewHandlerLog.E("Download error: " + ex);
                throw;
            }
        }
    }

    // =====================================================
    // NOTIFICATIONS
    // =====================================================
    internal static class NotifyEngine
    {
        private const string CHANNEL = "downloads";

        public static void Init(Context ctx)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var ch = new NotificationChannel(
                    CHANNEL,
                    "Downloads",
                    NotificationImportance.Default);

                var nm = (NotificationManager)ctx.GetSystemService(Context.NotificationService);
                nm.CreateNotificationChannel(ch);
            }
        }

        public static void ShowDone(
            Context ctx,
            string name,
            AndroidUri uri,
            string mime)
        {
            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, mime);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission |
                            ActivityFlags.NewTask);

            var chooser = Intent.CreateChooser(intent, "Open file");

            var pi = PendingIntent.GetActivity(
                ctx,
                0,
                chooser,
                PendingIntentFlags.Immutable |
                PendingIntentFlags.UpdateCurrent);

            var n = new Notification.Builder(ctx, CHANNEL)
                .SetContentTitle("Download complete")
                .SetContentText(name)
                .SetSmallIcon(AndroidResource.Drawable.StatSysDownloadDone)
                .SetContentIntent(pi)
                .SetAutoCancel(true)
                .Build();

            NotificationManager.FromContext(ctx)
                .Notify(new Random().Next(), n);
        }
    }

    // =====================================================
    // WEBVIEW HANDLER (UI ONLY)
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

            s.CacheMode = CacheModes.CacheElseNetwork;

            NotifyEngine.Init(wv.Context);

            wv.SetBackgroundColor(AndroidGraphics.Color.Black);

            wv.SetDownloadListener(new DLListener(wv.Context));
        }
    }

    // =====================================================
    // DOWNLOAD LISTENER
    // =====================================================
    internal class DLListener : Java.Lang.Object, IDownloadListener
    {
        private readonly Context _ctx;

        public DLListener(Context ctx) => _ctx = ctx;

        public async void OnDownloadStart(
            string url,
            string ua,
            string cd,
            string mime,
            long len)
        {
            var activity = Platform.CurrentActivity;

            // 🔴 ВАЖНО: проверка для Android 9
            if (Build.VERSION.SdkInt < BuildVersionCodes.Q)
            {
                if (ContextCompat.CheckSelfPermission(
                        activity,
                        Manifest.Permission.WriteExternalStorage)
                    != Permission.Granted)
                {
                    ActivityCompat.RequestPermissions(
                        activity,
                        new[] { Manifest.Permission.WriteExternalStorage },
                        1001);

                    AndroidWidget.Toast.MakeText(
                        activity,
                        "Разрешите доступ к хранилищу",
                        AndroidWidget.ToastLength.Long).Show();

                    return; // ❗ остановить загрузку
                }
            }

            try
            {
                if (string.IsNullOrEmpty(mime))
                    mime = "application/octet-stream";

                string name = URLUtil.GuessFileName(url, cd, mime);

                var uri = await DownloadEngine.Download(_ctx, url, mime, name);

                NotifyEngine.ShowDone(_ctx, name, uri, mime);
            }
            catch (Exception ex)
            {
                CustomWebViewHandlerLog.E("Download failed: " + ex);

#if DEBUG
    AndroidWidget.Toast.MakeText(_ctx, ex.ToString(), ToastLength.Long).Show();
#else
                AndroidWidget.Toast.MakeText(_ctx, "Ошибка загрузки", ToastLength.Long).Show();
#endif
            }
        }
    }
}