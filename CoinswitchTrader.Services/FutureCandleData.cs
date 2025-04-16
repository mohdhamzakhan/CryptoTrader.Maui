using CryptoTrader.Maui.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoTrader.Maui.CoinswitchTrader.Services
{
    public class FutureCandleData
    {
        [JsonConverter(typeof(MillisecondEpochConverter))]
        [JsonProperty("close_time")]
        public DateTime close_time { get; set; }
        [JsonProperty("o")]
        public decimal Open { get; set; }
        [JsonProperty("h")]
        public decimal High { get; set; }
        [JsonProperty("l")]
        public decimal Low { get; set; }
        [JsonProperty("c")]
        public decimal Close { get; set; }
        [JsonProperty("symbol")]
        public string symbol { get; set; }
        [JsonProperty("volume")]
        public decimal Volume { get; set; }
        [JsonConverter(typeof(MillisecondEpochConverter))]
        [JsonProperty("start_time")]
        public DateTime start_time { get; set; }
        [JsonProperty("interval")]
        public string interval { get; set; }
    }

    public class CandleResponse
    {
        [JsonProperty("data")]
        public List<FutureCandleData> data { get; set; }
    }
}
