namespace QtoRevitPlugin.Models
{
    public enum SupersedeKind { Modified, Deleted }

    /// <summary>
    /// Operazione di riconciliazione: marca vecchia riga come Superseded/Deleted
    /// e (per Modified) inserisce nuova riga con Version+1. Applicata in batch
    /// dentro IQtoRepository.AcceptDiffBatch in singola transazione SQL.
    /// </summary>
    public class SupersedeOp
    {
        public int OldAssignmentId { get; set; }
        public QtoAssignment NewVersion { get; set; } = null!;
        public ElementSnapshot NewSnapshot { get; set; } = null!;
        public ChangeLogEntry Log { get; set; } = null!;
        public SupersedeKind Kind { get; set; }
    }
}
