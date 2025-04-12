using Microsoft.Maui.Graphics;

namespace CryptoTrader.Maui.Models
{
    public class OrderModel
    {
        public string order_id { get; set; }
        public string symbol { get; set; }
        public decimal price { get; set; }
        public decimal average_price { get; set; }
        public decimal orig_qty { get; set; }
        public decimal executed_qty { get; set; }
        public string status { get; set; }
        public string side { get; set; }
        public string exchange { get; set; }
        public string order_source { get; set; }
        public long created_time { get; set; }
        public long updated_time { get; set; }

        public string CreatedTimeFormatted => DateTimeOffset.FromUnixTimeMilliseconds(created_time).LocalDateTime.ToString("dd-MM-yyyy HH:mm:ss");
        public string UpdatedTimeFormatted => DateTimeOffset.FromUnixTimeMilliseconds(updated_time).LocalDateTime.ToString("dd-MM-yyyy HH:mm:ss");

        public Color SideColor => side?.ToUpper() == "BUY" ? Colors.Green : Colors.Red;
        public string SideIcon => side?.ToUpper() == "BUY" ? "✅" : "❌";
    }
}
