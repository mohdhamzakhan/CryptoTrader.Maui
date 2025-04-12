using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using AndroidX.Core.App;
using CoinswitchTrader.Services;

namespace CryptoTrader.Maui.Platforms.Android
{
    [Service(
    Exported = true,
    ForegroundServiceType = ForegroundService.TypeDataSync
)]
    public class TradingBackgroundService : Service
    {
        public const int ServiceRunningNotificationId = 10000;
        private bool _isRunning = false;
        private TradingService? _tradingService;
        private SettingsService? _settingsService;
        private HistoricalDataService? _historicalDataService;

        public override void OnCreate()
        {
            base.OnCreate();
            Log.Debug("TradingService", "OnCreate called");

            // Show notification IMMEDIATELY in OnCreate to prevent the ANR
            ShowNotification();
        }

        private void DebugActiveNotifications()
        {
            try
            {
                var notificationManager = (NotificationManager)GetSystemService(NotificationService)!;

                if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                {
                    // Call the method to get active notifications
                    var activeNotifications = notificationManager.GetActiveNotifications();
                    Log.Debug("TradingService", $"Total active notifications: {activeNotifications.Length}");

                    foreach (var notification in activeNotifications)
                    {
                        Log.Debug("TradingService", $"Active notification ID: {notification.Id}, Package: {notification.PackageName}, Tag: {notification.Tag}");
                    }
                }
                else
                {
                    Log.Debug("TradingService", "Cannot check active notifications on this Android version");
                }
            }
            catch (Exception ex)
            {
                Log.Error("TradingService", $"Error checking active notifications: {ex.Message}");
            }
        }
        public override IBinder? OnBind(Intent? intent) => null;

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            Log.Debug("TradingService", "OnStartCommand called");

            bool notificationPermissionGranted = true;

            ShowNotification();

            // Debug notification status
            DebugActiveNotifications();
            if (intent?.Action == "STOP_SERVICE")
            {
                Log.Debug("TradingService", "Stop service request received");
                StopForeground(StopForegroundFlags.Remove);
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            // Show notification immediately when service starts to avoid ANR
            ShowNotification();

            if (!_isRunning)
            {
                _isRunning = true;

                Task.Run(() =>
                {
                    try
                    {
                        // Your existing trading service code...

                        // You could regularly update the notification with trading status
                        UpdateNotificationWithStatus("Trading active - monitoring markets");
                    }
                    catch (Exception ex)
                    {
                        Log.Error("TradingService", $"Error starting strategies: {ex}");
                        UpdateNotificationWithStatus("Error: " + ex.Message);
                        _isRunning = false;
                    }
                });
            }

            return StartCommandResult.Sticky;
        }

