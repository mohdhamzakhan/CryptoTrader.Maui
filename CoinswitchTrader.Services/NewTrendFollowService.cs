using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CoinswitchTrader.Services
{
    public class NewTrendFollowService
    {
        private readonly TradingService _tradingService;
        private readonly SettingsService _settingsService;
        private readonly HistoricalDataService _historicalDataService;
        private Dictionary<string, List<decimal>> _priceHistory = new();
        private Dictionary<string, List<string>> _openOrders = new();
        private Dictionary<string, decimal> _lastBuyPrices = new();
        private Dictionary<string, decimal> _lastSellPrices = new();
        private Dictionary<string, int> _consecutiveTrades = new();
        private bool _isRunning = false;
        private CancellationTokenSource _cancellationTokenSource;
        private decimal _minProfitPercent = 0.25m; // Minimum profit target in percentage

        public NewTrendFollowService(TradingService tradingService, SettingsService settingsService, HistoricalDataService historicalDataService)
        {
            _tradingService = tradingService;
            _settingsService = settingsService;
            _historicalDataService = historicalDataService;
        }

        public void StartTrading(List<string> symbols, List<string> exchanges, int scanIntervalMs = 1000)
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
                                var historicalData = await _historicalDataService.GetHistoricalDataAsync(symbol, exchange, timeframe: "1");
                                if (historicalData == null || historicalData.Count() < 20) return;
                                if (!_priceHistory.ContainsKey(key))
                                    _priceHistory[key] = new List<decimal>();

                                if (!_openOrders.ContainsKey(key))
                                    _openOrders[key] = new List<string>();

                                if (!_lastBuyPrices.ContainsKey(key))
                                    _lastBuyPrices[key] = 0;

                                if (!_lastSellPrices.ContainsKey(key))
                                    _lastSellPrices[key] = 0;

                                if (!_consecutiveTrades.ContainsKey(key))
                                    _consecutiveTrades[key] = 0;

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
                                //decimal sma = CalculateSMA(priceData, _settingsService.SMA_Period);
                                //decimal ema = CalculateEMA(priceData, _settingsService.EMA_Period);
                                //decimal rsi = CalculateRSI(priceData, _settingsService.RSI_Period);
                                //(decimal macd, decimal signal) = CalculateMACD(priceData, _settingsService.MACD_ShortPeriod, _settingsService.MACD_LongPeriod, _settingsService.MACD_SignalPeriod);

                                decimal sma = historicalData.Select(c => c.Sma ?? 0m).Take(_settingsService.SMA_Period).FirstOrDefault();
                                decimal ema = historicalData.Select(c => c.EMA12 ?? 0m).Take(_settingsService.EMA_Period).FirstOrDefault();
                                decimal rsi = historicalData.Select(c => c.Rsi ?? 0m).Take(_settingsService.RSI_Period).FirstOrDefault();
                                (decimal macd, decimal signal) = historicalData.Select(c => (c.Macd ?? 0m, c.MacdSignal ?? 0m)).Take(_settingsService.MACD_ShortPeriod).FirstOrDefault();


                                // Calculate trend strength
                                decimal trendStrength = Math.Abs(ema - sma) / sma * 100;
                                bool strongTrend = trendStrength > 1.5m;

                                // Calculate volatility for more aggressive trading
                                decimal volatility = CalculateVolatility(priceData, 20);
                                bool highVolatility = volatility > 0.5m;

                                // Calculate current spreads
                                decimal spread = (bestAsk / bestBid - 1) * 100;
                                bool tightSpread = spread < 0.5m;

                                // Enhanced buy/sell signals with profit targeting
                                bool buySignal = false;
                                bool sellSignal = false;

                                // AGGRESSIVE TRADING LOGIC
                                // More sensitive RSI thresholds for faster entries
                                if (highVolatility)
                                {
                                    // In high volatility, use more extreme RSI values
                                    buySignal = (rsi < 35) || (ema > sma && macd > signal && rsi < 40);
                                    sellSignal = (rsi > 65) || (ema < sma && macd < signal && rsi > 60);
                                }
                                else
                                {
                                    // In normal conditions, use standard indicators with more sensitivity
                                    buySignal = (ema > sma && macd > signal && rsi < 45) || (strongTrend && ema > sma && rsi < 48);
                                    sellSignal = (ema < sma && macd < signal && rsi > 55) || (strongTrend && ema < sma && rsi > 52);
                                }
                                Logger.Log($"[Trend] BuySignal={buySignal}, EMA={ema}, SMA={sma}, RSI={rsi}, MACD={macd}");
                                Logger.Log($"[Trend] SellSignal={sellSignal}, EMA={ema}, SMA={sma}, RSI={rsi}, MACD={macd}");


                                // PROFIT TARGETING - Override signals if minimum profit is available
                                if (_lastBuyPrices[key] > 0 && bestAsk > 0)
                                {
                                    decimal potentialProfit = (bestAsk / _lastBuyPrices[key] - 1) * 100;
                                    if (potentialProfit >= _minProfitPercent)
                                    {
                                        sellSignal = true;
                                        Logger.Log($"PROFIT TARGET REACHED: {potentialProfit:F2}% on {symbol}");
                                    }
                                }

                                if (_lastSellPrices[key] > 0 && bestBid > 0)
                                {
                                    decimal potentialProfit = (1 - bestBid / _lastSellPrices[key]) * 100;
                                    if (potentialProfit >= _minProfitPercent)
                                    {
                                        buySignal = true;
                                        Logger.Log($"PROFIT TARGET REACHED: {potentialProfit:F2}% on {symbol}");
                                    }
                                }

                                // MARKET MAKING - If spread is tight, we can be more aggressive
                                if (tightSpread)
                                {
                                    // Increase signal strength when spread is favorable
                                    if (rsi < 50) buySignal = true;
                                    if (rsi > 50) sellSignal = true;
                                }

                                Logger.Log($"{symbol} on {exchange}: RSI={rsi:F2}, SMA={sma:F2}, EMA={ema:F2}, MACD={macd:F2}, Signal={signal:F2}, Volatility={volatility:F2}%, Spread={spread:F2}%");
                                Logger.Log($"Buy Signal: {buySignal}, Sell Signal: {sellSignal}");

                                // Check for filled orders and update last prices
                                await CheckFilledOrders(symbol, exchange);

                                // Adjust Orders
                                await CancelAndAdjustOrders(symbol, exchange, buySignal, sellSignal, bestBid, bestAsk);

                                // Place Buy Orders at Multiple Bid Levels
                                if (buySignal)
                                {
                                    decimal inrBalance = await _tradingService.GetBalanceCurrencyAsync("INR");
                                    // More aggressive position sizing based on volatility and trend strength
                                    decimal positionSizeMultiplier = 1.0m;
                                    if (highVolatility) positionSizeMultiplier += 0.25m;
                                    if (strongTrend && ema > sma) positionSizeMultiplier += 0.25m;

                                    decimal maxTradeSize = Math.Min(_settingsService.ScalpingMaxTradeSize * positionSizeMultiplier, inrBalance);

                                    // Determine number of bid levels to use
                                    int bidLevels = 3; // Default
                                    if (highVolatility) bidLevels = 5; // More levels in high volatility

                                    foreach (var bid in bids.Take(bidLevels))
                                    {
                                        decimal bidPrice = ConvertToDecimal(bid[0].ToString());
                                        decimal quantity = (maxTradeSize / bidPrice);

                                        if (quantity * bidPrice >= 150) // Ensure minimum order size
                                        {
                                            var order = await _tradingService.CreateBuyOrderAsync(symbol, exchange, bidPrice, quantity);
                                            string orderId = order["data"]["order_id"].ToString();
                                            _openOrders[key].Add(orderId);
                                            _lastBuyPrices[key] = bidPrice; // Record last buy price
                                            Logger.Log($"BUY {quantity} {symbol} at {bidPrice}");

                                            // Increment consecutive trade counter
                                            _consecutiveTrades[key]++;
                                        }
                                        else
                                        {
                                            break;
                                        }
                                    }
                                }

                                // Place Sell Orders at Multiple Ask Levels
                                if (sellSignal)
                                {
                                    decimal assetBalance = await _tradingService.GetBalanceCurrencyAsync(symbol.Split('/')[0]);

                                    // Determine number of ask levels to use
                                    int askLevels = 3; // Default
                                    if (highVolatility) askLevels = 5; // More levels in high volatility

                                    decimal perOrderBalance = assetBalance / askLevels;

                                    foreach (var ask in asks.Take(askLevels))
                                    {
                                        decimal askPrice = ConvertToDecimal(ask[0].ToString());
                                        if (perOrderBalance * askPrice >= 150) // Ensure minimum order size
                                        {
                                            var order = await _tradingService.CreateSellOrderAsync(symbol, exchange, askPrice, perOrderBalance);
                                            string orderId = order["data"]["order_id"].ToString();
                                            _openOrders[key].Add(orderId);
                                            _lastSellPrices[key] = askPrice; // Record last sell price
                                            Logger.Log($"SELL {perOrderBalance} {symbol} at {askPrice}");

                                            // Increment consecutive trade counter
                                            _consecutiveTrades[key]++;
                                        }
                                        else
                                        {
                                            if (perOrderBalance * askPrice * askLevels >= 150)
                                            {
                                                var order = await _tradingService.CreateSellOrderAsync(symbol, exchange, askPrice, perOrderBalance * askLevels);
                                                string orderId = order["data"]["order_id"].ToString();
                                                _openOrders[key].Add(orderId);
                                                _lastSellPrices[key] = askPrice; // Record last sell price
                                                Logger.Log($"SELL {perOrderBalance} {symbol} at {askPrice}");
                                            }

                                            // Increment consecutive trade counter
                                            _consecutiveTrades[key]++;
                                        }
                                    }
                                }

                                // After a certain number of consecutive trades, take a brief pause
                                if (_consecutiveTrades[key] > 15)
                                {
                                    Logger.Log($"Taking a brief pause after {_consecutiveTrades[key]} consecutive trades on {symbol}");
                                    await Task.Delay(10000); // 10 second pause
                                    _consecutiveTrades[key] = 0; // Reset counter
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
                string status = orderData["status"].ToString().ToLower();

                // Skip already filled or canceled orders
                if (status == "filled" || status == "canceled")
                {
                    ordersToCancel.Add(orderId);
                    continue;
                }

                // Cancel buy orders when market is trending down (sell signal)
                // Cancel sell orders when market is trending up (buy signal)
                bool shouldCancel = (side == "buy" && sellSignal) || (side == "sell" && buySignal);

                // Also cancel orders that are far from the current market price
                if (!shouldCancel)
                {
                    decimal priceDeviation = side == "buy"
                        ? (price / bestBid - 1) * 100  // How much higher our buy order is vs market
                        : (1 - price / bestAsk) * 100; // How much lower our sell order is vs market

                    // More aggressive cancellation for faster repositioning
                    shouldCancel = priceDeviation > 1.0m; // Cancel if more than 1% away from market (reduced from 2%)
                }

                // Cancel orders that have been open too long (stale orders)
                if (!shouldCancel && orderData["created_at"] != null)
                {
                    DateTime createdAt = DateTime.Parse(orderData["created_at"].ToString());
                    TimeSpan orderAge = DateTime.UtcNow - createdAt;
                    shouldCancel = orderAge.TotalMinutes > 5; // Cancel orders older than 5 minutes
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

        // Check for filled orders and update last trade prices
        private async Task CheckFilledOrders(string symbol, string exchange)
        {
            string key = $"{symbol}_{exchange}";
            if (!_openOrders.ContainsKey(key)) return;

            List<string> filledOrders = new List<string>();

            foreach (var orderId in _openOrders[key])
            {
                var order = await _tradingService.GetOrderStatusAsync(orderId);
                if (order == null) continue;

                var orderData = order["data"];
                if (orderData == null) continue;

                string status = orderData["status"].ToString().ToLower();

                if (status == "filled")
                {
                    decimal price = ConvertToDecimal(orderData["price"].ToString());
                    string side = orderData["side"].ToString().ToLower();

                    if (side == "buy")
                    {
                        _lastBuyPrices[key] = price;
                        Logger.Log($"BUY ORDER FILLED at {price} for {symbol}");
                    }
                    else if (side == "sell")
                    {
                        _lastSellPrices[key] = price;
                        Logger.Log($"SELL ORDER FILLED at {price} for {symbol}");
                    }

                    filledOrders.Add(orderId);
                }
            }

            _openOrders[key].RemoveAll(filledOrders.Contains);
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

        // Calculate price volatility
        private decimal CalculateVolatility(List<decimal> priceData, int period)
        {
            if (priceData.Count < period)
                return 0.5m; // Default medium volatility if insufficient data

            var recentData = priceData.TakeLast(period).ToList();

            // Calculate percentage changes
            List<decimal> percentChanges = new List<decimal>();
            for (int i = 1; i < recentData.Count; i++)
            {
                decimal percentChange = (recentData[i] / recentData[i - 1] - 1) * 100;
                percentChanges.Add(Math.Abs(percentChange)); // Use absolute value of percentage change
            }

            // Standard deviation of percentage changes = volatility
            decimal mean = percentChanges.Average();
            decimal sumOfSquaredDifferences = percentChanges.Sum(x => (x - mean) * (x - mean));
            decimal variance = sumOfSquaredDifferences / percentChanges.Count;
            decimal stdDev = (decimal)Math.Sqrt((double)variance);

            return stdDev;
        }
    }
}