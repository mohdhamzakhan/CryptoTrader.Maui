using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Widget;
using CryptoTrader.Maui.Platforms.Android;

namespace CryptoTrader.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);



        // Request notification permission for Android 13+
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            if (CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted)
            {
                RequestPermissions(new[] { Android.Manifest.Permission.PostNotifications }, 100);
            }
        }
        // Start trading background service when app launches
        StartTradingService();
    }
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == 100)
        {
            if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
            {
                // Permission was granted
                Log.Debug("MainActivity", "Notification permission granted");
            }
            else
            {
                // Permission was denied
                Log.Debug("MainActivity", "Notification permission denied");
                Toast.MakeText(this, "Notification permission is required for service status", ToastLength.Long).Show();
            }
        }
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Ensure service is running when app is resumed
        StartTradingService();
    }

    private void StartTradingService()
    {
        try
        {
            Log.Debug("MainActivity", "Starting trading service...");
            // When starting the background service
            Intent serviceIntent = new Intent(Android.App.Application.Context, typeof(TradingBackgroundService));

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                Android.App.Application.Context.StartForegroundService(serviceIntent);
            }
            else
            {
                Android.App.Application.Context.StartService(serviceIntent);
            }


            Log.Debug("MainActivity", "Service start requested");
        }
        catch (Exception ex)
        {
            Log.Error("MainActivity", $"Failed to start trading service: {ex.Message}");
        }
    }
}