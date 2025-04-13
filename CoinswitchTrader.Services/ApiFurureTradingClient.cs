using CryptoTrader.Maui.CoinswitchTrader.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using System.Data;
using System.Text;

namespace CoinswitchTrader.Services
{
    public class ApiFurureTradingClient
    {
        private string secretKey;
        private string apiKey;
        private string baseUrl;
        private HttpClient httpClient;

        public ApiFurureTradingClient(string secretKey, string apiKey)
        {
            this.secretKey = secretKey;
            this.apiKey = apiKey;
            this.baseUrl = "https://coinswitch.co";
            this.httpClient = new HttpClient();
        }

        public async Task<string> CallApiAsync(string url, HttpMethod method, Dictionary<string, string> headers = null, object payload = null)
        {
            const int maxRetries = 1;
            const int retryDelay = 5000; // Delay before retrying on network issues in milliseconds
            const int timeout = 10000; // Timeout for each request in milliseconds

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    var request = new HttpRequestMessage(method, url);

                    if (headers != null)
                    {
                        foreach (var header in headers)
                        {
                            request.Headers.Add(header.Key, header.Value);
                        }
                    }

                    if (payload != null)
                    {
                        request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    }

                    using (var cts = new CancellationTokenSource(timeout))
                    {
                        // Check network availability before sending request
                        if (!await NetworkHelper.IsInternetAvailableAsync())
                        {
                            // Wait until the internet is back online
                            await NetworkHelper.WaitForInternetAsync();
                        }

                        // Send the request
                        var response = await httpClient.SendAsync(request, cts.Token);
                        //Task.Delay(5000).Wait();
                        // Ensure success status code or handle rate limiting
                        response.EnsureSuccessStatusCode(); // Throws an exception for HTTP error responses

                        var responseString = await response.Content.ReadAsStringAsync();

                        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        {
                            // Handle rate limiting
                            throw new Exception("Rate limiting encountered.");
                        }

                        return responseString;
                    }
                }
                catch (TaskCanceledException e) when (!e.CancellationToken.IsCancellationRequested)
                {
                    //LogMessage($"Request timed out. Retrying in {retryDelay / 1000} seconds. Exception: {e.Message}");
                    await Task.Delay(retryDelay);

                    if (attempt == maxRetries - 1)
                    {
                        break;
                        throw new TimeoutException("The request timed out.", e);
                    }

                }
                catch (HttpRequestException e) when (e.Message.Contains("No such host"))
                {
                    //LogMessage($"No such host. Retrying in {retryDelay / 1000} seconds. Exception: {e.Message}");
                    await Task.Delay(retryDelay);

                    if (attempt == maxRetries - 1)
                    {
                        break;
                        throw new Exception("No such host error after maximum retries.", e);
                    }

                }
                catch (HttpRequestException e)
                {
                    //LogMessage($"Request error: {e.Message}. Retrying in {retryDelay / 1000} seconds.");
                    await Task.Delay(retryDelay);

                    if (attempt == maxRetries - 1)
                    {
                        break;
                        throw;
                    }

                }
                catch (Exception ex)
                {
                    //LogMessage($"Unexpected error: {ex.Message}. Retrying in {retryDelay / 1000} seconds. {url}");
                    await Task.Delay(retryDelay);

                    if (attempt == maxRetries - 1)
                    {
                        break;
                        throw new Exception("An unexpected error occurred during API call.", ex);
                    }

                }
            }

