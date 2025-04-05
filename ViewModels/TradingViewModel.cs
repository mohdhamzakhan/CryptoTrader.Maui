using System.Threading.Tasks;
using CoinswitchTrader.Services;
using Newtonsoft.Json.Linq;

namespace CryptoTrader.Maui.ViewModels
{
    public class TradingViewModel : BaseViewModel
    {
        private readonly TradingService _tradingService;

        private string _selectedPair;
        public string SelectedPair
        {
            get => _selectedPair;
            set => SetProperty(ref _selectedPair, value);
        }

        private decimal _price;
        public decimal Price
        {
            get => _price;
            set => SetProperty(ref _price, value);
        }

        private decimal _quantity;
        public decimal Quantity
        {
            get => _quantity;
            set => SetProperty(ref _quantity, value);
        }

        public Command BuyCommand { get; }
        public Command SellCommand { get; }

        public TradingViewModel(TradingService tradingService)
        {
            _tradingService = tradingService;
            BuyCommand = new Command(async () => await ExecuteTrade("buy"));
            SellCommand = new Command(async () => await ExecuteTrade("sell"));
        }

        private async Task ExecuteTrade(string side)
        {
            if (string.IsNullOrEmpty(SelectedPair) || Price <= 0 || Quantity <= 0)
                return;

            JObject response = side == "buy"
                ? await _tradingService.CreateBuyOrderAsync(SelectedPair, "COINSWITCHX", Price, Quantity)
                : await _tradingService.CreateSellOrderAsync(SelectedPair, "COINSWITCHX", Price, Quantity);

            if (response["success"]?.Value<bool>() == true)
            {
                await App.Current.MainPage.DisplayAlert("Success", $"{side.ToUpper()} Order Placed!", "OK");
            }
            else
            {
                await App.Current.MainPage.DisplayAlert("Error", response["message"]?.ToString(), "OK");
            }
        }
    }
}
