using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoinswitchTrader.Services
{
    public static class NetworkHelper
    {
        private static readonly HttpClient httpClient = new HttpClient();

        public static bool IsInternetAvailable()
        {
            try
            {
                // Try to reach a reliable site
                var response = httpClient.GetAsync("https://coinswitch.co").Result;
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public static async Task WaitForInternetAsync()
        {
            while (!await IsInternetAvailableAsync())
            {
                await Task.Delay(5000); // Wait for 5 seconds before retrying
            }
        }

        public static async Task<bool> IsInternetAvailableAsync()
        {
            try
            {
                var response = await httpClient.GetAsync("https://coinswitch.co");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

}
