using CoinswitchTrader.Services;
using Microsoft.Extensions.Logging;

namespace CryptoTrader.Maui
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif
            // Register your services
            builder.Services.AddSingleton<TradingService>();
            builder.Services.AddSingleton<SettingsService>();
            return builder.Build();
        }
    }
}
