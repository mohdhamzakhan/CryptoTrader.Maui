using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoTrader.Maui.CoinswitchTrader.Services
{
   public class TradingSignal
    {
        public DateTime Timestamp { get; set; }
        public Enums.SignalType Type { get; set; }
        public decimal Price { get; set; }
        public string Symbol { get; set; }
        public decimal StopLevel { get; set; }
    }
}
