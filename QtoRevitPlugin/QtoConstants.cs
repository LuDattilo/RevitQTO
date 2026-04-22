using Autodesk.Revit.UI;
using System;

namespace QtoRevitPlugin
{
    /// <summary>
    /// Costanti identificative del plug-in: GUID, PaneId, schema versions.
    /// Fonte unica di verità — NON rigenerare questi GUID tra build (Revit persiste
    /// stato per-GUID in %AppData%\Autodesk\Revit\{ver}\UIState.dat e nell'Extensible
    /// Storage dei .rvt: cambiare i GUID significa perdere tutto lo stato utente).
    ///
    /// Slot riservati per componenti futuri (Sprint 3+) sono commentati: decommentare
    /// quando servono, MAI cambiare i GUID già in produzione.
    /// </summary>
    public static class QtoConstants
    {
        // ---------------------------------------------------------------------
        // DockablePane
        // ---------------------------------------------------------------------

        /// <summary>GUID stabile del DockablePane principale CME. In produzione dal Sprint 0.</summary>
        public static readonly Guid MainPaneGuid = new("A4B2C1D0-E5F6-7890-ABCD-EF1234567891");

        /// <summary>Alias tipizzato Revit di <see cref="MainPaneGuid"/>.</summary>
        public static readonly DockablePaneId MainPaneId = new DockablePaneId(MainPaneGuid);

        /// <summary>Titolo mostrato nella barra del pane — cambiare qui modifica la label visibile.</summary>
        public const string MainPaneTitle = "CME · Computo";

        // ---------------------------------------------------------------------
        // Extensible Storage schemas (Sprint 3+)
        // ---------------------------------------------------------------------

        // Ogni breaking change → nuovo GUID + migration handler (vedi C4 del doc analisi).

        /// <summary>Schema ES v1 — assegnazioni QTO per elemento + config multi-EP.</summary>
        public static readonly Guid EsSchemaV1 = new("B5C3D2E1-F6A7-8901-BCDE-F12345678902");

        // ---------------------------------------------------------------------
        // Shared Parameters (Sprint 3+)
        // ---------------------------------------------------------------------

        // Nomi dei shared param (i GUID SP stabili sono in QtoParameterDefinitions).
        // Questi sono solo alias single-source-of-truth per il nome stringa.

        public const string SpQtoCodice = "QTO_Codice";
        public const string SpQtoDescrizioneBreve = "QTO_DescrizioneBreve";
        public const string SpQtoStato = "QTO_Stato";
        public const string SpQtoAltezzaLocale = "QTO_AltezzaLocale"; // Sprint 2 task 7 (Sorgente B)
        public const string SpQtoLastSync = "QtoLastSync"; // già in uso via QtoLastSyncWriter

        // ---------------------------------------------------------------------
        // Applicazione
        // ---------------------------------------------------------------------

        /// <summary>Nome della ribbon tab CME (visibile in Revit).</summary>
        public const string RibbonTabName = "CME";

        /// <summary>Nome del ribbon panel dentro la tab CME.</summary>
        public const string RibbonPanelName = "Computo Metrico";
    }
}
