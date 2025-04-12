using CoinswitchTrader.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CoinswitchTrader.Services
{
    public class ChandelierExitStrategyService
    {
        private readonly List<decimal> _highs = new();
        private readonly List<decimal> _lows = new();
        private readonly List<decimal> _closes = new();

        private readonly int _atrPeriod = 22;
        private readonly decimal _atrMultiplier = 3.0m;
        private readonly decimal _trailingStopPercent = 5.0m;
        private readonly bool _exitOnReversal = true;

        private decimal _longStop = 0;
        private decimal _shortStop = 0;
        private int _direction = 1;
        private int _previousDirection = 1;

        private bool _isRunning = false;
        private bool _positionOpen = false;
        private decimal _entryPrice = 0m;

        private CancellationTokenSource _cancellationTokenSource;

        private readonly TradingService _tradingService;
        private readonly SettingsService _settingsService;
        private readonly HistoricalDataService _historicalDataService;

        public ChandelierExitStrategyService(TradingService tradingService, SettingsService settingsService, HistoricalDataService historicalDataService)
        {
            _tradingService = tradingService;
            _settingsService = settingsService;
            _historicalDataService = historicalDataService;
        }

        public void StartTrading(List<string> symbols, List<string> exchanges, int scanIntervalMs = 5000)
        {
            if (_isRunning) return;

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            Task.Run(async () =>
            {
                var loadedSymbols = new HashSet<string>();

                while (!token.IsCancellationRequested)
                {
                    foreach (var symbol in symbols)
                    {
                        foreach (var exchange in exchanges)
                        {
                            try
                            {
                                string key = $"{symbol}_{exchange}";

                                var depthResponse = await _tradingService.GetMarketDepthAsync(symbol, exchange);
                                if (depthResponse == null) continue;

                                var data = depthResponse["data"];
                                var bids = data["bids"] as JArray;
                                var asks = data["asks"] as JArray;
                                if (bids == null || asks == null) continue;

                                if (!loadedSymbols.Contains(key))
                                {
                                    var historicalDatas = await _historicalDataService.GetHistoricalDataAsync(symbol, exchange, timeframe: "15");
                                    foreach (var candle in historicalDatas)
                                    {
                                        AddNewCandle(candle.Open, candle.High, candle.Low, candle.Close);
                                    }
                                    loadedSymbols.Add(key);
                                }

                                var bestBid = ConvertToDecimal(bids[0][0].ToString());
                                var bestAsk = ConvertToDecimal(asks[0][0].ToString());
                                var currentPrice = (bestBid + bestAsk) / 2;

                                AddNewCandle(bestBid, bestAsk, bestBid, currentPrice);

                                if (!_positionOpen)
                                {
                                    if (IsBuySignal())
                                    {
                                        await ExecuteBuy(symbol, exchange, bestBid, currentPrice);
                                    }
                                    else if (IsSellSignal())
                                    {
                                        await ExecuteSell(symbol, exchange, bestAsk, currentPrice);
                                    }
                                }
                                else
                                {
                                    await ManageOpenPosition(symbol, exchange, currentPrice);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"[ChandelierExitStrategy] Error: {ex.Message}");
                            }
                        }
                    }

                    await Task.Delay(scanIntervalMs, token);
                }
            }, _cancellationTokenSource.Token);
        }

        public void StopTrading()
        {
            _isRunning = false;
            _cancellationTokenSource?.Cancel();
            Logger.Log("[ChandelierExitStrategy] Trading stopped.");
        }

        private async Task ExecuteBuy(string symbol, string exchange, decimal bidPrice, decimal currentPrice)
        {
            if (_settingsService.TradingMode == "SPOT")
            {
                decimal inrBalance = await _tradingService.GetBalanceCurrencyAsync("INR");
                decimal maxTradeSize = Math.Min(_settingsService.ScalpingMaxTradeSize, inrBalance);
                decimal quantity = maxTradeSize / bidPrice;

                if (quantity * bidPrice >= 150)
                {
                    var order = await _tradingService.CreateBuyOrderAsync(symbol, exchange, bidPrice, quantity);
                    Logger.Log($"SPOT BUY {quantity} {symbol} at {bidPrice}");
                    _entryPrice = bidPrice;
                    _positionOpen = true;
                }
            }
            else if (_settingsService.TradingMode == "FUTURES")
            {
                decimal usdtBalance = await _tradingService.GetBalanceCurrencyAsync("USDT");
              var data =await _tradingService.GetLeverageForCoin("EXCHANGE_2", symbol + "USDT");
                decimal leverage = ConvertToDecimal(data["data"]["leverage"].ToString());
                decimal maxTradeSize = Math.Min(_settingsService.ScalpingMaxTradeSize, usdtBalance);
                decimal quantity = maxTradeSize * leverage / currentPrice;

                //var order = await _tradingService.CreateFuturesBuyOrderAsync(symbol, exchange, quantity, leverage);
                //Logger.Log($"FUTURES LONG {quantity} {symbol} at {currentPrice}");
                //_entryPrice = currentPrice;
                //_positionOpen = true;
            }
        }

        private async Task ExecuteSell(string symbol, string exchange, decimal askPrice, decimal currentPrice)
        {
            if (_settingsService.TradingMode == "SPOT")
            {
                decimal assetBalance = await _tradingService.GetBalanceCurrencyAsync(symbol.Split('/')[0]);
                if (assetBalance * askPrice >= 150)
                {
                    var order = await _tradingService.CreateSellOrderAsync(symbol, exchange, askPrice, assetBalance);
                    Logger.Log($"SPOT SELL {assetBalance} {symbol} at {askPrice}");
                    _entryPrice = askPrice;
                    _positionOpen = true;
                }
            }
            else if (_settingsService.TradingMode == "FUTURES")
            {
                decimal usdtBalance = await _tradingService.GetBalanceCurrencyAsync("USDT");

                //decimal leverage = _settingsService.Leverage;
                //decimal maxTradeSize = Math.Min(_settingsService.ScalpingMaxTradeSize, usdtBalance);
                //decimal quantity = maxTradeSize * leverage / currentPrice;

                //var order = await _tradingService.CreateFuturesSellOrderAsync(symbol, exchange, quantity, leverage);
                //Logger.Log($"FUTURES SHORT {quantity} {symbol} at {currentPrice}");
                //_entryPrice = currentPrice;
                //_positionOpen = true;
            }
        }

        private async Task ManageOpenPosition(string symbol, string exchange, decimal currentPrice)
        {
            decimal trailingStop = GetTrailingStopPrice(_entryPrice);

            if (_settingsService.TradingMode == "SPOT")
            {
                if (currentPrice <= trailingStop)
                {
                    Logger.Log($"SPOT Trailing Stop hit at {currentPrice}, closing position.");
                    _positionOpen = false;
                }
            }
            else if (_settingsService.TradingMode == "FUTURES")
            {
                //if (_direction == 1 && currentPrice <= trailingStop)
                //{
                //    await _tradingService.CloseFuturesLongAsync(symbol, exchange);
                //    Logger.Log($"FUTURES LONG closed at {currentPrice} due to trailing stop.");
                //    _positionOpen = false;
                //}
                //else if (_direction == -1 && currentPrice >= trailingStop)
                //{
                //    await _tradingService.CloseFuturesShortAsync(symbol, exchange);
                //    Logger.Log($"FUTURES SHORT closed at {currentPrice} due to trailing stop.");
                //    _positionOpen = false;
                //}
            }
        }

        private decimal ConvertToDecimal(string price) => decimal.TryParse(price, out var result) ? result : 0m;

        public void AddNewCandle(decimal open, decimal high, decimal low, decimal close)
        {
            _highs.Add(high);
            _lows.Add(low);
            _closes.Add(close);

            if (_highs.Count > _atrPeriod * 2)
            {
                _highs.RemoveAt(0);
                _lows.RemoveAt(0);
                _closes.RemoveAt(0);
            }

            UpdateStops();
        }

        private void UpdateStops()
        {
            if (_closes.Count < _atrPeriod) return;

            decimal atr = CalculateATR();
            decimal highestHigh = _highs.TakeLast(_atrPeriod).Max();
            decimal lowestLow = _lows.TakeLast(_atrPeriod).Min();
            decimal close = _closes.Last();

            _longStop = highestHigh - _atrMultiplier * atr;
            _shortStop = lowestLow + _atrMultiplier * atr;

            _previousDirection = _direction;
            if (close > _shortStop)
                _direction = 1; // Long
            else if (close < _longStop)
                _direction = -1; // Short
        }

        private decimal CalculateATR()
        {
            decimal atrSum = 0;
            for (int i = 1; i < _closes.Count; i++)
            {
                decimal highLow = _highs[i] - _lows[i];
                decimal highClosePrev = Math.Abs(_highs[i] - _closes[i - 1]);
                decimal lowClosePrev = Math.Abs(_lows[i] - _closes[i - 1]);
                decimal trueRange = Math.Max(highLow, Math.Max(highClosePrev, lowClosePrev));
                atrSum += trueRange;
            }
            return atrSum / (_closes.Count - 1);
        }

        public bool IsBuySignal() => _direction == 1 && _previousDirection == -1;

        public bool IsSellSignal() => _direction == -1 && _previousDirection == 1;

        public decimal GetTrailingStopPrice(decimal entryPrice)
        {
            return entryPrice * (1 - _trailingStopPercent / 100m);
        }
    }
}
