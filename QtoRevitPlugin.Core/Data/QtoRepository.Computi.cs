using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Data
{
    // file-scoped: QtoRepository (partial) — Computi module
    public partial class QtoRepository
    {
        // =====================================================================
        // Modulo Computi (schema v12) — Plan C-0
        // Entità PriMus-compliant: Documento, Capitoli/Categorie/WBS,
        // Voci Elenco Prezzi, Voci Computo (VCItem) con Misure (RGItem).
        // =====================================================================

        public int InsertComputoDocument(ComputoDocument doc)
        {
            const string sql = @"
                INSERT INTO ComputoDocuments (
                    WorkSessionId, TipoDocumento, Versione, Fgs, PercPrezzi,
                    Comune, Provincia, Oggetto, Committente, Impresa, ParteOpera,
                    Currency, CreatedAt, UpdatedAt)
                VALUES (
                    @WorkSessionId, @TipoDocumento, @Versione, @Fgs, @PercPrezzi,
                    @Comune, @Provincia, @Oggetto, @Committente, @Impresa, @ParteOpera,
                    @Currency, @CreatedAt, @UpdatedAt);
                SELECT last_insert_rowid();";
            doc.Id = _conn.ExecuteScalar<int>(sql, doc);
            return doc.Id;
        }

        public ComputoDocument? GetComputoDocumentBySession(int workSessionId)
        {
            const string sql = "SELECT * FROM ComputoDocuments WHERE WorkSessionId = @wsid;";
            return _conn.QuerySingleOrDefault<ComputoDocument>(sql, new { wsid = workSessionId });
        }

        public void UpdateComputoDocument(ComputoDocument doc)
        {
            const string sql = @"
                UPDATE ComputoDocuments SET
                    TipoDocumento = @TipoDocumento, Versione = @Versione, Fgs = @Fgs,
                    PercPrezzi = @PercPrezzi, Comune = @Comune, Provincia = @Provincia,
                    Oggetto = @Oggetto, Committente = @Committente, Impresa = @Impresa,
                    ParteOpera = @ParteOpera, Currency = @Currency, UpdatedAt = @UpdatedAt
                WHERE Id = @Id;";
            _conn.Execute(sql, doc);
        }

        public int InsertChapterNode(ChapterNode node)
        {
            const string sql = @"
                INSERT INTO ChapterNodes (DocumentId, Level, Codice, DesSintetica, DesEstesa,
                    DataInit, Durata, CodFase, Percentuale, ParentId, SortOrder, IsActive)
                VALUES (@DocumentId, @Level, @Codice, @DesSintetica, @DesEstesa,
                    @DataInit, @Durata, @CodFase, @Percentuale, @ParentId, @SortOrder, @IsActive);
                SELECT last_insert_rowid();";
            node.Id = _conn.ExecuteScalar<int>(sql, node);
            return node.Id;
        }

        public IReadOnlyList<ChapterNode> GetChapterNodes(int documentId)
        {
            const string sql = "SELECT * FROM ChapterNodes WHERE DocumentId = @d ORDER BY SortOrder;";
            return _conn.Query<ChapterNode>(sql, new { d = documentId }).AsList();
        }

        public void UpdateChapterNode(ChapterNode node)
        {
            const string sql = @"
                UPDATE ChapterNodes SET Level = @Level, Codice = @Codice, DesSintetica = @DesSintetica,
                    DesEstesa = @DesEstesa, DataInit = @DataInit, Durata = @Durata, CodFase = @CodFase,
                    Percentuale = @Percentuale, ParentId = @ParentId, SortOrder = @SortOrder, IsActive = @IsActive
                WHERE Id = @Id;";
            _conn.Execute(sql, node);
        }

        public void DeleteChapterNode(int id) =>
            _conn.Execute("DELETE FROM ChapterNodes WHERE Id = @id;", new { id });

        public int InsertCategoryNode(CategoryNode node)
        {
            const string sql = @"
                INSERT INTO CategoryNodes (DocumentId, Level, Codice, DesSintetica, DesEstesa,
                    DataInit, Durata, CodFase, Percentuale, ParentId, SortOrder, IsActive)
                VALUES (@DocumentId, @Level, @Codice, @DesSintetica, @DesEstesa,
                    @DataInit, @Durata, @CodFase, @Percentuale, @ParentId, @SortOrder, @IsActive);
                SELECT last_insert_rowid();";
            node.Id = _conn.ExecuteScalar<int>(sql, node);
            return node.Id;
        }

        public IReadOnlyList<CategoryNode> GetCategoryNodes(int documentId)
        {
            const string sql = "SELECT * FROM CategoryNodes WHERE DocumentId = @d ORDER BY SortOrder;";
            return _conn.Query<CategoryNode>(sql, new { d = documentId }).AsList();
        }

        public void UpdateCategoryNode(CategoryNode node)
        {
            const string sql = @"
                UPDATE CategoryNodes SET Level = @Level, Codice = @Codice, DesSintetica = @DesSintetica,
                    DesEstesa = @DesEstesa, DataInit = @DataInit, Durata = @Durata, CodFase = @CodFase,
                    Percentuale = @Percentuale, ParentId = @ParentId, SortOrder = @SortOrder, IsActive = @IsActive
                WHERE Id = @Id;";
            _conn.Execute(sql, node);
        }

        public void DeleteCategoryNode(int id) =>
            _conn.Execute("DELETE FROM CategoryNodes WHERE Id = @id;", new { id });

        public int InsertWbsNode(WbsNode node)
        {
            const string sql = @"
                INSERT INTO WbsNodes (DocumentId, Kind, Codice, DesSintetica, ParentId, Level, SortOrder, IsActive)
                VALUES (@DocumentId, @Kind, @Codice, @DesSintetica, @ParentId, @Level, @SortOrder, @IsActive);
                SELECT last_insert_rowid();";
            node.Id = _conn.ExecuteScalar<int>(sql, node);
            return node.Id;
        }

        public IReadOnlyList<WbsNode> GetWbsNodes(int documentId, string? kind = null)
        {
            if (string.IsNullOrEmpty(kind))
            {
                const string sql = "SELECT * FROM WbsNodes WHERE DocumentId = @d ORDER BY SortOrder;";
                return _conn.Query<WbsNode>(sql, new { d = documentId }).AsList();
            }
            const string sqlK = "SELECT * FROM WbsNodes WHERE DocumentId = @d AND Kind = @k ORDER BY SortOrder;";
            return _conn.Query<WbsNode>(sqlK, new { d = documentId, k = kind }).AsList();
        }

        public void UpdateWbsNode(WbsNode node)
        {
            const string sql = @"
                UPDATE WbsNodes SET Kind = @Kind, Codice = @Codice, DesSintetica = @DesSintetica,
                    ParentId = @ParentId, Level = @Level, SortOrder = @SortOrder, IsActive = @IsActive
                WHERE Id = @Id;";
            _conn.Execute(sql, node);
        }

        public void DeleteWbsNode(int id) =>
            _conn.Execute("DELETE FROM WbsNodes WHERE Id = @id;", new { id });

        public int InsertMeasurementRow(MeasurementRow row)
        {
            const string sql = @"
                INSERT INTO MeasurementRows (DocumentId, PriceItemId, Quantita, DataMis, Flags,
                    SpCatId, CatId, SbCatId, WbsComputoNodeId, SortOrder)
                VALUES (@DocumentId, @PriceItemId, @Quantita, @DataMis, @Flags,
                    @SpCatId, @CatId, @SbCatId, @WbsComputoNodeId, @SortOrder);
                SELECT last_insert_rowid();";
            row.Id = _conn.ExecuteScalar<int>(sql, row);
            return row.Id;
        }

        public IReadOnlyList<MeasurementRow> GetMeasurementRows(int documentId)
        {
            const string sql = "SELECT * FROM MeasurementRows WHERE DocumentId = @d ORDER BY SortOrder;";
            return _conn.Query<MeasurementRow>(sql, new { d = documentId }).AsList();
        }

        public void UpdateMeasurementRow(MeasurementRow row)
        {
            const string sql = @"
                UPDATE MeasurementRows SET PriceItemId = @PriceItemId, Quantita = @Quantita,
                    DataMis = @DataMis, Flags = @Flags, SpCatId = @SpCatId, CatId = @CatId,
                    SbCatId = @SbCatId, WbsComputoNodeId = @WbsComputoNodeId, SortOrder = @SortOrder
                WHERE Id = @Id;";
            _conn.Execute(sql, row);
        }

        public void DeleteMeasurementRow(int id) =>
            _conn.Execute("DELETE FROM MeasurementRows WHERE Id = @id;", new { id });

        public void RecalcMeasurementRowQuantita(int rowId)
        {
            const string sql = @"
                UPDATE MeasurementRows
                SET Quantita = (SELECT COALESCE(SUM(Quantita), 0) FROM MeasurementSubRows WHERE MeasurementRowId = @id)
                WHERE Id = @id;";
            _conn.Execute(sql, new { id = rowId });
        }

        public int InsertMeasurementSubRow(MeasurementSubRow subRow)
        {
            const string sql = @"
                INSERT INTO MeasurementSubRows (MeasurementRowId, IDVV, Descrizione,
                    PartiUguali, Lunghezza, Larghezza, HPeso, Quantita, Flags, SortOrder)
                VALUES (@MeasurementRowId, @IDVV, @Descrizione, @PartiUguali, @Lunghezza,
                    @Larghezza, @HPeso, @Quantita, @Flags, @SortOrder);
                SELECT last_insert_rowid();";
            subRow.Id = _conn.ExecuteScalar<int>(sql, subRow);
            return subRow.Id;
        }

        public IReadOnlyList<MeasurementSubRow> GetMeasurementSubRows(int measurementRowId)
        {
            const string sql = "SELECT * FROM MeasurementSubRows WHERE MeasurementRowId = @r ORDER BY SortOrder;";
            return _conn.Query<MeasurementSubRow>(sql, new { r = measurementRowId }).AsList();
        }

        public void UpdateMeasurementSubRow(MeasurementSubRow subRow)
        {
            const string sql = @"
                UPDATE MeasurementSubRows SET IDVV = @IDVV, Descrizione = @Descrizione,
                    PartiUguali = @PartiUguali, Lunghezza = @Lunghezza, Larghezza = @Larghezza,
                    HPeso = @HPeso, Quantita = @Quantita, Flags = @Flags, SortOrder = @SortOrder
                WHERE Id = @Id;";
            _conn.Execute(sql, subRow);
        }

        public void DeleteMeasurementSubRow(int id) =>
            _conn.Execute("DELETE FROM MeasurementSubRows WHERE Id = @id;", new { id });

        public int InsertXpweExportJob(XpweExportJob job)
        {
            const string sql = @"
                INSERT INTO XpweExportJobs (DocumentId, ExportedAt, TipoDocumento, XpweVersion,
                    FilePath, FileChecksum, ValidationReport)
                VALUES (@DocumentId, @ExportedAt, @TipoDocumento, @XpweVersion,
                    @FilePath, @FileChecksum, @ValidationReport);
                SELECT last_insert_rowid();";
            job.Id = _conn.ExecuteScalar<int>(sql, job);
            return job.Id;
        }

        public IReadOnlyList<XpweExportJob> GetXpweExportJobs(int documentId)
        {
            const string sql = "SELECT * FROM XpweExportJobs WHERE DocumentId = @d ORDER BY ExportedAt DESC;";
            return _conn.Query<XpweExportJob>(sql, new { d = documentId }).AsList();
        }
    }
}
