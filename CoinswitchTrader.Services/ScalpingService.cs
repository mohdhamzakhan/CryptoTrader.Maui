using CoinswitchTrader.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CoinswitchTrader.Services
{
    public enum TradingStrategy
    {
        SimpleScalping,
        ScalpingEngulfing,
        StochasticOscillator,
        MovingAverage,
        Combined
    }

    public class ScalpingService
    {
        private readonly TradingService _tradingService;
        private readonly SettingsService _settingsService;
        private readonly HistoricalDataService _historicalDataService;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning = false;
        private Dictionary<string, List<decimal>> _priceHistory = new Dictionary<string, List<decimal>>();

        public event EventHandler<ScalpingEventArgs> ScalpingOpportunityDetected;
        public event EventHandler<ScalpingEventArgs> ScalpingTradeExecuted;
        public event EventHandler<string> ScalpingLogMessage;

        public ScalpingService(TradingService tradingService, SettingsService settingsService, HistoricalDataService historicalDataService)
        {
            _tradingService = tradingService;
            _settingsService = settingsService;
            _historicalDataService = historicalDataService;
        }

        public bool IsRunning => _isRunning;


        public void StartScalping(List<string> symbols, List<string> exchanges, int scanIntervalMs = 2000)
        {
            if (_isRunning)
                return;

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            // Initialize price history
            foreach (var symbol in symbols)
            {
                foreach (var exchange in exchanges)
                {
                    string key = $"{symbol}_{exchange}";
                    if (!_priceHistory.ContainsKey(key))
                    {
                        _priceHistory[key] = new List<decimal>();
                    }
                }
            }

            Task.Run(async () =>
            {
                LogMessage($"Scalping service started using {_settingsService.ActiveStrategy} strategy");

                while (!token.IsCancellationRequested)
                {
                    foreach (var symbol in symbols)
                    {
                        foreach (var exchange in exchanges)
                        {
                            try
                            {
                                // Update price history first
                                await UpdatePriceHistory(symbol, exchange);

                                // Execute strategy based on active selection
                                switch (_settingsService.ActiveStrategy)
                                {
                                    case TradingStrategy.SimpleScalping:
                                        await ScanForScalpingOpportunity(symbol, exchange, token);
                                        break;
                                    case TradingStrategy.StochasticOscillator:
                                        await ScanWithStochasticOscillator(symbol, exchange, token);
                                        break;
                                    case TradingStrategy.MovingAverage:
                                        await ScanWithMovingAverage(symbol, exchange, token);
                                        break;
                                    case TradingStrategy.Combined:
                                        await ScanWithCombinedStrategy(symbol, exchange, token);
                                        break;
                                    case TradingStrategy.ScalpingEngulfing:
                                        await ScanForBullishTrade(symbol, exchange, token);
                                        break;

                                }
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"Error scanning {symbol} on {exchange}: {ex.Message}");
                            }
                        }
                    }

                    // Wait for the next scan interval
                    try
                    {
                        await Task.Delay(scanIntervalMs, token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }

                _isRunning = false;
                LogMessage("Scalping service stopped");
            }, token);
        }

        public void StopScalping()
        {
            if (!_isRunning)
                return;

            _cancellationTokenSource?.Cancel();
            _isRunning = false;
        }

        private async Task UpdatePriceHistory(string symbol, string exchange)
        {
            string exchangeLower = exchange.ToLower();
            string key = $"{symbol}_{exchange}";

            // Get latest price
            var tickerData = await _tradingService.GetTickerAsync(symbol, exchange);
            if (tickerData != null &&
                tickerData.ContainsKey("data") &&
                tickerData["data"].HasValues &&
                tickerData["data"][exchangeLower] != null &&
                tickerData["data"][exchangeLower].HasValues &&
                tickerData["data"][exchangeLower]["lastPrice"] == null)
            {
                return;
            }

            decimal lastPrice = decimal.Parse(tickerData["data"][exchangeLower]["lastPrice"].ToString());

            // Add to history
            _priceHistory[key].Add(lastPrice);

            // Keep only the last 100 price points (or adjust as needed)
            int maxHistorySize = Math.Max(100, _settingsService.SlowMAPeriod * 2);
            if (_priceHistory[key].Count > maxHistorySize)
            {
                _priceHistory[key].RemoveAt(0);
            }
        }

        private bool IsSwingHigh(List<CandleData> candles, int index, int lookback = 2)
        {
            if (index < lookback || index >= candles.Count - lookback)
                return false; // Not enough candles for comparison

            decimal currentHigh = candles[index].High;

            for (int i = 1; i <= lookback; i++)
            {
                if (candles[index - i].High >= currentHigh || candles[index + i].High >= currentHigh)
                    return false; // Not a swing high
            }

            return true;
        }

        private bool IsBearishDivergence(List<CandleData> candles)
        {
            var recentHighs = candles
    .Select((c, i) => new { Candle = c, Index = i })
    .Where(x => IsSwingHigh(candles, x.Index))
    .Select(x => x.Candle)
    .TakeLast(5)
    .ToList();
            if (recentHighs.Count() < 2) return false;

            bool priceMakingHigherHighs = recentHighs.Last().Close > recentHighs[recentHighs.Count() - 2].Close;
            bool rsiMakingLowerHighs = recentHighs.Last().Rsi < recentHighs[recentHighs.Count() - 2].Rsi;

            return priceMakingHigherHighs && rsiMakingLowerHighs;
        }

        private bool IsBullishEngulfing(List<CandleData> candles)
        {
            var lastCandle = candles[^1];  // Current candle
            var prevCandle = candles[^2];  // Previous candle

            return prevCandle.Close < prevCandle.Open  // Previous was red (bearish)
                && lastCandle.Close > lastCandle.Open  // Current is green (bullish)
                && lastCandle.Close > prevCandle.Open  // Engulfs the previous red candle
                && lastCandle.Open < prevCandle.Close;
        }


        private async Task ScanForBullishTrade(string symbol, string exchange, CancellationToken token)
        {
            // Get historical data (last 50 candles)
            var historicalData = await _historicalDataService.GetHistoricalDataAsync(symbol, exchange, timeframe: "1");
            if (historicalData == null || historicalData.Count() < 20) return;

            // Extract closing prices for EMA & RSI calculation
            List<decimal> closingPrices = historicalData.Select(c => c.Close).ToList();

            // Compute 20-period EMA
            decimal ema20 = CalculateEMA(closingPrices, 20);
            decimal lastClose = closingPrices.Last();

            // Ensure price is above EMA 20
            if (lastClose <= ema20) return;

            // Compute RSI (14-period)
            decimal rsi = CalculateRSI(closingPrices, 14);

            // RSI must be above 50 & no bearish divergence
            if (rsi < 50 || IsBearishDivergence(historicalData)) return;

            // Check for Bullish Engulfing Candle
            if (!IsBullishEngulfing(historicalData)) return;

            // Place Buy Order
            decimal tradeSize = _settingsService.ScalpingMaxTradeSize;
            decimal availableINR = await _tradingService.GetBalanceCurrencyAsync("INR");
            if (tradeSize > availableINR) return;

            await _tradingService.CreateBuyOrderAsync(symbol, exchange, lastClose, tradeSize / lastClose);

            LogMessage($"✅ Buy Signal for {symbol} at {lastClose} (EMA 20: {ema20}, RSI: {rsi})");
        }


        private async Task ScanForScalpingOpportunity(string symbol, string exchange, CancellationToken token)
        {
            // Get current order book
            var depthResponse = await _tradingService.GetMarketDepthAsync(symbol, exchange);
            if (depthResponse == null)
            {
                LogMessage($"Failed to get market depth for {symbol} on {exchange}");
                return;
            }

            var data = depthResponse["data"];
            var bids = data["bids"] as JArray;
            var asks = data["asks"] as JArray;

            if (bids == null || asks == null || !bids.Any() || !asks.Any()) return;

            // Get available balance for the crypto asset
            decimal assetBalance = await _tradingService.GetBalanceCurrencyAsync(symbol.Split('/')[0]);

            // Best bid (highest buy offer) & Best ask (lowest sell offer)
            var bestBid = decimal.Parse(bids[0][0].ToString());
            var bestAsk = decimal.Parse(asks[0][0].ToString());
            var bidVolume = decimal.Parse(bids[0][1].ToString());
            var askVolume = decimal.Parse(asks[0][1].ToString());

            // Avoid selling if below exchange min trade value (₹150)
            if (bestBid * assetBalance > 150)
            {
                decimal sellProfit = (bestBid - bestAsk) / bestAsk;
                decimal fees = (2 * _settingsService.TradingFeeRate) + (_settingsService.ApplyTdsAdjustment ? _settingsService.TdsRate : 0);

                // Ensure selling is still profitable
                if (sellProfit > fees)
                {
                    await _tradingService.CreateSellOrderAsync(symbol, exchange, bestBid, assetBalance);
                    LogMessage($"✅ Sold {symbol} at {bestBid}, ensuring profit after fees & TDS.");
                }
            }

            // Check spread and profit conditions for scalping
            decimal spreadPercentage = (bestAsk - bestBid) / bestBid;
            decimal requiredSpread = _settingsService.ScalpingProfitThreshold + (2 * _settingsService.TradingFeeRate) + (_settingsService.ApplyTdsAdjustment ? _settingsService.TdsRate : 0);

            if (spreadPercentage <= requiredSpread) return; // Not profitable enough

            // Determine trade size (cannot exceed available INR balance)
            decimal maxTradeSize = _settingsService.ScalpingMaxTradeSize;
            decimal tradeSize = Math.Min(maxTradeSize, Math.Min(bidVolume * bestBid, askVolume * bestAsk));
            decimal availableINR = await _tradingService.GetBalanceCurrencyAsync("INR");
            if (tradeSize > availableINR) return;

            // Calculate estimated profit after all deductions
            decimal buyQuantity = tradeSize / bestAsk;
            decimal sellValue = buyQuantity * bestBid;
            decimal buyValue = tradeSize;
            decimal profitBeforeFees = sellValue - buyValue;
            decimal tradingFees = (buyValue + sellValue) * _settingsService.TradingFeeRate;
            decimal tdsCost = sellValue * _settingsService.TdsRate;
            decimal netProfit = profitBeforeFees - tradingFees - tdsCost;

            if (netProfit > 0) // Only execute trade if profitable
            {
                var opportunity = new ScalpingEventArgs
                {
                    Symbol = symbol,
                    Exchange = exchange,
                    BuyPrice = bestAsk,
                    SellPrice = bestBid,
                    Quantity = buyQuantity,
                    ProfitPercentage = spreadPercentage,
                    EstimatedProfit = netProfit,
                    Timestamp = DateTime.Now,
                    Strategy = TradingStrategy.SimpleScalping
                };

                await ExecuteTrade(opportunity, token, bestAsk, bestBid, buyQuantity, _settingsService.ScalpingProfitThreshold, _settingsService.TradingFeeRate, _settingsService.TdsRate);
            }
        }


        private async Task ScanWithStochasticOscillator(string symbol, string exchange, CancellationToken token)
        {
            string key = $"{symbol}_{exchange}";

            // Need enough price history
            if (_priceHistory[key].Count < _settingsService.StochasticKPeriod)
            {
                return;
            }

            // Get current market data
            var depthResponse = await _tradingService.GetMarketDepthAsync(symbol, exchange);
            if (depthResponse == null)
            {
                LogMessage($"Failed to get market depth for {symbol} on {exchange}");
                return;
            }

            var data = depthResponse["data"];
            var bids = data["bids"] as JArray;
            var asks = data["asks"] as JArray;

            if (bids == null || asks == null || !bids.Any() || !asks.Any())
            {
                return;
            }

            // Calculate Stochastic oscillator
            var kPeriod = _settingsService.StochasticKPeriod;
            var dPeriod = _settingsService.StochasticDPeriod;
            var slowing = _settingsService.StochasticSlowing;

            var priceData = _priceHistory[key].ToList();
            var (k, d) = CalculateStochasticOscillator(priceData, kPeriod, dPeriod, slowing);

            // Get best bid and ask
            var bestBid = decimal.Parse(bids[0][0].ToString());
            var bestAsk = decimal.Parse(asks[0][0].ToString());
            var bidVolume = decimal.Parse(bids[0][1].ToString());
            var askVolume = decimal.Parse(asks[0][1].ToString());

            // Get previous K and D values
            var previousPriceData = priceData.Take(priceData.Count - 1).ToList();
            var (previousK, previousD) = CalculateStochasticOscillator(previousPriceData, kPeriod, dPeriod, slowing);

            // Check for oversold/overbought conditions and crossovers
            bool buySignal = false;
            bool sellSignal = false;

            // Oversold region (K and D below 20)
            if (k < 20 && d < 20)
            {
                // Bullish crossover (K crosses above D)
                if (previousK < previousD && k > d)
                {
                    buySignal = true;
                    LogMessage($"Stochastic buy signal for {symbol}: K({k:F2}) crossed above D({d:F2}) in oversold region");
                }
            }

            // Overbought region (K and D above 80)
            if (k > 80 && d > 80)
            {
                // Bearish crossover (K crosses below D)
                if (previousK > previousD && k < d)
                {
                    sellSignal = true;
                    LogMessage($"Stochastic sell signal for {symbol}: K({k:F2}) crossed below D({d:F2}) in overbought region");
                }
            }

            // Handle buy signal
            if (buySignal)
            {
                decimal dataForInr = await _tradingService.GetBalanceCurrencyAsync("INR");
                decimal maxTradeSize = Math.Min(_settingsService.ScalpingMaxTradeSize, dataForInr);
                decimal quantity = maxTradeSize / bestAsk;

                var opportunity = new ScalpingEventArgs
                {
                    Symbol = symbol,
                    Exchange = exchange,
                    BuyPrice = bestAsk,
                    SellPrice = null, // Not selling immediately
                    Quantity = quantity,
                    ProfitPercentage = 0, // Not calculated yet
                    EstimatedProfit = 0, // Not calculated yet
                    Timestamp = DateTime.Now,
                    Strategy = TradingStrategy.StochasticOscillator,
                    StochasticK = k,
                    StochasticD = d
                };

                // Notify about the opportunity
                ScalpingOpportunityDetected?.Invoke(this, opportunity);

                // Execute buy order if trading is enabled
                if (_settingsService.ScalpingEnabled)
                {
                    try
                    {
                        var buyResult = await _tradingService.CreateBuyOrderAsync(
                            symbol, exchange, bestAsk, quantity);

                        if (buyResult != null)
                        {
                            LogMessage($"Stochastic strategy: Buy order placed for {quantity} {symbol} at {bestAsk}");
                            ScalpingTradeExecuted?.Invoke(this, opportunity);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Error executing stochastic buy: {ex.Message}");
                    }
                }
            }

            // Handle sell signal for existing holdings
            if (sellSignal)
            {
                decimal dataForCurrent = await _tradingService.GetBalanceCurrencyAsync(symbol.Split('/')[0]);
                if (dataForCurrent > 0)
                {
                    var opportunity = new ScalpingEventArgs
                    {
                        Symbol = symbol,
                        Exchange = exchange,
                        BuyPrice = null, // Not buying
                        SellPrice = bestBid,
                        Quantity = dataForCurrent,
                        ProfitPercentage = 0, // Not calculated
                        EstimatedProfit = 0, // Not calculated
                        Timestamp = DateTime.Now,
                        Strategy = TradingStrategy.StochasticOscillator,
                        StochasticK = k,
                        StochasticD = d
                    };

                    // Notify about the opportunity
                    ScalpingOpportunityDetected?.Invoke(this, opportunity);

                    // Execute sell order if trading is enabled
                    if (_settingsService.ScalpingEnabled)
                    {
                        try
                        {
                            var sellResult = await _tradingService.CreateSellOrderAsync(
                                symbol, exchange, bestBid, dataForCurrent);

                            if (sellResult != null)
                            {
                                LogMessage($"Stochastic strategy: Sell order placed for {dataForCurrent} {symbol} at {bestBid}");
                                ScalpingTradeExecuted?.Invoke(this, opportunity);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Error executing stochastic sell: {ex.Message}");
                        }
                    }
                }
            }
        }

        private async Task ScanWithMovingAverage(string symbol, string exchange, CancellationToken token)
        {
            string key = $"{symbol}_{exchange}";

            // Need enough price history
            if (_priceHistory[key].Count < _settingsService.SlowMAPeriod)
            {
                return;
            }

            // Get current market data
            var depthResponse = await _tradingService.GetMarketDepthAsync(symbol, exchange);
            if (depthResponse == null)
            {
                LogMessage($"Failed to get market depth for {symbol} on {exchange}");
                return;
            }

            var data = depthResponse["data"];
            var bids = data["bids"] as JArray;
            var asks = data["asks"] as JArray;

            if (bids == null || asks == null || !bids.Any() || !asks.Any())
            {
                return;
            }

            // Get best bid and ask
            var bestBid = decimal.Parse(bids[0][0].ToString());
            var bestAsk = decimal.Parse(asks[0][0].ToString());
            var bidVolume = decimal.Parse(bids[0][1].ToString());
            var askVolume = decimal.Parse(asks[0][1].ToString());

            // Calculate MAs
            var priceData = _priceHistory[key].ToList();
            decimal fastMA = CalculateSMA(priceData, _settingsService.FastMAPeriod);
            decimal slowMA = CalculateSMA(priceData, _settingsService.SlowMAPeriod);

            // Get previous MAs
            var previousPriceData = priceData.Take(priceData.Count - 1).ToList();
            decimal previousFastMA = CalculateSMA(previousPriceData, _settingsService.FastMAPeriod);
            decimal previousSlowMA = CalculateSMA(previousPriceData, _settingsService.SlowMAPeriod);

            // Check for crossovers
            bool buySignal = previousFastMA <= previousSlowMA && fastMA > slowMA;
            bool sellSignal = previousFastMA >= previousSlowMA && fastMA < slowMA;

            if (buySignal)
            {
                LogMessage($"MA buy signal for {symbol}: Fast MA({fastMA:F2}) crossed above Slow MA({slowMA:F2})");

                decimal dataForInr = await _tradingService.GetBalanceCurrencyAsync("INR");
                decimal maxTradeSize = Math.Min(_settingsService.ScalpingMaxTradeSize, dataForInr);
                decimal quantity = maxTradeSize / bestAsk;

                var opportunity = new ScalpingEventArgs
                {
                    Symbol = symbol,
                    Exchange = exchange,
                    BuyPrice = bestAsk,
                    SellPrice = null, // Not selling immediately
                    Quantity = quantity,
                    ProfitPercentage = 0, // Not calculated yet
                    EstimatedProfit = 0, // Not calculated yet
                    Timestamp = DateTime.Now,
                    Strategy = TradingStrategy.MovingAverage,
                    FastMA = fastMA,
                    SlowMA = slowMA
                };

                // Notify about the opportunity
                ScalpingOpportunityDetected?.Invoke(this, opportunity);

                // Execute buy order if trading is enabled
                if (_settingsService.ScalpingEnabled)
                {
                    try
                    {
                        var buyResult = await _tradingService.CreateBuyOrderAsync(
                            symbol, exchange, bestAsk, quantity);

                        if (buyResult != null)
                        {
                            LogMessage($"MA Strategy: Buy order placed for {quantity} {symbol} at {bestAsk}");
                            ScalpingTradeExecuted?.Invoke(this, opportunity);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Error executing MA buy: {ex.Message}");
                    }
                }
            }

            if (sellSignal)
            {
                LogMessage($"MA sell signal for {symbol}: Fast MA({fastMA:F2}) crossed below Slow MA({slowMA:F2})");

                decimal dataForCurrent = await _tradingService.GetBalanceCurrencyAsync(symbol.Split('/')[0]);
                if (dataForCurrent > 0)
                {
                    var opportunity = new ScalpingEventArgs
                    {
                        Symbol = symbol,
                        Exchange = exchange,
                        BuyPrice = null, // Not buying
                        SellPrice = bestBid,
                        Quantity = dataForCurrent,
                        ProfitPercentage = 0, // Not calculated
                        EstimatedProfit = 0, // Not calculated
                        Timestamp = DateTime.Now,
                        Strategy = TradingStrategy.MovingAverage,
                        FastMA = fastMA,
                        SlowMA = slowMA
                    };

                    // Notify about the opportunity
                    ScalpingOpportunityDetected?.Invoke(this, opportunity);

                    // Execute sell order if trading is enabled
                    if (_settingsService.ScalpingEnabled)
                    {
                        try
                        {
                            var sellResult = await _tradingService.CreateSellOrderAsync(
                                symbol, exchange, bestBid, dataForCurrent);

                            if (sellResult != null)
                            {
                                LogMessage($"MA Strategy: Sell order placed for {dataForCurrent} {symbol} at {bestBid}");
                                ScalpingTradeExecuted?.Invoke(this, opportunity);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Error executing MA sell: {ex.Message}");
                        }
                    }
                }
            }
        }

        private async Task ScanWithCombinedStrategy(string symbol, string exchange, CancellationToken token)
        {
            string key = $"{symbol}_{exchange}";

            // Need enough price history
            if (_priceHistory[key].Count < Math.Max(_settingsService.SlowMAPeriod, _settingsService.StochasticKPeriod))
            {
                return;
            }

            // Get current market data
            var depthResponse = await _tradingService.GetMarketDepthAsync(symbol, exchange);
            if (depthResponse == null)
            {
                LogMessage($"Failed to get market depth for {symbol} on {exchange}");
                return;
            }

            var data = depthResponse["data"];
            var bids = data["bids"] as JArray;
            var asks = data["asks"] as JArray;

            if (bids == null || asks == null || !bids.Any() || !asks.Any())
            {
                return;
            }

            // Get best bid and ask
            var bestBid = decimal.Parse(bids[0][0].ToString());
            var bestAsk = decimal.Parse(asks[0][0].ToString());

            var priceData = _priceHistory[key].ToList();

            // Calculate Moving Averages
            decimal fastMA = CalculateSMA(priceData, _settingsService.FastMAPeriod);
            decimal slowMA = CalculateSMA(priceData, _settingsService.SlowMAPeriod);

            var previousPriceData = priceData.Take(priceData.Count - 1).ToList();
            decimal previousFastMA = CalculateSMA(previousPriceData, _settingsService.FastMAPeriod);
            decimal previousSlowMA = CalculateSMA(previousPriceData, _settingsService.SlowMAPeriod);

            bool maBuySignal = previousFastMA <= previousSlowMA && fastMA > slowMA;
            bool maSellSignal = previousFastMA >= previousSlowMA && fastMA < slowMA;

            // Calculate Stochastic oscillator
            var kPeriod = _settingsService.StochasticKPeriod;
            var dPeriod = _settingsService.StochasticDPeriod;
            var slowing = _settingsService.StochasticSlowing;

            var (k, d) = CalculateStochasticOscillator(priceData, kPeriod, dPeriod, slowing);
            var (previousK, previousD) = CalculateStochasticOscillator(previousPriceData, kPeriod, dPeriod, slowing);

            bool stochasticBuySignal = k < 20 && d < 20 && previousK < previousD && k > d;
            bool stochasticSellSignal = k > 80 && d > 80 && previousK > previousD && k < d;

            // Combined signals - both indicators must agree
            bool combinedBuySignal = maBuySignal && stochasticBuySignal;
            bool combinedSellSignal = maSellSignal && stochasticSellSignal;

            if (combinedBuySignal)
            {
                LogMessage($"Combined buy signal for {symbol}: MA and Stochastic indicators aligned");

                decimal dataForInr = await _tradingService.GetBalanceCurrencyAsync("INR");
                decimal maxTradeSize = Math.Min(_settingsService.ScalpingMaxTradeSize, dataForInr);
                decimal quantity = maxTradeSize / bestAsk;

                var opportunity = new ScalpingEventArgs
                {
                    Symbol = symbol,
                    Exchange = exchange,
                    BuyPrice = bestAsk,
                    SellPrice = null,
                    Quantity = quantity,
                    ProfitPercentage = 0,
                    EstimatedProfit = 0,
                    Timestamp = DateTime.Now,
                    Strategy = TradingStrategy.Combined,
                    FastMA = fastMA,
                    SlowMA = slowMA,
                    StochasticK = k,
                    StochasticD = d
                };

                // Notify about the opportunity
                ScalpingOpportunityDetected?.Invoke(this, opportunity);

                // Execute buy order if trading is enabled
                if (_settingsService.ScalpingEnabled)
                {
                    try
                    {
                        var buyResult = await _tradingService.CreateBuyOrderAsync(
                            symbol, exchange, bestAsk, quantity);

                        if (buyResult != null)
                        {
                            LogMessage($"Combined Strategy: Buy order placed for {quantity} {symbol} at {bestAsk}");
                            ScalpingTradeExecuted?.Invoke(this, opportunity);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Error executing combined buy: {ex.Message}");
                    }
                }
            }

            if (combinedSellSignal)
            {
                LogMessage($"Combined sell signal for {symbol}: MA and Stochastic indicators aligned");

                decimal dataForCurrent = await _tradingService.GetBalanceCurrencyAsync(symbol.Split('/')[0]);
                if (dataForCurrent > 0)
                {
                    var opportunity = new ScalpingEventArgs
                    {
                        Symbol = symbol,
                        Exchange = exchange,
                        BuyPrice = null,
                        SellPrice = bestBid,
                        Quantity = dataForCurrent,
                        ProfitPercentage = 0,
                        EstimatedProfit = 0,
                        Timestamp = DateTime.Now,
                        Strategy = TradingStrategy.Combined,
                        FastMA = fastMA,
                        SlowMA = slowMA,
                        StochasticK = k,
                        StochasticD = d
                    };

                    // Notify about the opportunity
                    ScalpingOpportunityDetected?.Invoke(this, opportunity);

                    // Execute sell order if trading is enabled
                    if (_settingsService.ScalpingEnabled)
                    {
                        try
                        {
                            var sellResult = await _tradingService.CreateSellOrderAsync(
                                symbol, exchange, bestBid, dataForCurrent);

                            if (sellResult != null)
                            {
                                LogMessage($"Combined Strategy: Sell order placed for {dataForCurrent} {symbol} at {bestBid}");
                                ScalpingTradeExecuted?.Invoke(this, opportunity);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Error executing combined sell: {ex.Message}");
                        }
                    }
                }
            }
        }

        private async Task ExecuteTrade(ScalpingEventArgs opportunity, CancellationToken token,
            decimal bestAsk, decimal bestBid, decimal buyQuantity,
            decimal profitThreshold, decimal tradingFeeRate, decimal tdsRate)
        {
            // Notify about the opportunity
            ScalpingOpportunityDetected?.Invoke(this, opportunity);

            LogMessage($"Scalping opportunity: {opportunity.Symbol} on {opportunity.Exchange}, " +
                      $"Spread: {opportunity.ProfitPercentage:P2}, " +
                      $"Est. Profit: {opportunity.EstimatedProfit:C2}");

            if (!_settingsService.ScalpingEnabled)
            {
                // Automatic trading disabled
                return;
            }

            if (token.IsCancellationRequested)
                return;

            // Execute trades if auto-trading is enabled
            try
            {
                // Place buy order
                var buyResult = await _tradingService.CreateBuyOrderAsync(
                    opportunity.Symbol, opportunity.Exchange, bestAsk, buyQuantity);

                if (buyResult == null)
                {
                    LogMessage($"Failed to place buy order: {buyResult?["message"]}");
                    return; // Stop execution if buy fails
                }

                LogMessage($"Buy order placed for {buyQuantity} {opportunity.Symbol} at {bestAsk}");

                // Wait for the market to update
                await Task.Delay(500); // Adjust delay if needed

                // Fetch latest market depth before selling
                var updatedDepth = await _tradingService.GetMarketDepthAsync(opportunity.Symbol, opportunity.Exchange);
                var updatedBestBid = decimal.Parse(updatedDepth["data"]["bids"][0][0].ToString());

                // Recalculate spread & profit
                decimal updatedSpread = (updatedBestBid - bestAsk) / bestAsk;
                decimal updatedProfit = (updatedBestBid * buyQuantity) - (bestAsk * buyQuantity);
                decimal updatedTradingFees = (bestAsk * buyQuantity + updatedBestBid * buyQuantity) * tradingFeeRate;
                decimal updatedTdsCost = updatedBestBid * buyQuantity * tdsRate;
                decimal updatedNetProfit = updatedProfit - updatedTradingFees - updatedTdsCost;

                decimal requiredSpread = profitThreshold + (2 * tradingFeeRate) + tdsRate;
                if (updatedSpread < requiredSpread || updatedNetProfit <= 0)
                {
                    LogMessage($"Skipping sell order: Unprofitable after updated spread calculation.");
                    return;
                }

                // Place sell order
                var sellResult = await _tradingService.CreateSellOrderAsync(
                    opportunity.Symbol, opportunity.Exchange, updatedBestBid, buyQuantity);

                if (sellResult == null)
                {
                    LogMessage($"Sell order failed! Consider manually selling {buyQuantity} {opportunity.Symbol}.");
                }
                else
                {
                    LogMessage($"Sell order placed for {buyQuantity} {opportunity.Symbol} at {updatedBestBid}");
                    ScalpingTradeExecuted?.Invoke(this, opportunity);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error executing scalping trade: {ex.Message}");
            }
        }

        #region Technical Indicators

        private (decimal k, decimal d) CalculateStochasticOscillator(List<decimal> prices, int kPeriod, int dPeriod, int slowing)
        {
            if (prices.Count < Math.Max(kPeriod, dPeriod))
                return (50, 50); // Default neutral values if not enough data

            // Calculate %K
            var recentPrices = prices.Skip(Math.Max(0, prices.Count - kPeriod)).ToList();
            decimal high = recentPrices.Max();
            decimal low = recentPrices.Min();
            decimal current = prices.Last();

            // Avoid division by zero
            if (high == low)
                return (50, 50);

            decimal rawK = ((current - low) / (high - low)) * 100;

            // Apply slowing if needed (simple moving average of raw %K values)
            if (slowing > 1 && prices.Count >= kPeriod + slowing - 1)
            {
                List<decimal> kValues = new List<decimal>();
                for (int i = 0; i < slowing; i++)
                {
                    var slowingPrices = prices.Skip(Math.Max(0, prices.Count - kPeriod - i)).Take(kPeriod).ToList();
                    decimal slowingHigh = slowingPrices.Max();
                    decimal slowingLow = slowingPrices.Min();
                    decimal slowingCurrent = prices[prices.Count - 1 - i];

                    if (slowingHigh != slowingLow)
                    {
                        kValues.Add(((slowingCurrent - slowingLow) / (slowingHigh - slowingLow)) * 100);
                    }
                    else
                    {
                        kValues.Add(50);
                    }
                }
                rawK = kValues.Average();
            }

            // Calculate %D (SMA of %K)
            decimal d = 50; // Default value
            if (prices.Count >= kPeriod + dPeriod)
            {
                List<decimal> dValues = new List<decimal>();
                for (int i = 0; i < dPeriod; i++)
                {
                    var dPrices = prices.Skip(Math.Max(0, prices.Count - kPeriod - i)).Take(kPeriod).ToList();
                    decimal dHigh = dPrices.Max();
                    decimal dLow = dPrices.Min();
                    decimal dCurrent = prices[prices.Count - 1 - i];

                    if (dHigh != dLow)
                    {
                        dValues.Add(((dCurrent - dLow) / (dHigh - dLow)) * 100);
                    }
                    else
                    {
                        dValues.Add(50);
                    }
                }
                d = dValues.Average();
            }

            return (rawK, d);
        }

        private decimal CalculateSMA(List<decimal> prices, int period)
        {
            if (prices.Count < period)
                return prices.Average(); // Return average of available prices if not enough data

            return prices.Skip(Math.Max(0, prices.Count - period)).Take(period).Average();
        }

        private decimal CalculateEMA(List<decimal> prices, int period)
        {
            if (prices.Count < period)
                return prices.Average();

            // Start with SMA for the first EMA value
            decimal ema = prices.Take(period).Average();

            // Multiplier: (2 / (period + 1))
            decimal multiplier = 2m / (period + 1m);

            // Calculate EMA for remaining prices
            for (int i = period; i < prices.Count; i++)
            {
                ema = ((prices[i] - ema) * multiplier) + ema;
            }

            return ema;
        }

        public decimal CalculateRSI(List<decimal> prices, int period = 14)
        {
            if (prices.Count < period) return 0; // Ensure enough data

            decimal gain = 0, loss = 0;

            for (int i = 1; i < period; i++)
            {
                decimal change = prices[i] - prices[i - 1];
                if (change > 0) gain += change;
                else loss -= change; // Convert loss to positive
            }

            decimal avgGain = gain / period;
            decimal avgLoss = loss / period;

            for (int i = period; i < prices.Count; i++)
            {
                decimal change = prices[i] - prices[i - 1];
                if (change > 0)
                {
                    avgGain = ((avgGain * (period - 1)) + change) / period;
                    avgLoss = (avgLoss * (period - 1)) / period;
                }
                else
                {
                    avgGain = (avgGain * (period - 1)) / period;
                    avgLoss = ((avgLoss * (period - 1)) - change) / period;
                }
            }

            decimal rs = avgLoss == 0 ? 100 : avgGain / avgLoss;
            return 100 - (100 / (1 + rs));
        }



        private void LogMessage(string message)
        {
            ScalpingLogMessage?.Invoke(this, message);
        }
        #endregion
    }

    public class ScalpingEventArgs : EventArgs
    {
        public string Symbol { get; set; }
        public string Exchange { get; set; }
        public decimal? BuyPrice { get; set; }
        public decimal? SellPrice { get; set; }
        public decimal Quantity { get; set; }
        public decimal ProfitPercentage { get; set; }
        public decimal EstimatedProfit { get; set; }
        public DateTime Timestamp { get; set; }
        public TradingStrategy Strategy { get; set; }

        // Technical indicator values
        public decimal? StochasticK { get; set; }
        public decimal? StochasticD { get; set; }
        public decimal? FastMA { get; set; }
        public decimal? SlowMA { get; set; }
        public decimal? RSI { get; set; }
    }
}