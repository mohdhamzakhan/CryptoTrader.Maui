using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoinswitchTrader.Services
{
    public class TradingService
    {
        private readonly ApiTradingClient _apiClient;
        private readonly SettingsService _settingsService;

        public TradingService([FromKeyedServices("secretKey")] string secretKey, [FromKeyedServices("apiKey")] string apiKey, SettingsService settingsService)
         {
            _settingsService = settingsService;
            _apiClient = new ApiTradingClient(secretKey,apiKey);
        }

        /// <summary>
        /// Creates a buy order with fee adjustment
        /// </summary>
        public async Task<JObject> CreateBuyOrderAsync(string symbol, string exchange, decimal price, decimal quantity)
        {
            // Calculate the effective quantity after trading fees
            decimal effectiveQuantity = AdjustBuyQuantityForFees(quantity, price);

            var payload = new Dictionary<string, object>
            {
                { "side", "buy" },
                { "symbol", symbol },
                { "type", "limit" },
                { "price", price },
                { "quantity", effectiveQuantity },
                { "exchange", exchange }
            };

            var response = await _apiClient.CreateOrderAsync(payload);
            return JsonConvert.DeserializeObject<JObject>(response);
        }

        /// <summary>
        /// Creates a sell order with TDS and fee adjustment
        /// </summary>
        public async Task<JObject> CreateSellOrderAsync(string symbol, string exchange, decimal price, decimal quantity)
        {
            // Calculate the effective quantity after TDS and trading fees
            decimal effectiveQuantity = AdjustSellQuantityForTdsAndFees(quantity, price);

            var payload = new Dictionary<string, object>
            {
                { "side", "sell" },
                { "symbol", symbol },
                { "type", "limit" },
                { "price", price },
                { "quantity", effectiveQuantity },
                { "exchange", exchange }
            };

            var response = await _apiClient.CreateOrderAsync(payload);
            return JsonConvert.DeserializeObject<JObject>(response);
        }

        /// <summary>
        /// Adjusts buy quantity accounting for trading fees
        /// </summary>
        private decimal AdjustBuyQuantityForFees(decimal quantity, decimal price)
        {
            decimal tradingFeeRate = _settingsService.TradingFeeRate;

            // Calculate effective quantity
            decimal totalCost = quantity * price;
            decimal feeAmount = totalCost * tradingFeeRate;
            decimal netAmountAvailable = totalCost - feeAmount;

            return netAmountAvailable / price;
        }

        /// <summary>
        /// Adjusts sell quantity accounting for TDS and trading fees
        /// </summary>
        private decimal AdjustSellQuantityForTdsAndFees(decimal quantity, decimal price)
        {
            decimal tdsRate = _settingsService.TdsRate;
            decimal tradingFeeRate = _settingsService.TradingFeeRate;

            // Calculate sell price needed to cover TDS and fees
            decimal originalSellValue = quantity * price;

            // Factor in TDS if enabled
            decimal tdsAdjustment = _settingsService.ApplyTdsAdjustment ?
                                    originalSellValue * tdsRate : 0;

            // Factor in trading fees
            decimal feeAmount = originalSellValue * tradingFeeRate;

            // Calculate required price to still achieve target after fees/TDS
            decimal requiredTotalValue = originalSellValue + tdsAdjustment + feeAmount;
            decimal adjustedPrice = requiredTotalValue / quantity;

            return quantity;
        }

        /// <summary>
        /// Gets the user's portfolio (account balances)
        /// </summary>
        public async Task<JObject> GetPortfolioAsync()
        {
            var response = await _apiClient.GetUserPortfolioAsync();
            return JsonConvert.DeserializeObject<JObject>(response);
        }

        /// <summary>
        /// Gets recent trades for a given symbol
        /// </summary>
        public async Task<JObject> GetRecentTradesAsync(string symbol, string exchange, int limit = 50)
        {
            var parameters = new Dictionary<string, string>
            {
                { "symbol", symbol },
                { "exchange", exchange },
                { "limit", limit.ToString() }
            };

            var response = await _apiClient.GetTradesAsync(parameters);
            return JsonConvert.DeserializeObject<JObject>(response);
        }

        /// <summary>
        /// Gets the current market depth for a symbol
        /// </summary>
        public async Task<JObject> GetMarketDepthAsync(string symbol, string exchange)
        {
            var parameters = new Dictionary<string, string>
            {
                { "symbol", symbol },
                { "exchange", exchange }
            };

            var response = await _apiClient.GetDepthAsync(parameters);
            return JsonConvert.DeserializeObject<JObject>(response);
        }

        /// <summary>
        /// Gets candlestick data for technical analysis
        /// </summary>
        public async Task<JObject> GetCandlestickDataAsync(
     string symbol,
     string exchange,
     string interval = "60", // Default to 1-hour interval
     int limit = 100)
        {
            // Convert interval to milliseconds
            int intervalMinutes = int.Parse(interval); // Convert string interval to int (e.g., "5" → 5)
            long intervalMs = intervalMinutes * 60 * 1000; // Convert to milliseconds

            // Get current local time
            DateTime endTimeLocal = DateTime.Now;
            DateTime startTimeLocal = endTimeLocal.AddMilliseconds(-limit * intervalMs);

            // Convert local time to Unix time in milliseconds
            long startTimeUnix = new DateTimeOffset(startTimeLocal).ToUnixTimeMilliseconds();
            long endTimeUnix = new DateTimeOffset(endTimeLocal).ToUnixTimeMilliseconds();

            var parameters = new Dictionary<string, string>
    {
        { "symbol", symbol },
        { "exchange", exchange },
        { "interval", interval },  // Keep interval as string
        { "start_time", startTimeUnix.ToString() },
        { "end_time", endTimeUnix.ToString() }
    };

            var response = await _apiClient.GetCandlestickDataAsync(parameters);
            return JsonConvert.DeserializeObject<JObject>(response);
        }




        /// <summary>
        /// Gets all open orders
        /// </summary>
        public async Task<JObject> GetOpenOrdersAsync()
        {
            var response = await _apiClient.GetOpenOrdersAsync();
            return JsonConvert.DeserializeObject<JObject>(response);
        }

        /// <summary>
        /// Gets all closed orders
        /// </summary>
        public async Task<JObject> GetClosedOrdersAsync()
        {
            var response = await _apiClient.GetClosedOrdersAsync();
            return JsonConvert.DeserializeObject<JObject>(response);
        }

        /// <summary>
        /// Cancels an order by order ID
        /// </summary>
        public async Task<JObject> CancelOrderAsync(string orderId, string exchange)
        {
            var payload = new Dictionary<string, object>
            {
                { "order_id", orderId },
            };

            var response = await _apiClient.CancelOrderAsync(payload);
            return JsonConvert.DeserializeObject<JObject>(response);
        }

        public async Task<JObject> GetOrderStatusAsync(string orderId)
        {
            var payload = new Dictionary<string, string>
            {
                { "order_id", orderId }
            };
            var response = await _apiClient.GetOrderAsync(payload);
            return JsonConvert.DeserializeObject<JObject>(response);
        }

        // Forward other necessary methods from the API client
        public async Task<bool> ValidateCredentialsAsync()
        {
            try
            {
                var response = await _apiClient.ValidateKeysAsync();
                var result = JsonConvert.DeserializeObject<JObject>(response);
                return result["success"]?.Value<bool>() ?? false;
            }
            catch
            {
                return false;
            }
        }
        public async Task<JObject> Get24hAllPairsDataAsync(Dictionary<string, string> paramsDict)
        {
            var response = await _apiClient.Get24hAllPairsDataAsync(paramsDict);
            return JsonConvert.DeserializeObject<JObject>(response);
        }

        public async Task<decimal> GetBalanceCurrencyAsync(string currency)
        {
            var response = await _apiClient.GetUserPortfolioAsync();
            var json = JsonConvert.DeserializeObject<JObject>(response);
            var inrData = json["data"]?.FirstOrDefault(x => x["currency"]?.ToString() == currency.ToUpper());
            return inrData != null ? decimal.Parse(inrData["main_balance"].ToString()) : 0;

        }

        public async Task<JObject> GetTickerAsync(string symbol, string exchange)
        {
            var parameters = new Dictionary<string, string>
            {
                { "symbol", symbol },
                { "exchange", exchange }
            };
            var response = await _apiClient.Get24hCoinPairDataAsync(parameters);
            return JsonConvert.DeserializeObject<JObject>(response);
        }
    }
}