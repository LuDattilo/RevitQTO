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
    public static class QtoParameterDefinitions
    {
        /// <summary>Nome del gruppo nel file .txt shared parameter (convenzione ACCA-like).</summary>
        public const string SharedFileGroupName = "QTO";

        private static readonly Guid QtoCodiceGuid            = new("C1D2E3F4-1122-3344-5566-778899AABBCC");
        private static readonly Guid QtoDescrizioneBreveGuid  = new("C1D2E3F4-1122-3344-5566-778899AABBCD");
        private static readonly Guid QtoStatoGuid             = new("C1D2E3F4-1122-3344-5566-778899AABBCE");
        private static readonly Guid QtoAltezzaLocaleGuid     = new("C1D2E3F4-1122-3344-5566-778899AABBCF");
        private static readonly Guid QtoLastSyncGuid          = new("C1D2E3F4-1122-3344-5566-778899AABBD0");

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

        private static readonly BuiltInCategory[] SpatialElementCategories =
        {
            BuiltInCategory.OST_Rooms,
            BuiltInCategory.OST_MEPSpaces,
        };

        private static readonly BuiltInCategory[] ProjectInformationCategories =
        {
            BuiltInCategory.OST_ProjectInformation,
        };

        public static readonly QtoSharedParam QtoCodice = new QtoSharedParam(
            name: QtoConstants.SpQtoCodice,
            guid: QtoCodiceGuid,
            group: SharedFileGroupName,
            specTypeId: SpecTypeId.String.Text,
            description: "Codice EP assegnato all'elemento per il computo metrico CME.",
            targetCategories: ModelElementCategories,
            bindGroup: GroupTypeId.IdentityData,
            isInstance: true);

        public static readonly QtoSharedParam QtoDescrizioneBreve = new QtoSharedParam(
            name: QtoConstants.SpQtoDescrizioneBreve,
            guid: QtoDescrizioneBreveGuid,
            group: SharedFileGroupName,
            specTypeId: SpecTypeId.String.Text,
            description: "Descrizione breve della voce EP associata (copia dal listino).",
            targetCategories: ModelElementCategories,
            bindGroup: GroupTypeId.IdentityData,
            isInstance: true);

        public static readonly QtoSharedParam QtoStato = new QtoSharedParam(
            name: QtoConstants.SpQtoStato,
            guid: QtoStatoGuid,
            group: SharedFileGroupName,
            specTypeId: SpecTypeId.String.Text,
            description: "Stato computazione: vuoto / COMPUTATO / PARZIALE / NP / ESCLUSO.",
            targetCategories: ModelElementCategories,
            bindGroup: GroupTypeId.IdentityData,
            isInstance: true);

        public static readonly QtoSharedParam QtoAltezzaLocale = new QtoSharedParam(
            name: QtoConstants.SpQtoAltezzaLocale,
            guid: QtoAltezzaLocaleGuid,
            group: SharedFileGroupName,
            specTypeId: SpecTypeId.Length,
            description: "Altezza utile del locale (sotto controsoffitto), usata dalla Sorgente B per il calcolo volumetrico.",
            targetCategories: SpatialElementCategories,
            bindGroup: GroupTypeId.Geometry,
            isInstance: true);

        public static readonly QtoSharedParam QtoLastSync = new QtoSharedParam(
            name: QtoConstants.SpQtoLastSync,
            guid: QtoLastSyncGuid,
            group: SharedFileGroupName,
            specTypeId: SpecTypeId.String.Text,
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
    /// Usa ForgeTypeId (Revit 2022+) per entrambi i target net48/net8.
    /// </summary>
    public sealed class QtoSharedParam
    {
        public string Name { get; }
        public Guid Guid { get; }
        public string Group { get; }
        public ForgeTypeId SpecTypeId { get; }
        public string Description { get; }
        public BuiltInCategory[] TargetCategories { get; }
        public ForgeTypeId BindGroup { get; }
        public bool IsInstance { get; }

        public QtoSharedParam(
            string name,
            Guid guid,
            string group,
            ForgeTypeId specTypeId,
            string description,
            BuiltInCategory[] targetCategories,
            ForgeTypeId bindGroup,
            bool isInstance)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Guid = guid;
            Group = group ?? throw new ArgumentNullException(nameof(group));
            SpecTypeId = specTypeId ?? throw new ArgumentNullException(nameof(specTypeId));
            Description = description ?? string.Empty;
            TargetCategories = targetCategories ?? throw new ArgumentNullException(nameof(targetCategories));
            BindGroup = bindGroup ?? throw new ArgumentNullException(nameof(bindGroup));
            IsInstance = isInstance;
        }
    }
}
