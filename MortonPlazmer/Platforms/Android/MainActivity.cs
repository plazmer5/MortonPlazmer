using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Webkit;
using MortonPlazmer.Platforms.Android;

namespace MortonPlazmer
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public partial class MainActivity : MauiAppCompatActivity
    {
        public UniversalDownloadListener? DownloadListener { get; set; }

        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (requestCode == 1001 && resultCode == Result.Ok && data?.Data != null)
            {
                DownloadListener?.OnFileSelected(data.Data);
            }
        }
    }

}