            return null;
        }
        public string SignatureMessage(string method, string url, string epochTime)
        {
            return method + url + epochTime;
        }
        // Helper method to convert hex string to byte array
        private byte[] HexStringToByteArray(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return null;

            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

        // Helper method to convert byte array to hex string
        private string ByteArrayToHexString(byte[] bytes)
        {
            StringBuilder hex = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }
        public string GetSignatureOfRequest(string secretKey, string requestString)
        {
            try
            {
                byte[] requestBytes = Encoding.UTF8.GetBytes(requestString);
                byte[] secretKeyBytes = HexStringToByteArray(secretKey);

                // Use Bouncy Castle for Ed25519 signatures
                Ed25519PrivateKeyParameters privateKey = new Ed25519PrivateKeyParameters(secretKeyBytes, 0);
                Ed25519Signer signer = new Ed25519Signer();
                signer.Init(true, privateKey);
                signer.BlockUpdate(requestBytes, 0, requestBytes.Length);
                byte[] signatureBytes = signer.GenerateSignature();

                // Convert to lowercase hex string
                string signature = ByteArrayToHexString(signatureBytes);
                return signature;
            }
            catch (Exception ex)
            {
                Logger.Log($"Signature error: {ex.Message}");
                return null;
            }
        }

        public async Task<string> GetFuturesInstrumentInfoAsync(Dictionary<string, string> paramsDict = null)
        {
            try
            {
                // 1. Prepare endpoint
                string endpoint = "/trade/api/v2/futures/instrument_info";

                // 2. Add query parameters if needed
                if (paramsDict != null && paramsDict.Count > 0)
                {
                    string queryString = string.Join("&", paramsDict.Select(kv => $"{kv.Key}={kv.Value}"));
                    endpoint += "?" + queryString;
                    endpoint = Uri.UnescapeDataString(endpoint.Replace("+", " "));
                }

                // 3. Signature stuff
                string epochTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                string signatureMsg = SignatureMessage("GET", endpoint, epochTime); // Combine method + endpoint + epoch
                string signature = GetSignatureOfRequest(this.secretKey, signatureMsg); // HMAC SHA256

                if (signature == null)
                {
                    return JsonConvert.SerializeObject(new { message = "Please Enter Valid Keys" });
                }

                // 4. Full URL
                string url = $"{this.baseUrl}{endpoint}";

                // 5. Headers
                var headers = new Dictionary<string, string>
        {
            { "Content-Type", "application/json" },
            { "X-AUTH-SIGNATURE", signature },
            { "X-AUTH-APIKEY", this.apiKey },
            { "X-REQUEST-ID", "prod-Hamza" },
            { "X-AUTH-EPOCH", epochTime }
        };

                // 6. Actually call the API (no body for GET)
                return await CallApiAsync(url, HttpMethod.Get, headers, null);
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }

        private async Task<string> MakeRequestAsync(string method, string endpoint, Dictionary<string, string> queryParams = null, object body = null)
        {
            try
            {
                // Append query parameters for GET
                if (method == HttpMethod.Get.Method && queryParams != null && queryParams.Count > 0)
                {
                    endpoint += '?' + string.Join("&", queryParams.Select(kv => $"{kv.Key}={kv.Value}"));
                    endpoint = Uri.UnescapeDataString(endpoint.Replace("+", " "));
                }

                string epochTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                string signatureMsg = SignatureMessage(method, endpoint, epochTime);
                string signature = GetSignatureOfRequest(this.secretKey, signatureMsg);

                if (signature == null)
                {
                    return JsonConvert.SerializeObject(new { message = "Please Enter Valid Keys" });
                }

                string fullUrl = $"{this.baseUrl}{endpoint}"; // Don't redeclare 'url', use 'fullUrl'

                // Create Headers
                var headers = new Dictionary<string, string>
        {
            { "X-AUTH-SIGNATURE", signature },
            { "X-AUTH-APIKEY", this.apiKey },
            { "X-REQUEST-ID", "prod-Hamza" },
            { "X-AUTH-EPOCH", epochTime }
        };

                // Call API with full URL, method, headers, and body
                return await CallApiAsync(fullUrl, new HttpMethod(method), headers, body);
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }




        public Dictionary<string, object> RemoveTrailingZeros(Dictionary<string, object> dictionary)
        {
            foreach (var key in dictionary.Keys.ToList())
            {
                if (dictionary[key] is double d && d == (int)d)
                {
                    dictionary[key] = (int)d;
                }
            }
            return dictionary;
        }

        public async Task<List<FutureCandleData>> GetKlinesAsync(string symbol, string interval, int limit = 100)
        {
            try
            {
                var parameters = new Dictionary<string, string>
                {
                    { "symbol", symbol },
                    { "interval", interval },
                    { "limit", limit.ToString() }
                };

                var response = await MakeRequestAsync("GET", "/trade/api/v2/futures/klines", parameters);
                var candlesArray = JsonConvert.DeserializeObject<List<List<object>>>(response);

                var candles = new List<FutureCandleData>();

                foreach (var candleData in candlesArray)
                {
                    var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(candleData[0])).DateTime;
                    var open = Convert.ToDecimal(candleData[1]);
                    var high = Convert.ToDecimal(candleData[2]);
                    var low = Convert.ToDecimal(candleData[3]);
                    var close = Convert.ToDecimal(candleData[4]);
                    var volume = Convert.ToDecimal(candleData[5]);

                    candles.Add(new FutureCandleData
                    {
                        Timestamp = timestamp,
                        Open = open,
                        High = high,
                        Low = low,
                        Close = close,
                        Volume = volume
                    });
                }

                return candles;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting klines: {ex.Message}");
                return new List<FutureCandleData>();
            }
        }

        // ================= FUTURES APIs =================

        // 1. Get Current Leverage
        public Task<string> GetLeverageAsync(Dictionary<string, string> paramsDict = null)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/futures/leverage", paramsDict);
        }

        // 2. Change Leverage
        public Task<string> ChangeLeverageAsync(object body)
        {
            return MakeRequestAsync("POST", "/trade/api/v2/futures/leverage", null, body);
        }

        // 3. Create Futures Order
        public Task<string> CreateFuturesOrderAsync(object body)
        {
            return MakeRequestAsync("POST", "/trade/api/v2/futures/order", null, body);
        }

        // 4. Cancel Futures Order
        public Task<string> CancelFuturesOrderAsync(object body)
        {
            return MakeRequestAsync("POST", "/trade/api/v2/futures/order/cancel", null, body);
        }

        // 5. Get Futures Order Details
        public Task<string> GetFuturesOrderDetailsAsync(Dictionary<string, string> paramsDict)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/futures/order/detail", paramsDict);
        }

        // 6. Get Open Futures Orders
        public Task<string> GetFuturesOpenOrdersAsync(Dictionary<string, string> paramsDict = null)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/futures/openOrders", paramsDict);
        }

        // 7. Get Futures Order History
        public Task<string> GetFuturesOrderHistoryAsync(Dictionary<string, string> paramsDict = null)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/futures/orders", paramsDict);
        }

        // 8. Get Positions
        public Task<string> GetFuturesPositionsAsync(Dictionary<string, string> paramsDict = null)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/futures/positions", paramsDict);
        }

        // 9. Close Position
        public Task<string> CloseFuturesPositionAsync(object body)
        {
            return MakeRequestAsync("POST", "/trade/api/v2/futures/close_position", null, body);
        }

        // 10. Get Margin Mode
        public Task<string> GetMarginModeAsync(Dictionary<string, string> paramsDict = null)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/futures/margin_mode", paramsDict);
        }

        // 11. Change Margin Mode
        public Task<string> ChangeMarginModeAsync(object body)
        {
            return MakeRequestAsync("POST", "/trade/api/v2/futures/margin_mode", null, body);
        }

        // 12. Get Funding History
        public Task<string> GetFundingHistoryAsync(Dictionary<string, string> paramsDict = null)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/futures/funding_rate/history", paramsDict);
        }

        // 13. Get Insurance Fund History
        public Task<string> GetInsuranceFundHistoryAsync(Dictionary<string, string> paramsDict = null)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/futures/insurance_fund/history", paramsDict);
        }

        public string FormatJson(string json)
        {
            var parsedJson = JsonConvert.DeserializeObject(json);
            return JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
        }

    }
}
