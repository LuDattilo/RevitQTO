using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Extraction;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI.Views;
using RevitTaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Orchestratore UI-side del tagging EP→Element (Sprint UI-4).
    ///
    /// Responsabilità:
    ///   1. Raccoglie le istanze Revit della categoria + FamilyType selezionato,
    ///      filtrate per fase attiva della sessione.
    ///   2. Apre <see cref="PickEpDialog"/> per la scelta della voce EP.
    ///   3. Estrae la quantità di ogni istanza (default: Count=1.0; v2: Area/Volume/Length).
    ///   4. Chiama <see cref="IAssignmentService.AssignEp"/> per persistere il batch.
    ///   5. Se è il primo uso di quell'EP → prompt "salvare nei preferiti personali?".
    ///   6. Fire <see cref="SessionManager.NotifyActivePhaseChanged"/>?
    ///      No: <c>AssignEp</c> aggiorna la session; <c>SessionChanged</c> viene
    ///      sollevato tramite <c>SessionManager.Flush()</c> che il caller può chiamare
    ///      dopo il batch (o via kind specifico in futuro).
    /// </summary>
    public class AssignEpCommandRunner
    {
        /// <summary>Risultato compatto del run per feedback UI.</summary>
        public class RunResult
        {
            public bool Cancelled { get; set; }
            public int Inserted { get; set; }
            public int Skipped { get; set; }
            public double TotalAmount { get; set; }
            public string UserMessage { get; set; } = string.Empty;
        }

        /// <summary>
        /// Esegue il flusso di tagging per le istanze di <paramref name="familyName"/>/
        /// <paramref name="typeName"/> nella categoria <paramref name="category"/>.
        /// </summary>
        public RunResult Run(BuiltInCategory category, string familyName, string typeName)
        {
            var app = QtoApplication.Instance
                ?? throw new InvalidOperationException("QtoApplication non inizializzata.");
            var session = app.SessionManager?.ActiveSession;
            var repo = app.SessionManager?.Repository;

            if (session == null || repo == null)
            {
                RevitTaskDialog.Show("CME — Assegna EP",
                    "Nessun computo attivo. Crea o apri un file .cme dalla Home prima di assegnare voci EP.");
                return new RunResult { Cancelled = true, UserMessage = "Nessun computo attivo" };
            }

            var doc = app.CurrentUiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                RevitTaskDialog.Show("CME — Assegna EP", "Nessun documento Revit attivo.");
                return new RunResult { Cancelled = true, UserMessage = "Nessun documento Revit" };
            }

            // 1. Raccolta istanze del FamilyType selezionato, filtrate per fase
            var instances = CollectInstances(doc, category, familyName, typeName, session.ActivePhaseId);
            if (instances.Count == 0)
            {
                RevitTaskDialog.Show("CME — Assegna EP",
                    $"Nessuna istanza di «{familyName} · {typeName}» trovata" +
                    (session.ActivePhaseId > 0 ? $" nella fase «{session.ActivePhaseName}»." : "."));
                return new RunResult { Cancelled = true, UserMessage = "Nessuna istanza" };
            }

            // 2. Apri dialog scelta EP + quantità
            var dialog = new PickEpDialog();
            dialog.SetSubtitle($"{instances.Count} istanza/e di «{familyName} · {typeName}»"
                + (session.ActivePhaseId > 0 ? $" · fase «{session.ActivePhaseName}»" : ""));

            // Preview probe: richiamato dal dialog ad ogni cambio QuantityMode.
            // Estraggo le quantità reali dalle istanze per fornire totale e media;
            // se l'extractor fallisce per un param non disponibile, fallback a
            // Count (1.0 per istanza) — consistente con l'extractor stesso.
            var extractor = new QuantityExtractor();
            (double totQty, double avgQty) ProbeQuantity(QuantityMode mode)
            {
                var key = QuantityModeDefaults.ExtractorKey(mode);
                double sum = 0.0;
                int counted = 0;
                foreach (var el in instances)
                {
                    var q = extractor.Extract(el, key, out _);
                    sum += q;
                    counted++;
                }
                var avg = counted > 0 ? sum / counted : 0.0;
                return (sum, avg);
            }

            dialog.SetQuantityContext(
                ostCode: category.ToString(), // BuiltInCategory.ToString() → "OST_Walls"
                instanceCount: instances.Count,
                probe: ProbeQuantity);

            var ok = dialog.ShowDialog();
            if (ok != true || dialog.SelectedItem == null)
                return new RunResult { Cancelled = true, UserMessage = "Annullato" };

            var picked = dialog.SelectedItem;
            var chosenMode = dialog.QuantityMode;
            var extractorKey = QuantityModeDefaults.ExtractorKey(chosenMode);

            // 3. Estrae quantità secondo il mode scelto dall'utente
            var targets = new List<AssignmentTarget>(instances.Count);
            int extractionFailures = 0;
            foreach (var el in instances)
            {
                var qty = extractor.Extract(el, extractorKey, out var extractErr);
                if (extractErr != null) extractionFailures++;

                var phaseCreated = el.get_Parameter(BuiltInParameter.PHASE_CREATED)?.AsValueString() ?? string.Empty;
                var phaseDemolished = el.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED)?.AsValueString() ?? string.Empty;
                targets.Add(new AssignmentTarget(
                    elementId: ElementIdValue(el.Id),
                    uniqueId: el.UniqueId,
                    category: category.ToString(),
                    familyName: familyName,
                    quantity: qty,
                    phaseCreated: phaseCreated,
                    phaseDemolished: phaseDemolished));
            }

            // 4. Chiama AssignmentService Core
            var service = new AssignmentService(repo);
            var request = new AssignmentRequest(session.Id, picked.Code)
            {
                EpDescription = !string.IsNullOrWhiteSpace(picked.ShortDescription)
                    ? picked.ShortDescription
                    : picked.Description,
                Unit = picked.Unit,
                UnitPrice = picked.UnitPrice,
                PriceListId = picked.PriceListId,
                CreatedBy = Environment.UserName,
                // Tracciamo nel RuleApplied la modalità quantità usata; utile per
                // auditing ed export che mostri "da che parametro arriva la qty".
                RuleApplied = $"Manuale · Qty={QuantityModeDefaults.DisplayLabel(chosenMode)}",
            };
            foreach (var t in targets) request.Targets.Add(t);

            var outcome = service.AssignEp(request);

            // 5. Propaga il cambio alla UI: la session è già stata aggiornata nel DB
            // dal service; sollevo SessionChanged kind=Renamed per rivalutazione KPI.
            app.SessionManager.Flush();

            // 6. Prompt favorites al primo uso
            if (outcome.IsFirstUseOfEp)
                PromptSaveFavorite(picked, outcome);

            var qtyNote = extractionFailures > 0
                ? $" [⚠ {extractionFailures} istanza/e senza parametro {QuantityModeDefaults.DisplayLabel(chosenMode)}]"
                : string.Empty;

            return new RunResult
            {
                Cancelled = false,
                Inserted = outcome.InsertedCount,
                Skipped = outcome.SkippedCount,
                TotalAmount = outcome.TotalAmount,
                UserMessage = $"Assegnate {outcome.InsertedCount} istanza/e a «{picked.Code}»" +
                              (outcome.SkippedCount > 0 ? $" ({outcome.SkippedCount} ignorate)" : "") +
                              qtyNote +
                              $" — € {outcome.TotalAmount:N2}"
            };
        }

        /// <summary>
        /// Chiede all'utente se salvare la voce appena usata tra i preferiti personali.
        /// Il flusso è quello descritto in backlog T2.
        /// </summary>
        private void PromptSaveFavorite(EpPickRow picked, AssignmentOutcome outcome)
        {
            var td = new RevitTaskDialog("CME — Voce nuova nel computo")
            {
                MainInstruction = $"Salvare «{picked.Code}» nei preferiti personali?",
                MainContent = $"Hai appena usato «{picked.Code}» per la prima volta in questo computo " +
                              $"({outcome.InsertedCount} istanza/e). I preferiti personali sono accessibili " +
                              "rapidamente da «Listino → Preferiti personali» in ogni progetto.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.Yes,
            };

            var answer = td.Show();
            if (answer != TaskDialogResult.Yes) return;

            TrySavePersonalFavorite(picked);
        }

        private void TrySavePersonalFavorite(EpPickRow picked)
        {
            try
            {
                var repoFav = new QtoRevitPlugin.Data.FileFavoritesRepository(
                    QtoRevitPlugin.Data.FileFavoritesRepository.GetDefaultGlobalDir());
                var current = repoFav.LoadGlobal();

                // Evita duplicati: se code già presente, no-op silenzioso
                if (current.Items.Any(i => string.Equals(i.Code, picked.Code, StringComparison.OrdinalIgnoreCase)))
                    return;

                current.Items.Add(new FavoriteItem
                {
                    Code = picked.Code,
                    ShortDesc = picked.ShortDescription,
                    Description = picked.Description,
                    Unit = picked.Unit,
                    UnitPrice = picked.UnitPrice,
                    ListId = picked.PriceListId,
                    AddedAt = DateTime.UtcNow,
                });
                repoFav.SaveGlobal(current);
            }
            catch (Exception ex)
            {
                RevitTaskDialog.Show("CME — Salvataggio preferito",
                    $"Non è stato possibile salvare il preferito: {ex.Message}");
            }
        }

        /// <summary>
        /// Raccoglie istanze Revit della categoria/FamilyType richiesto, applicando
        /// il filtro di fase se la sessione ha una fase attiva.
        /// </summary>
        private static List<Element> CollectInstances(
            Document doc,
            BuiltInCategory category,
            string familyName,
            string typeName,
            int activePhaseId)
        {
            var collector = new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsNotElementType();

            if (activePhaseId > 0)
            {
#if REVIT2025_OR_LATER
                var phaseId = new ElementId((long)activePhaseId);
#else
                var phaseId = new ElementId(activePhaseId);
#endif
                var statuses = new List<ElementOnPhaseStatus>
                {
                    ElementOnPhaseStatus.New,
                    ElementOnPhaseStatus.Existing,
                };
                collector = collector.WherePasses(new ElementPhaseStatusFilter(phaseId, statuses));
            }

            var result = new List<Element>();
            foreach (var el in collector)
            {
                if (el is FamilyInstance fi)
                {
                    if (string.Equals(fi.Symbol?.FamilyName, familyName, StringComparison.Ordinal) &&
                        string.Equals(fi.Symbol?.Name, typeName, StringComparison.Ordinal))
                    {
                        result.Add(el);
                    }
                    continue;
                }

                // System family (Wall/Floor/Roof/etc.) — Family = CategoryName, Type = ElementType.Name
                var etype = doc.GetElement(el.GetTypeId()) as ElementType;
                if (etype == null) continue;
                var fam = etype.FamilyName ?? string.Empty;
                var typ = etype.Name ?? string.Empty;
                if (string.Equals(fam, familyName, StringComparison.Ordinal) &&
                    string.Equals(typ, typeName, StringComparison.Ordinal))
                {
                    result.Add(el);
                }
            }

            return result;
        }

        /// <summary>
        /// Estrae l'intero da un <see cref="ElementId"/> gestendo la differenza
        /// di API tra Revit 2024 (IntegerValue) e 2025+ (Value long).
        /// </summary>
        private static long ElementIdValue(ElementId id)
        {
#if REVIT2025_OR_LATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }
    }
}
