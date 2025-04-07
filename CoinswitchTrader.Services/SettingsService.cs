using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;

namespace CoinswitchTrader.Services
{
    public class SettingsService
    {
        // Default values
        private const decimal DEFAULT_TDS_RATE = 0.01m; // 1% TDS
        private const decimal DEFAULT_TRADING_FEE_RATE = 0.002m; // 0.2% trading fee
        private const bool DEFAULT_APPLY_TDS_ADJUSTMENT = true;
        private const bool DEFAULT_SCALPING_ENABLED = false;
        private const decimal DEFAULT_SCALPING_PROFIT_THRESHOLD = 0.005m; // 0.5%
        private const decimal DEFAULT_SCALPING_MAX_TRADE_SIZE = 1000m; // ₹1000
        private const int DEFAULT_BID = 0;
        private const int DEFAULT_ASK = 1;
        private const bool DEFAULT_LOGGING_ENABLED = false;

        // Keys for settings storage
        private const string TDS_RATE_KEY = "TdsRate";
        private const string TRADING_FEE_RATE_KEY = "TradingFeeRate";
        private const string APPLY_TDS_ADJUSTMENT_KEY = "ApplyTdsAdjustment";
        private const string SECRET_KEY = "9f7e37bc68a33d5addefc7ceb957cd5336aef9b7b3404c00f4a0af1bcc49c58d";
        private const string API_KEY = "aa18ca959d1a13d7635af410ce76ce09a67952cb1f566b8ed50f5b6c36363fbb";
        private const string DEFAULT_EXCHANGE = "DefaultExchange";
        private const string DEFAULT_TRADING_PAIR = "DefaultTradingPair";
        private const string SCALPING_ENABLED = "true";
        private const string SCALPING_PROFIT_THRESHOLD = "ScalpingProfitThreshold";
        private const string SCALPING_MAX_TRADE_SIZE = "ScalpingMaxTradeSize";
        private const string SCALPING_TRADING_PAIR = "BTC,ETH";
        private const string DEFAULT_BID_KEY = "1";
        private const string DEFAULT_ASK_KEY = "1";
        private const string LOGGING_ENABLED = "true";


        public int StochasticKPeriod { get; set; } = 14;
        public int StochasticDPeriod { get; set; } = 3;
        public int StochasticSlowing { get; set; } = 3;
        // In SettingsService class
        public int FastMAPeriod { get; set; } = 20;
        public int SlowMAPeriod { get; set; } = 50;
        public ObservableCollection<TradingStrategy> StrategyOptions { get; } =
        new ObservableCollection<TradingStrategy>(Enum.GetValues(typeof(TradingStrategy)).Cast<TradingStrategy>());

        private TradingStrategy _selectedStrategy;
        public int _SMA_Period { get; set; } = 14;  // Common periods: 10, 14, 20, 50, 200
        public int _EMA_Period { get; set; } = 14;  // Common periods: 10, 14, 20, 50

        public int _Long_EMA_Period { get; set; } = 26;  // Common periods: 10, 14, 20, 50
        public int _RSI_Period { get; set; } = 14;  // Default RSI period: 14
        public int _MACD_ShortPeriod { get; set; } = 12;  // Standard MACD short period
        public int _MACD_LongPeriod { get; set; } = 26;   // Standard MACD long period
        public int _MACD_SignalPeriod { get; set; } = 9;  // Standard MACD signal period
        


        public SettingsService()
        {
            InitializeDefaultSettings();
        }

        private void InitializeDefaultSettings()
        {
            // Only set defaults if not already set
            if (!Preferences.ContainsKey(TDS_RATE_KEY))
                Preferences.Set(TDS_RATE_KEY, DEFAULT_TDS_RATE.ToString());

            if (!Preferences.ContainsKey(TRADING_FEE_RATE_KEY))
                Preferences.Set(TRADING_FEE_RATE_KEY, DEFAULT_TRADING_FEE_RATE.ToString());

            if (!Preferences.ContainsKey(APPLY_TDS_ADJUSTMENT_KEY))
                Preferences.Set(APPLY_TDS_ADJUSTMENT_KEY, DEFAULT_APPLY_TDS_ADJUSTMENT);

            if (!Preferences.ContainsKey(DEFAULT_EXCHANGE))
                Preferences.Set(DEFAULT_EXCHANGE, "COINSWITCHX");

            if (!Preferences.ContainsKey(DEFAULT_TRADING_PAIR))
                Preferences.Set(DEFAULT_TRADING_PAIR, "BTC/INR");

            if (!Preferences.ContainsKey(SCALPING_ENABLED))
                Preferences.Set(SCALPING_ENABLED, false);

            if (!Preferences.ContainsKey(LOGGING_ENABLED))
                Preferences.Set(LOGGING_ENABLED, false);

            if (!Preferences.ContainsKey(SCALPING_PROFIT_THRESHOLD))
                Preferences.Set(SCALPING_PROFIT_THRESHOLD, "0.005"); // 0.5%

            if (!Preferences.ContainsKey(SCALPING_MAX_TRADE_SIZE))
                Preferences.Set(SCALPING_MAX_TRADE_SIZE, "1000"); // ₹1000

            if (!Preferences.ContainsKey(SCALPING_TRADING_PAIR))
                Preferences.Set(SCALPING_TRADING_PAIR, "BTC,ETH");

            if (!Preferences.ContainsKey(DEFAULT_BID_KEY.ToString()))
                Preferences.Set(DEFAULT_BID_KEY.ToString(), DEFAULT_BID.ToString());

            if (!Preferences.ContainsKey(DEFAULT_ASK_KEY))
                Preferences.Set(DEFAULT_ASK_KEY.ToString(), DEFAULT_ASK.ToString());

            if (!Preferences.ContainsKey(SCALPING_TRADING_PAIR))
                Preferences.Set(SCALPING_TRADING_PAIR, "BTC,ETH");

            if (!Preferences.ContainsKey(DEFAULT_BID_KEY))
                Preferences.Set(DEFAULT_BID_KEY, "1");

            if (!Preferences.ContainsKey(DEFAULT_ASK_KEY))
                Preferences.Set(DEFAULT_ASK_KEY, "1");
        }

