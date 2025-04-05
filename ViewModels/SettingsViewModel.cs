using CoinswitchTrader.Services;
namespace CryptoTrader.Maui.ViewModels
{
    public class SettingsViewModel : BaseViewModel
    {
        private readonly SettingsService _settingsService;

        public Command SaveSettingsCommand { get; }

        private string _apiKey;
        public string ApiKey
        {
            get => _apiKey;
            set => SetProperty(ref _apiKey, value);
        }

        private string _secretKey;
        public string SecretKey
        {
            get => _secretKey;
            set => SetProperty(ref _secretKey, value);
        }

        private decimal _tdsRate;
        public decimal TdsRate
        {
            get => _tdsRate;
            set => SetProperty(ref _tdsRate, value);
        }

        private decimal _tradingFee;
        public decimal TradingFee
        {
            get => _tradingFee;
            set => SetProperty(ref _tradingFee, value);
        }

        public SettingsViewModel(SettingsService settingsService)
        {
            _settingsService = settingsService;
            SaveSettingsCommand = new Command(SaveSettings);
            LoadSettings();
        }

        private void LoadSettings()
        {
            ApiKey = _settingsService.ApiKey;
            SecretKey = _settingsService.SecretKey;
            TdsRate = _settingsService.TdsRate;
            TradingFee = _settingsService.TradingFeeRate;
        }

        private void SaveSettings()
        {
            _settingsService.ApiKey = ApiKey;
            _settingsService.SecretKey = SecretKey;
            _settingsService.TdsRate = TdsRate;
            _settingsService.TradingFeeRate = TradingFee;
        }
    }
}
