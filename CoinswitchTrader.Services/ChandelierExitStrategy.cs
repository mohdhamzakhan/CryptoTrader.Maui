using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CryptoTrader.Maui.CoinswitchTrader.Services.Enums;

namespace CryptoTrader.Maui.CoinswitchTrader.Services
{
    class ChandelierExitStrategy
    {
        private ChandelierExitSettings _settings;
        private List<FutureCandleData> _historicalData = new List<FutureCandleData>();
        private decimal _longStop;
        private decimal _shortStop;
        private decimal _longStopPrev;
        private decimal _shortStopPrev;
        private int _direction = 0; // Start with no direction until we have enough data
        private bool _isInitialized = false;

        public ChandelierExitStrategy(ChandelierExitSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public void UpdateSettings(ChandelierExitSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            // Recalculate indicators with new settings if we have data
            if (_historicalData.Count >= _settings.AtrPeriod)
            {
                CalculateIndicators();
            }
        }

        public void AddCandle(FutureCandleData candle)
        {
            if (candle == null) throw new ArgumentNullException(nameof(candle));

            _historicalData.Add(candle);

            // Maintain only necessary historical data
            while (_historicalData.Count > _settings.AtrPeriod * 3)
            {
                _historicalData.RemoveAt(0);
            }

            if (_historicalData.Count >= _settings.AtrPeriod)
            {
                CalculateIndicators();

                // Set initial direction if not set yet
                if (_direction == 0)
                {
                    // Determine initial direction based on price relative to stops
                    var lastCandle = _historicalData.Last();
                    _direction = lastCandle.Close > _shortStop ? 1 : lastCandle.Close < _longStop ? -1 : 1;
                    _isInitialized = true;
                }
            }
        }

        public TradingSignal GetSignal()
        {
            // Ensure we have enough data and initialization has completed
            if (!_isInitialized || _historicalData.Count < _settings.AtrPeriod + 1)
                return null;

            var candle = _historicalData.Last();
            var previousCandle = _historicalData[_historicalData.Count - 2];

            // Only generate signals if bar confirmation is enabled and we have a closed candle
            if (_settings.AwaitBarConfirmation && candle.close_time > DateTime.Now)
            {
                return null; // Current candle is still forming
            }

            // Calculate new direction
            int newDirection = candle.Close > _shortStopPrev ? 1 : candle.Close < _longStopPrev ? -1 : _direction;

            // Check for signals
            if (newDirection == 1 && _direction == -1)
            {
                _direction = newDirection;
                return new TradingSignal
                {
                    Timestamp = candle.close_time,
                    Type = SignalType.Buy,
                    Price = candle.Close,
                    Symbol = "", // To be filled by caller
                    StopLevel = _longStop  // Add the stop level
                };
            }
            else if (newDirection == -1 && _direction == 1)
            {
                _direction = newDirection;
                return new TradingSignal
                {
                    Timestamp = candle.close_time,
                    Type = SignalType.Sell,
                    Price = candle.Close,
                    Symbol = "", // To be filled by caller
                    StopLevel = _shortStop  // Add the stop level
                };
            }

            _direction = newDirection;
            return null;
        }

        private void CalculateIndicators()
        {
            // Calculate ATR
            decimal atr = CalculateATR(_settings.AtrPeriod) * (decimal)_settings.AtrMultiplier;
            if (atr == 0) return; // Avoid division by zero issues

            // Calculate long and short stops
            decimal highest = _settings.UseClosePriceForExtremums
                ? HighestClose(_settings.AtrPeriod)
                : HighestHigh(_settings.AtrPeriod);

            decimal lowest = _settings.UseClosePriceForExtremums
                ? LowestClose(_settings.AtrPeriod)
                : LowestLow(_settings.AtrPeriod);

            // Save previous values
            _longStopPrev = _longStop != 0 ? _longStop : highest - atr;
            _shortStopPrev = _shortStop != 0 ? _shortStop : lowest + atr;

            // Calculate current stops
            _longStop = highest - atr;
            _shortStop = lowest + atr;

            // Apply trailing stop logic only if we have previous data
            if (_historicalData.Count > 1)
            {
                var previousCandle = _historicalData[_historicalData.Count - 2];
                if (previousCandle.Close > _longStopPrev)
                {
                    _longStop = Math.Max(_longStop, _longStopPrev);
                }

                if (previousCandle.Close < _shortStopPrev)
                {
                    _shortStop = Math.Min(_shortStop, _shortStopPrev);
                }
            }
        }

        private decimal CalculateATR(int period)
        {
            if (_historicalData.Count < period + 1)
                return 0;

            var trueRanges = new List<decimal>();

            for (int i = 1; i < _historicalData.Count; i++)
            {
                var current = _historicalData[i];
                var previous = _historicalData[i - 1];

                decimal tr1 = current.High - current.Low;
                decimal tr2 = Math.Abs(current.High - previous.Close);
                decimal tr3 = Math.Abs(current.Low - previous.Close);

                decimal tr = Math.Max(Math.Max(tr1, tr2), tr3);
                trueRanges.Add(tr);
            }

            // Calculate simple moving average of true ranges
            var recentTrueRanges = trueRanges.TakeLast(period).ToList();
            return recentTrueRanges.Sum() / recentTrueRanges.Count;
        }
        private decimal HighestHigh(int period)
        {
            return _historicalData.TakeLast(period).Max(c => c.High);
        }

        private decimal LowestLow(int period)
        {
            return _historicalData.TakeLast(period).Min(c => c.Low);
        }

        private decimal HighestClose(int period)
        {
            return _historicalData.TakeLast(period).Max(c => c.Close);
        }

        private decimal LowestClose(int period)
        {
            return _historicalData.TakeLast(period).Min(c => c.Close);
        }

        public decimal GetTrailingStopPrice(decimal entryPrice, bool isLong)
        {
            if (isLong)
            {
                // For long positions: price - (price * stop%)
                return entryPrice * (1 - ((decimal)_settings.TrailingStopLossPercent / 100));
            }
            else
            {
                // For short positions: price + (price * stop%)
                return entryPrice * (1 + ((decimal)_settings.TrailingStopLossPercent / 100));
            }
        }

        public (decimal longStop, decimal shortStop) GetStopLevels()
        {
            return (_longStop, _shortStop);
        }
    }
}
