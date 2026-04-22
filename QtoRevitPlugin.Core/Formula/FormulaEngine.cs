using System;
using System.Collections.Generic;
using System.Globalization;
using NCalc2;

namespace QtoRevitPlugin.Formula
{
    /// <summary>
    /// Wrapper NCalc per valutare formule testuali contenenti identificatori risolti via
    /// <see cref="IParameterResolver"/>. Usato da <c>RoomExtractor</c> (Sorgente B §I12) per
    /// calcolare quantità derivate da parametri Room/Space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Comportamento: cultura invariant (decimal '.' interno indipendentemente dalla locale Windows),
    /// identificatori case-insensitive (NCalc <c>EvaluateOptions.IgnoreCase</c>), eccezioni NCalc
    /// catturate e ritornate come <see cref="FormulaResult"/> con <c>IsValid = false</c>.
    /// Niente throw fuori dal metodo: l'estrattore deve poter iterare centinaia di Room senza
    /// stoppare al primo errore.
    /// </para>
    /// <para>
    /// Identificatori non risolti dal resolver (ritornano null) vengono tracciati in
    /// <see cref="FormulaResult.UnresolvedIds"/> e sostituiti con <c>0.0</c>: la formula continua
    /// a valutare ma il risultato è ovviamente degradato — chi legge il risultato deve controllare
    /// <c>UnresolvedIds</c> prima di fidarsi del Value.
    /// </para>
    /// </remarks>
    public class FormulaEngine
    {
        /// <summary>
        /// Valuta <paramref name="formula"/> risolvendo gli identificatori tramite <paramref name="resolver"/>.
        /// Ritorna sempre un <see cref="FormulaResult"/> (non throw-on-error).
        /// </summary>
        public FormulaResult Evaluate(string formula, IParameterResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));

            var result = new FormulaResult();

            if (string.IsNullOrWhiteSpace(formula))
            {
                result.IsValid = false;
                result.Error = "Formula vuota.";
                return result;
            }

            Expression expr;
            try
            {
                expr = new Expression(formula, EvaluateOptions.IgnoreCase);
            }
            catch (Exception ex)
            {
                // Il ctor può throw per espressioni sintatticamente malformate in alcune versioni NCalc.
                result.IsValid = false;
                result.Error = ex.Message;
                return result;
            }

            if (expr.HasErrors())
            {
                result.IsValid = false;
                result.Error = expr.Error ?? "Errore di sintassi sconosciuto.";
                return result;
            }

            expr.EvaluateParameter += (name, args) =>
            {
                var resolved = resolver.TryResolve(name);
                if (resolved.HasValue)
                {
                    args.Result = resolved.Value;
                }
                else
                {
                    // Traccia l'identificatore non risolto ma continua: impone 0 così NCalc non crasha.
                    if (!result.UnresolvedIds.Contains(name))
                        result.UnresolvedIds.Add(name);
                    args.Result = 0.0;
                }
            };

            try
            {
                var raw = expr.Evaluate();
                result.Value = ToDouble(raw);
                result.IsValid = true;
                return result;
            }
            catch (EvaluationException ex)
            {
                result.IsValid = false;
                result.Error = ex.Message;
                return result;
            }
            catch (DivideByZeroException ex)
            {
                // NCalc in alcuni casi propaga DivideByZeroException invece di produrre Infinity.
                result.IsValid = false;
                result.Error = ex.Message;
                return result;
            }
            catch (Exception ex)
            {
                // Altre eccezioni (overflow, format, ecc.) — trattate come formula invalida.
                result.IsValid = false;
                result.Error = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Controlla solo la sintassi della formula (nessun resolver richiesto).
        /// Utile per validazione in tempo reale nell'editor.
        /// </summary>
        public bool Validate(string formula, out string error)
        {
            if (string.IsNullOrWhiteSpace(formula))
            {
                error = "Formula vuota.";
                return false;
            }

            try
            {
                var expr = new Expression(formula, EvaluateOptions.IgnoreCase);
                if (expr.HasErrors())
                {
                    error = expr.Error ?? "Errore di sintassi sconosciuto.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Converte il risultato <c>object</c> di NCalc (int/long/double/decimal/bool) in double invariant.
        /// </summary>
        private static double ToDouble(object? raw)
        {
            if (raw == null) return 0.0;
            if (raw is double d) return d;
            if (raw is bool b) return b ? 1.0 : 0.0;
            return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Esito di una valutazione di formula. <see cref="Value"/> è significativo solo se
    /// <see cref="IsValid"/> è true; <see cref="UnresolvedIds"/> può essere non vuota anche
    /// con IsValid=true (valutazione conclusa con id sostituiti con 0).
    /// </summary>
    public class FormulaResult
    {
        /// <summary>Risultato numerico della formula. 0 se <see cref="IsValid"/> è false.</summary>
        public double Value { get; set; }

        /// <summary>true se la formula è stata valutata senza errori di sintassi o runtime.</summary>
        public bool IsValid { get; set; }

        /// <summary>Messaggio d'errore se <see cref="IsValid"/> è false; null altrimenti.</summary>
        public string? Error { get; set; }

        /// <summary>
        /// Lista (in ordine di incontro, deduplicata) degli identificatori che il resolver non ha
        /// saputo risolvere. La formula è stata comunque valutata sostituendo tali id con 0.
        /// </summary>
        public List<string> UnresolvedIds { get; set; } = new List<string>();
    }
}
