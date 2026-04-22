using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace QtoRevitPlugin.ExtensibleStorage
{
    /// <summary>
    /// Read/Write del <see cref="ProjectPriceListSnapshot"/> nel DataStorage del .rvt.
    ///
    /// **Scopo**: garantire la portabilità cross-PC del progetto. Senza UserLibrary,
    /// il .rvt resta comunque leggibile perché contiene lo snapshot delle voci usate.
    /// Vedi <c>docs/ARCHITECTURE.md</c> per il design a 2 livelli completo.
    ///
    /// **Pattern uso** (Sprint 5 TagAssignmentHandler):
    /// <code>
    /// using var tx = new Transaction(doc, "CME — Assegna EP");
    /// tx.Start();
    /// _snapshotService.UpsertItem(doc, priceItem, priceListPublicId, priceListName);
    /// tx.Commit();
    /// </code>
    ///
    /// **Limiti**: JSON serializzato in 1 singolo field ES. Oltre ~120 voci distinte
    /// (~60KB JSON) rischia di superare il limite per-Entity (~64KB). Per progetti
    /// grandi, TODO futuro: split multi-Entity con chunking.
    /// </summary>
    public class ProjectSnapshotService
    {
        // GUID stabile schema v1 — MAI rigenerare in produzione
        private static readonly Guid SchemaV1Guid = new Guid("D4E5F6A7-B8C9-0123-DEFA-12345678ABCD");
        private const string SchemaName = "QtoProjectSnapshotV1";
        private const string VendorId = "GPA";
        private const string FieldSnapshotJson = "SnapshotJson";
        private const string DataStorageName = "QtoProject";

        private readonly Schema _schema;
        private readonly JsonSerializerOptions _jsonOpts = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ProjectSnapshotService()
        {
            _schema = LookupOrCreateSchema();
        }

        // ---------------------------------------------------------------------
        // Schema bootstrap
        // ---------------------------------------------------------------------

        private static Schema LookupOrCreateSchema()
        {
            var existing = Schema.Lookup(SchemaV1Guid);
            if (existing != null) return existing;

            var builder = new SchemaBuilder(SchemaV1Guid);
            builder.SetSchemaName(SchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Vendor);
            builder.SetVendorId(VendorId);
            builder.AddSimpleField(FieldSnapshotJson, typeof(string));
            return builder.Finish();
        }

        // ---------------------------------------------------------------------
        // Read
        // ---------------------------------------------------------------------

        /// <summary>
        /// Legge lo snapshot dal DataStorage del documento. Ritorna null se non presente
        /// (progetto mai taggato) o se la deserializzazione JSON fallisce.
        /// Operazione read-only, non richiede Transaction.
        /// </summary>
        public ProjectPriceListSnapshot? Read(Document doc)
        {
            if (doc == null) return null;

            var ds = FindDataStorage(doc);
            if (ds == null) return null;

            var entity = ds.GetEntity(_schema);
            if (!entity.IsValid()) return null;

            try
            {
                var json = entity.Get<string>(FieldSnapshotJson);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonSerializer.Deserialize<ProjectPriceListSnapshot>(json, _jsonOpts);
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("ProjectSnapshotService.Read", ex);
                return null;
            }
        }

        // ---------------------------------------------------------------------
        // Write — richiede Transaction aperta dal caller
        // ---------------------------------------------------------------------

        /// <summary>
        /// Scrive/sovrascrive lo snapshot. **Richiede Transaction aperta dal caller.**
        /// Se il DataStorage "QtoProject" non esiste lo crea.
        /// </summary>
        public void Write(Document doc, ProjectPriceListSnapshot snapshot)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            snapshot.SnapshotUpdatedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(snapshot, _jsonOpts);

            var ds = FindDataStorage(doc) ?? CreateDataStorage(doc);
            var entity = new Entity(_schema);
            entity.Set(FieldSnapshotJson, json);
            ds.SetEntity(entity);
        }

        // ---------------------------------------------------------------------
        // Convenience: UpsertItem (aggiunge voce se non presente, idempotente)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Aggiunge/aggiorna una voce nello snapshot. Se già presente (stesso Code),
        /// viene sovrascritta con i valori correnti. **Richiede Transaction aperta.**
        /// </summary>
        public void UpsertItem(
            Document doc,
            PriceItem item,
            string listPublicId,
            string listName,
            string listVersion = "")
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            var snapshot = Read(doc) ?? new ProjectPriceListSnapshot
            {
                ListPublicId = listPublicId,
                ListName = listName,
                ListVersion = listVersion
            };

            // Se il listPublicId cambia (es. progetto migrato a nuovo prezzario),
            // aggiorniamo ma manteniamo le voci esistenti finché non si esplicita una pulizia.
            if (!string.IsNullOrEmpty(listPublicId))
            {
                snapshot.ListPublicId = listPublicId;
                snapshot.ListName = listName;
                snapshot.ListVersion = listVersion;
            }

            var existing = snapshot.UsedItems.FirstOrDefault(u =>
                string.Equals(u.Code, item.Code, StringComparison.OrdinalIgnoreCase));

            var snap = ToSnapshot(item);
            if (existing != null)
            {
                // Sovrascrivi in-place
                var idx = snapshot.UsedItems.IndexOf(existing);
                snapshot.UsedItems[idx] = snap;
            }
            else
            {
                snapshot.UsedItems.Add(snap);
            }

            Write(doc, snapshot);
        }

        /// <summary>
        /// Rimuove una voce dallo snapshot se nessun elemento la usa più (garbage collect).
        /// **Richiede Transaction aperta.** Nota: il caller deve aver già verificato che
        /// nessun elemento referenzia il Code (tramite scan ExtensibleStorageRepo).
        /// </summary>
        public void RemoveItem(Document doc, string code)
        {
            var snapshot = Read(doc);
            if (snapshot == null) return;

            var removed = snapshot.UsedItems.RemoveAll(i =>
                string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));

            if (removed > 0) Write(doc, snapshot);
        }

        // ---------------------------------------------------------------------
        // DataStorage lookup/create
        // ---------------------------------------------------------------------

        private static DataStorage? FindDataStorage(Document doc)
        {
            var collector = new FilteredElementCollector(doc).OfClass(typeof(DataStorage));
            foreach (DataStorage ds in collector)
            {
                if (string.Equals(ds.Name, DataStorageName, StringComparison.Ordinal))
                    return ds;
            }
            return null;
        }

        private static DataStorage CreateDataStorage(Document doc)
        {
            var ds = DataStorage.Create(doc);
            ds.Name = DataStorageName;
            return ds;
        }

        private static PriceItemSnapshot ToSnapshot(PriceItem item) => new PriceItemSnapshot
        {
            Code = item.Code,
            ShortDesc = item.ShortDesc,
            Description = item.Description,
            Unit = item.Unit,
            UnitPrice = item.UnitPrice,
            SuperChapter = item.SuperChapter,
            Chapter = item.Chapter,
            SubChapter = item.SubChapter,
            IsCustom = item.IsNP
        };
    }
}
