using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CoinswitchTrader.Services
{
    public class HistoricalDataService
    {
        private readonly TradingService _tradingService;
        private readonly SettingsService _settingsService;
        private Dictionary<string, List<CandleData>> _historicalDataCache = new Dictionary<string, List<CandleData>>();

        public HistoricalDataService(TradingService tradingService, SettingsService settingsService)
        {
            _tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
            _settingsService = settingsService;
        }

        public async Task<List<CandleData>> GetHistoricalDataAsync(string symbol, string exchange, string timeframe = "1h", int limit = 100)
        {
            string cacheKey = $"{symbol}_{exchange}_{timeframe}";

            if (_historicalDataCache.ContainsKey(cacheKey))
            {
                return _historicalDataCache[cacheKey];
            }

            try
            {
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

                // Calculate Indicators
                CalculateSMA(candles, _settingsService.SMA_Period);
                CalculateRSI(candles, _settingsService.RSI_Period);
                CalculateEMA(candles, _settingsService.EMA_Period, "EMA12");
                CalculateEMA(candles, _settingsService.Long_EMA_Period, "EMA26");
                CalculateMACD(candles, _settingsService.MACD_ShortPeriod,_settingsService.MACD_LongPeriod,_settingsService.MACD_SignalPeriod);

                _historicalDataCache[cacheKey] = candles;

                return candles;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error fetching historical data: {ex.Message}");
                return new List<CandleData>();
            }
        }

        // --- Indicators Calculation ---

        private void CalculateSMA(List<CandleData> candles, int period)
        {
            for (int i = 0; i < candles.Count; i++)
            {
                if (i >= period - 1)
                {
                    var sma = candles.Skip(i - period + 1).Take(period).Average(c => c.Close);
                    candles[i].Sma = sma;
                }
            }
        }

        public static void CalculateEMA(List<CandleData> candles, int period, string emaPropertyName)
        {
            if (candles == null || candles.Count == 0 || period <= 0)
                throw new ArgumentException("Invalid input data for EMA calculation.");

            decimal multiplier = 2m / (period + 1);
            decimal? previousEma = null;

            for (int i = 0; i < candles.Count; i++)
            {
                decimal close = candles[i].Close;

                if (i + 1 < period)
                {
                    // Not enough data points yet
                    SetEmaValue(candles[i], emaPropertyName, null);
                    continue;
                }
                else if (i + 1 == period)
                {
                    // First EMA value is just the SMA of the first `period` closes
                    decimal sma = candles.Take(period).Average(c => c.Close);
                    previousEma = sma;
                    SetEmaValue(candles[i], emaPropertyName, previousEma);
                }
                else
                {
                    // EMA formula
                    previousEma = (close - previousEma) * multiplier + previousEma;
                    SetEmaValue(candles[i], emaPropertyName, previousEma);
                }
            }
        }

        // Helper to set a dynamic EMA property by reflection
        private static void SetEmaValue(CandleData candle, string propertyName, decimal? value)
        {
            var prop = typeof(CandleData).GetProperty(propertyName);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(candle, value);
            }
        }


        private void CalculateRSI(List<CandleData> candles, int period)
        {
            decimal gain = 0, loss = 0;
            for (int i = 1; i <= period; i++)
            {
                var change = candles[i].Close - candles[i - 1].Close;
                if (change >= 0)
                    gain += change;
                else
                    loss -= change;
            }

            gain /= period;
            loss /= period;
            var rs = loss == 0 ? 100 : gain / loss;
            candles[period].Rsi = 100 - (100 / (1 + rs));

            for (int i = period + 1; i < candles.Count; i++)
            {
                var change = candles[i].Close - candles[i - 1].Close;
                var currentGain = Math.Max(change, 0);
                var currentLoss = Math.Max(-change, 0);

                gain = (gain * (period - 1) + currentGain) / period;
                loss = (loss * (period - 1) + currentLoss) / period;

                rs = loss == 0 ? 100 : gain / loss;
                candles[i].Rsi = 100 - (100 / (1 + rs));
            }
        }

        private void CalculateMACD(List<CandleData> candles, int shortPeriod = 12, int longPeriod = 26, int signalPeriod = 9)
        {
            var emaShort = CalculateEMA(candles.Select(c => c.Close).ToList(), shortPeriod);
            var emaLong = CalculateEMA(candles.Select(c => c.Close).ToList(), longPeriod);

            var macdLine = new List<decimal>();
            for (int i = 0; i < candles.Count; i++)
            {
                macdLine.Add(emaShort[i] - emaLong[i]);
            }

            var signalLine = CalculateEMA(macdLine, signalPeriod);

            for (int i = 0; i < candles.Count; i++)
            {
                candles[i].Macd = macdLine[i];
                candles[i].MacdSignal = signalLine[i];
                candles[i].MacdHistogram = macdLine[i] - signalLine[i];
            }
        }

        private List<decimal> CalculateEMA(List<decimal> values, int period)
        {
            var ema = new List<decimal>();
            decimal multiplier = 2m / (period + 1);

            for (int i = 0; i < values.Count; i++)
            {
                if (i == 0)
                {
                    ema.Add(values[i]);
                }
                else
                {
                    ema.Add((values[i] - ema[i - 1]) * multiplier + ema[i - 1]);
                }
            }
            return ema;
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

        public decimal? Rsi { get; set; } // Relative Strength Index
        public decimal? Sma { get; set; } // Simple Moving Average
        public decimal? Macd { get; set; } // MACD Line
        public decimal? MacdSignal { get; set; } // Signal Line
        public decimal? MacdHistogram { get; set; } // MACD Histogram
        public decimal? EMA12 { get; set; }
        public decimal? EMA26 { get; set; }
    }
}
