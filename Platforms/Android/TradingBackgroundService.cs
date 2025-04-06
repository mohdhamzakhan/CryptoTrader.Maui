using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using CoinswitchTrader.Services;
using Android.Content.PM;
using Android.Util;


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
            _tradingPair  = _settingsService.ScalpingSymbols.Split(',')
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
                var provider = IPlatformApplication.Current.Services;

                var tradingService = provider.GetService<TradingService>();
                var settingsService = provider.GetService<SettingsService>();
                var historicalDataService = provider.GetService<HistoricalDataService>();

                if (tradingService == null || settingsService == null || historicalDataService == null)
                {
                    Log.Error("TradingService", "One or more services are not available.");
                    return;
                }

                var tradingPairs = settingsService.ScalpingSymbols.Split(',')
                                    .Select(s => s.Trim() + "/INR").ToList();
                var exchangePairs = settingsService.DefaultExchange.Split(',')
                                      .Select(s => s.Trim()).ToList();

                var strategy1 = new TrendFollowService(tradingService, settingsService, _historicalDataService);
                var strategy2 = new NewTrendFollowService(tradingService, settingsService, _historicalDataService);
                var strategy3 = new ScalpingService(tradingService, settingsService, historicalDataService);

                strategy1.StartTrading(tradingPairs, exchangePairs);
                strategy2.StartTrading(tradingPairs, exchangePairs);
                strategy3.StartScalping(tradingPairs, exchangePairs);
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