        public int SMA_Period
        {
            get { return _SMA_Period; }
            set { _SMA_Period = value; }
        }

        public int EMA_Period
        {
            get { return _EMA_Period; }
            set { _EMA_Period = value; }
        }

        public int Long_EMA_Period
        {
            get { return _Long_EMA_Period; }
            set { _Long_EMA_Period = value; }
        }

        public int RSI_Period
        {
            get { return _RSI_Period; }
            set { _RSI_Period = value; }
        }

        public int MACD_ShortPeriod
        {
            get { return _MACD_ShortPeriod; }
            set { _MACD_ShortPeriod = value; }
        }

        public int MACD_LongPeriod
        {
            get { return _MACD_LongPeriod; }
            set { _MACD_LongPeriod = value; }
        }

        public int MACD_SignalPeriod
        {
            get { return _MACD_SignalPeriod; }
            set { _MACD_SignalPeriod = value; }
        }
        public int BidPosition
        {
            get
            {
                string value = Preferences.Get(DEFAULT_BID_KEY, DEFAULT_BID.ToString());
                return Int32.TryParse(value, out int result) ? result : DEFAULT_BID;
            }
            set => Preferences.Set(TDS_RATE_KEY, value.ToString());
        }

        public int AskPosition
        {
            get
            {
                string value = Preferences.Get(DEFAULT_ASK_KEY, DEFAULT_ASK.ToString());
                return Int32.TryParse(value, out int result) ? result : DEFAULT_ASK;
            }
            set => Preferences.Set(TDS_RATE_KEY, value.ToString());
        }

        // TDS Rate property (percentage as decimal, e.g., 0.01 for 1%)
        public decimal TdsRate
        {
            get
            {
                string value = Preferences.Get(TDS_RATE_KEY, DEFAULT_TDS_RATE.ToString());
                return decimal.TryParse(value, out decimal result) ? result : DEFAULT_TDS_RATE;
            }
            set => Preferences.Set(TDS_RATE_KEY, value.ToString());
        }

        // Trading Fee Rate property (percentage as decimal)
        public decimal TradingFeeRate
        {
            get
            {
                string value = Preferences.Get(TRADING_FEE_RATE_KEY, DEFAULT_TRADING_FEE_RATE.ToString());
                return decimal.TryParse(value, out decimal result) ? result : DEFAULT_TRADING_FEE_RATE;
            }
            set => Preferences.Set(TRADING_FEE_RATE_KEY, value.ToString());
        }

        // Whether to apply TDS adjustment to order calculations
        public bool ApplyTdsAdjustment
        {
            get => Preferences.Get(APPLY_TDS_ADJUSTMENT_KEY, DEFAULT_APPLY_TDS_ADJUSTMENT);
            set => Preferences.Set(APPLY_TDS_ADJUSTMENT_KEY, value);
        }

        // API Credentials
        public string ApiKey
        {
            get => Preferences.Get(API_KEY, string.Empty);
            set => Preferences.Set(API_KEY, value);
        }

        public string SecretKey
        {
            get => Preferences.Get(SECRET_KEY, string.Empty);
            set => Preferences.Set(SECRET_KEY, value);
        }

        // Default Exchange
        public string DefaultExchange
        {
            get => Preferences.Get(DEFAULT_EXCHANGE, "COINSWITCHX");
            set => Preferences.Set(DEFAULT_EXCHANGE, value);
        }

        // Default Trading Pair
        public string DefaultTradingPair
        {
            get => Preferences.Get(DEFAULT_TRADING_PAIR, "BTC/INR");
            set => Preferences.Set(DEFAULT_TRADING_PAIR, value);
        }

        // Scalping Settings
        public bool ScalpingEnabled
        {
            get => Preferences.Get(SCALPING_ENABLED, false);
            set => Preferences.Set(SCALPING_ENABLED, value);
        }

        public bool LoggingEnabled
        {
            get => Preferences.Get(LOGGING_ENABLED, false);
            set => Preferences.Set(LOGGING_ENABLED, value);
        }


        public decimal ScalpingProfitThreshold
        {
            get
            {
                string value = Preferences.Get(SCALPING_PROFIT_THRESHOLD, "0.005");
                return decimal.TryParse(value, out decimal result) ? result : 0.005m;
            }
            set => Preferences.Set(SCALPING_PROFIT_THRESHOLD, value.ToString());
        }

        public decimal ScalpingMaxTradeSize
        {
            get
            {
                string value = Preferences.Get(SCALPING_MAX_TRADE_SIZE, "1000");
                return decimal.TryParse(value, out decimal result) ? result : 1000m;
            }
            set => Preferences.Set(SCALPING_MAX_TRADE_SIZE, value.ToString());
        }

        public string ScalpingSymbols
        {
            get
            {
                string value = Preferences.Get(SCALPING_TRADING_PAIR, "BTC,ETH");
                return value;
            }
            set => Preferences.Set(SCALPING_TRADING_PAIR, value.ToString());
        }

        public TradingStrategy ActiveStrategy
        {
            get => _selectedStrategy;
            set => _selectedStrategy = value;
        }

        public TradingStrategy SelectedStrategy { get; internal set; }

        // Clear all settings (for logout)
        public void ClearSettings()
        {
            Preferences.Clear();
            InitializeDefaultSettings();
        }
    }
}