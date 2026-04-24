using System;
using System.Collections.Generic;
using System.Linq;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Services.Computi
{
    public class MeasurementService : IMeasurementService
    {
        private readonly IQtoRepository _repo;
        public MeasurementService(IQtoRepository repo) => _repo = repo;

        public IReadOnlyList<MeasurementRow> GetRows(int documentId) =>
            _repo.GetMeasurementRows(documentId);

        public IReadOnlyList<MeasurementSubRow> GetSubRows(int measurementRowId) =>
            _repo.GetMeasurementSubRows(measurementRowId);

        public MeasurementRow CreateRow(int documentId, int priceItemId,
            int? spCatId = null, int? catId = null, int? sbCatId = null, int? wbsComputoNodeId = null)
        {
            if (priceItemId <= 0)
                throw new DomainValidationException("MeasurementRow", "INVALID_PRICE_ITEM",
                    "PriceItemId non valido.");

            var existing = _repo.GetMeasurementRows(documentId);
            var sortOrder = existing.Count == 0 ? 1 : existing.Max(r => r.SortOrder) + 1;

            var row = new MeasurementRow
            {
                DocumentId = documentId,
                PriceItemId = priceItemId,
                Quantita = 0,
                SpCatId = spCatId,
                CatId = catId,
                SbCatId = sbCatId,
                WbsComputoNodeId = wbsComputoNodeId,
                SortOrder = sortOrder
            };
            row.Id = _repo.InsertMeasurementRow(row);
            return row;
        }

        public MeasurementSubRow AddOrUpdateSubRow(int measurementRowId, int idvv, string? descrizione,
            double partiUguali = 1, double? lunghezza = null, double? larghezza = null, double? hPeso = null)
        {
            var quantita = ComputeQuantita(partiUguali, lunghezza, larghezza, hPeso);

            // Upsert per IDVV > 0 (Revit elementId)
            if (idvv > 0)
            {
                var existing = _repo.GetMeasurementSubRows(measurementRowId)
                    .FirstOrDefault(s => s.IDVV == idvv);
                if (existing != null)
                {
                    existing.Descrizione = descrizione;
                    existing.PartiUguali = partiUguali;
                    existing.Lunghezza = lunghezza;
                    existing.Larghezza = larghezza;
                    existing.HPeso = hPeso;
                    existing.Quantita = quantita;
                    _repo.UpdateMeasurementSubRow(existing);
                    _repo.RecalcMeasurementRowQuantita(measurementRowId);
                    return existing;
                }
            }

            var siblings = _repo.GetMeasurementSubRows(measurementRowId);
            var sortOrder = siblings.Count == 0 ? 1 : siblings.Max(s => s.SortOrder) + 1;
            var subRow = new MeasurementSubRow
            {
                MeasurementRowId = measurementRowId,
                IDVV = idvv,
                Descrizione = descrizione,
                PartiUguali = partiUguali,
                Lunghezza = lunghezza,
                Larghezza = larghezza,
                HPeso = hPeso,
                Quantita = quantita,
                SortOrder = sortOrder
            };
            subRow.Id = _repo.InsertMeasurementSubRow(subRow);
            _repo.RecalcMeasurementRowQuantita(measurementRowId);
            return subRow;
        }

        public void UpdateSubRow(MeasurementSubRow subRow)
        {
            if (subRow == null) throw new ArgumentNullException(nameof(subRow));
            if (subRow.Id <= 0)
                throw new DomainValidationException("MeasurementSubRow", "NO_ID", "Id non valido.");
            subRow.Quantita = ComputeQuantita(subRow.PartiUguali, subRow.Lunghezza, subRow.Larghezza, subRow.HPeso);
            _repo.UpdateMeasurementSubRow(subRow);
            _repo.RecalcMeasurementRowQuantita(subRow.MeasurementRowId);
        }

        public void DeleteSubRow(int subRowId, int measurementRowId)
        {
            _repo.DeleteMeasurementSubRow(subRowId);
            _repo.RecalcMeasurementRowQuantita(measurementRowId);
        }

        public void DeleteRow(int measurementRowId) =>
            _repo.DeleteMeasurementRow(measurementRowId);

        /// <summary>
        /// Formula PriMus: Quantita = PartiUguali × (Lunghezza ?? 1) × (Larghezza ?? 1) × (HPeso ?? 1).
        /// I fattori null o 0 valgono come 1.
        /// </summary>
        public static double ComputeQuantita(double partiUguali, double? lunghezza, double? larghezza, double? hPeso)
        {
            double l = (lunghezza.HasValue && lunghezza.Value != 0) ? lunghezza.Value : 1.0;
            double la = (larghezza.HasValue && larghezza.Value != 0) ? larghezza.Value : 1.0;
            double h = (hPeso.HasValue && hPeso.Value != 0) ? hPeso.Value : 1.0;
            return partiUguali * l * la * h;
        }
    }
}
