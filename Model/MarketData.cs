using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoTrader.Maui.Model
{
    public class MarketData
    {
        public string Symbol { get; set; }
        public decimal Ask { get; set; }
        public decimal Bid { get; set; }
    }
}
