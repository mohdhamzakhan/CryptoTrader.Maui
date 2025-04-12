using CoinswitchTrader.Services;
using CryptoTrader.Maui.Model;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoTrader.Maui.CoinswitchTrader.Services
{

    public class DashboardServices
    {
        private readonly TradingService _tradingService;
        private readonly SettingsService _settingsService;
        private CancellationTokenSource _cancellationTokenSource;
        public DashboardServices(TradingService tradingService, SettingsService settingsService)
        {
            _settingsService = settingsService;
            _tradingService = tradingService;
        }
        private decimal ConvertToDecimal(string price) => decimal.Parse(price);
        public async Task<List<MarketData>> GetMarketDataAsync(List<string> symbols, List<string> exchanges)
        {
            List<MarketData> marketData = new List<MarketData>();
            try
            {
                foreach (var symbol in symbols)
                {
                    foreach (var exchange in exchanges)
                    {
                        var depthResponse = await _tradingService.GetMarketDepthAsync(symbol, exchange);
                        if (depthResponse == null) continue;

                        var data = depthResponse["data"];
                        var bids = data["bids"] as JArray;
                        var asks = data["asks"] as JArray;
                        if (bids == null || asks == null || !bids.Any() || !asks.Any()) continue;

                        decimal bestBid = ConvertToDecimal(bids[0][0].ToString());
                        decimal bestAsk = ConvertToDecimal(asks[0][0].ToString());
                        var marketDataItem = new MarketData
                        {
                            Symbol = symbol,
                            Bid = bestBid,
                            Ask = bestAsk
                        };
                        marketData.Add(marketDataItem);
                    }
                }
                return marketData;
            }
            catch (Exception ex)
            {
                throw new Exception("Error fetching market data: " + ex.Message);
                
            }
        }
    }
}
