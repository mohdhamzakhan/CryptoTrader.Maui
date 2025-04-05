using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CoinswitchTrader.Services
{
    public class HistoricalDataService
    {
        private readonly TradingService _tradingService;
        private Dictionary<string, List<CandleData>> _historicalDataCache = new Dictionary<string, List<CandleData>>();

        public HistoricalDataService(TradingService tradingService)
        {
            _tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
        }

        public async Task<List<CandleData>> GetHistoricalDataAsync(string symbol, string exchange, string timeframe = "1h", int limit = 100)
        {
            string cacheKey = $"{symbol}_{exchange}_{timeframe}";

            // Check if we already have cached data
            if (_historicalDataCache.ContainsKey(cacheKey))
            {
                return _historicalDataCache[cacheKey];
            }

            try
            {
                // Fetch historical candle data from the trading service
                var response = await _tradingService.GetCandlestickDataAsync(symbol, exchange, timeframe, limit);

                if (response == null || !response.ContainsKey("data") || !(response["data"] is JArray))
                {
                    return new List<CandleData>();
                }

                var candlesArray = response["data"] as JArray;
                var candles = new List<CandleData>();

                foreach (var candle in candlesArray)
                {
                    var candleData = new CandleData
                    {
                        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(candle["start_time"].ToString())).DateTime,
                        Open = decimal.Parse(candle["o"].ToString()),
                        High = decimal.Parse(candle["h"].ToString()),
                        Low = decimal.Parse(candle["l"].ToString()),
                        Close = decimal.Parse(candle["c"].ToString()),
                        Volume = decimal.Parse(candle["volume"].ToString())
                    };

                    candles.Add(candleData);
                }


                // Cache the data
                _historicalDataCache[cacheKey] = candles;

                return candles;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error fetching historical data: {ex.Message}");
                return new List<CandleData>();
            }
        }

        public async Task<Dictionary<string, decimal>> GetMarketPricesAsync(List<string> symbols, string exchange)
        {
            string exchangeLower = exchange.ToLower();
            var prices = new Dictionary<string, decimal>();

            foreach (var symbol in symbols)
            {
                try
                {
                    var tickerData = await _tradingService.GetTickerAsync(symbol, exchange);
                    if (tickerData != null &&
     tickerData.ContainsKey("data") &&
     tickerData["data"].HasValues &&
     tickerData["data"][exchangeLower] != null &&
     tickerData["data"][exchangeLower].HasValues &&
     tickerData["data"][exchangeLower]["lastPrice"] != null)
                    {
                        decimal lastPrice = decimal.Parse(tickerData["data"][exchangeLower]["lastPrice"].ToString());
                        prices[symbol] = lastPrice;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error fetching price for {symbol}: {ex.Message}");
                }
            }

            return prices;
        }

        public void ClearCache()
        {
            _historicalDataCache.Clear();
        }

        public void ClearCache(string symbol, string exchange, string timeframe = "1h")
        {
            string cacheKey = $"{symbol}_{exchange}_{timeframe}";
            if (_historicalDataCache.ContainsKey(cacheKey))
            {
                _historicalDataCache.Remove(cacheKey);
            }
        }
    }

    public class CandleData
    {
        public DateTime Timestamp { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public decimal? Rsi { get; set; } // Nullable in case RSI is not always available
    }
}