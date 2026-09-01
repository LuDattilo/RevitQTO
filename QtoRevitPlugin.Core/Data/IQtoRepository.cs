using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;
using System.Collections.Generic;

namespace QtoRevitPlugin.Data
{
    public interface IQtoRepository
    {
        int InsertSession(WorkSession session);
        void UpdateSession(WorkSession session);
        WorkSession? GetSession(int sessionId);

        int InsertAssignment(QtoAssignment assignment);
        void UpdateAssignment(QtoAssignment assignment);
        IReadOnlyList<QtoAssignment> GetAssignments(int sessionId);

        void AppendChangeLog(ChangeLogEntry entry);
        IReadOnlyList<ChangeLogEntry> GetChangeLog(int sessionId);

        void UpsertSnapshot(ElementSnapshot snapshot);
        IReadOnlyList<ElementSnapshot> GetSnapshots(int sessionId);

        // ComputoChapter CRUD (Sprint 9)
        int InsertComputoChapter(ComputoChapter ch);
        void UpdateComputoChapter(ComputoChapter ch);
        void DeleteComputoChapter(int chapterId);
        System.Collections.Generic.IReadOnlyList<ComputoChapter> GetComputoChapters(int sessionId);

        // Reconciliation batch (Sprint 9)
        void AcceptDiffBatch(System.Collections.Generic.IReadOnlyList<SupersedeOp> ops);

        // ProjectInfo (Sprint 10): metadati computo per intestazione export XPWE/PDF/Excel
        ProjectInfo? GetProjectInfo(int sessionId);
        void UpsertProjectInfo(ProjectInfo info);

        // SoaCategory (Sprint 10 step 2 · v8): codici OG/OS normativi (read-only).
        // Seedati al primo avvio da SoaCategorySeed. Utilizzati nel ComboBox della
        // Struttura Computo per assegnare OG/OS ai nodi.
        System.Collections.Generic.IReadOnlyList<SoaCategory> GetSoaCategories();

        // UserFavorites (v10 · UserLibrary.db). Lista preferiti utente globale.
        System.Collections.Generic.IReadOnlyList<UserFavorite> GetFavorites();
        int AddFavorite(UserFavorite fav);
        void RemoveFavorite(int id);
        bool IsFavorite(string code, int? listId);

        /// <summary>
        /// Ritorna l'insieme degli EpCode usati attivamente nel computo (QtoAssignments
        /// con AuditStatus='Active') per la sessione data. Usato per marcare i preferiti
        /// come "Usato/Non usato" nel panel Preferiti e per il bulk "Rimuovi inutilizzati".
        /// Ritorna un HashSet per lookup O(1) lato chiamante.
        /// </summary>
        System.Collections.Generic.HashSet<string> GetUsedEpCodes(int sessionId);

        /// <summary>
        /// Bulk-delete dei preferiti i cui Id sono nella lista. Esegue in una singola
        /// transazione. NON tocca il listino (UserFavorites vive in UserLibrary.db ma
        /// è una tabella separata da PriceItems). Ritorna il numero di righe cancellate.
        /// </summary>
        int RemoveFavorites(System.Collections.Generic.IEnumerable<int> favoriteIds);

        // RevitParamMapping (v9 · .cme). Mapping configurabile campi Informazioni
        // Progetto → parametri Revit da cui ereditare il valore.
        System.Collections.Generic.IReadOnlyList<RevitParamMapping> GetRevitParamMappings(int sessionId);
        void UpsertRevitParamMapping(RevitParamMapping mapping);
        void DeleteRevitParamMapping(int sessionId, string fieldKey);

        /// <summary>
        /// Batch read di PriceItem per Id. Usato dalla ricerca semantica AI
        /// (SemanticSearch) per risolvere gli Id top-N in oggetti completi.
        /// Ignora silenziosamente gli Id orfani (nessun throw).
        /// </summary>
        System.Collections.Generic.IReadOnlyList<PriceItem> GetPriceItems(
            System.Collections.Generic.IReadOnlyList<int> ids);

        /// <summary>
        /// Plan C-6: lookup esatto per Code (usato da ApplyEp per risolvere la voce EP attiva).
        /// Ritorna i match (potrebbero essercene multipli se la stessa Code esiste in più PriceList
        /// attive). Solo i listini attivi (IsActive=1).
        /// </summary>
        System.Collections.Generic.IReadOnlyList<PriceItem> GetPriceItemsByCode(string code);

        /// <summary>
        /// Plan C-6 debug: fallback LIKE per trovare voci simili al code passato.
        /// Usato quando GetPriceItemsByCode fallisce, per diagnosticare problemi di
        /// corrispondenza (prefisso diverso, caratteri invisibili). Ignora IsActive.
        /// </summary>
        System.Collections.Generic.IReadOnlyList<PriceItem> SearchPriceItemsByCodeLike(string code, int limit);

