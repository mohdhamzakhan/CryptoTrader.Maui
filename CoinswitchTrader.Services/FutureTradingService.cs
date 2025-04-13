using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CryptoTrader.Maui.CoinswitchTrader.Services.Enums;
using CoinswitchTrader.Services;

namespace CryptoTrader.Maui.CoinswitchTrader.Services
{
    public class FutureTradingService
    {
        private ApiFurureTradingClient _apiClient;
        private ChandelierExitStrategy _strategy;
        private ChandelierExitSettings _settings;
        private Timer _marketDataTimer;
        private Timer _positionCheckTimer;
        private string _currentSymbol = "BTCUSDT";
        private string _interval = "1h"; // 1 hour candles by default

        public ObservableCollection<FutureCandleData> Candles { get; private set; } = new ObservableCollection<FutureCandleData>();
        public ObservableCollection<TradingSignal> Signals { get; private set; } = new ObservableCollection<TradingSignal>();
        public ObservableCollection<Position> Positions { get; private set; } = new ObservableCollection<Position>();

        public event EventHandler<TradingSignal> NewSignalGenerated;
        public event EventHandler<FutureCandleData> NewCandleReceived;
        public event EventHandler<string> ErrorOccurred;

        public FutureTradingService()
        {
            _settings = new ChandelierExitSettings();
            _strategy = new ChandelierExitStrategy(_settings);
        }

        public void Initialize(string apiKey, string secretKey)
        {
            _apiClient = new ApiFurureTradingClient(secretKey,apiKey);

            // Start timers for data fetching
            _marketDataTimer = new Timer(async _ => await FetchMarketData(), null, 0, 60000); // Every minute
            _positionCheckTimer = new Timer(async _ => await CheckPositions(), null, 0, 30000); // Every 30 seconds
        }

        public void UpdateSettings(ChandelierExitSettings settings)
        {
            _settings = settings;
            _strategy.UpdateSettings(settings);
        }

        public void ChangeSymbol(string symbol)
        {
            _currentSymbol = symbol;
            Candles.Clear();
            Signals.Clear();

            // Immediately fetch new data
            Task.Run(async () => await FetchMarketData(true));
        }

        public void ChangeInterval(string interval)
        {
            _interval = interval;
            Candles.Clear();
            Signals.Clear();

            // Immediately fetch new data
            Task.Run(async () => await FetchMarketData(true));
        }

        private async Task FetchMarketData(bool fetchHistorical = false)
        {
            try
            {
                int limit = fetchHistorical ? 100 : 5;
                var candles = await _apiClient.GetKlinesAsync(_currentSymbol, _interval, limit);

                if (candles.Count() == 0)
                    return;

                // Merge with existing candles
                var existingTimestamps = Candles.Select(c => c.Timestamp).ToHashSet();

                foreach (var candle in candles.OrderBy(c => c.Timestamp))
                {
                    // Update or add candle
                    if (existingTimestamps.Contains(candle.Timestamp))
                    {
                        var existingCandle = Candles.FirstOrDefault(c => c.Timestamp == candle.Timestamp);
                        if (existingCandle != null)
                        {
                            var index = Candles.IndexOf(existingCandle);
                            Candles[index] = candle;
                        }
                    }
                    else
                    {
                        Candles.Add(candle);

                        // Process candle for signals
                        _strategy.AddCandle(candle);
                        var signal = _strategy.GetSignal();

                        if (signal != null)
                        {
                            signal.Symbol = _currentSymbol;
                            Signals.Add(signal);
                            NewSignalGenerated?.Invoke(this, signal);

                            // Execute trade based on signal
                            await ExecuteTradeFromSignal(signal);
                        }

                        NewCandleReceived?.Invoke(this, candle);
                    }
                }

                // Keep only the recent candles in memory
                while (Candles.Count > 500)
                {
                    Candles.RemoveAt(0);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error fetching market data: {ex.Message}");
            }
        }

        private async Task CheckPositions()
        {
            try
            {
                var response = await _apiClient.GetFuturesPositionsAsync();
                var positionsData = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);

                // Clear and update positions
                Positions.Clear();

                // Parse positions data
                if (positionsData != null && positionsData.ContainsKey("positions"))
                {
                    var positions = positionsData["positions"] as JArray;
                    if (positions != null)
                    {
                        foreach (JObject position in positions)
                        {
                            var symbol = position["symbol"].ToString();
                            var entryPrice = decimal.Parse(position["entryPrice"].ToString());
                            var quantity = decimal.Parse(position["positionAmt"].ToString());
                            var pnl = decimal.Parse(position["unrealizedProfit"].ToString());
                            var isLong = quantity > 0;

                            // Calculate current stop loss based on trailing percentage
                            var currentPrice = decimal.Parse(position["markPrice"].ToString());
                            var stopPrice = _strategy.GetTrailingStopPrice(currentPrice, isLong);

                            Positions.Add(new Position
                            {
                                Symbol = symbol,
                                EntryPrice = entryPrice,
                                Quantity = Math.Abs(quantity),
                                IsLong = isLong,
                                CurrentStopLoss = stopPrice,
                                PnL = pnl,
                                EntryTime = DateTime.Now // This should come from the API if available
                            });

                            // Check if we need to close position based on stop loss
                            if ((isLong && currentPrice <= stopPrice) || (!isLong && currentPrice >= stopPrice))
                            {
                                await ClosePosition(symbol);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error checking positions: {ex.Message}");
            }
        }

        private async Task ExecuteTradeFromSignal(TradingSignal signal)
        {
            try
            {
                // Close any existing positions if setting enabled
                if (_settings.ExitOnSignalReversal)
                {
                    await ClosePosition(signal.Symbol);
                }

                // Create new order
                var order = new
                {
                    symbol = signal.Symbol,
                    side = signal.Type == SignalType.Buy ? "BUY" : "SELL",
                    type = "MARKET",
                    quantity = CalculatePositionSize(signal.Price),
                    reduceOnly = false
                };

                var response = await _apiClient.CreateFuturesOrderAsync(order);

                // Log or handle response
                Console.WriteLine($"Order response: {response}");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error executing trade: {ex.Message}");
            }
        }

        private async Task ClosePosition(string symbol)
        {
            try
            {
                var closeRequest = new
                {
                    symbol = symbol,
                    reduceOnly = true,
                };

                var response = await _apiClient.CloseFuturesPositionAsync(closeRequest);
                Console.WriteLine($"Position close response: {response}");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error closing position: {ex.Message}");
            }
        }

        private decimal CalculatePositionSize(decimal price)
        {
            // Simple implementation - adjust as needed
            const decimal riskPercent = 0.02m; // 2% risk

            // Assuming account balance of 10000 USD
            const decimal accountBalance = 10000m;

            // Calculate quantity
            decimal riskedAmount = accountBalance * riskPercent;
            decimal stopLossPercent = (decimal)_settings.TrailingStopLossPercent / 100m;
            decimal quantity = riskedAmount / (price * stopLossPercent);

            // Round to appropriate precision
            return Math.Round(quantity, 4);
        }

        public Task<string> GetLeverage( string symbol,string exchange)
        {
            var param = new Dictionary<string, string>
            {
                { "symbol", symbol },
                { "exchange", exchange }
            };
            return _apiClient.GetLeverageAsync(param);
        }

        public void Dispose()
        {
            _marketDataTimer?.Dispose();
            _positionCheckTimer?.Dispose();
        }
    }
}
