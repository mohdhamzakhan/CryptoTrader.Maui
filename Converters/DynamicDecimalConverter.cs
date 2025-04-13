using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoTrader.Maui.Model
{
    internal class DynamicDecimalConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double doubleValue)
            {
                // Example: dynamic formatting based on value
                if (doubleValue >= 1)
                    return doubleValue.ToString("F2"); // 2 decimals for bigger numbers
                else if (doubleValue >= 0.01)
                    return doubleValue.ToString("F4"); // 4 decimals for medium numbers
                else
                    return doubleValue.ToString("F6"); // 6 decimals for small numbers
            }

            return value?.ToString() ?? "";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
