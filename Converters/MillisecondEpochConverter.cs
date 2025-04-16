using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoTrader.Maui.Converters
{
    public class MillisecondEpochConverter: JsonConverter<DateTime>
    {
        public override DateTime ReadJson(JsonReader reader, Type objectType, DateTime existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.Value == null)
                return default;

            var timestamp = Convert.ToInt64(reader.Value);
            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
        }
        public override void WriteJson(JsonWriter writer, DateTime value, JsonSerializer serializer)
        {
            var timestamp = new DateTimeOffset(value).ToUnixTimeMilliseconds();
            writer.WriteValue(timestamp);
        }
    }
}
