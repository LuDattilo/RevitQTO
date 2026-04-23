namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Servizio Core per creare <c>QtoAssignment</c> da richieste UI.
    /// Astratto dal DB concreto (riceve <c>IQtoRepository</c>) e dall'API Revit
    /// (riceve <see cref="AssignmentRequest"/> con dati POCO). Testabile in
    /// isolation con repository SQLite di prova.
    /// </summary>
    public interface IAssignmentService
    {
        /// <summary>
        /// Esegue un batch di assegnazione:
        /// <list type="number">
        ///   <item>Per ogni <see cref="AssignmentTarget"/> valido crea un nuovo
        ///         <c>QtoAssignment</c> con Status=Active in transazione.</item>
        ///   <item>Salta target con UniqueId vuoto o Quantity &lt;= 0, registrando
        ///         il motivo nello <see cref="AssignmentOutcome.SkipReasons"/>.</item>
        ///   <item>Aggiorna <c>WorkSession.TotalElements</c> (= count distinti UniqueId
        ///         assegnati), <c>TaggedElements</c> (= count assegnazioni attive) e
        ///         <c>TotalAmount</c> (= sum Quantity * UnitPrice) dopo il batch.</item>
        ///   <item>Determina <see cref="AssignmentOutcome.IsFirstUseOfEp"/> controllando
        ///         se l'EpCode era già presente in <c>GetUsedEpCodes(sessionId)</c> PRIMA
        ///         del batch: se no, è primo uso (trigger UI prompt preferiti).</item>
        /// </list>
        /// </summary>
        AssignmentOutcome AssignEp(AssignmentRequest request);
    }
}
