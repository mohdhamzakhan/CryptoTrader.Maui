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
        private readonly HistoricalDataService _historicalDataService;
        private Dictionary<string, List<decimal>> _priceHistory = new();
        private Dictionary<string, List<string>> _openOrders = new();
        private bool _isRunning = false;
        private CancellationTokenSource _cancellationTokenSource;

        public TrendFollowService(TradingService tradingService, SettingsService settingsService, HistoricalDataService historicalDataService)
        {
            _tradingService = tradingService;
            _settingsService = settingsService;
            _historicalDataService = historicalDataService;
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

                                var historicalData = await _historicalDataService.GetHistoricalDataAsync(symbol, exchange, timeframe: "1");
                                if (historicalData == null || historicalData.Count() < 20) return;

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
                               // if (_priceHistory[key].Count < _settingsService.SMA_Period) continue;

                                // Calculate indicators
                                var priceData = _priceHistory[key];
                                //decimal sma = CalculateSMA(priceData, _settingsService.SMA_Period);
                                //decimal ema = CalculateEMA(priceData, _settingsService.EMA_Period);
                                //decimal rsi = CalculateRSI(priceData, _settingsService.RSI_Period);
                                //(decimal macd, decimal signal) = CalculateMACD(priceData, _settingsService.MACD_ShortPeriod, _settingsService.MACD_LongPeriod, _settingsService.MACD_SignalPeriod);

                                decimal sma = historicalData.Select(c => c.Sma ?? 0m).Skip(_settingsService.SMA_Period).LastOrDefault();
                                decimal ema = historicalData.Select(c => c.EMA12 ?? 0m).Skip(_settingsService.EMA_Period).LastOrDefault();
                                decimal rsi = historicalData.Select(c => c.Rsi ?? 0m).Skip(_settingsService.RSI_Period).LastOrDefault();
                                (decimal macd, decimal signal) = historicalData.Select(c => (c.Macd ?? 0m, c.MacdSignal ?? 0m)).Skip(_settingsService.MACD_ShortPeriod).LastOrDefault();

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

                                    foreach (var bid in bids.Skip(_settingsService.BidPosition).Take(3)) // Buy at top 3 bid levels
                                    {
                                        decimal bidPrice = ConvertToDecimal(bid[0].ToString());
                                        decimal quantity = (maxTradeSize  / bidPrice); // Divide by 3 to place equal orders

                                        if (quantity * bidPrice >= 150) // Ensure minimum order size
                                        {
                                            var order = await _tradingService.CreateBuyOrderAsync(symbol, exchange, bidPrice, quantity);
                                            string orderId = order["data"]["order_id"].ToString();
                                            _openOrders[key].Add(orderId);
                                            Logger.Log($"BUY {quantity} {symbol} at {bidPrice}");
#if ANDROID
                                            var context = Android.App.Application.Context;
                                            Android.Widget.Toast.MakeText(context, $"Order Placed {orderId}", Android.Widget.ToastLength.Short)?.Show();
#endif
                                        }
                                    }
                                }

                                // Place Sell Orders at Multiple Ask Levels
                                if (sellSignal)
                                {
                                    decimal assetBalance = await _tradingService.GetBalanceCurrencyAsync(symbol.Split('/')[0]);
                                    decimal perOrderBalance = assetBalance / 3; // Split into 3 equal parts
                                    
                                    foreach (var ask in asks.Skip(_settingsService.AskPosition).Take(3)) // Sell at top 3 ask levels
                                    {
                                        decimal askPrice = ConvertToDecimal(ask[0].ToString());
                                        if (perOrderBalance * askPrice >= 150) // Ensure minimum order size
                                        {
                                            var order = await _tradingService.CreateSellOrderAsync(symbol, exchange, askPrice, perOrderBalance);
                                            string orderId = order["data"]["order_id"].ToString();
                                            _openOrders[key].Add(orderId);
                                            Logger.Log($"SELL {perOrderBalance} {symbol} at {askPrice}");
//#if ANDROID
//                                            var context = Android.App.Application.Context;
//                                            Android.Widget.Toast.MakeText(context, $"Order Placed {orderId}", Android.Widget.ToastLength.Short)?.Show();
//#endif
                                        }
                                        else
                                        {
                                            if (perOrderBalance * askPrice * 3 >= 150)
                                            {
                                                var order = await _tradingService.CreateSellOrderAsync(symbol, exchange, askPrice, perOrderBalance * 3);
                                                string orderId = order["data"]["order_id"].ToString();
                                                _openOrders[key].Add(orderId);
                                                Logger.Log($"SELL {perOrderBalance} {symbol} at {askPrice}");
//#if ANDROID
//                                                var context = Android.App.Application.Context;
//                                                Android.Widget.Toast.MakeText(context, $"Order Placed {orderId}", Android.Widget.ToastLength.Short)?.Show();
//#endif
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
    }
}