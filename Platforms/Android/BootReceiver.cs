using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;

namespace CryptoTrader.Maui.Platforms.Android
{
    [BroadcastReceiver(Enabled = true, Exported = true)]
    [IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced })]
    public class BootReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent == null)
                return;

            string action = intent.Action ?? string.Empty;
            Log.Debug("BootReceiver", $"Received action: {action}");

            if (action == Intent.ActionBootCompleted || action == Intent.ActionMyPackageReplaced)
            {
                try
                {
                    var serviceIntent = new Intent(context, typeof(TradingBackgroundService));

                    // For Android 8.0 (API 26) and higher
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                    {
                        context.StartForegroundService(serviceIntent);
                    }
                    else
                    {
                        context.StartService(serviceIntent);
                    }

                    Log.Debug("BootReceiver", "Trading service started after boot/update");
                }
                catch (Exception ex)
                {
                    Log.Error("BootReceiver", $"Failed to start trading service: {ex.Message}");
                }
            }
        }
    }
}