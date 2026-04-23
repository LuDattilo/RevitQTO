using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QtoRevitPlugin.Application;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Helper Revit-side per navigare a un elemento a partire dal suo UniqueId
    /// (identificatore stabile salvato nelle assegnazioni QtoAssignment). Usato
    /// da HealthView per doppio click sulle righe anomalie/mismatch.
    ///
    /// <para>Comportamento: seleziona l'elemento in Revit + zooma la view attiva
    /// su di esso via <c>UIDocument.ShowElements</c>. Se l'UniqueId non è risolvibile
    /// (elemento cancellato, file sbagliato, ecc.) ritorna false senza throwear.</para>
    /// </summary>
    public static class RevitNavigationHelper
    {
        /// <summary>Risultato della navigazione, per UI feedback.</summary>
        public enum NavigationResult
        {
            /// <summary>Elemento trovato e selezionato + ShowElements chiamato.</summary>
            Selected,
            /// <summary>Nessun documento Revit attivo.</summary>
            NoDocument,
            /// <summary>UniqueId non risolvibile nel documento corrente (elemento cancellato o file sbagliato).</summary>
            NotFound,
            /// <summary>UniqueId vuoto / invalid.</summary>
            InvalidInput,
            /// <summary>Eccezione imprevista.</summary>
            Error,
        }

        /// <summary>
        /// Seleziona un elemento nel documento attivo dato il suo UniqueId e
        /// zooma la vista su di esso. Ritorna l'esito.
        /// </summary>
        public static NavigationResult SelectByUniqueId(string uniqueId)
        {
            if (string.IsNullOrWhiteSpace(uniqueId))
                return NavigationResult.InvalidInput;

            var uiApp = QtoApplication.Instance?.CurrentUiApp;
            var uidoc = uiApp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (uidoc == null || doc == null)
                return NavigationResult.NoDocument;

            try
            {
                var el = doc.GetElement(uniqueId);
                if (el == null || el.Id == ElementId.InvalidElementId)
                    return NavigationResult.NotFound;

                var ids = new List<ElementId> { el.Id };
                uidoc.Selection.SetElementIds(ids);
                uidoc.ShowElements(ids);
                return NavigationResult.Selected;
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("RevitNavigationHelper.SelectByUniqueId", ex);
                return NavigationResult.Error;
            }
        }

        /// <summary>
        /// Label user-facing per feedback dopo SelectByUniqueId, usata da UI
        /// come status message o TaskDialog.
        /// </summary>
        public static string DescribeResult(NavigationResult result, string uniqueId) => result switch
        {
            NavigationResult.Selected => $"Elemento selezionato in Revit ({uniqueId}).",
            NavigationResult.NoDocument => "Nessun documento Revit attivo. Apri il progetto prima di navigare.",
            NavigationResult.NotFound => $"Elemento «{uniqueId}» non trovato nel documento corrente. " +
                                          "Potrebbe essere stato eliminato o il .cme è riferito a un altro modello.",
            NavigationResult.InvalidInput => "UniqueId invalido.",
            _ => "Errore durante la selezione dell'elemento."
        };
    }
}
