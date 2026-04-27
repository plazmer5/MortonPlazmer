using Android.Content;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MortonPlazmer.Platforms.Android.Cache
{
    internal static class CacheCore
    {
        private const string CacheFolderName = "web_cache";
        private const long MaxCacheSizeBytes = 500L * 1024 * 1024; // 500 MB
        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

        private static string Dir(Context ctx)
        {
            var cacheDir = ctx.CacheDir
                ?? throw new InvalidOperationException("CacheDir is null");

            return Path.Combine(cacheDir.AbsolutePath, CacheFolderName);
        }

        // =========================
        // PUBLIC API
        // =========================

        public static void OnAppStart(Context ctx)
        {
            CleanupAsync(ctx);
        }

        public static void OnResume(Context ctx)
        {
            CleanupAsync(ctx);
        }

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

            CleanupAsync(ctx);
        }

        // =========================
        // CLEANUP LOGIC
        // =========================

        public static void Cleanup(Context ctx)
        {
            var dir = new DirectoryInfo(Dir(ctx));
            if (!dir.Exists) return;

            var files = dir.GetFiles()
                .OrderByDescending(f => f.LastAccessTimeUtc)
                .ToList();

            var now = DateTime.UtcNow;
            long totalSize = 0;

            foreach (var f in files)
            {
                var age = now - f.LastAccessTimeUtc;
                totalSize += f.Length;

                bool tooOld = age > MaxAge;
                bool overLimit = totalSize > MaxCacheSizeBytes;

                if (tooOld || overLimit)
                {
                    try { f.Delete(); }
                    catch { }
                }
            }
        }

        public static void CleanupAsync(Context ctx)
        {
            Task.Run(() => Cleanup(ctx));
        }
    }
}