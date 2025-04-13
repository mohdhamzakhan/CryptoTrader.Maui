using CryptoTrader.Maui.CoinswitchTrader.Services;
using CryptoTrader.Maui.ViewModels;

namespace CryptoTrader.Maui.Pages;

public partial class ChandelierExitAppMainpage : ContentPage
{
	private ChandelierViewModel _viewModel;
    private FutureTradingService _futureTradingService;
    public ChandelierExitAppMainpage()
    {
        InitializeComponent();
        _futureTradingService = new FutureTradingService();
        _viewModel = new ChandelierViewModel(_futureTradingService);
        BindingContext = _viewModel;
    }

}