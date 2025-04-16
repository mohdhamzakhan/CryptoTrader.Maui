using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoTrader.Maui.CoinswitchTrader.Services
{
    public class FuturePosition
    {
        public string Exchange { get; set; }
        public string PositionId { get; set; }
        public string Symbol { get; set; }
        public string PositionSide { get; set; }
        public decimal Leverage { get; set; }
        public decimal PositionSize { get; set; } // in base quantity
        public decimal PositionValue { get; set; } // in USDT
        public decimal PositionMargin { get; set; } // in USDT
        public decimal MaintMargin { get; set; } // minimum margin to avoid liquidation
        public decimal AvgEntryPrice { get; set; }
        public decimal MarkPrice { get; set; }
        public decimal LastPrice { get; set; }
        public decimal UnrealisedPnl { get; set; }
        public decimal LiquidationPrice { get; set; }
        public string Status { get; set; }
        public long CreatedAt { get; set; } // Unix timestamp in milliseconds
        public long UpdatedAt { get; set; } // Unix timestamp in milliseconds

        public bool IsLong { get; set; }            // <-- Add this
        public decimal CurrentStopLoss { get; set; } // <-- Add this
        public decimal PnL { get; set; }             // <-- Add this
        public long EntryTime { get; set; }          // <-- Add this (long for timestamp)
    }

    public class PositionResponse
    {
        [JsonProperty("data")]
        public List<FuturePosition> data { get; set; }
    }
}
