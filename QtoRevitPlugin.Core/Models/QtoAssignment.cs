using System;

namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Singola assegnazione EP per un elemento Revit (o Room, o voce manuale).
    /// Un elemento può avere più assegnazioni (multi-EP), tutte in questa tabella.
    /// </summary>
    public class QtoAssignment
    {
        public int Id { get; set; }
        public int SessionId { get; set; }

        public int ElementId { get; set; }
        public string UniqueId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string PhaseCreated { get; set; } = string.Empty;
        public string PhaseDemolished { get; set; } = string.Empty;

        public string EpCode { get; set; } = string.Empty;
        public string EpDescription { get; set; } = string.Empty;

        public double Quantity { get; set; }
        public double QuantityGross { get; set; }
        public double QuantityDeducted { get; set; }
        public string Unit { get; set; } = string.Empty;
        public double UnitPrice { get; set; }
        public double Total => Quantity * UnitPrice;

        public string RuleApplied { get; set; } = string.Empty;
        public QtoSource Source { get; set; } = QtoSource.RevitElement;

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedAt { get; set; }

        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? ModifiedBy { get; set; }
        public int Version { get; set; } = 1;
        public AssignmentStatus AuditStatus { get; set; } = AssignmentStatus.Active;

        public bool IsDeleted { get; set; }
        public bool IsExcluded { get; set; }
        public string ExclusionReason { get; set; } = string.Empty;
    }
}
