using CoinswitchTrader.Services;
using CryptoTrader.Maui.ViewModels;

namespace CryptoTrader.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsService _settingsService;

    public SettingsPage()
    {
        InitializeComponent();
        _settingsService = new SettingsService();
        LoadSettings();
    }

    private void LoadSettings()
    {
        if(string.IsNullOrEmpty(_settingsService.ApiKey))
        {
            _settingsService.SecretKey = "aa18ca959d1a13d7635af410ce76ce09a67952cb1f566b8ed50f5b6c36363fbb";
            _settingsService.ApiKey = "9f7e37bc68a33d5addefc7ceb957cd5336aef9b7b3404c00f4a0af1bcc49c58d";
        }
        ApiKeyEntry.Text = _settingsService.ApiKey;
        SecretKeyEntry.Text = _settingsService.SecretKey;
        TdsRateEntry.Text = _settingsService.TdsRate.ToString();
        TradingFeeEntry.Text = _settingsService.TradingFeeRate.ToString();
        ExchangeEntry.Text = _settingsService.DefaultExchange;
        TradingPairEntry.Text = _settingsService.DefaultTradingPair;
        ScalpingToggle.IsToggled = _settingsService.ScalpingEnabled;
        ScalpingProfitThresholdEntry.Text = _settingsService.ScalpingProfitThreshold.ToString();
        ScalpingMaxTradeSizeEntry.Text = _settingsService.ScalpingMaxTradeSize.ToString();
        ScalpingPairEntry.Text = _settingsService.ScalpingSymbols.ToString();
        BidPosition.Text = _settingsService.BidPosition.ToString();
        AskPosition.Text = _settingsService.AskPosition.ToString();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        try
        {
            _settingsService.ApiKey = ApiKeyEntry.Text;
            _settingsService.SecretKey = SecretKeyEntry.Text;
            _settingsService.TdsRate = decimal.Parse(TdsRateEntry.Text);
            _settingsService.TradingFeeRate = decimal.Parse(TradingFeeEntry.Text);
            _settingsService.DefaultExchange = ExchangeEntry.Text;
            _settingsService.DefaultTradingPair = TradingPairEntry.Text;
            _settingsService.ScalpingEnabled = ScalpingToggle.IsToggled;
            _settingsService.ScalpingProfitThreshold = decimal.Parse(ScalpingProfitThresholdEntry.Text);
            _settingsService.ScalpingMaxTradeSize = decimal.Parse(ScalpingMaxTradeSizeEntry.Text);
            _settingsService.ScalpingSymbols = ScalpingPairEntry.Text;

            await DisplayAlert("Success", "Settings saved successfully!", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to save settings: {ex.Message}", "OK");
        }
    }
}