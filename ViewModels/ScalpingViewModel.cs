using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CoinswitchTrader.Services;


namespace CryptoTrader.Maui.ViewModels
{
    public class ScalpingViewModel : BaseViewModel
    {
        private readonly ScalpingService _scalpingService;

        public ObservableCollection<ScalpingTrade> ScalpingTrades { get; set; }
        public Command StartScalpingCommand { get; }
        public Command StopScalpingCommand { get; }

        private bool _isScalpingEnabled;
        public bool IsScalpingEnabled
        {
            get => _isScalpingEnabled;
            set => SetProperty(ref _isScalpingEnabled, value);
        }

        public ScalpingViewModel(ScalpingService scalpingService)
        {
            _scalpingService = scalpingService;
            ScalpingTrades = new ObservableCollection<ScalpingTrade>();
            StartScalpingCommand = new Command(StartScalping);
            StopScalpingCommand = new Command(StopScalping);
        }

        private void StartScalping()
        {
            IsScalpingEnabled = true;
            _scalpingService.StartScalping(new List<string> { "BTC/INR" }, new List<string> { "COINSWITCHX" });
        }

        private void StopScalping()
        {
            IsScalpingEnabled = false;
            _scalpingService.StopScalping();
        }
    }

    public class ScalpingTrade
    {
        public string Symbol { get; set; }
        public decimal Profit { get; set; }
    }
}
