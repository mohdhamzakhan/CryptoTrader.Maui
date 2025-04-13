using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoTrader.Maui.CoinswitchTrader.Services
{
    public class ChandelierExitSettings
    {
        public int AtrPeriod { get; set; } = 22;
        public float AtrMultiplier { get; set; } = 3.0f;
        public bool UseClosePriceForExtremums { get; set; } = true;

        // Visual parameters
        public bool ShowBuySellLabels { get; set; } = true;

        // Logic options
        public bool AwaitBarConfirmation { get; set; } = true;
        public bool ExitOnSignalReversal { get; set; } = true;

        // Risk management
        public float TrailingStopLossPercent { get; set; } = 5.0f;
    }
}
