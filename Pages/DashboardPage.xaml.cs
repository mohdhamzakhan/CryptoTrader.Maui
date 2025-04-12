using CoinswitchTrader.Services;
using CryptoTrader.Maui.CoinswitchTrader.Services;
using CryptoTrader.Maui.Model;
using CryptoTrader.Maui.ViewModels;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.Timers;

namespace CryptoTrader.Maui.Pages;

public partial class DashboardPage : ContentPage
{
    private ObservableCollection<Model.MarketData> _marketDataList = new();
    private ObservableCollection<OrderData> _openOrdersList = new();
    private System.Timers.Timer _refreshTimer;

    private readonly TradingService _tradingService;
    private readonly SettingsService _settingsService;
    private readonly DashboardServices _dashboardService;
    public DashboardPage()
    {
        InitializeComponent();
        MarketListView.ItemsSource = _marketDataList;
        OpenOrdersListView.ItemsSource = _openOrdersList;

        StartAutoRefresh();
        _settingsService = new SettingsService();
        _tradingService = new TradingService(_settingsService.SecretKey, _settingsService.ApiKey, _settingsService);
        _dashboardService = new DashboardServices(_tradingService, _settingsService);
    }
    private void StartAutoRefresh()
    {
        _refreshTimer = new System.Timers.Timer(10000); // 10 seconds
        _refreshTimer.Elapsed += async (s, e) => await RefreshData();
        _refreshTimer.Start();
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        await RefreshData();
    }

    private async Task RefreshData()
    {
        try
        {
            // Fetch market data
            var marketData = await FetchMarketDataAsync();
            if (marketData != null)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _marketDataList.Clear();
                    foreach (var item in marketData)
                        _marketDataList.Add(item);
                });
            }

            // Fetch open orders
            var openOrders = await FetchOpenOrdersAsync();
            if (openOrders != null)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _openOrdersList.Clear();
                    foreach (var order in openOrders)
                        _openOrdersList.Add(order);
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during RefreshData: {ex}");
        }
    }


    private async Task<List<Model.MarketData>> FetchMarketDataAsync()
    {
        try
        {
            // TODO: Call your live market API here and parse the response
            // For now, returning dummy data
            await Task.Delay(500); // simulate network delay

            List<string> tradingPair = _settingsService.ScalpingSymbols.Split(',')
                                     .Select(s => s.Trim() + "/INR")
                                     .ToList();
            var data = await _dashboardService.GetMarketDataAsync(tradingPair, new List<string> { "COINSWITCHX" });

            return data.Select(item => new Model.MarketData
            {
                Symbol = $"{item.Symbol}/INR",
                Bid = item.Bid,
                Ask = item.Ask
            }).ToList();

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching market data: {ex.Message}");
            return null;
        }
    }

    private async Task<List<OrderData>> FetchOpenOrdersAsync()
    {
        try
        {
            // Call your API to get open orders
            using var client = new HttpClient();
            var response = await client.GetAsync("YOUR_OPEN_ORDERS_API_URL");
            if (response.IsSuccessStatusCode)
            {
                var jsonString = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(jsonString);

                var orders = json["data"]?["orders"]?.ToObject<List<OrderData>>();
                return orders ?? new List<OrderData>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching open orders: {ex.Message}");
        }

        return null;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _refreshTimer?.Stop();
    }
}