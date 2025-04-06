using CoinswitchTrader.Services;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;

namespace CryptoTrader.Maui.Pages;

public partial class TradingPage : ContentPage
{
    private readonly TradingService _tradingService;
    private readonly SettingsService _settingsService;
    private readonly HistoricalDataService _historicalDataService;
    private readonly TrendFollowService _trendFollowService;
    private bool _isRunning = false;
    public ObservableCollection<string> OrderLogs { get; set; }

    public List<TradingStrategy> TradingStrategies { get; set; }
    public TradingStrategy SelectedStrategy { get; set; }

    public TradingPage()
    {
        InitializeComponent();
        _settingsService = new SettingsService();
        string apiKey = _settingsService.ApiKey;
        string secretKey = _settingsService.SecretKey;
        _tradingService = new TradingService(secretKey, apiKey, _settingsService);
        _historicalDataService = new HistoricalDataService(_tradingService, _settingsService);
        OrderLogs = new ObservableCollection<string>();
        OrderListView.ItemsSource = OrderLogs;
        _trendFollowService = new TrendFollowService(_tradingService, _settingsService, _historicalDataService);

        // Populate the TradingStrategy dropdown
        TradingStrategies = Enum.GetValues(typeof(TradingStrategy)).Cast<TradingStrategy>().ToList();
        SelectedStrategy = _settingsService.SelectedStrategy; // Load saved strategy

        LoadSettings();
        BindingContext = this;
    }

    private void LoadSettings()
    {
        TdsRateEntry.Text = _settingsService.TdsRate.ToString("0.##");
        TradingFeeEntry.Text = _settingsService.TradingFeeRate.ToString("0.##");
        ScalpingToggle.IsToggled = _settingsService.ScalpingEnabled;
    }

    private async void PlaceBuyOrderClicked(object sender, EventArgs e)
    {
        try
        {
            string symbol = SymbolEntry.Text;
            string exchange = ExchangeEntry.Text;
            decimal price = Convert.ToDecimal(PriceEntry.Text);
            decimal quantity = Convert.ToDecimal(QuantityEntry.Text);

            var response = await _tradingService.CreateBuyOrderAsync(symbol, exchange, price, quantity);
            LogOrder("BUY", response);
            await DisplayAlert("Trade", "Buy order placed successfully!", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to place buy order: {ex.Message}", "OK");
        }
    }

    private async void PlaceSellOrderClicked(object sender, EventArgs e)
    {
        try
        {
            string symbol = SymbolEntry.Text;
            string exchange = ExchangeEntry.Text;
            decimal price = Convert.ToDecimal(PriceEntry.Text);
            decimal quantity = Convert.ToDecimal(QuantityEntry.Text);

            var response = await _tradingService.CreateSellOrderAsync(symbol, exchange, price, quantity);
            LogOrder("SELL", response);
            await DisplayAlert("Trade", "Sell order placed successfully!", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to place sell order: {ex.Message}", "OK");
        }
    }

    private async void StartScalpingClicked(object sender, EventArgs e)
    {
        _settingsService.ScalpingEnabled = true;
        _settingsService.ActiveStrategy = SelectedStrategy; // Save selected strategy
        if (_settingsService.ScalpingEnabled)
        {
            var scalpingService = new ScalpingService(_tradingService, _settingsService, _historicalDataService);
            List<string> scalpingPair = _settingsService.ScalpingSymbols.Split(',')
                                 .Select(s => s.Trim() + "/INR")
                                 .ToList();
            scalpingService.StartScalping(scalpingPair, new List<string> { "COINSWITCHX" });
            OrderLogs.Insert(0, "Scalping started...");
            await DisplayAlert("Scalping", "Scalping started successfully!", "OK");
        }
        else
        {
            OrderLogs.Insert(0, "Enable scalping in settings first.");
            await DisplayAlert("Scalping", "Please enable scalping in settings.", "OK");
        }
    }

    private void StartTradingClicked(object sender, EventArgs e)
    {
        var trendFollow = new TrendFollowService(_tradingService, _settingsService, _historicalDataService);
        if (btnTrading.Text == "Start Trading")
        {
            List<string> tradingPair = _settingsService.ScalpingSymbols.Split(',')
                                     .Select(s => s.Trim() + "/INR")
                                     .ToList();
            trendFollow.StartTrading(tradingPair, new List<string> { "COINSWITCHX" });
            btnTrading.Text = "Stop Trading";
        }
        else
        {
            btnTrading.Text = "Start Trading";
            trendFollow.StopTrading();
        }
    }

    private void StartNewTradingClicked(object sender, EventArgs e)
    {
        var trendFollow = new NewTrendFollowService(_tradingService, _settingsService, _historicalDataService);
        if (btnNewTrading.Text == "Start Trading")
        {
            List<string> tradingPair = _settingsService.ScalpingSymbols.Split(',')
                                     .Select(s => s.Trim() + "/INR")
                                     .ToList();
            trendFollow.StartTrading(tradingPair, new List<string> { "COINSWITCHX" });
            btnNewTrading.Text = "Stop Trading";
        }
        else
        {
            btnNewTrading.Text = "Start Trading";
            trendFollow.StopTrading();
        }
    }

    private void LogOrder(string type, JObject response)
    {
        OrderLogs.Insert(0, $"{DateTime.Now:HH:mm:ss} - {type} Order: {response}");
    }

    private void SaveSettingsClicked(object sender, EventArgs e)
    {
        try
        {
            _settingsService.TdsRate = Convert.ToDecimal(TdsRateEntry.Text);
            _settingsService.TradingFeeRate = Convert.ToDecimal(TradingFeeEntry.Text);
            _settingsService.ScalpingEnabled = ScalpingToggle.IsToggled;
            _settingsService.SelectedStrategy = SelectedStrategy; // Save selected strategy

            DisplayAlert("Settings", "Settings saved successfully!", "OK");
        }
        catch (Exception ex)
        {
            DisplayAlert("Error", $"Failed to save settings: {ex.Message}", "OK");
        }
    }
}