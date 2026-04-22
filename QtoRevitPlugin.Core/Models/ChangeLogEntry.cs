using System;

namespace QtoRevitPlugin.Models
{
    public class ChangeLogEntry
    {
        public int ChangeId { get; set; }
        public int SessionId { get; set; }
        public string ElementUniqueId { get; set; } = "";
        public string PriceItemCode { get; set; } = "";
        public string ChangeType { get; set; } = "";
        public string? OldValueJson { get; set; }
        public string? NewValueJson { get; set; }
        public string UserId { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}
