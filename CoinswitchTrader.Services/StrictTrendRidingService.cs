using CoinswitchTrader.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoTrader.Maui.CoinswitchTrader.Services
{
    public class StrictTrendRidingService
    {
        private readonly SettingsService _settingsService;
        public StrictTrendRidingService(SettingsService settingsService)
        {
            _settingsService = settingsService;
        }
        private decimal _entryPrice;
        private string _currentPosition = "NONE"; // NONE, LONG, SHORT

        public async Task ExecuteStrategyAsync()
        {
            var trendSignal = GetTrendSignal();

            if (_settingsService.TradingMode == "FUTURES")
            {
                await HandleFuturesTradingAsync(trendSignal);
            }
            else // SPOT Trading
            {
                await HandleSpotTradingAsync(trendSignal);
            }

            await CheckTrailingStopAsync();
            await CheckLiquidationRiskAsync();
        }

        private async Task HandleSpotTradingAsync(string trendSignal)
        {
            if (trendSignal == "BUY" && _currentPosition != "LONG")
            {
                await SpotApiService.BuyAsync();
                _currentPosition = "LONG";
                _entryPrice = await MarketDataService.GetCurrentPriceAsync();
            }
            else if (trendSignal == "SELL" && _currentPosition == "LONG")
            {
                await SpotApiService.SellAsync();
                _currentPosition = "NONE";
            }
        }

        private async Task HandleFuturesTradingAsync(string trendSignal)
        {
            if (trendSignal == "BUY" && _currentPosition != "LONG")
            {
                await CloseCurrentPositionAsync();
                await OpenLongFuturesAsync();
            }
            else if (trendSignal == "SELL" && _currentPosition != "SHORT")
            {
                await CloseCurrentPositionAsync();
                await OpenShortFuturesAsync();
            }
        }

        private async Task OpenLongFuturesAsync()
        {
            _entryPrice = await FuturesApiService.BuyAsync(SettingsService.Leverage);
            _currentPosition = "LONG";
            await SetTakeProfitAndStopLossAsync();
        }

        private async Task OpenShortFuturesAsync()
        {
            _entryPrice = await FuturesApiService.SellShortAsync(SettingsService.Leverage);
            _currentPosition = "SHORT";
            await SetTakeProfitAndStopLossAsync();
        }

        private async Task CloseCurrentPositionAsync()
        {
            if (_currentPosition == "LONG")
                await FuturesApiService.SellAsync();
            else if (_currentPosition == "SHORT")
                await FuturesApiService.BuyToCoverAsync();

            _currentPosition = "NONE";
        }

        private async Task SetTakeProfitAndStopLossAsync()
        {
            decimal takeProfitPrice, stopLossPrice;

            if (_currentPosition == "LONG")
            {
                takeProfitPrice = _entryPrice * (1 + SettingsService.ProfitTargetPercent / 100);
                stopLossPrice = _entryPrice * (1 - SettingsService.StopLossPercent / 100);
            }
            else // SHORT
            {
                takeProfitPrice = _entryPrice * (1 - SettingsService.ProfitTargetPercent / 100);
                stopLossPrice = _entryPrice * (1 + SettingsService.StopLossPercent / 100);
            }

            await FuturesApiService.SetTakeProfitAsync(takeProfitPrice);
            await FuturesApiService.SetStopLossAsync(stopLossPrice);
        }

        private async Task CheckTrailingStopAsync()
        {
            if (_currentPosition == "NONE")
                return;

            decimal currentPrice = await MarketDataService.GetCurrentPriceAsync();

            if (_currentPosition == "LONG")
            {
                if (currentPrice > _entryPrice * 1.01m) // 1% gain
                {
                    decimal newStop = currentPrice * 0.995m;
                    await FuturesApiService.AdjustStopLossUpwardAsync(newStop);
                }
            }
            else if (_currentPosition == "SHORT")
            {
                if (currentPrice < _entryPrice * 0.99m) // 1% gain on short
                {
                    decimal newStop = currentPrice * 1.005m;
                    await FuturesApiService.AdjustStopLossDownwardAsync(newStop);
                }
            }
        }

        private async Task CheckLiquidationRiskAsync()
        {
            if (SettingsService.TradingMode != "FUTURES")
                return;

            decimal marginRatio = await FuturesApiService.GetMarginRatioAsync();

            if (marginRatio > 0.5m) // 50% margin used
            {
                await CloseCurrentPositionAsync();
                Console.WriteLine("⚠️ Liquidation Risk! Closing position.");
            }
        }

        private string GetTrendSignal()
        {
            //bool upTrend = IndicatorService.IsUpTrend();
            //bool downTrend = IndicatorService.IsDownTrend();

            //if (upTrend && !downTrend)
            //    return "BUY";
            //else if (downTrend && !upTrend)
            //    return "SELL";
            //else
            //    return "HOLD";
        }
    }
}
