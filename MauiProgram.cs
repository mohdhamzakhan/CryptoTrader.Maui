using CoinswitchTrader.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
#if ANDROID
using CryptoTrader.Maui.Platforms.Android;
#endif


namespace CryptoTrader.Maui
{
    public static class MauiProgram
    {
        public static class ServiceHelper
        {
            public static IServiceProvider Services { get; set; }
        }
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
            builder.Services.AddKeyedSingleton<string>("secretKey", "aa18ca959d1a13d7635af410ce76ce09a67952cb1f566b8ed50f5b6c36363fbb");
            builder.Services.AddKeyedSingleton<string>("apiKey", "9f7e37bc68a33d5addefc7ceb957cd5336aef9b7b3404c00f4a0af1bcc49c58d");

            // Register your services
            builder.Services.AddSingleton<TradingService>();
            builder.Services.AddSingleton<SettingsService>();
            builder.Services.AddSingleton<HistoricalDataService>();
#if ANDROID
            builder.Services.AddTransient<TradingBackgroundService>();
#endif

            var app = builder.Build();
            ServiceHelper.Services = app.Services;
            return app;
        }
    }
}
