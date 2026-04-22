using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace QtoRevitPlugin.ExtensibleStorage
{
    /// <summary>
    /// Persiste <see cref="QtoElementData"/> dentro il .rvt via Extensible Storage v1.
    /// La verità autoritativa del plugin è il modello: SQLite è cache performante,
    /// ES è la fonte durabile cross-macchina (cfr. RecoveryService).
    ///
    /// ## Schema v1 fields
    /// - AssignedEpCodes : IList&lt;string&gt;   (multi-EP)
    /// - Source          : string               ("RevitElement" | "Room" | "Manual")
    /// - LastTagged      : string (ISO 8601 UTC)
    /// - ExclusionReason : string               (empty = non escluso)
    ///
    /// ## Transazioni
    /// Il repo NON apre transazioni Revit proprie. Il caller (tipicamente un
    /// <c>IExternalEventHandler</c> di tagging) deve avere una Transaction aperta prima
    /// di <see cref="Write"/> / <see cref="Remove"/>.
    ///
    /// ## Migration v1 → vN
    /// <see cref="MigrateIfNeeded"/> è un placeholder per future versioni dello schema.
    /// Convenzione: ogni breaking change → nuovo GUID schema + nuova costante, il migrator
    /// legge la v precedente, costruisce la nuova Entity, elimina la vecchia. Mai modificare
    /// il GUID v1 in produzione (C4 dell'analisi repository).
    /// </summary>
    public class ExtensibleStorageRepo
    {
        // GUID stabile — fonte unica di verità in QtoConstants.EsSchemaV1
        private static Guid SchemaV1Guid => QtoConstants.EsSchemaV1;

        private const string SchemaName = "QtoAssignmentV1";
        private const string VendorId = "GPA";

        // Field names (fixed per schema v1 — mai rinominare senza bump versione)
        private const string FieldAssignedEpCodes = "AssignedEpCodes";
        private const string FieldSource = "Source";
        private const string FieldLastTagged = "LastTagged";
        private const string FieldExclusionReason = "ExclusionReason";

        private readonly Schema _schema;

        public ExtensibleStorageRepo()
        {
            _schema = LookupOrCreateSchemaV1();
        }

        /// <summary>GUID dello schema corrente — esposto per diagnostica/recovery tooling.</summary>
        public static Guid CurrentSchemaGuid => SchemaV1Guid;

        // ---------------------------------------------------------------------
        // Schema bootstrap
        // ---------------------------------------------------------------------

        private static Schema LookupOrCreateSchemaV1()
        {
            var existing = Schema.Lookup(SchemaV1Guid);
            if (existing != null) return existing;

            try
            {
                var builder = new SchemaBuilder(SchemaV1Guid);
                builder.SetSchemaName(SchemaName);
                builder.SetReadAccessLevel(AccessLevel.Public);
                builder.SetWriteAccessLevel(AccessLevel.Vendor);
                builder.SetVendorId(VendorId);

                builder.AddArrayField(FieldAssignedEpCodes, typeof(string));
                builder.AddSimpleField(FieldSource, typeof(string));
                builder.AddSimpleField(FieldLastTagged, typeof(string));
                builder.AddSimpleField(FieldExclusionReason, typeof(string));

                return builder.Finish();
            }
            catch (Exception ex)
            {
                CrashLogger.WriteException("ExtensibleStorageRepo.LookupOrCreateSchemaV1", ex);
                throw;
            }
        }

        // ---------------------------------------------------------------------
        // Read / Write / Remove
        // ---------------------------------------------------------------------

        /// <summary>
        /// Legge i dati QTO dell'elemento. Ritorna null se l'elemento non ha entity di questo schema
        /// (= mai taggato). Non apre transazioni — operazione read-only safe in qualunque contesto.
        /// </summary>
        public QtoElementData? Read(Element element)
        {
            if (element == null) return null;

            var entity = element.GetEntity(_schema);
            if (!entity.IsValid()) return null;

            return new QtoElementData
            {
                AssignedEpCodes = SafeGetArray<string>(entity, FieldAssignedEpCodes),
                Source = SafeGet<string>(entity, FieldSource) ?? "RevitElement",
                LastTagged = ParseIso8601(SafeGet<string>(entity, FieldLastTagged)),
                ExclusionReason = EmptyToNull(SafeGet<string>(entity, FieldExclusionReason))
            };
        }

        /// <summary>
        /// Scrive/sovrascrive i dati QTO sull'elemento. **Richiede Transaction aperta dal caller.**
        /// </summary>
        public void Write(Element element, QtoElementData data)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (data == null) throw new ArgumentNullException(nameof(data));

            var entity = new Entity(_schema);
            entity.Set(FieldAssignedEpCodes, data.AssignedEpCodes ?? new List<string>());
            entity.Set(FieldSource, data.Source ?? "RevitElement");
            entity.Set(FieldLastTagged, data.LastTagged.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            entity.Set(FieldExclusionReason, data.ExclusionReason ?? string.Empty);

            element.SetEntity(entity);
        }

        /// <summary>
        /// Rimuove l'entity dall'elemento (untag completo). **Richiede Transaction aperta dal caller.**
        /// Ritorna true se era presente e rimossa, false se già assente.
        /// </summary>
        public bool Remove(Element element)
        {
            if (element == null) return false;

            var entity = element.GetEntity(_schema);
            if (!entity.IsValid()) return false;

            element.DeleteEntity(_schema);
            return true;
        }

        // ---------------------------------------------------------------------
        // Enumerazione
        // ---------------------------------------------------------------------

        /// <summary>
        /// Enumera Id di tutti gli elementi del doc che hanno entity di questo schema.
        /// Implementazione: scan con FilteredElementCollector. Per modelli grandi considerare
        /// <c>ExtensibleStorageFilter</c> (API Revit) come ottimizzazione futura.
        /// </summary>
        public IEnumerable<ElementId> EnumerateTaggedElements(Document doc)
        {
            if (doc == null) yield break;

            // Regola C7 (performance): filtri rapidi prima, lenti dopo.
            // Lo schema filter è "lento" (richiede ispezione entity); precediamo con
            // WhereElementIsNotElementType come filtro veloce di base.
            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            foreach (var el in collector)
            {
                Entity entity;
                try { entity = el.GetEntity(_schema); }
                catch { continue; }

                if (entity.IsValid())
                    yield return el.Id;
            }
        }

        // ---------------------------------------------------------------------
        // Migration placeholder
        // ---------------------------------------------------------------------

        /// <summary>
        /// Placeholder per future migrazioni schema (v1 → v2, etc). In v1 è no-op.
        /// Convenzione futura:
        /// <code>
        /// var oldV1 = Schema.Lookup(SchemaV1Guid);
        /// var newV2 = Schema.Lookup(SchemaV2Guid);
        /// if (oldV1 != null &amp;&amp; el.GetEntity(oldV1).IsValid() &amp;&amp; newV2 != null)
        /// {
        ///     var v1Data = ReadV1(el, oldV1);
        ///     var v2Entity = BuildV2Entity(v1Data);
        ///     el.SetEntity(v2Entity);       // richiede Transaction
        ///     el.DeleteEntity(oldV1);
        /// }
        /// </code>
        /// </summary>
        public void MigrateIfNeeded(Element element)
        {
            // v1 attuale — no-op.
            _ = element;
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private static T? SafeGet<T>(Entity entity, string field) where T : class
        {
            try { return entity.Get<T>(field); }
            catch { return null; }
        }

        private static IList<T> SafeGetArray<T>(Entity entity, string field)
        {
            try
            {
                var raw = entity.Get<IList<T>>(field);
                return raw ?? new List<T>();
            }
            catch { return new List<T>(); }
        }

        private static DateTime ParseIso8601(string? iso)
        {
            if (string.IsNullOrEmpty(iso)) return DateTime.MinValue;
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                       DateTimeStyles.RoundtripKind, out var dt)
                ? dt
                : DateTime.MinValue;
        }

        private static string? EmptyToNull(string? s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
