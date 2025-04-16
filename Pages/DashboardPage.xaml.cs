using CoinswitchTrader.Services;
using CryptoTrader.Maui.CoinswitchTrader.Services;
using CryptoTrader.Maui.Model;
using CryptoTrader.Maui.Models;
using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows.Input;

#if ANDROID
using Android.Content;
using CryptoTrader.Maui.Platforms.Android;
#endif

namespace CryptoTrader.Maui.Pages;

public partial class DashboardPage : ContentPage
{
    private ObservableCollection<Model.MarketData> _marketDataList = new();
    private ObservableCollection<OrderData> _openOrdersList = new();
    private System.Timers.Timer _refreshTimer;
    public ICommand CancelOrderCommand { get; }

    private readonly TradingService _tradingService;
    private readonly SettingsService _settingsService;
    private readonly DashboardServices _dashboardService;
    private readonly FutureTradingService _futureTradingService;
    public DashboardPage()
    {
        InitializeComponent();
        StartAutoRefresh();
        _settingsService = new SettingsService();
        _tradingService = new TradingService(_settingsService.SecretKey, _settingsService.ApiKey, _settingsService);
        _dashboardService = new DashboardServices(_tradingService, _settingsService);
        _futureTradingService = new FutureTradingService();
        CancelOrderCommand = new Command<OrderModel>(OnCancelOrder);
       
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
                    MarketPrice.ItemsSource = marketData;
                });
            }

            var order = await FetchOpenOrdersAsync();
            var InrBalance = await _tradingService.GetBalanceCurrencyAsync("INR");
            var usdtBalance = await _futureTradingService.GetFutureBalance("USDT");
            if (order != null)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    OrdersCollectionView.ItemsSource = order;
                    BalanceLabel.Text = $"INR Balance: {InrBalance}";
                    FutureBalanceLabel.Text = $"USDT Balance: {usdtBalance}";
                });
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during RefreshData: {ex}");
        }
    }
    [SupportedOSPlatform("android26.0")]
    private void OnStartTradingClicked(object sender, EventArgs e)
    {
#if ANDROID
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
    private void OnCancelOrder(OrderModel order)
    {
        if (order == null)
            return;

        // Logic to cancel the order
        // For example:
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
                Symbol = $"{item.Symbol}",
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

    private async void OnShareLogsClicked(object sender, EventArgs e)
    {
        try
        {
           await Logger.ShareLogFileAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to share logs: {ex.Message}", "OK");
        }
    }

    private async void OnDeleteLogsClicked(object sender, EventArgs e)
    {
        try
        {
            Logger.Delete();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to delete logs: {ex.Message}", "OK");
        }
    }


    private async Task<List<OrderModel>> FetchOpenOrdersAsync()
    {
        try
        {
           return await _dashboardService.GetCurrentOpenOrdersAsync();


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