using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoTrader.Maui.CoinswitchTrader.Services
{
    public class Position
    {
        public string Symbol { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal Quantity { get; set; }
        public DateTime EntryTime { get; set; }
        public decimal CurrentStopLoss { get; set; }
        public bool IsLong { get; set; }
        public decimal PnL { get; set; }
    }
}
