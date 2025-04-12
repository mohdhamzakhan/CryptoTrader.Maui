using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using System.Data;
using System.Text;

namespace CoinswitchTrader.Services
{
    public class ApiTradingClient
    {
        private string secretKey;
        private string apiKey;
        private string baseUrl;
        private HttpClient httpClient;

        public ApiTradingClient(string secretKey, string apiKey)
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

        public async Task<string> MakeRequestAsync(string method, string endpoint, object payload = null, Dictionary<string, string> paramsDict = null)
        {
            try
            {
                if (method == HttpMethod.Get.Method && paramsDict != null && paramsDict.Count > 0)
                {
                    endpoint += '?' + string.Join("&", paramsDict.Select(kv => $"{kv.Key}={kv.Value}"));
                    endpoint = Uri.UnescapeDataString(endpoint.Replace("+", " "));
                }

                string epochTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                string signatureMsg = SignatureMessage(method, endpoint, epochTime);
                string signature = GetSignatureOfRequest(this.secretKey, signatureMsg);

                if (signature == null)
                {
                    return JsonConvert.SerializeObject(new { message = "Please Enter Valid Keys" });
                }

                string url = $"{this.baseUrl}{endpoint}";

                // Use the CallApiAsync method for the actual API call
                var headers = new Dictionary<string, string>
                {
                    { "X-AUTH-SIGNATURE", signature },
                    { "X-AUTH-APIKEY", this.apiKey },
                    { "X-REQUEST-ID", "prod-Hamza" },
                    { "X-AUTH-EPOCH", epochTime }
                };

                return await CallApiAsync(url, new HttpMethod(method), headers, payload);
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }

        public async Task<string> MakeRequestObjectAsync(string method, string endpoint, object payload = null, Dictionary<string, object> paramsDict = null)
        {
            try
            {
                if (method == HttpMethod.Get.Method && paramsDict != null && paramsDict.Count > 0)
                {
                    endpoint += '?' + string.Join("&", paramsDict.Select(kv => $"{kv.Key}={kv.Value}"));
                    endpoint = Uri.UnescapeDataString(endpoint.Replace("+", " "));
                }

                string epochTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                string signatureMsg = SignatureMessage(method, endpoint, epochTime);
                string signature = GetSignatureOfRequest(this.secretKey, signatureMsg);

                if (signature == null)
                {
                    return JsonConvert.SerializeObject(new { message = "Please Enter Valid Keys" });
                }

                string url = $"{this.baseUrl}{endpoint}";

                // Use the CallApiAsync method for the actual API call
                var headers = new Dictionary<string, string>
                {
                    { "X-AUTH-SIGNATURE", signature },
                    { "X-AUTH-APIKEY", this.apiKey },
                    { "X-REQUEST-ID", "prod-Hamza" },
                    { "X-AUTH-EPOCH", epochTime }
                };

                return await CallApiAsync(url, new HttpMethod(method), headers, payload);
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

        public Task<string> PingAsync()
        {
            return MakeRequestAsync("GET", "/trade/api/v2/ping");
        }

        public Task<string> TDSAsync()
        {
            return MakeRequestAsync("GET", "/trade/api/v2/tds");
        }

        public Task<string> ValidateKeysAsync()
        {
            return MakeRequestAsync("GET", "/trade/api/v2/validate/keys");
        }

        public Task<string> CreateOrderAsync(Dictionary<string, object> payload)
        {
            payload = RemoveTrailingZeros(payload);
            return MakeRequestAsync("POST", "/trade/api/v2/order", payload);
        }

        public Task<string> CancelOrderAsync(Dictionary<string, object> payload)
        {
            return MakeRequestAsync("DELETE", "/trade/api/v2/order", payload);
        }

        public Task<string> GetOpenOrdersAsync(Dictionary<string, object> paramsDict = null)
        {
            return MakeRequestObjectAsync("GET", "/trade/api/v2/orders", null, paramsDict);
        }

        public Task<string> GetClosedOrdersAsync(Dictionary<string, object> paramsDict = null)
        {
            if (paramsDict == null) paramsDict = new Dictionary<string, object>();
            // paramsDict["open"] = "false";
            return MakeRequestObjectAsync("GET", "/trade/api/v2/orders", null, paramsDict);
        }

        public Task<string> GetOrderAsync(Dictionary<string, string> paramsDict)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/order", null, paramsDict);
        }

        public Task<string> GetUserPortfolioAsync()
        {
            return MakeRequestAsync("GET", "/trade/api/v2/user/portfolio");
        }

        public Task<string> GetTradingFee(Dictionary<string, string> paramsDict)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/tradingFee", null, paramsDict);
        }
        public Task<string> Get24hAllPairsDataAsync(Dictionary<string, string> paramsDict)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/24hr/all-pairs/ticker", null, paramsDict);
        }

