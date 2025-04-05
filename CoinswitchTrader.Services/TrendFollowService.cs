using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CoinswitchTrader.Services
{
    public class TrendFollowService
    {
        private readonly TradingService _tradingService;
        private readonly SettingsService _settingsService;
        private Dictionary<string, List<decimal>> _priceHistory = new();
        private Dictionary<string, List<string>> _openOrders = new();
        private bool _isRunning = false;
        private CancellationTokenSource _cancellationTokenSource;

        public TrendFollowService(TradingService tradingService, SettingsService settingsService)
        {
            _tradingService = tradingService;
            _settingsService = settingsService;
        }

        public void StartTrading(List<string> symbols, List<string> exchanges, int scanIntervalMs = 2000)
        {
            if (_isRunning) return;

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    foreach (var symbol in symbols)
                    {
                        foreach (var exchange in exchanges)
                        {
                            try
                            {
                                string key = $"{symbol}_{exchange}";

                                if (!_priceHistory.ContainsKey(key))
                                    _priceHistory[key] = new List<decimal>();

                                if (!_openOrders.ContainsKey(key))
                                    _openOrders[key] = new List<string>();

                                var depthResponse = await _tradingService.GetMarketDepthAsync(symbol, exchange);
                                if (depthResponse == null) continue;

                                var data = depthResponse["data"];
                                var bids = data["bids"] as JArray;
                                var asks = data["asks"] as JArray;

                                if (bids == null || asks == null || !bids.Any() || !asks.Any()) continue;

                                decimal bestBid = ConvertToDecimal(bids[0][0].ToString());
                                decimal bestAsk = ConvertToDecimal(asks[0][0].ToString());

                                // Use weighted average of top bids/asks for better price representation
                                decimal totalVolume = 0;
                                decimal weightedPrice = 0;

                                // Process top bids (buy orders)
                                int depthToUse = Math.Min(10, bids.Count()); // Use top 10 or all available
                                for (int i = 0; i < depthToUse; i++)
                                {
                                    decimal price = ConvertToDecimal(bids[i][0].ToString());
                                    decimal volume = ConvertToDecimal(bids[i][1].ToString());

                                    weightedPrice += price * volume;
                                    totalVolume += volume;
                                }

                                // Process top asks (sell orders)
                                depthToUse = Math.Min(10, asks.Count()); // Use top 10 or all available
                                for (int i = 0; i < depthToUse; i++)
                                {
                                    decimal price = ConvertToDecimal(asks[i][0].ToString());
                                    decimal volume = ConvertToDecimal(asks[i][1].ToString());

                                    weightedPrice += price * volume;
                                    totalVolume += volume;
                                }

                                // Calculate volume-weighted average price
                                decimal vwap = totalVolume > 0 ? weightedPrice / totalVolume : (bestBid + bestAsk) / 2;

                                // Only add to price history if price has changed or initial entry
                                if (!_priceHistory[key].Any() || Math.Abs(_priceHistory[key].Last() - vwap) > 0.0001m)
                                {
                                    _priceHistory[key].Add(vwap);

                                    // Ensure price history doesn't grow too large
                                    if (_priceHistory[key].Count > _settingsService.SMA_Period * 3)
                                        _priceHistory[key] = _priceHistory[key].TakeLast(_settingsService.SMA_Period * 3).ToList();
                                }
                                if (_priceHistory[key].Count < _settingsService.SMA_Period) continue;

                                // Calculate indicators
                                var priceData = _priceHistory[key];
                                decimal sma = CalculateSMA(priceData, _settingsService.SMA_Period);
                                decimal ema = CalculateEMA(priceData, _settingsService.EMA_Period);
                                decimal rsi = CalculateRSI(priceData, _settingsService.RSI_Period);
                                (decimal macd, decimal signal) = CalculateMACD(priceData, _settingsService.MACD_ShortPeriod, _settingsService.MACD_LongPeriod, _settingsService.MACD_SignalPeriod);

                                // Calculate trend strength
                                decimal trendStrength = Math.Abs(ema - sma) / sma * 100;
                                bool strongTrend = trendStrength > 1.5m;

                                // Enhanced buy/sell signals with trend strength consideration
                                bool buySignal = (ema > sma && macd > signal && rsi < 40) || (strongTrend && ema > sma && rsi < 45);
                                bool sellSignal = (ema < sma && macd < signal && rsi > 65) || (strongTrend && ema < sma && rsi > 60);

                                Logger.Log($"{symbol} on {exchange}: RSI={rsi}, SMA={sma}, EMA={ema}, MACD={macd}, Signal={signal}, Trend={trendStrength}%");
                                Logger.Log($"Buy Signal: {buySignal}, Sell Signal: {sellSignal}");

                                // Adjust Orders
                                await CancelAndAdjustOrders(symbol, exchange, buySignal, sellSignal, bestBid, bestAsk);

                                // Place Buy Orders at Multiple Bid Levels
                                if (buySignal)
                                {
                                    decimal inrBalance = await _tradingService.GetBalanceCurrencyAsync("INR");
                                    decimal maxTradeSize = Math.Min(_settingsService.ScalpingMaxTradeSize, inrBalance);

                                    foreach (var bid in bids.Take(3)) // Buy at top 3 bid levels
                                    {
                                        decimal bidPrice = ConvertToDecimal(bid[_settingsService.BidPosition].ToString());
                                        decimal quantity = (maxTradeSize  / bidPrice); // Divide by 3 to place equal orders

                                        if (quantity * bidPrice >= 150) // Ensure minimum order size
                                        {
                                            var order = await _tradingService.CreateBuyOrderAsync(symbol, exchange, bidPrice, quantity);
                                            string orderId = order["data"]["order_id"].ToString();
                                            _openOrders[key].Add(orderId);
                                            Logger.Log($"BUY {quantity} {symbol} at {bidPrice}");
                                        }
                                    }
                                }

                                // Place Sell Orders at Multiple Ask Levels
                                if (sellSignal)
                                {
                                    decimal assetBalance = await _tradingService.GetBalanceCurrencyAsync(symbol.Split('/')[0]);
                                    decimal perOrderBalance = assetBalance / 3; // Split into 3 equal parts
                                    
                                    foreach (var ask in asks.Take(3)) // Sell at top 3 ask levels
                                    {
                                        decimal askPrice = ConvertToDecimal(ask[_settingsService.AskPosition].ToString());
                                        if (perOrderBalance * askPrice >= 150) // Ensure minimum order size
                                        {
                                            var order = await _tradingService.CreateSellOrderAsync(symbol, exchange, askPrice, perOrderBalance);
                                            string orderId = order["data"]["order_id"].ToString();
                                            _openOrders[key].Add(orderId);
                                            Logger.Log($"SELL {perOrderBalance} {symbol} at {askPrice}");
                                        }
                                        else
                                        {
                                            if (perOrderBalance * askPrice * 3 >= 150)
                                            {
                                                var order = await _tradingService.CreateSellOrderAsync(symbol, exchange, askPrice, perOrderBalance * 3);
                                                string orderId = order["data"]["order_id"].ToString();
                                                _openOrders[key].Add(orderId);
                                                Logger.Log($"SELL {perOrderBalance} {symbol} at {askPrice}");
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"Error trading {symbol} on {exchange}: {ex.Message}");
                            }

                            await Task.Delay(scanIntervalMs, token);
                        }
                    }
                }
            }, _cancellationTokenSource.Token);
        }

        public void StopTrading()
        {
            _isRunning = false;
            _cancellationTokenSource?.Cancel();
        }

        // Cancel and Adjust Orders
        private async Task CancelAndAdjustOrders(string symbol, string exchange, bool buySignal, bool sellSignal, decimal bestBid, decimal bestAsk)
        {
            string key = $"{symbol}_{exchange}";
            if (!_openOrders.ContainsKey(key)) return;

            List<string> ordersToCancel = new List<string>();

            foreach (var orderId in _openOrders[key])
            {
                var order = await _tradingService.GetOrderStatusAsync(orderId);
                if (order == null) continue;

                var orderData = order["data"];
                if (orderData == null) continue;

                decimal price = ConvertToDecimal(orderData["price"].ToString());
                string side = orderData["side"].ToString().ToLower();

                // Cancel buy orders when market is trending down (sell signal)
                // Cancel sell orders when market is trending up (buy signal)
                bool shouldCancel = (side == "buy" && sellSignal) || (side == "sell" && buySignal);

                // Also cancel orders that are far from the current market price
                if (!shouldCancel)
                {
                    decimal priceDeviation = side == "buy"
                        ? (price / bestBid - 1) * 100  // How much higher our buy order is vs market
                        : (1 - price / bestAsk) * 100; // How much lower our sell order is vs market

                    shouldCancel = priceDeviation > 2.0m; // Cancel if more than 2% away from market
                }

                if (shouldCancel)
                {
                    await _tradingService.CancelOrderAsync(orderId, exchange);
                    ordersToCancel.Add(orderId);
                    Logger.Log($"Cancelled {side} order {orderId} for {symbol}");
                }
            }

            _openOrders[key].RemoveAll(ordersToCancel.Contains);
        }

        private decimal ConvertToDecimal(string price) => decimal.Parse(price);

        private decimal CalculateSMA(List<decimal> priceData, int period) => priceData.TakeLast(period).Average();

        private decimal CalculateEMA(List<decimal> priceData, int period)
        {
            if (priceData.Count <= period)
                return priceData.Average();

            // Take the most recent data for calculation
            var data = priceData.TakeLast(period * 2).ToList();

            decimal k = 2m / (period + 1);
            decimal ema = data.Take(period).Average(); // Initial EMA is SMA of first 'period' elements

            for (int i = period; i < data.Count; i++)
            {
                ema = data[i] * k + ema * (1 - k);
            }

            return ema;
        }

        private decimal CalculateRSI(List<decimal> priceData, int period)
        {
            if (priceData.Count < period + 1)
                return 50; // Neutral RSI if insufficient data

            // We need the most recent data for RSI
            var relevantData = priceData.TakeLast(period * 2).ToList();

            List<decimal> changes = new List<decimal>();
            for (int i = 1; i < relevantData.Count; i++)
            {
                changes.Add(relevantData[i] - relevantData[i - 1]);
            }

            // Calculate initial averages
            decimal avgGain = changes.Take(period).Where(change => change > 0).DefaultIfEmpty(0).Average();
            decimal avgLoss = changes.Take(period).Where(change => change < 0).Select(change => Math.Abs(change)).DefaultIfEmpty(0).Average();

            // Apply Wilder's smoothing
            for (int i = period; i < changes.Count; i++)
            {
                decimal change = changes[i];
                decimal currentGain = change > 0 ? change : 0;
                decimal currentLoss = change < 0 ? Math.Abs(change) : 0;

                avgGain = ((avgGain * (period - 1)) + currentGain) / period;
                avgLoss = ((avgLoss * (period - 1)) + currentLoss) / period;
            }

            if (avgLoss == 0)
                return 100; // No losses, RSI is 100

            decimal rs = avgGain / avgLoss;
            decimal rsi = 100 - (100 / (1 + rs));

            return Math.Round(rsi, 2);
        }

        private (decimal, decimal) CalculateMACD(List<decimal> priceData, int shortPeriod, int longPeriod, int signalPeriod)
        {
            if (priceData.Count < Math.Max(shortPeriod, longPeriod) + signalPeriod)
                return (0, 0);

            // Use the most recent data
            var data = priceData.TakeLast(Math.Max(shortPeriod, longPeriod) * 2 + signalPeriod).ToList();

            // Calculate MACD line values for the recent period
            List<decimal> macdValues = new List<decimal>();

            for (int i = 0; i <= signalPeriod + 5; i++) // Additional points for better signal line
            {
                if (data.Count - i < Math.Max(shortPeriod, longPeriod)) break;

                var subset = data.Take(data.Count - i).ToList();
                decimal shortEMA = CalculateEMA(subset, shortPeriod);
                decimal longEMA = CalculateEMA(subset, longPeriod);
                macdValues.Insert(0, shortEMA - longEMA); // Insert at beginning to maintain chronological order
            }

            // The current MACD value is the first/last one
            decimal macd = macdValues.LastOrDefault();

            // Calculate signal line (EMA of MACD values)
            decimal signal;
            if (macdValues.Count >= signalPeriod)
            {
                // Simple EMA calculation on MACD values
                decimal k = 2m / (signalPeriod + 1);
                signal = macdValues.Take(signalPeriod).Average();

                for (int i = signalPeriod; i < macdValues.Count; i++)
                {
                    signal = macdValues[i] * k + signal * (1 - k);
                }
            }
            else
            {
                // Not enough data for proper signal line
                signal = macdValues.Average();
            }

            return (macd, signal);
        }
    }
}