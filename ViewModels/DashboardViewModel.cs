using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CoinswitchTrader.Services;
using Newtonsoft.Json.Linq;

namespace CryptoTrader.Maui.ViewModels
{
    public class DashboardViewModel : BaseViewModel
    {
        private readonly TradingService _tradingService;

        public ObservableCollection<MarketData> MarketDataList { get; set; }
        private decimal _balance;
        public decimal Balance
        {
            get => _balance;
            set => SetProperty(ref _balance, value);
        }

        public Command RefreshCommand { get; }

        public DashboardViewModel(TradingService tradingService)
        {
            _tradingService = tradingService;
            MarketDataList = new ObservableCollection<MarketData>();
            RefreshCommand = new Command(async () => await LoadMarketData());
        }

        private async Task LoadMarketData()
        {
            var portfolio = await _tradingService.GetPortfolioAsync();
            Balance = portfolio["balance"]?.Value<decimal>() ?? 0;

            var marketResponse = await _tradingService.Get24hAllPairsDataAsync(new Dictionary<string, string>());
            if (marketResponse != null)
            {
                MarketDataList.Clear();
                foreach (var item in marketResponse["data"])
                {
                    MarketDataList.Add(new MarketData
                    {
                        Symbol = item["symbol"]?.ToString(),
                        Price = item["lastPrice"]?.Value<decimal>() ?? 0
                    });
                }
            }
        }
    }

    public class MarketData
    {
        public string Symbol { get; set; }
        public decimal Price { get; set; }
    }
}
