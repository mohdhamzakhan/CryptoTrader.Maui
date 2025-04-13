using CoinswitchTrader.Services;
using CryptoTrader.Maui.CoinswitchTrader.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CryptoTrader.Maui.ViewModels
{
    public class ChandelierViewModel : INotifyPropertyChanged
    {
        private readonly FutureTradingService _tradingService;
        private bool _isConnected;
        private string _statusMessage;
        private string _apiKey;
        private string _secretKey;
        private string _selectedSymbol = "BTCUSDT";
        private string _selectedInterval = "1h";
        private ChandelierExitSettings _settings;
        private SettingsService _settingsService;

        public ObservableCollection<FutureCandleData> Candles => _tradingService.Candles;
        public ObservableCollection<TradingSignal> Signals => _tradingService.Signals;
        public ObservableCollection<Position> Positions => _tradingService.Positions;

        public ObservableCollection<string> AvailableIntervals { get; } = new ObservableCollection<string>
        {
            "1m", "5m", "15m", "30m", "1h", "4h", "1d"
        };

        public ObservableCollection<string> AvailableSymbols { get; } = new ObservableCollection<string>
        {
            "BTCUSDT", "ETHUSDT", "SOLUSDT", "BNBUSDT", "ADAUSDT", "XRPUSDT", "DOGEUSDT", "LTCUSDT"
        };

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public string ApiKey
        {
            get => _apiKey;
            set
            {
                _apiKey = value;
                OnPropertyChanged();
            }
        }

        public string SecretKey
        {
            get => _secretKey;
            set
            {
                _secretKey = value;
                OnPropertyChanged();
            }
        }

        public string SelectedSymbol
        {
            get => _selectedSymbol;
            set
            {
                if (_selectedSymbol != value)
                {
                    _selectedSymbol = value;
                    OnPropertyChanged();
                    if (IsConnected)
                    {
                        _tradingService.ChangeSymbol(value);
                    }
                }
            }
        }

        public string SelectedInterval
        {
            get => _selectedInterval;
            set
            {
                if (_selectedInterval != value)
                {
                    _selectedInterval = value;
                    OnPropertyChanged();
                    if (IsConnected)
                    {
                        _tradingService.ChangeInterval(value);
                    }
                }
            }
        }

        public ChandelierExitSettings Settings
        {
            get => _settings;
            set
            {
                _settings = value;
                OnPropertyChanged();
                if (IsConnected)
                {
                    _tradingService.UpdateSettings(value);
                }
            }
        }

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand SaveSettingsCommand { get; }

        public ChandelierViewModel(FutureTradingService tradingService)
        {
            _tradingService = tradingService;
            _settings = new ChandelierExitSettings();
            _settingsService = new SettingsService();
            _apiKey = _settingsService.ApiKey;
            _secretKey = _settingsService.SecretKey;

            // Initialize commands
            ConnectCommand = new Command(Connect, CanConnect);
            DisconnectCommand = new Command(Disconnect, () => IsConnected);
            SaveSettingsCommand = new Command(SaveSettings);

            // Subscribe to service events
            _tradingService.NewSignalGenerated += OnNewSignalGenerated;
            _tradingService.ErrorOccurred += OnErrorOccurred;
        }

        private bool CanConnect()
        {
            return !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(SecretKey) && !IsConnected;
        }

        private void Connect()
        {
            try
            {
                _tradingService.Initialize(ApiKey, SecretKey);
                ConnectAsync().Wait();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Connection failed: {ex.Message}";
            }
        }

        private async Task ConnectAsync()
        {
            try
            {
                _tradingService.Initialize(SecretKey,ApiKey);
                string symbol = "btcusdt";
                string exchnage = "EXCHANGE_2";

                var str = await _tradingService.GetLeverage(symbol, exchnage);
                StatusMessage = "Connected to trading service";
            }
            catch (Exception ex)
            {
                IsConnected = false;
                StatusMessage = $"Connection failed: {ex.Message}";
            }
        }

        private void Disconnect()
        {
            IsConnected = false;
            StatusMessage = "Disconnected from trading service";
        }

        private void SaveSettings()
        {
            _tradingService.UpdateSettings(Settings);
            StatusMessage = "Strategy settings updated";
        }

        private void OnNewSignalGenerated(object sender, TradingSignal signal)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusMessage = $"New {signal.Type} signal for {signal.Symbol} at {signal.Price}";
            });
        }

        private void OnErrorOccurred(object sender, string errorMessage)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusMessage = $"Error: {errorMessage}";
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
