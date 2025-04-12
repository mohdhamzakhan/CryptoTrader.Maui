using CoinswitchTrader.Services;
using CryptoTrader.Maui.Models;
using Microsoft.Maui.Controls;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;

namespace CryptoTrader.Maui.Pages;

public partial class AnalyticsPage : ContentPage
{
    private readonly TradingService _tradingService;
    private readonly SettingsService _settingsService;
    private ObservableCollection<OrderModel> _orders = new(); // ✅ Add this!

    public AnalyticsPage()
    {
        InitializeComponent();

        _settingsService = new SettingsService();
        string apiKey = _settingsService.ApiKey;
        string secretKey = _settingsService.SecretKey;

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(secretKey))
        {
            DisplayAlert("Error", "API Key or Secret Key is missing.", "OK");
            return;
        }

        _tradingService = new TradingService(secretKey, apiKey, _settingsService);
    }

    private async void OnViewHistoryClicked(object sender, EventArgs e)
    {
        await LoadOrdersAsync();
    }

    private async Task LoadOrdersAsync()
    {
        try
        {
            string tradingPairsString = string.Join(",",
                _settingsService.ScalpingSymbols.Split(',')
                .Select(s => s.Trim() + "/INR")
            );

            // API call to get orders
            var mergedOrders = await _tradingService.GetOrderHistoryAsync(tradingPairsString, "COINSWITCHX");
            OrdersCollectionView.ItemsSource = new ObservableCollection<OrderModel>(mergedOrders);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }
}
