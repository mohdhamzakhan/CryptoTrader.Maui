using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using CoinswitchTrader.Services;
using Android.Content.PM;
using Android.Util;
using static CryptoTrader.Maui.MauiProgram;


namespace CryptoTrader.Maui.Platforms.Android
{
    [Service(Enabled = true, ForegroundServiceType = ForegroundService.TypeDataSync)]
    public class TradingBackgroundService : Service
    {
        private readonly TradingService _tradingService;
        private readonly SettingsService _settingsService;
        private readonly List<string> _tradingPair;
        private readonly List<string> _exchangePair;
        private readonly HistoricalDataService _historicalDataService;

        public TradingBackgroundService(TradingService tradingService, SettingsService settingsService, HistoricalDataService historicalDataService)
        {
            _tradingService = tradingService;
            _settingsService = settingsService;
            _historicalDataService = historicalDataService;
            _tradingPair = _settingsService.ScalpingSymbols.Split(',')
                                     .Select(s => s.Trim() + "/INR")
                                     .ToList();
            _exchangePair = _settingsService.DefaultExchange.Split(",")
                .Select(s => s.Trim())
                .ToList();

        }
        public TradingBackgroundService()
        {

        }
        public const int ServiceRunningNotificationId = 10000;

        public override IBinder? OnBind(Intent? intent) => null;
        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            ShowNotification();

            Task.Run(() =>
            {
                try
                {
                    Log.Debug("TradingService", "Starting background service");
                    var provider = ServiceHelper.Services;
                    Log.Debug("TradingService", "Got service provider");

                    var tradingService = provider.GetService<TradingService>();
                    Log.Debug("TradingService", $"TradingService resolved: {tradingService != null}");

                    var settingsService = provider.GetService<SettingsService>();
                    Log.Debug("TradingService", $"SettingsService resolved: {settingsService != null}");

                    var historicalDataService = provider.GetService<HistoricalDataService>();
                    Log.Debug("TradingService", $"HistoricalDataService resolved: {historicalDataService != null}");

                    if (tradingService == null || settingsService == null || historicalDataService == null)
                    {
                        Log.Error("TradingService", "One or more services are not available.");
                        return;
                    }

                    // Log settings values
                    Log.Debug("TradingService", $"ScalpingSymbols: {settingsService.ScalpingSymbols}");
                    Log.Debug("TradingService", $"DefaultExchange: {settingsService.DefaultExchange}");

                    var tradingPairs = settingsService.ScalpingSymbols.Split(',')
                                        .Select(s => s.Trim() + "/INR").ToList();
                    Log.Debug("TradingService", $"Trading pairs: {string.Join(", ", tradingPairs)}");

                    var exchangePairs = settingsService.DefaultExchange.Split(',')
                                          .Select(s => s.Trim()).ToList();
                    Log.Debug("TradingService", $"Exchange pairs: {string.Join(", ", exchangePairs)}");

                    try
                    {
                        Log.Debug("TradingService", "Creating strategy1");
                        var strategy1 = new TrendFollowService(tradingService, settingsService, historicalDataService);
                        Log.Debug("TradingService", "Strategy1 created");

                        Log.Debug("TradingService", "Starting strategy1");
                        strategy1.StartTrading(tradingPairs, exchangePairs);
                        Log.Debug("TradingService", "Strategy1 started successfully");

                        Log.Debug("TradingService", "Creating strategy2");
                        var strategy2 = new NewTrendFollowService(tradingService, settingsService, historicalDataService);
                        Log.Debug("TradingService", "Strategy2 created");

                        Log.Debug("TradingService", "Starting strategy2");
                        strategy2.StartTrading(tradingPairs, exchangePairs);
                        Log.Debug("TradingService", "Strategy2 started successfully");

                        Log.Debug("TradingService", "Creating strategy3");
                        var strategy3 = new ScalpingService(tradingService, settingsService, historicalDataService);
                        Log.Debug("TradingService", "Strategy3 created");

                        Log.Debug("TradingService", "Starting strategy3");
                        strategy3.StartScalping(tradingPairs, exchangePairs);
                        Log.Debug("TradingService", "Strategy3 started successfully");
                    }
                    catch (Exception ex)
                    {
                        Log.Error("TradingService", $"Error starting strategies: {ex.Message}");
                        Log.Error("TradingService", $"Stack trace: {ex.StackTrace}");
                        if (ex.InnerException != null)
                        {
                            Log.Error("TradingService", $"Inner exception: {ex.InnerException.Message}");
                            Log.Error("TradingService", $"Inner stack trace: {ex.InnerException.StackTrace}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("TradingService", $"Error in background service: {ex.Message}");
                    Log.Error("TradingService", $"Stack trace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        Log.Error("TradingService", $"Inner exception: {ex.InnerException.Message}");
                        Log.Error("TradingService", $"Inner stack trace: {ex.InnerException.StackTrace}");
                    }
                }
            });

            return StartCommandResult.Sticky;
        }

        private void ShowNotification()
        {
            string channelId = "trading_service_channel";
            string channelName = "Trading Background Service";

            var notificationManager = (NotificationManager)GetSystemService(NotificationService)!;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(channelId, channelName, NotificationImportance.Default);
                notificationManager.CreateNotificationChannel(channel);
            }

            var notification = new NotificationCompat.Builder(this, channelId)
                .SetContentTitle("Crypto Trading Running")
                .SetContentText("The trading service is executing in the background.")
                .SetSmallIcon(Microsoft.Maui.Resource.Drawable.ic_m3_chip_close) // Make sure this exists
                .Build();

            StartForeground(ServiceRunningNotificationId, notification);
        }
    }
}
