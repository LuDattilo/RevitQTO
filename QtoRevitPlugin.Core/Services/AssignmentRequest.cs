using System.Collections.Generic;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Parametri di una richiesta di assegnazione EP → N elementi Revit,
    /// preparata dalla UI e consumata da <see cref="IAssignmentService"/>.
    /// I campi Revit-side (UniqueId, ElementId, Category, FamilyName, PhaseCreated)
    /// sono già stati raccolti — il servizio Core non vede l'API Revit.
    /// </summary>
    public class AssignmentRequest
    {
        public AssignmentRequest(int sessionId, string epCode)
        {
            SessionId = sessionId;
            EpCode = epCode ?? string.Empty;
            Targets = new List<AssignmentTarget>();
        }

        public int SessionId { get; }
        public string EpCode { get; set; }
        public string EpDescription { get; set; } = string.Empty;

        /// <summary>Prezzo unitario (snapshot dal listino al momento del tagging).</summary>
        public double UnitPrice { get; set; }

        /// <summary>Unità di misura (snapshot dal listino: m², m³, ecc.).</summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>Id del listino di provenienza (per lookup "è già preferito?" in seguito).</summary>
        public int? PriceListId { get; set; }

        /// <summary>PublicId (GUID) del listino, quando disponibile — identificatore stabile cross-machine.</summary>
        public string? PriceListPublicId { get; set; }

        /// <summary>Regola applicata (opzionale, default "Manuale").</summary>
        public string RuleApplied { get; set; } = "Manuale";

        /// <summary>Username dell'operatore (per audit).</summary>
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>Elementi Revit bersaglio dell'assegnazione (uno per istanza).</summary>
        public List<AssignmentTarget> Targets { get; }
    }

    /// <summary>
    /// Singolo elemento Revit destinatario di un'assegnazione.
    /// Contiene solo dati POCO estratti dalla UI/Extraction; niente riferimenti Revit API.
    /// </summary>
    public class AssignmentTarget
    {
        public AssignmentTarget(
            long elementId,
            string uniqueId,
            string category,
            string familyName,
            double quantity,
            string phaseCreated = "",
            string phaseDemolished = "")
        {
            ElementId = elementId;
            UniqueId = uniqueId ?? string.Empty;
            Category = category ?? string.Empty;
            FamilyName = familyName ?? string.Empty;
            Quantity = quantity;
            PhaseCreated = phaseCreated ?? string.Empty;
            PhaseDemolished = phaseDemolished ?? string.Empty;
        }

        /// <summary>Id Revit (long per compatibilità R2025+ ElementId.Value).</summary>
        public long ElementId { get; }
        public string UniqueId { get; }
        public string Category { get; }
        public string FamilyName { get; }

        /// <summary>Quantità (area/volume/lunghezza) dell'elemento singolo.</summary>
        public double Quantity { get; }
        public string PhaseCreated { get; }
        public string PhaseDemolished { get; }
    }

    /// <summary>
    /// Esito di un batch di assegnazione.
    /// </summary>
    public class AssignmentOutcome
    {
        public AssignmentOutcome(
            int insertedCount,
            int skippedCount,
            bool isFirstUseOfEp,
            double totalAmount,
            IReadOnlyList<string> skipReasons)
        {
            InsertedCount = insertedCount;
            SkippedCount = skippedCount;
            IsFirstUseOfEp = isFirstUseOfEp;
            TotalAmount = totalAmount;
            SkipReasons = skipReasons;
        }

        /// <summary>Numero di QtoAssignment effettivamente scritti in DB.</summary>
        public int InsertedCount { get; }

        /// <summary>Numero di target ignorati (duplicati attivi o input non validi).</summary>
        public int SkippedCount { get; }

        /// <summary>
        /// True se nel batch appena eseguito è stata usata per la prima volta
        /// questa <see cref="AssignmentRequest.EpCode"/> sulla sessione.
        /// La UI usa questo flag per proporre il prompt "salva nei preferiti?".
        /// </summary>
        public bool IsFirstUseOfEp { get; }

        /// <summary>Somma delle quantità × prezzi del batch (utile per feedback UI immediato).</summary>
        public double TotalAmount { get; }

        /// <summary>Motivi per cui alcuni target sono stati scartati (diagnostica).</summary>
        public IReadOnlyList<string> SkipReasons { get; }
    }
}
