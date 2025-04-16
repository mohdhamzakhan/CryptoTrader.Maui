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
        private SettingsService _settingsService;
        private ChandelierExitSettings _settings;
        private Timer _marketDataTimer;
        private Timer _positionCheckTimer;
        private string _currentSymbol = "BTCUSDT";
        private string _interval = "60"; // 1 hour candles by default
        private const string _exchange = "EXCHANGE_2"; // Replace with actual exchange if needed
        public ObservableCollection<FutureCandleData> Candles { get; private set; } = new ObservableCollection<FutureCandleData>();
        public ObservableCollection<TradingSignal> Signals { get; private set; } = new ObservableCollection<TradingSignal>();
        public ObservableCollection<Position> Positions { get; private set; } = new ObservableCollection<Position>();

        public event EventHandler<TradingSignal> NewSignalGenerated;
        public event EventHandler<FutureCandleData> NewCandleReceived;
        public event EventHandler<string> ErrorOccurred;

        private bool _isRunning = false;

        public FutureTradingService()
        {
            _settings = new ChandelierExitSettings();
            _strategy = new ChandelierExitStrategy(_settings);
            _settingsService = new SettingsService();
            Initialize(_settingsService.SecretKey, _settingsService.ApiKey);

        }

        public void Initialize(string apiKey, string secretKey)
        {
            _apiClient = new ApiFurureTradingClient(apiKey, secretKey);

            // Start timers for data fetching
            _marketDataTimer = new Timer(async _ => await FetchMarketData(), null, 0, 60000); // Every minute
            _positionCheckTimer = new Timer(async _ => await CheckPositions(), null, 0, 30000); // Every 30 seconds
            _isRunning = true;
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
                var candles = await _apiClient.GetKlinesAsync(_currentSymbol, _interval, _exchange, limit);

                if (candles.Count() == 0)
                    return;

                // Merge with existing candles
                var existingTimestamps = Candles.Select(c => c.close_time).ToHashSet();

                foreach (var candle in candles.OrderBy(c => c.close_time))
                {
                    // Update or add candle
                    if (existingTimestamps.Contains(candle.close_time))
                    {
                        var existingCandle = Candles.FirstOrDefault(c => c.close_time == candle.close_time);
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
                var param = new Dictionary<string, string>
        {
            { "symbol", _currentSymbol },
            { "exchange", "EXCHANGE_2" } // Hardcoded to EXCHANGE_2 as per the API spec
        };
                var response = await _apiClient.GetFuturesPositionsAsync(param);
                var positionsData = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);

                // Clear and update positions
                Positions.Clear();

                // Parse positions data based on the new format
                if (!positionsData.ContainsKey("message") && positionsData.ContainsKey("data") && positionsData != null)
                {
                    var positions = positionsData["data"] as JArray;
                    if (positions != null)
                    {
                        foreach (JObject position in positions)
                        {
                            var symbol = position["symbol"].ToString();
                            var entryPrice = decimal.Parse(position["avg_entry_price"].ToString());
                            var quantity = decimal.Parse(position["position_size"].ToString());
                            var pnl = decimal.Parse(position["unrealised_pnl"].ToString());
                            var isLong = position["position_side"].ToString() == "LONG";
                            var markPrice = decimal.Parse(position["mark_price"].ToString());
                            var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(position["created_at"].ToString())).DateTime;

                            // Calculate current stop loss based on trailing percentage
                            var stopPrice = _strategy.GetTrailingStopPrice(markPrice, isLong);

                            Positions.Add(new Position
                            {
                                Symbol = symbol,
                                EntryPrice = entryPrice,
                                Quantity = Math.Abs(quantity),
                                IsLong = isLong,
                                CurrentStopLoss = stopPrice,
                                PnL = pnl,
                                EntryTime = createdAt
                            });

                            // Check if we need to close position based on stop loss
                            if ((isLong && markPrice <= stopPrice) || (!isLong && markPrice >= stopPrice))
                            {
                                await ClosePosition(symbol);
                            }
                        }
                    }
                }
                else
                    return;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error checking positions: {ex.Message}");
                Logger.Log($"Error checking positions: {ex.Message}");
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
                    symbol = signal.Symbol.ToLower(), // Ensure the symbol is in lowercase (dogeusdt)
                    exchange = "EXCHANGE_2",  // Hardcoded to EXCHANGE_2 as per the API spec
                    price = signal.Price, // Only add price for LIMIT orders
                    side = signal.Type == SignalType.Buy ? "BUY" : "SELL",
                    order_type = "MARKET", // Adjust order type based on the signal type
                    quantity = CalculatePositionSize(signal.Price),
                    //trigger_price = signal.Price, // Add trigger_price for TAKE_PROFIT or STOP_MARKET types
                    reduce_only = false  // Adjust based on your requirements (true/false)
                };

                var response = await _apiClient.CreateFuturesOrderAsync(order);


                Logger.Log($"Order response: {response}");
                // Log or handle response

                await CheckPositionsAsync();
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
                // Get current positions
                var positions = await _apiClient.GetOpenPositionsAsync(symbol, _exchange);
                var position = positions.FirstOrDefault(p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

                if (position != null && Math.Abs(position.PositionValue) > 0)
                {
                    // Create an order to close the position
                    var closeOrder = new
                    {
                        symbol = symbol.ToLower(),
                        exchange = _exchange,
                        price = 0m, // Market order, so price is not needed
                        side = (position.PositionSide == "LONG") ? "SELL" : "BUY", // Opposite side of the current position
                        order_type = "MARKET",
                        quantity = Math.Abs(position.PositionSize),
                        reduce_only = true // Important: this closes the position
                    };

                    var response = await _apiClient.CreateFuturesOrderAsync(closeOrder);
                    Logger.Log($"Closed position for {symbol}. Response: {response}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error closing position: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"Error closing position: {ex.Message}");
            }
        }
        private async Task CancelExistingStopOrdersAsync(string symbol)
        {
            try
            {
                // Get open orders for the symbol
                var payload = new Dictionary<string, string>
        {
            { "symbol", symbol },
            { "exchange", _exchange }
        };

                var response = await _apiClient.GetFuturesOpenOrdersAsync(payload);
                var openOrders = JsonConvert.DeserializeObject<JObject>(response);

                // Find and cancel stop orders
                if (openOrders != null && openOrders["data"] is JArray orders)
                {
                    foreach (JObject order in orders)
                    {
                        string orderType = order["order_type"]?.ToString();
                        string orderSymbol = order["symbol"]?.ToString();
                        string orderId = order["order_id"]?.ToString();

                        if (orderSymbol == symbol.ToLower() &&
                           (orderType == "STOP_MARKET" || orderType == "STOP_LIMIT"))
                        {
                            // Cancel this stop order
                            var cancelPayload = new
                            {
                                symbol = symbol.ToLower(),
                                exchange = _exchange,
                                order_id = orderId
                            };

                            await _apiClient.CancelFuturesOrderAsync(cancelPayload);
                            Logger.Log($"Cancelled stop order {orderId} for {symbol}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error cancelling stop orders: {ex.Message}");
                // Continue with creating new stop order even if cancellation fails
            }
        }
        private Position ConvertToPosition(FuturePosition fp)
        {
            if (fp == null) return null;

            try
            {
                return new Position
                {
                    Symbol = fp.Symbol ?? string.Empty,
                    IsLong = fp.PositionSide?.Equals("LONG", StringComparison.OrdinalIgnoreCase) ?? false,
                    EntryPrice = fp.AvgEntryPrice,
                    Quantity = Math.Abs(fp.PositionSize), // Ensure positive quantity
                    CurrentStopLoss = fp.CurrentStopLoss,
                    PnL = fp.UnrealisedPnl,
                    EntryTime = DateTimeOffset.FromUnixTimeMilliseconds(fp.CreatedAt).DateTime
                };
            }
            catch (Exception ex)
            {
                Logger.Log($"Error converting position data: {ex.Message}");
                return null;
            }
        }

        private async Task CheckPositionsAsync()
        {
            if (!_isRunning) return;

            try
            {
                // Check internet connectivity
                if (!await NetworkHelper.IsInternetAvailableAsync())
                {
                    Logger.Log("No internet connection available, skipping position check");
                    return;
                }

                // Get open positions for the current symbol and exchange
                var futurePositions = await _apiClient.GetOpenPositionsAsync(_currentSymbol, _exchange);
                if (futurePositions == null)
                {
                    Logger.Log("Received null position data from API");
                    return;
                }

                // Convert FuturePosition objects to Position objects
                var positions = new List<Position>();

                foreach (var fp in futurePositions)
                {
                    var position = ConvertToPosition(fp);
                    if (position != null)
                    {
                        positions.Add(position);
                    }
                }

                // Update positions collection and UI
                try
                {
                    // Using Task.Run to avoid blocking if MainThread isn't available
                    await Task.Run(async () =>
                    {
                        try
                        {
                            // Try to update on main thread if available
                            await MainThread.InvokeOnMainThreadAsync(() =>
                            {
                                Positions.Clear();
                                foreach (var position in positions)
                                {
                                    Positions.Add(position);
                                }
                            });
                        }
                        catch
                        {
                            // If MainThread isn't available, update directly
                            lock (Positions)
                            {
                                Positions.Clear();
                                foreach (var position in positions)
                                {
                                    Positions.Add(position);
                                }
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error updating positions collection: {ex.Message}");
                }

                // Update stop losses if needed
                foreach (var position in positions)
                {
                    await UpdateStopLossIfNeededAsync(position);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error checking positions: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"Position check error: {ex.Message}");
            }
        }
        private decimal CalculatePositionSize(decimal price)
        {
            // Simple implementation - adjust as needed
            const decimal riskPercent = 0.02m; // 2% risk

            // Assuming account balance of 10000 USD
            decimal accountBalance = Task.Run(() => GetFutureBalance("USDT")).Result;

            // Calculate quantity
            decimal riskedAmount = accountBalance * riskPercent;
            decimal stopLossPercent = (decimal)_settings.TrailingStopLossPercent / 100m;
            decimal quantity = riskedAmount / (price * stopLossPercent);

            // Round to appropriate precision
            return Math.Round(riskedAmount, 4);
        }

        public async Task<decimal> GetFutureBalance(string coinName)
        {
            var response = await _apiClient.GetFuturesAccountInfoAsync();
            var balanceData = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);

            if (balanceData != null && balanceData.ContainsKey("data"))
            {
                var data = balanceData["data"] as JObject;
                var balanceArray = data?["base_asset_balances"] as JArray;

                if (balanceArray != null)
                {
                    var asset = balanceArray.FirstOrDefault(x => x["base_asset"]?.ToString() == coinName);
                    if (asset != null)
                    {
                        var availableBalance = asset["balances"]?["total_available_balance"]?.ToString();
                        if (decimal.TryParse(availableBalance, out var result))
                        {
                            return result;
                        }
                    }
                }
            }
            return 0m;
        }


        private async Task UpdateStopLossIfNeededAsync(Position position)
        {
            try
            {
                // Get current price
                var payload = new Dictionary<string, string>
        {
            { "symbol", position.Symbol },
            { "exchange", _exchange }
        };
                var apiresponse = await _apiClient.GetLatestPriceAsync(payload);

                var res = JsonConvert.DeserializeObject<JObject>(apiresponse);
                if (res == null || res["data"] == null)
                {
                    Logger.Log($"Invalid price data response for {position.Symbol}");
                    return;
                }

                decimal currentPrice = decimal.Parse(res["data"]?["EXCHANGE_2"]?["last_price"]?.ToString() ?? "0");
                if (currentPrice == 0)
                {
                    Logger.Log($"Unable to get current price for {position.Symbol}");
                    return;
                }

                // Calculate trailing stop price based on position direction
                decimal newStopPrice;

                if (position.IsLong)
                {
                    // For long positions, only move stop loss up
                    var trailingStop = currentPrice * (1 - ((decimal)_settings.TrailingStopLossPercent / 100m));
                    newStopPrice = position.CurrentStopLoss == 0 ? trailingStop :
                                   Math.Max(position.CurrentStopLoss, trailingStop);
                }
                else
                {
                    // For short positions, only move stop loss down
                    var trailingStop = currentPrice * (1 + ((decimal)_settings.TrailingStopLossPercent / 100m));
                    newStopPrice = position.CurrentStopLoss == 0 ? trailingStop :
                                   Math.Min(position.CurrentStopLoss, trailingStop);
                }

                // Update stop loss if it changed significantly (more than 0.5%) or is not set
                if (position.CurrentStopLoss == 0 ||
                    Math.Abs((newStopPrice / position.CurrentStopLoss) - 1) > 0.005m)
                {
                    // First cancel any existing stop orders
                    await CancelExistingStopOrdersAsync(position.Symbol);

                    // Create new stop loss order
                    var stopOrder = new
                    {
                        symbol = position.Symbol.ToLower(),
                        exchange = _exchange,
                        price = newStopPrice,
                        side = position.IsLong ? "SELL" : "BUY",
                        order_type = "STOP_MARKET",
                        quantity = Math.Abs(position.Quantity),
                        trigger_price = newStopPrice,
                        reduce_only = true
                    };

                    var response = await _apiClient.CreateFuturesOrderAsync(stopOrder);
                    var responseObj = JsonConvert.DeserializeObject<JObject>(response);
                    string orderId = responseObj?["data"]?["order_id"]?.ToString();

                    Logger.Log($"Updated stop loss for {position.Symbol} from {position.CurrentStopLoss} to {newStopPrice}, order ID: {orderId}");

                    // Update the position object with new stop loss
                    position.CurrentStopLoss = newStopPrice;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating stop loss: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"Stop loss update error: {ex.Message}");
            }
        }
        public async Task<string> GetLeverage(string symbol, string exchange)
        {
            var param = new Dictionary<string, string>
            {
                { "symbol", symbol },
                { "exchange", exchange }
            };
            return await _apiClient.GetLeverageAsync(param);
        }


        public void Dispose()
        {
            _marketDataTimer?.Dispose();
            _positionCheckTimer?.Dispose();
        }
    }
}
