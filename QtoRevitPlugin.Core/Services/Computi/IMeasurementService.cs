using System.Collections.Generic;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Services.Computi
{
    public interface IMeasurementService
    {
        IReadOnlyList<MeasurementRow> GetRows(int documentId);
        IReadOnlyList<MeasurementSubRow> GetSubRows(int measurementRowId);

        MeasurementRow CreateRow(int documentId, int priceItemId,
            int? spCatId = null, int? catId = null, int? sbCatId = null, int? wbsComputoNodeId = null);

        MeasurementSubRow AddOrUpdateSubRow(int measurementRowId, int idvv, string? descrizione,
            double partiUguali = 1, double? lunghezza = null, double? larghezza = null, double? hPeso = null);

        void UpdateSubRow(MeasurementSubRow subRow);
        void DeleteSubRow(int subRowId, int measurementRowId);
        void DeleteRow(int measurementRowId);
    }
}
