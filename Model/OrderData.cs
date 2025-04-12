using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoTrader.Maui.Model
{
    public class OrderData
    {
        public string OrderId { get; set; }
        public string Symbol { get; set; }
        public double Price { get; set; }
        public double AveragePrice { get; set; }
        public double OrigQty { get; set; }
        public double ExecutedQty { get; set; }
        public string Status { get; set; }
        public string Side { get; set; }
    }
}
