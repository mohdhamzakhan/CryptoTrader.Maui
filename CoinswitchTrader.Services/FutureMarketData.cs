using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace CryptoTrader.Maui.CoinswitchTrader.Services
{
    class FutureMarketData : INotifyPropertyChanged
    {
        private ObservableCollection<FutureCandleData> _candles = new ObservableCollection<FutureCandleData>();
        public ObservableCollection<FutureCandleData> Candles
        {
            get => _candles;
            set
            {
                _candles = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<TradingSignal> _signals = new ObservableCollection<TradingSignal>();
        public ObservableCollection<TradingSignal> Signals
        {
            get => _signals;
            set
            {
                _signals = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<Position> _positions = new ObservableCollection<Position>();
        public ObservableCollection<Position> Positions
        {
            get => _positions;
            set
            {
                _positions = value;
                OnPropertyChanged();
            }
        }

        private string _selectedSymbol = "BTCUSDT";
        public string SelectedSymbol
        {
            get => _selectedSymbol;
            set
            {
                _selectedSymbol = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
