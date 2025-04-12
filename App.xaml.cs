#if ANDROID
using Android.Content;
using CryptoTrader.Maui.Platforms.Android;
#endif

namespace CryptoTrader.Maui
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            MainPage = new AppShell();
            StartTradingService();
        }

        private void StartTradingService()
        {
#if ANDROID26_0_OR_GREATER
        var context = Android.App.Application.Context;
        var intent = new Intent(context, typeof(TradingBackgroundService));
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
#endif
        }
    }
}
