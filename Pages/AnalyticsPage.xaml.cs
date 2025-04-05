namespace CryptoTrader.Maui.Pages;

public partial class AnalyticsPage : ContentPage
{
    public AnalyticsPage()
    {
        InitializeComponent();
        //BindingContext = new AnalyticsViewModel();
    }

    private async void OnViewHistoryClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Trade History", "Viewing trade history...", "OK");
    }
    private async void OnExportReportsClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Reports", "Exporting reports...", "OK");
    }

}