using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using CryptoTrader.Maui.Platforms.Android;

namespace CryptoTrader.Maui
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            var intent = new Intent(this, typeof(TradingBackgroundService));

            if(Build.VERSION.SdkInt >= BuildVersionCodes.OMr1)
                StartForegroundService(intent);
            else
                StartService(intent);
        }

        //protected override void OnResume()
        //{
        //    base.OnResume();

        //    // Delay service start to avoid Android 12+ restrictions
        //    Task.Delay(1000).ContinueWith(_ =>
        //    {
        //        var intent = new Intent(this, typeof(TradingBackgroundService));
        //        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        //        {
        //            StartForegroundService(intent);
        //        }
        //        else
        //        {
        //            StartService(intent);
        //        }
        //    }, TaskScheduler.Default);
        //}

    }

}
