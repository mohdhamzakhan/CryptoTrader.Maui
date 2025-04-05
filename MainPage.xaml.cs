#if ANDROID
using Android.Content;
using Android.Content.PM;
#endif
namespace CryptoTrader.Maui
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }
#if ANDROID
        private void StartService(object sender, EventArgs e)
        {
            var intent = new Intent(Android.App.Application.Context, typeof(ForegroundService));
            Android.App.Application.Context.StartForegroundService(intent);
        }

        private void StopService(object sender, EventArgs e)
        {
            var intent = new Intent(Android.App.Application.Context, typeof(ForegroundService));
            Android.App.Application.Context.StopService(intent);
        }
#endif
    }
}

