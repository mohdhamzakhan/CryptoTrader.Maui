using CoinswitchTrader.Services;

namespace CryptoTrader.Maui.Pages;

public partial class ScalpingPage : ContentPage
{
    private readonly ScalpingService _scalpingService;
    private readonly SettingsService _settingsService;
    private readonly HistoricalDataService _historicalDataService;
    private readonly TradingService _tradeService;

    public ScalpingPage()
    {
        InitializeComponent();

        // Load API keys from SettingsService
        _settingsService = new SettingsService();
        string apiKey = _settingsService.ApiKey;
        string secretKey = _settingsService.SecretKey;
        _tradeService =new TradingService(apiKey, secretKey,_settingsService);
        _historicalDataService = new HistoricalDataService(_tradeService, _settingsService);

        // Create TradingService with stored keys
        var tradingService = new TradingService(secretKey, apiKey, _settingsService);
        _scalpingService = new ScalpingService(tradingService, _settingsService,_historicalDataService);
    }

    private async void OnStartScalpingClicked(object sender, EventArgs e)
    {
        _scalpingService.StartScalping(new List<string> { "BTC/INR" }, new List<string> { "COINSWITCHX" });

        await DisplayAlert("Scalping", "Scalping started with saved API keys!", "OK");
    }

    private async void OnStopScalpingClicked(object sender, EventArgs e)
    {
        _scalpingService.StopScalping();

        await DisplayAlert("Scalping", "Scalping stopped!", "OK");
    }
}
