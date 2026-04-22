using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace QtoRevitPlugin.SharedParams
{
    /// <summary>
    /// Registro delle definizioni dei Shared Parameter usati dal plugin CME.
    /// Single source of truth: i GUID qui dentro sono <b>STABILI</b> — mai rigenerare,
    /// perché Revit persiste il binding elemento↔param usando il GUID dell'External
    /// Definition. Cambiarli significa orfanare tutti i dati già taggati sui .rvt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stato `QTO_Stato` — valori validi (da §I1/§I11 documentazione):
    /// <c>""</c> (vuoto) / <c>"COMPUTATO"</c> / <c>"PARZIALE"</c> / <c>"NP"</c> / <c>"ESCLUSO"</c>.
    /// </para>
    /// <para>
    /// Tutte le definizioni usano <see cref="ForgeTypeId"/> Revit 2021+. Il ramo
    /// <c>REVIT2024_OR_EARLIER</c> (net48) compila ma alcune API unit-related
    /// cambiano. La logica di creazione delle <c>ExternalDefinition</c> è in
    /// <see cref="SharedParameterManager"/> che gestisce il conditional compilation
    /// per <see cref="ExternalDefinitionCreationOptions"/>.
    /// </para>
    /// </remarks>
    public static class QtoParameterDefinitions
    {
        /// <summary>Nome del gruppo nel file .txt shared parameter (convenzione ACCA-like).</summary>
        public const string SharedFileGroupName = "QTO";

        // =====================================================================
        // GUID stabili dei 5 parametri QTO.
        //
        // ATTENZIONE: questi GUID identificano univocamente il parametro in tutti
        // i .rvt che il plugin ha mai toccato. NON modificarli. Se serve una
        // semantica nuova → nuovo parametro con nuovo GUID + data migration.
        // =====================================================================

        private static readonly Guid QtoCodiceGuid            = new("C1D2E3F4-1122-3344-5566-778899AABBCC");
        private static readonly Guid QtoDescrizioneBreveGuid  = new("C1D2E3F4-1122-3344-5566-778899AABBCD");
        private static readonly Guid QtoStatoGuid             = new("C1D2E3F4-1122-3344-5566-778899AABBCE");
        private static readonly Guid QtoAltezzaLocaleGuid     = new("C1D2E3F4-1122-3344-5566-778899AABBCF");
        private static readonly Guid QtoLastSyncGuid          = new("C1D2E3F4-1122-3344-5566-778899AABBD0");

        // =====================================================================
        // Set categorie: suddivisi per destinazione d'uso.
        // =====================================================================

        /// <summary>
        /// Categorie modellabili su cui viene bindato l'assegnazione QTO core
        /// (Codice, DescrizioneBreve, Stato). Coerente con §I1 / §I12 / Fase 3.
        /// Le categorie MEP fitting sono incluse per permettere quantity take-off
        /// a livello di componente impiantistico singolo.
        /// </summary>
        private static readonly BuiltInCategory[] ModelElementCategories =
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Columns,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_Railings,
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_Casework,
            BuiltInCategory.OST_Furniture,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_PipeFitting,
        };

        /// <summary>Spatial elements (Rooms + Spaces) per l'altezza utile Sorgente B.</summary>
        private static readonly BuiltInCategory[] SpatialElementCategories =
        {
            BuiltInCategory.OST_Rooms,
            BuiltInCategory.OST_MEPSpaces,
        };

        /// <summary>Solo ProjectInformation per l'heartbeat QtoLastSync.</summary>
        private static readonly BuiltInCategory[] ProjectInformationCategories =
        {
            BuiltInCategory.OST_ProjectInformation,
        };

        // =====================================================================
        // Definizioni dei 5 Shared Parameter.
        // =====================================================================

        public static readonly QtoSharedParam QtoCodice = new QtoSharedParam(
            name: QtoConstants.SpQtoCodice,
            guid: QtoCodiceGuid,
            group: SharedFileGroupName,
#if REVIT2025_OR_LATER
            specTypeId: SpecTypeId.String.Text,
#else
            parameterType: ParameterType.Text,
#endif
            description: "Codice EP assegnato all'elemento per il computo metrico CME.",
            targetCategories: ModelElementCategories,
            bindGroup: GroupTypeId.IdentityData,
            isInstance: true);

        public static readonly QtoSharedParam QtoDescrizioneBreve = new QtoSharedParam(
            name: QtoConstants.SpQtoDescrizioneBreve,
            guid: QtoDescrizioneBreveGuid,
            group: SharedFileGroupName,
#if REVIT2025_OR_LATER
            specTypeId: SpecTypeId.String.Text,
#else
            parameterType: ParameterType.Text,
#endif
            description: "Descrizione breve della voce EP associata (copia dal listino).",
            targetCategories: ModelElementCategories,
            bindGroup: GroupTypeId.IdentityData,
            isInstance: true);

        public static readonly QtoSharedParam QtoStato = new QtoSharedParam(
            name: QtoConstants.SpQtoStato,
            guid: QtoStatoGuid,
            group: SharedFileGroupName,
#if REVIT2025_OR_LATER
            specTypeId: SpecTypeId.String.Text,
#else
            parameterType: ParameterType.Text,
#endif
            description: "Stato computazione: vuoto / COMPUTATO / PARZIALE / NP / ESCLUSO.",
            targetCategories: ModelElementCategories,
            bindGroup: GroupTypeId.IdentityData,
            isInstance: true);

        public static readonly QtoSharedParam QtoAltezzaLocale = new QtoSharedParam(
            name: QtoConstants.SpQtoAltezzaLocale,
            guid: QtoAltezzaLocaleGuid,
            group: SharedFileGroupName,
#if REVIT2025_OR_LATER
            specTypeId: SpecTypeId.Length,
#else
            parameterType: ParameterType.Length,
#endif
            description: "Altezza utile del locale (sotto controsoffitto), usata dalla Sorgente B per il calcolo volumetrico.",
            targetCategories: SpatialElementCategories,
            bindGroup: GroupTypeId.Geometry,
            isInstance: true);

        public static readonly QtoSharedParam QtoLastSync = new QtoSharedParam(
            name: QtoConstants.SpQtoLastSync,
            guid: QtoLastSyncGuid,
            group: SharedFileGroupName,
#if REVIT2025_OR_LATER
            specTypeId: SpecTypeId.String.Text,
#else
            parameterType: ParameterType.Text,
#endif
            description: "Timestamp ISO-8601 UTC dell'ultima scrittura QTO sul modello (heartbeat recovery).",
            targetCategories: ProjectInformationCategories,
            bindGroup: GroupTypeId.Data,
            isInstance: true);

        /// <summary>Elenco immutabile delle 5 definizioni QTO, usato dal <see cref="SharedParameterManager"/>.</summary>
        public static IReadOnlyList<QtoSharedParam> All => new[]
        {
            QtoCodice,
            QtoDescrizioneBreve,
            QtoStato,
            QtoAltezzaLocale,
            QtoLastSync,
        };
    }

    /// <summary>
    /// Metadati immutabili di un shared parameter gestito dal plugin CME.
    /// </summary>
    /// <remarks>
    /// Il tipo del parametro è esposto via <see cref="ForgeTypeId"/> su Revit 2025+ e
    /// via <see cref="ParameterType"/> legacy su Revit 2024 e precedenti. Il campo
    /// rimane single-property grazie al conditional compilation: il chiamante
    /// (<c>SharedParameterManager</c>) compila contro una sola delle due.
    /// </remarks>
    public sealed class QtoSharedParam
    {
        /// <summary>Nome visibile del parametro (coincide con la const in <see cref="QtoConstants"/>).</summary>
        public string Name { get; }

        /// <summary>GUID stabile del SP — <b>MAI</b> rigenerato tra build.</summary>
        public Guid Guid { get; }

        /// <summary>Nome del gruppo dentro il file .txt condiviso (default: "QTO").</summary>
        public string Group { get; }

#if REVIT2025_OR_LATER
        /// <summary>Tipo del parametro (Revit 2022+): es. <c>SpecTypeId.String.Text</c>, <c>SpecTypeId.Length</c>.</summary>
        public ForgeTypeId SpecTypeId { get; }
#else
        /// <summary>Tipo del parametro (Revit ≤ 2024): es. <c>ParameterType.Text</c>, <c>ParameterType.Length</c>.</summary>
        public ParameterType ParameterType { get; }
#endif

        /// <summary>Descrizione del SP (mostrata nella UI Revit Manage → Shared Parameters).</summary>
        public string Description { get; }

        /// <summary>Categorie Revit a cui bindare il parametro al momento dell'install.</summary>
        public BuiltInCategory[] TargetCategories { get; }

        /// <summary>Gruppo UI dove il parametro appare nell'Element Properties (Data / Geometry / IdentityData).</summary>
        public ForgeTypeId BindGroup { get; }

        /// <summary>true = InstanceBinding; false = TypeBinding. Il plugin usa sempre Instance (per-elemento).</summary>
        public bool IsInstance { get; }

        public QtoSharedParam(
            string name,
            Guid guid,
            string group,
#if REVIT2025_OR_LATER
            ForgeTypeId specTypeId,
#else
            ParameterType parameterType,
#endif
            string description,
            BuiltInCategory[] targetCategories,
            ForgeTypeId bindGroup,
            bool isInstance)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Guid = guid;
            Group = group ?? throw new ArgumentNullException(nameof(group));
#if REVIT2025_OR_LATER
            SpecTypeId = specTypeId ?? throw new ArgumentNullException(nameof(specTypeId));
#else
            ParameterType = parameterType;
#endif
            Description = description ?? string.Empty;
            TargetCategories = targetCategories ?? throw new ArgumentNullException(nameof(targetCategories));
            BindGroup = bindGroup ?? throw new ArgumentNullException(nameof(bindGroup));
            IsInstance = isInstance;
        }
    }
}
