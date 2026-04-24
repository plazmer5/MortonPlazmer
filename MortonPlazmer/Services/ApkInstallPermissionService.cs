#if ANDROID
using Android.Content;
using Android.OS;
using Android.Provider;
#endif

public static class ApkInstallPermissionService
{
    public static bool HasInstallPermission()
    {
#if ANDROID
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            return Android.App.Application.Context.PackageManager.CanRequestPackageInstalls();
        }
#endif
        return true;
    }

    public static void OpenInstallSettings()
    {
#if ANDROID
        var context = Android.App.Application.Context;

        var intent = new Intent(Settings.ActionManageUnknownAppSources);
        intent.SetData(Android.Net.Uri.Parse("package:" + context.PackageName));
        intent.SetFlags(ActivityFlags.NewTask);

        context.StartActivity(intent);
#endif
    }
}