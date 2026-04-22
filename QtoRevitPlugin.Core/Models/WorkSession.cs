using System;

namespace QtoRevitPlugin.Models
{
    public enum SessionStatus
    {
        InProgress,
        Completed,
        Exported
    }

    public class WorkSession
    {
        public int Id { get; set; }
        public string ProjectPath { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string SessionName { get; set; } = string.Empty;
        public SessionStatus Status { get; set; } = SessionStatus.InProgress;

        public int ActivePhaseId { get; set; }
        public string ActivePhaseName { get; set; } = string.Empty;

        public int TotalElements { get; set; }
        public int TaggedElements { get; set; }
        public double TaggedPercent => TotalElements > 0
            ? (double)TaggedElements / TotalElements * 100.0
            : 0.0;

        public double TotalAmount { get; set; }
        public string LastEpCode { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastSavedAt { get; set; }
        public DateTime? ModelSnapshotDate { get; set; }
    }
}