        public Task<string> Get24hCoinPairDataAsync(Dictionary<string, string> paramsDict)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/24hr/ticker", null, paramsDict);
        }

        public Task<string> GetDepthAsync(Dictionary<string, string> paramsDict)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/depth", null, paramsDict);
        }

        public Task<string> GetTradesAsync(Dictionary<string, string> paramsDict)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/trades", null, paramsDict);
        }

        public Task<string> GetCandlestickDataAsync(Dictionary<string, string> paramsDict)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/candles", null, paramsDict);
        }

        public Task<string> GetExchangePrecisionAsync(Dictionary<string, object> payload)
        {
            return MakeRequestAsync("POST", "/trade/api/v2/exchangePrecision", payload);
        }

        public Task<string> GetCoinsAsync(Dictionary<string, string> paramsDict)
        {
            return MakeRequestAsync("GET", "/trade/api/v2/coins", null, paramsDict);
        }

        public string FormatJson(string json)
        {
            var parsedJson = JsonConvert.DeserializeObject(json);
            return JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
        }
        public class DepthResponse
        {
            public bool Success { get; set; }
            public decimal Bid { get; set; }
            public decimal Ask { get; set; }
            public decimal BidVol { get; set; }
            public decimal AskVol { get; set; }
            public string Exchange { get; set; }
        }

        public async Task<List<DepthResponse>> GetBestBidAsk(List<string> exchanges, string coin)
        {
            var depthResponses = new List<DepthResponse>();
            var invalidDepth = new DepthResponse
            {
                Success = false,
                Bid = 0,
                Ask = 0,
                BidVol = 0,
                AskVol = 0
            };

            foreach (var exchange in exchanges)
            {
                var symbol = (exchange == "C2C1" || exchange == "C2C2") ? $"{coin}/USDT" : $"{coin}/INR";
                try
                {
                    var response = await GetDepth(exchange, symbol);
                    //Hamza Changed
                    if (response == null || response["data"] == null)
                    {
                        ////MessageBox.Show($"Unable to fetch correct data for Coin {coin}, {exchange}");
                        depthResponses.Add(invalidDepth);
                        continue;
                    }
                    var data = response["data"];
                    var bids = data["bids"];
                    var asks = data["asks"];
                    var depthInfo = new DepthResponse
                    {
                        Success = true,
                        Bid = Convert.ToDecimal(bids[0][0].ToString()),
                        Ask = Convert.ToDecimal(asks[0][0].ToString()),
                        BidVol = Convert.ToDecimal(bids[0][1].ToString()),
                        AskVol = Convert.ToDecimal(asks[0][1].ToString()),
                        Exchange = exchange
                    };

                    depthResponses.Add(depthInfo);
                }
                catch (Exception e)
                {
                    //MessageBox.Show($"Unable to get Data for Coin {coin}, {exchange}, exception: {e}");
                    depthResponses.Add(invalidDepth);
                }
            }

            return depthResponses;
        }

        private async Task<dynamic> GetDepth(string exchange, string symbol)
        {
            // Implement API call here
            // var response = await httpClient.GetStringAsync($"{baseUrl}/trade/api/v2/depth?exchange={exchange}&symbol={symbol}");
            //paylo
            //var response = await GetDepthAsync()

            var payload = new Dictionary<string, string>
        {

            { "symbol", symbol },
            { "exchange", exchange }
        };

            var response = await GetDepthAsync(payload);
            return JsonConvert.DeserializeObject<dynamic>(response);
        }

        public DepthResponse GetBuyUsdtInrRate(List<DepthResponse> bidAskRate)
        {
            return bidAskRate.OrderBy(x => x.Ask).First();
        }

        public DepthResponse GetSellUsdtInrRate(List<DepthResponse> bidAskRate)
        {
            return bidAskRate.OrderByDescending(x => x.Bid).First();
        }

        public List<string> GetCoins()
        {
            return new List<string> { "usdt" };
        }

        public Dictionary<string, string> GetExchangePriceInfo(DepthResponse bidsDetails, DepthResponse asksDetails)
        {
            var details = new Dictionary<string, string>
        {
            { "Buy_USDT_Rate", null },
            { "Buy_USDT_Exchange", null },
            { "Sell_USDT_Rate", null },
            { "Sell_USDT_Exchange", null }
        };

            if (!string.IsNullOrEmpty(asksDetails.Exchange))
            {
                details["Buy_USDT_Rate"] = asksDetails.Ask.ToString();
                details["Buy_USDT_Exchange"] = asksDetails.Exchange;
            }

            if (!string.IsNullOrEmpty(bidsDetails.Exchange))
            {
                details["Sell_USDT_Rate"] = bidsDetails.Bid.ToString();
                details["Sell_USDT_Exchange"] = bidsDetails.Exchange;
            }

            details["Coin_Buy_Rate"] = asksDetails.Ask.ToString();
            details["Coin_Buy_Exchange"] = asksDetails.Exchange;
            details["Coin_Sell_Rate"] = bidsDetails.Bid.ToString();
            details["Coin_Sell_Exchange"] = bidsDetails.Exchange;

            return details;
        }

        public async Task FindArbitrage(string startTime, string endTime, decimal arbitragePercentageThreshold)
        {
            File.Delete("d:\\data\\coin.csv");
            File.AppendAllText("d:\\data\\coin.csv", $"Status,bid,ask,bidVolume,askvolume,coin,buy,sell,{Environment.NewLine}");



            var endDateTime = DateTime.Parse(endTime);
            var startDateTime = DateTime.Parse(startTime);

            while (DateTime.Now < endDateTime)
            {
                if (DateTime.Now < startDateTime)
                {
                    //MessageBox.Show("Not Started yet !!!!");
                    await Task.Delay(10000);
                    continue;
                }

                var coins = GetCoins();
                foreach (var coin in coins)
                {
                    var bidAskDetailsAllMarkets = await GetBestBidAsk(new List<string> { "COINSWITCHX", "WAZIRX" }, coin);
                    var bidAskDetailsUsdtMarket = (await GetBestBidAsk(new List<string> { "C2C1", "C2C2" }, coin)).FirstOrDefault();

                    if (bidAskDetailsUsdtMarket != null && bidAskDetailsUsdtMarket.Success)
                    {
                        var usdtRateDetailsInrMarket = await GetBestBidAsk(new List<string> { "COINSWITCHX", "WAZIRX" }, "USDT");
                        var buyUsdtRateDetailsInrMarket = GetBuyUsdtInrRate(usdtRateDetailsInrMarket);
                        var sellUsdtRateDetailsInrMarket = GetSellUsdtInrRate(usdtRateDetailsInrMarket);
                        bidAskDetailsUsdtMarket.Exchange += $" (Buy USDT)";
                        bidAskDetailsUsdtMarket.Ask *= buyUsdtRateDetailsInrMarket.Ask;
                        bidAskDetailsUsdtMarket.Bid *= sellUsdtRateDetailsInrMarket.Bid;

                        bidAskDetailsAllMarkets.Add(bidAskDetailsUsdtMarket);
                    }

                    var sortedDataBidDesc = bidAskDetailsAllMarkets.OrderByDescending(x => x.Bid).ToList();
                    var sortedDataAskAsc = bidAskDetailsAllMarkets.OrderBy(x => x.Ask).ToList();
                    var fetchTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                    if (!sortedDataBidDesc.Any())
                    {
                        ////MessageBox.Show($"Market Does Not Exist For the coin {coin}");
                        continue;
                    }

                    foreach (var bid in sortedDataBidDesc)
                    {
                        foreach (var ask in sortedDataAskAsc)
                        {
                            var bestSellInrMarket = bid.Bid;
                            var bestBuyInrMarket = ask.Ask;
                            var arbitragePercentageInrMarket = ((bestSellInrMarket - bestBuyInrMarket) / bestBuyInrMarket) * 100;

                            if (arbitragePercentageInrMarket >= arbitragePercentageThreshold)
                            {
                                var exchangeInfo = GetExchangePriceInfo(bid, ask);
                                var row = new List<object>
                            {
                                    fetchTime,
                                    coin,
                                    arbitragePercentageInrMarket,
                                    Math.Min(bid.BidVol, ask.AskVol),
                                    exchangeInfo["Buy_USDT_Rate"],
                                    exchangeInfo["Buy_USDT_Exchange"],
                                    exchangeInfo["Coin_Buy_Rate"],
                                    exchangeInfo["Coin_Buy_Exchange"],
                                    exchangeInfo["Coin_Sell_Rate"],
                                    exchangeInfo["Coin_Sell_Exchange"],
                                    exchangeInfo["Sell_USDT_Rate"],
                                    exchangeInfo["Sell_USDT_Exchange"]
                            };

                                Dictionary<string, object> buyPayload;
                                if (exchangeInfo["Buy_USDT_Exchange"].Contains("C2"))
                                {
                                    buyPayload = new Dictionary<string, object>
                                {
                                    { "side", "buy" },
                                    { "symbol", coin+ "/usdt" },
                                    { "type", "limit" },
                                    { "price", ask.Ask },
                                    { "quantity", 200/ask.Ask},
                                    { "exchange", exchangeInfo["Buy_USDT_Exchange"].Split('(')[0] }
                                };
                                }
                                else
                                {
                                    buyPayload = new Dictionary<string, object>
                                {
                                    { "side", "buy" },
                                    { "symbol", coin+ "/inr" },
                                    { "type", "limit" },
                                    { "price", ask.Ask },
                                    { "quantity", 200/ask.Ask},
                                    { "exchange", exchangeInfo["Buy_USDT_Exchange"] }
                                };
                                }




                                var sellPayload = new Dictionary<string, object>();
                                if (exchangeInfo["Sell_USDT_Exchange"].Contains("C2"))
                                {
                                    sellPayload = new Dictionary<string, object>
                                {
                                    { "side", "sell" },
                                    { "symbol", coin+$"/usdt" },
                                    { "type", "limit" },
                                    {  "price", bid.Bid  },
                                    { "quantity", 200/ask.Ask},
                                    { "exchange", exchangeInfo["Sell_USDT_Exchange"].Split('(')[0] }
                                };
                                }
                                else
                                {
                                    sellPayload = new Dictionary<string, object>
                                {
                                     { "side", "sell" },
                                    { "symbol", coin+"/inr" },
                                    { "type", "limit" },
                                    {  "price", bid.Bid  },
                                    { "quantity",200/ask.Ask},
                                    { "exchange", exchangeInfo["Sell_USDT_Exchange"] }
                                };
                                }


                                //File.AppendAllText("d:\\data\\coin.csv", $"{bid.Bid}, {ask.Bid}, {bid.Ask}, {ask.Bid},{bid.BidVol}, {ask.BidVol}, {bid.AskVol}, {ask.BidVol}, {Convert.ToDecimal(exchangeInfo["Buy_USDT_Rate"])}, {Convert.ToDecimal(exchangeInfo["Sell_USDT_Rate"])}, {Environment.NewLine}");

                                var buy = await CreateOrderAsync(buyPayload);
                                var sell = await CreateOrderAsync(sellPayload);

                                //Thread.Sleep(1000);
                                //File.AppendAllText("d:\\data\\coin.csv", $"{ask.Ask},{bid.Bid}, {exchangeInfo["Buy_USDT_Exchange"].Split('(')[0]},{exchangeInfo["Sell_USDT_Exchange"].Split('(')[0]},{Environment.NewLine}");

                                //sheet.Cells[sheet.Dimension.End.Row + 1, 1].LoadFromCollection(row, false);


                                ////MessageBox.Show($"{buy},{sell}");


                                File.AppendAllText("d:\\data\\coin.csv", $"FOUND Arbitrage,{bid.Bid},{ask.Ask},{bid.BidVol},{ask.AskVol},{coin},{buy},{sell},{Environment.NewLine}");
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }
            }


            // //MessageBox.Show("Time Up !!!!!!! Exiting");
        }
        public async Task FindArbitrage(string startTime, string endTime, decimal arbitragePercentageThreshold, bool start, decimal amount, List<string> tradeCoin)
        {
            if (start)
            {
                File.Delete("d:\\data\\coin.csv");
                File.AppendAllText("d:\\data\\coin.csv", $"Status,bid,ask,bidVolume,askvolume,coin,buy,sell,{Environment.NewLine}");

                var endDateTime = DateTime.Parse(endTime);
                var startDateTime = DateTime.Parse(startTime);

                while (DateTime.Now < endDateTime)
                {
                    if (DateTime.Now < startDateTime)
                    {
                        //MessageBox.Show("Not Started yet !!!!");
                        await Task.Delay(10000);
                        continue;
                    }

                    var coins = tradeCoin;
                    foreach (var coin in coins)
                    {
                        var bidAskDetailsSell = await FetchMarketData("sell", coin + "/INR");
                        var bidAskDetailsBuy = await FetchMarketData("buy", coin + "/INR");

                        // Merge orders from sell and buy responses
                        var mergedOrders = MergeOrderData(bidAskDetailsSell, bidAskDetailsBuy);

                        if (mergedOrders.Any())
                        {
                            continue;
                        }

                        var bidAskDetailsAllMarkets = await GetBestBidAsk(new List<string> { "COINSWITCHX" }, coin);
                        var bidAskDetailsUsdtMarket = (await GetBestBidAsk(new List<string> { "C2C1" }, coin)).FirstOrDefault();

                        if (bidAskDetailsUsdtMarket != null && bidAskDetailsUsdtMarket.Success)
                        {
                            var usdtRateDetailsInrMarket = await GetBestBidAsk(new List<string> { "COINSWITCHX" }, "USDT");
                            var buyUsdtRateDetailsInrMarket = GetBuyUsdtInrRate(usdtRateDetailsInrMarket);
                            var sellUsdtRateDetailsInrMarket = GetSellUsdtInrRate(usdtRateDetailsInrMarket);
                            bidAskDetailsUsdtMarket.Exchange += $" (Buy USDT)";
                            bidAskDetailsUsdtMarket.Ask *= buyUsdtRateDetailsInrMarket.Ask;
                            bidAskDetailsUsdtMarket.Bid *= sellUsdtRateDetailsInrMarket.Bid;

                            bidAskDetailsAllMarkets.Add(bidAskDetailsUsdtMarket);
                        }

                        var sortedDataBidDesc = bidAskDetailsAllMarkets.OrderByDescending(x => x.Bid).ToList();
                        var sortedDataAskAsc = bidAskDetailsAllMarkets.OrderBy(x => x.Ask).ToList();
                        var fetchTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                        if (!sortedDataBidDesc.Any())
                        {
                            continue;
                        }

                        foreach (var bid in sortedDataBidDesc)
                        {
                            foreach (var ask in sortedDataAskAsc)
                            {
                                var bestSellInrMarket = bid.Bid;
                                var bestBuyInrMarket = ask.Ask;
                                var arbitragePercentageInrMarket = ((bestSellInrMarket - bestBuyInrMarket) / bestBuyInrMarket) * 100;

                                if (arbitragePercentageInrMarket >= arbitragePercentageThreshold)
                                {
                                    var exchangeInfo = GetExchangePriceInfo(bid, ask);
                                    var row = new List<object>
                            {
                                fetchTime,
                                coin,
                                arbitragePercentageInrMarket,
                                Math.Min(bid.BidVol, ask.AskVol),
                                exchangeInfo["Buy_USDT_Rate"],
                                exchangeInfo["Buy_USDT_Exchange"],
                                exchangeInfo["Coin_Buy_Rate"],
                                exchangeInfo["Coin_Buy_Exchange"],
                                exchangeInfo["Coin_Sell_Rate"],
                                exchangeInfo["Coin_Sell_Exchange"],
                                exchangeInfo["Sell_USDT_Rate"],
                                exchangeInfo["Sell_USDT_Exchange"]
                            };

                                    var buyPayload = GetOrderPayload("buy", coin, ask.Ask, amount, exchangeInfo);
                                    var sellPayload = GetOrderPayload("sell", coin, bid.Bid, amount, exchangeInfo);

                                    var sellOrder = CreateOrderAsync(sellPayload);
                                    var buyOrder = sellOrder.ContinueWith(async (t) =>
                                    {
                                        await t; // Ensure buy order is complete
                                        return await CreateOrderAsync(buyPayload);
                                    }).Unwrap();

                                    var buyResult = await buyOrder;
                                    var sellResult = await sellOrder;

                                    var payload = new Dictionary<string, object>
                            {
                                {"exchange", exchangeInfo["Sell_USDT_Exchange"].Split('(')[0]},
                                {"symbol", coin + "/usdt"}
                            };

                                    var res = await GetExchangePrecisionAsync(payload);
                                    await Task.Delay(1000);

                                    File.AppendAllText("d:\\data\\coin.txt", $"FOUND Arbitrage,{bid.Bid},{ask.Ask},{bid.BidVol},{ask.AskVol},{coin},{buyResult},{sellResult},{Environment.NewLine}");
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }


        public async Task<JObject> FetchMarketData(string side, string coin)
        {
            var parameters = new Dictionary<string, object>
        {
            { "count", "500" },
            {"from_time", ToUnixTimestampMilliseconds(DateTime.Now.AddDays(-10)).ToString()},
            {"to_time", ToUnixTimestampMilliseconds(DateTime.Now).ToString()},
            { "side", side },
            { "symbols", coin.ToUpper() },
            { "exchanges", "coinswitchx" },
            { "type", "limit" },
            {"open", true }
        };

            var jsonResponse = await GetOpenOrdersAsync(parameters);
            return jsonResponse is string ? JsonConvert.DeserializeObject<JObject>(jsonResponse.ToString()) : null;
        }

        public List<JToken> MergeOrderData(JObject data1, JObject data2)
        {
            var mergedOrders = new List<JToken>();

            if (data1 != null && data1["data"]?["orders"] != null)
                mergedOrders.AddRange(data1["data"]["orders"]);

            if (data2 != null && data2["data"]?["orders"] != null)
                mergedOrders.AddRange(data2["data"]["orders"]);

            return mergedOrders;
        }


        private Dictionary<string, object> GetOrderPayload(string side, string coin, decimal price, decimal amount, Dictionary<string, string> exchangeInfo)
        {
            var symbol = exchangeInfo.ContainsKey("Buy_USDT_Exchange") && exchangeInfo["Buy_USDT_Exchange"].Contains("C2")
                ? coin + "/usdt"
                : coin + "/inr";

            return new Dictionary<string, object>
    {
        { "side", side },
        { "symbol", symbol },
        { "type", "limit" },
        { "price", price },
        { "quantity", amount / price },
        { "exchange", exchangeInfo.ContainsKey("Buy_USDT_Exchange") ? exchangeInfo["Buy_USDT_Exchange"].Split('(')[0] : exchangeInfo["Sell_USDT_Exchange"] }
    };
        }




        private readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public long ToUnixTimestampMilliseconds(DateTime dateTime)
        {
            DateTime istDateTime = dateTime.ToUniversalTime();
            return (long)(istDateTime.ToUniversalTime() - UnixEpoch).TotalMilliseconds;
        }

        public long ToUnixTimestampMillisecondsToIST(DateTime dateTime)
        {
            DateTime istDateTime = dateTime.ToUniversalTime().AddHours(5).AddMinutes(30);
            return (long)(istDateTime.ToUniversalTime() - UnixEpoch).TotalMilliseconds;
        }
        public DateTime UnixTimeStampToDateTimeToIST(long unixTimeStamp)
        {
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(unixTimeStamp);
            DateTime dateTime = dateTimeOffset.DateTime.AddHours(5).AddMinutes(30);

            return dateTime;
        }

        public DateTime UnixTimeStampToDateTime(long unixTimeStamp)
        {
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(unixTimeStamp);
            DateTime dateTime = dateTimeOffset.DateTime;

            return dateTime;
        }

    }
}
