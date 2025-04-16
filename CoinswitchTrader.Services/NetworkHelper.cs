using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoinswitchTrader.Services
{
    public static class NetworkHelper
    {
        private static DateTime _lastNetworkCheck = DateTime.MinValue;
        private static bool _lastNetworkStatus = true;
        private static readonly object _lockObject = new object();
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(5);

        public static async Task<bool> IsInternetAvailableAsync()
        {
            // Use cached result if recent enough
            lock (_lockObject)
            {
                if (DateTime.UtcNow - _lastNetworkCheck < _cacheDuration)
                {
                    return _lastNetworkStatus;
                }
            }

            try
            {
                // Test multiple reliable endpoints
                string[] hosts = new string[] { "api.coinswitch.co", "8.8.8.8", "1.1.1.1" };

                foreach (string host in hosts)
                {
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(3);
                        try
                        {
                            await client.GetAsync($"https://{host}");

                            // Update cache and return success
                            lock (_lockObject)
                            {
                                _lastNetworkCheck = DateTime.UtcNow;
                                _lastNetworkStatus = true;
                            }
                            return true;
                        }
                        catch
                        {
                            // Try next host
                            continue;
                        }
                    }
                }

                // If we get here, all hosts failed
                lock (_lockObject)
                {
                    _lastNetworkCheck = DateTime.UtcNow;
                    _lastNetworkStatus = false;
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error checking network: {ex.Message}");

                lock (_lockObject)
                {
                    _lastNetworkCheck = DateTime.UtcNow;
                    _lastNetworkStatus = false;
                }
                return false;
            }
        }

        public static async Task WaitForInternetAsync(int maxWaitTimeSeconds = 300)
        {
            int waitTime = 0;
            int checkIntervalSeconds = 5;

            while (!await IsInternetAvailableAsync())
            {
                await Task.Delay(checkIntervalSeconds * 1000);
                waitTime += checkIntervalSeconds;

                if (waitTime >= maxWaitTimeSeconds)
                {
                    throw new TimeoutException($"No internet connection after waiting {maxWaitTimeSeconds} seconds");
                }
            }
        }
    }
}