        // EmbeddingCache (AI — modulo opzionale). Cache vettori per voci di listino,
        // pre-calcolati al primo load e invalidati al cambio modello o listino.
        /// <summary>True se esiste un embedding per (priceItemId, modelName).</summary>
        bool HasEmbedding(int priceItemId, string modelName);
        /// <summary>Insert o aggiorna (UNIQUE constraint UpSert) l'embedding per l'item.</summary>
        void UpsertEmbedding(int priceItemId, string modelName, byte[] vectorBlob);
        /// <summary>Legge gli embedding cached per una lista di PriceItemId + modello.
        /// Ordine non garantito; il chiamante ricostruisce la mappa.</summary>
        System.Collections.Generic.IReadOnlyList<QtoRevitPlugin.AI.EmbeddingEntry> GetEmbeddings(
            System.Collections.Generic.IReadOnlyList<int> priceItemIds,
            string modelName);
        /// <summary>Bulk-delete embedding per un modello (es. l'utente ha cambiato modello).
        /// Ritorna il numero di righe rimosse.</summary>
        int DeleteEmbeddingsForModel(string modelName);
        /// <summary>Bulk-delete embedding per un listino (es. re-import dopo versione cambiata).</summary>
        int DeleteEmbeddingsForPriceList(int priceListId);

        // NuoviPrezzi (I8 D.Lgs. 36/2023 All. II.14). Voci per lavorazioni non
        // presenti nell'EP contrattuale, con analisi prezzi + workflow approvazione.
        System.Collections.Generic.IReadOnlyList<NuovoPrezzo> GetNuoviPrezzi(int sessionId);
        int InsertNuovoPrezzo(NuovoPrezzo np);
        void UpdateNuovoPrezzo(NuovoPrezzo np);
        void DeleteNuovoPrezzo(int id);

        // ManualItems (I13). Voci EP manuali svincolate dagli elementi Revit:
        // oneri di sicurezza, trasporti, noli, voci a corpo, ecc.
        System.Collections.Generic.IReadOnlyList<ManualQuantityEntry> GetManualItems(int sessionId);
        int InsertManualItem(ManualQuantityEntry item);
        void UpdateManualItem(ManualQuantityEntry item);
        /// <summary>Soft delete (IsDeleted=1) per audit trail.</summary>
        void DeleteManualItem(int id);

        // RoomMappingConfigs (Sprint 11)
        int InsertRoomMappingConfig(RoomMappingConfig cfg);
        IReadOnlyList<RoomMappingConfig> GetRoomMappingConfigs(int sessionId);
        void UpdateRoomMappingConfig(RoomMappingConfig cfg);
        void DeleteRoomMappingConfig(int id);

        // SelectionRules (I6). Preset regole di selezione salvati come JSON blob.
        // Alternative al file JSON (persistenza globale user); qui persistiamo nel .cme
        // specifico della sessione per condivisione col team via file .cme workshared.
        /// <summary>Ritorna (Id, Name) delle regole salvate — JSON non deserializzato.</summary>
        System.Collections.Generic.IReadOnlyList<(int Id, string Name)> GetSelectionRulePresetNames();
        /// <summary>Carica il preset deserializzato per Id.</summary>
        SelectionRulePreset? GetSelectionRulePreset(int id);
        /// <summary>Salva o aggiorna per Name (UNIQUE implicito logico). Ritorna l'Id.</summary>
        int UpsertSelectionRulePreset(SelectionRulePreset preset);
        void DeleteSelectionRulePreset(int id);

        // -------------------- Modulo Computi (schema v12) --------------------

        int InsertComputoDocument(ComputoDocument doc);
        ComputoDocument? GetComputoDocumentBySession(int workSessionId);
        void UpdateComputoDocument(ComputoDocument doc);

        int InsertChapterNode(ChapterNode node);
        IReadOnlyList<ChapterNode> GetChapterNodes(int documentId);
        void UpdateChapterNode(ChapterNode node);
        void DeleteChapterNode(int id);

        int InsertCategoryNode(CategoryNode node);
        IReadOnlyList<CategoryNode> GetCategoryNodes(int documentId);
        void UpdateCategoryNode(CategoryNode node);
        void DeleteCategoryNode(int id);

        int InsertWbsNode(WbsNode node);
        IReadOnlyList<WbsNode> GetWbsNodes(int documentId, string? kind = null);
        void UpdateWbsNode(WbsNode node);
        void DeleteWbsNode(int id);

        int InsertMeasurementRow(MeasurementRow row);
        IReadOnlyList<MeasurementRow> GetMeasurementRows(int documentId);
        void UpdateMeasurementRow(MeasurementRow row);
        void DeleteMeasurementRow(int id);
        void RecalcMeasurementRowQuantita(int rowId);

        int InsertMeasurementSubRow(MeasurementSubRow subRow);
        IReadOnlyList<MeasurementSubRow> GetMeasurementSubRows(int measurementRowId);
        void UpdateMeasurementSubRow(MeasurementSubRow subRow);
        void DeleteMeasurementSubRow(int id);

        /// <summary>v13: sotto-righe con IDVV&gt;0 prive di categoria (legacy), per il backfill una-tantum.</summary>
        IReadOnlyList<MeasurementSubRow> GetSubRowsMissingCategory(int documentId);

        /// <summary>v13: aggiornamento mirato di categoria/famiglia di una sotto-riga (backfill).</summary>
        void UpdateSubRowCategory(int subRowId, string? category, string? familyName);

        int InsertXpweExportJob(XpweExportJob job);
        IReadOnlyList<XpweExportJob> GetXpweExportJobs(int documentId);
    }

    public interface IFavoritesRepository
    {
        FavoriteSet LoadGlobal();
        void SaveGlobal(FavoriteSet set);
        FavoriteSet? LoadForProject(string cmePath);
        void SaveForProject(string cmePath, FavoriteSet set);
    }
}