        private void UpdateNotificationWithStatus(string status)
        {
            try
            {
                string channelId = "trading_service_channel";

                // Create an intent to open app
                var pendingIntent = PendingIntent.GetActivity(
                    this,
                    0,
                    new Intent(this, typeof(MainActivity)),
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

                // Create stop action
                var stopIntent = new Intent(this, typeof(TradingBackgroundService)).SetAction("STOP_SERVICE");
                var stopPendingIntent = PendingIntent.GetService(
                    this,
                    0,
                    stopIntent,
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

                // Get trading pairs
                string tradingPairsText = "No active pairs";
                if (_settingsService != null)
                {
                    var pairs = _settingsService.ScalpingSymbols.Split(',')
                        .Select(s => s.Trim())
                        .Take(3)
                        .ToList();

                    if (pairs.Any())
                    {
                        tradingPairsText = string.Join(", ", pairs);
                        if (_settingsService.ScalpingSymbols.Split(',').Length > 3)
                            tradingPairsText += " and more";
                    }
                }

                // Update the notification
                var notificationBuilder = new NotificationCompat.Builder(this, channelId)
                    .SetContentTitle("Crypto Trading Active")
                    .SetContentText(status)
                    .SetStyle(new NotificationCompat.BigTextStyle()
                        .BigText($"{status}\nMonitoring: {tradingPairsText}"))
                    .SetSmallIcon(Resource.Drawable.abc_ic_search_api_material)
                    .SetOngoing(true)
                    .SetContentIntent(pendingIntent)
                    .AddAction(Resource.Drawable.abc_ic_clear_material, "Stop Trading", stopPendingIntent)
                    .SetPriority(NotificationCompat.PriorityDefault);

                var notificationManager = (NotificationManager)GetSystemService(NotificationService)!;
                notificationManager.Notify(ServiceRunningNotificationId, notificationBuilder.Build());
            }
            catch (Exception ex)
            {
                Log.Error("TradingService", $"Failed to update notification: {ex.Message}");
            }
        }

        private void InitializeServices()
        {
            try
            {
                Log.Debug("TradingService", "Resolving services...");
                var provider = MauiApplication.Current.Services;

                _tradingService = provider.GetService<TradingService>();
                _settingsService = provider.GetService<SettingsService>();
                _historicalDataService = provider.GetService<HistoricalDataService>();

                if (_tradingService == null || _settingsService == null || _historicalDataService == null)
                {
                    Log.Error("TradingService", "One or more required services are missing.");
                    StopSelf();
                    return;
                }

                StartTradingStrategies();
            }
            catch (Exception ex)
            {
                Log.Error("TradingService", $"Error resolving services: {ex}");
                StopSelf();
            }
        }

        private void StartTradingStrategies()
        {
            try
            {
                _isRunning = true;

                if (_settingsService == null)
                {
                    Log.Error("TradingService", "Settings service is null.");
                    StopSelf();
                    return;
                }

                var tradingPairs = _settingsService.ScalpingSymbols.Split(',')
                                    .Select(s => s.Trim() + "/INR")
                                    .ToList();

                var exchangePairs = _settingsService.DefaultExchange.Split(',')
                                      .Select(s => s.Trim())
                                      .ToList();

                Log.Debug("TradingService", $"Trading pairs: {string.Join(", ", tradingPairs)}");
                Log.Debug("TradingService", $"Exchange pairs: {string.Join(", ", exchangePairs)}");

                // Start your trading strategies
                var strategy1 = new TrendFollowService(_tradingService!, _settingsService, _historicalDataService!);
                strategy1.StartTrading(tradingPairs, exchangePairs);

                var strategy2 = new NewTrendFollowService(_tradingService!, _settingsService, _historicalDataService!);
                strategy2.StartTrading(tradingPairs, exchangePairs);

                var strategy3 = new ScalpingService(_tradingService!, _settingsService, _historicalDataService!);
                strategy3.StartScalping(tradingPairs, exchangePairs);

                Log.Debug("TradingService", "All strategies started successfully.");

                // Update notification with running status
                UpdateNotification("Trading strategies active");
            }
            catch (Exception ex)
            {
                Log.Error("TradingService", $"Error starting strategies: {ex}");
                _isRunning = false;
                StopSelf();
            }
        }

        private void ShowNotification()
        {
            try
            {
                string channelId = "trading_service_channel";
                string channelName = "Trading Background Service";

                var notificationManager = (NotificationManager)GetSystemService(NotificationService)!;

                // Create channel with HIGHER importance
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    // Create the NotificationChannel with HIGH importance
                    var channel = new NotificationChannel(
                        channelId,
                        channelName,
                        NotificationImportance.High)  // Changed from Default to High
                    {
                        Description = "Shows the status of crypto trading service",
                        LockscreenVisibility = NotificationVisibility.Public,
                    };

                    notificationManager.CreateNotificationChannel(channel);
                    Log.Debug("TradingService", "Notification channel created with HIGH importance");
                }

                // Create a PendingIntent to launch your app
                var pendingIntent = PendingIntent.GetActivity(
                    this,
                    0,
                    new Intent(this, typeof(MainActivity)),
                    PendingIntentFlags.Immutable);

                // Build a better notification
                var notificationBuilder = new NotificationCompat.Builder(this, channelId)
                    .SetContentTitle("Crypto Trading Running")
                    .SetContentText("Trading service is active and monitoring markets")
                    .SetSmallIcon(Resource.Drawable.abc_ic_menu_share_mtrl_alpha) // Make sure this icon exists
                    .SetColor(unchecked((int)0xFF2196F3)) // Material Blue color
                    .SetColorized(true)
                    .SetOngoing(true)
                    .SetContentIntent(pendingIntent)
                    .SetPriority(NotificationCompat.PriorityHigh) // Set high priority
                    .SetVisibility(NotificationCompat.VisibilityPublic)
                    .SetCategory(NotificationCompat.CategoryService);

                var notification = notificationBuilder.Build();

                // Start foreground with the notification
                StartForeground(ServiceRunningNotificationId, notification);
                Log.Debug("TradingService", "Notification shown and foreground started with HIGH priority");
            }
            catch (Exception ex)
            {
                Log.Error("TradingService", $"Error showing notification: {ex}");
            }
        }

        private void UpdateNotification(string status)
        {
            try
            {
                string channelId = "trading_service_channel";

                // Create an intent to open the app when notification is tapped
                var pendingIntent = PendingIntent.GetActivity(
                    this,
                    0,
                    new Intent(this, typeof(MainActivity)),
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

                // Get trading pairs for display
                string tradingPairsText = "No active pairs";
                if (_settingsService != null)
                {
                    var pairs = _settingsService.ScalpingSymbols.Split(',')
                        .Select(s => s.Trim())
                        .Take(3) // Limit to first 3 for display
                        .ToList();

                    if (pairs.Any())
                    {
                        tradingPairsText = string.Join(", ", pairs);
                        if (_settingsService.ScalpingSymbols.Split(',').Length > 3)
                            tradingPairsText += " and more";
                    }
                }

                var notification = new NotificationCompat.Builder(this, channelId)
                    .SetContentTitle("Crypto Trading Active")
                    .SetContentText(status)
                    .SetStyle(new NotificationCompat.BigTextStyle()
                        .BigText($"{status}\nMonitoring: {tradingPairsText}"))
                    .SetSmallIcon(Resource.Drawable.abc_ic_search_api_material)
                    .SetOngoing(true)
                    .SetContentIntent(pendingIntent)
                    .Build();

                var notificationManager = (NotificationManager)GetSystemService(NotificationService)!;
                notificationManager.Notify(ServiceRunningNotificationId, notification);
                Log.Debug("TradingService", "Notification updated");
            }
            catch (Exception ex)
            {
                Log.Error("TradingService", $"Error updating notification: {ex}");
            }
        }

        public override void OnDestroy()
        {
            _isRunning = false;
            Log.Debug("TradingService", "Service destroyed");
            base.OnDestroy();
        }
    }
}