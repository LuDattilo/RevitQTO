namespace QtoRevitPlugin.Formula
{
    /// <summary>
    /// Contratto per risolvere identificatori NCalc in valori numerici.
    /// Implementazioni tipiche:
    ///   - <c>RevitParameterResolver</c> (plugin main, legge parametri Room/Space via Revit API)
    ///   - <c>FakeParameterResolver</c> (unit test, dictionary-based)
    /// Il resolver è stateful (costruito attorno a un contesto — es. un singolo Room) e
    /// viene passato a <see cref="FormulaEngine.Evaluate"/> per ogni valutazione.
    /// </summary>
    public interface IParameterResolver
    {
        /// <summary>
        /// Risolve un identificatore (es. "Area", "Perimeter", "H_Controsoffitto") nel suo valore double.
        /// Ritorna <c>null</c> se l'identificatore non è presente nel contesto — il FormulaEngine lo
        /// tracciarà in <see cref="FormulaResult.UnresolvedIds"/> e sostituirà con 0 senza crashare.
        /// </summary>
        double? TryResolve(string parameterName);
    }
}
