using Android.App;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui;
using MortonPlazmer.Platforms.Android;
using MortonPlazmer.Platforms.Android.Cache;
using CacheCore = MortonPlazmer.Platforms.Android.Cache.CacheCore;

namespace MortonPlazmer;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges =
        ConfigChanges.ScreenSize |
        ConfigChanges.Orientation |
        ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize |
        ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        CacheCore.OnAppStart(this);
    }

    protected override void OnResume()
    {
        base.OnResume();
        CacheCore.OnResume(this);
    }
}