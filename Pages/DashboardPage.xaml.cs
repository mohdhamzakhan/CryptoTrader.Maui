using CoinswitchTrader.Services;
using CryptoTrader.Maui.ViewModels;

namespace CryptoTrader.Maui.Pages;

public partial class DashboardPage : ContentPage
{
    public DashboardPage()
    {
        InitializeComponent();
        BindingContext = new DashboardViewModel(new TradingService("aa18ca959d1a13d7635af410ce76ce09a67952cb1f566b8ed50f5b6c36363fbb", "9f7e37bc68a33d5addefc7ceb957cd5336aef9b7b3404c00f4a0af1bcc49c58d",  new SettingsService()));
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Refresh", "Market data refreshed!", "OK");
    }
    private void OnStartServiceClicked(object sender, EventArgs e)
    {
#if ANDROID
    var context = Android.App.Application.Context;
    var intent = new Android.Content.Intent(context, typeof(CryptoTrader.Maui.Platforms.Android.TradingBackgroundService));

    if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
    {
        context.StartForegroundService(intent);
        Android.Widget.Toast.MakeText(context, "Trading Service Started", Android.Widget.ToastLength.Short)?.Show();
    }
    else
    {
        context.StartService(intent);
    }
#endif
    }

}