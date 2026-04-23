# AI Integration — Sprint AI

Riferimento: `QTO-AI-Integration.md` (doc di architettura) + code review 2026-04-23.

**Stato al 2026-04-23 18:46**: backend AI completo e testato; UI da costruire.

---

## Fasi completate (commit `8141772` + successivi, 285 test passing)

### F1. Interfacce e DTOs — `QtoRevitPlugin.Core/AI/`
- `IQtoAiProvider` — punto di accesso unico per VM (SuggestEpAsync, FindSemanticMismatchesAsync, SummarizeDescriptionAsync, SemanticSearchAsync)
- `IEmbeddingProvider` / `ITextModelProvider`
- DTOs: `MappingSuggestion` + `MatchConfidence` (VeryLikely/Likely/Uncertain/Unlikely), `SemanticMismatch`, `QuantityAnomaly` + `AnomalySeverity`
- Tutti i metodi ricevono `CancellationToken`

### F2. NullAiProvider — fallback no-op
- `NullAiProvider.Instance` singleton
- `NullEmbeddingProvider.Instance`, `NullTextModelProvider.Instance`
- Pattern: graceful degradation, zero allocazioni (collezioni vuote statiche)
- 8 test

### F3. EmbeddingCache CRUD repository
- Model `EmbeddingEntry` (Id, PriceItemId, ModelName, VectorBlob, CreatedAt)
- Helper `EmbeddingSerializer` (Buffer.BlockCopy, float[] ↔ byte[], validazione)
- Metodi repo in `IQtoRepository`:
  - `HasEmbedding(priceItemId, modelName)`
  - `UpsertEmbedding(id, modelName, blob)` — ON CONFLICT UNIQUE
  - `GetEmbeddings(ids, modelName)` — bulk read con Dapper IN
  - `DeleteEmbeddingsForModel(modelName)`
  - `DeleteEmbeddingsForPriceList(priceListId)` — via JOIN su PriceItems
- 11 test

### F4. AnomalyDetector — statistica pura (nessuna dipendenza)
- Z-score per gruppi EP con soglia 2.5 (Media) / 3.5 (Alta)
- Skip gruppi < 3 elementi, skip gruppi con σ ≈ 0
- Configurabile: `Threshold`, `HighSeverityThreshold`, `MinSampleSize`
- 8 test (outlier singolo, multi-gruppo isolato, threshold custom, etc.)

### F5. OllamaEmbeddingProvider
- HTTP client injectable (per testing)
- `POST /api/embeddings` con payload `{model, prompt}`
- Parse JSON response `{embedding: []}` in `float[]`
- `IsAvailable`: probe `GET /api/tags` con timeout 2s
- `EmbedBatchAsync`: iterazione sequenziale (Ollama non ha batch nativo)
- Disposable

### F6. OllamaTextModelProvider
- `POST /api/generate` con `stream=false` + `options.num_predict`
- Parse campo `response`, fallback empty se mancante
- Timeout 60s (LLM lento)

### F7. OllamaAiProvider — AI provider composto
- `EnsureEmbeddingCacheAsync(items, progress, ct)` — pre-calcola batch e persiste
- `LoadEmbeddingCache(ids)` — in-memory dict per lookup O(1)
- `SuggestEpAsync`: embedding query → cosine su cache → topN oltre threshold 0.65
- `SummarizeDescriptionAsync`: prompt italiano + `SanitizeShortDesc` (rimuove quote/punteggiatura)
- `FindSemanticMismatchesAsync`: **scaffolding** — necessita mapping Code→Id esposto da repo per essere completo
- `SemanticSearchAsync`: **scaffolding** — necessita `IQtoRepository.GetPriceItems(ids)` batch
- `CosineSimilarity.Compute` statico, gestisce zero-vector e lunghezze diverse

### F8. QtoAiFactory
- `Create(settings, repo, logger?)`: istanzia il provider appropriato
  - `AiEnabled=false` → NullAiProvider
  - Ollama non raggiungibile → NullAiProvider + log warning
  - Altrimenti → OllamaAiProvider con soglie configurate
- 5 test (inclusi null-args, URL invalido, logger null)

### F9. CmeSettings estensione AI
Aggiunti campi in `QtoRevitPlugin.Core/Models/CmeSettings.cs`:
- `AiEnabled` (default false)
- `OllamaBaseUrl` (default http://localhost:11434)
- `EmbeddingModel` (default nomic-embed-text)
- `TextModel` (default llama3.2:3b)
- `SuggestThreshold` (0.65), `SemanticSearchThreshold` (0.60), `MismatchThreshold` (0.45)

Serializzazione JSON automatica via `SettingsService.Load`.

---

## Fasi DA COMPLETARE (prossima sessione)

### F11. Completare `FindSemanticMismatchesAsync` e `SemanticSearchAsync`

Richiede:
1. Aggiungere `IQtoRepository.GetPriceItems(IReadOnlyList<int> ids)` per batch read PriceItem
2. `OllamaAiProvider.LoadEmbeddingCache` deve anche popolare una `Dictionary<string, int>` Code→PriceItemId
3. `FindSemanticMismatches`: risolvere embedding EP via Code → PriceItemId → cache
4. `SemanticSearch`: dopo topN su cache, batch-load i PriceItem via nuovo metodo repo
5. Test per entrambi i metodi completati

**Stima**: ~3h + test.

### F12. UI: pannello "Suggerimenti AI" in TaggingView

Richiede scheda TaggingView che attualmente non è la priorità (stai refactorando UI manuale).

Quando pronta:
- Al cambio selezione Revit, chiama `aiProvider.SuggestEpAsync(familyName, category)`
- Box con 3 voci EP + Score badge
- Click → avvia mapping (richiede conferma)

### F13. UI: status bar AI nel DockablePane

TextBlock binding a una property VM `AiStatus`:
- `"AI attiva · nomic-embed-text"` verde se Ollama up
- `"AI non disponibile (Ollama offline)"` rosso se offline e AiEnabled=true
- Vuoto se AiEnabled=false

### F14. UI: Settings AI (SetupView → sub-tab Avanzate?)

- Toggle AI on/off
- TextBox URL Ollama (default pre-compilato)
- ComboBox modello embedding / LLM (enum con valori consigliati)
- Sliders soglie (0.3-0.9 per ognuna delle 3)
- Bottone "Test connessione Ollama" che fa probe e mostra modelli disponibili

### F15. Health Check AI in PreviewView

- Dopo ogni export, esegui `FindSemanticMismatchesAsync(_allAssignments)`
- Badge nella matrice stati: "Semanticamente improbabile" giallo
- Click → pannello lato con 3 suggestion alternativi

### F16. Import listino — pre-cache embedding

Nel flusso `SetupViewModel.OnImportClick` dopo `InsertPriceItemsBatch`:
```csharp
if (aiProvider is OllamaAiProvider ollama)
{
    await Task.Run(() => ollama.EnsureEmbeddingCacheAsync(items, progress, ct));
}
```
Con progress bar indeterminata (5s per 20.000 voci su CPU media).

### F17. Startup check Ollama + badge

In `QtoApplication.OnStartup`:
```csharp
_settings = SettingsService.Load();
_aiProvider = QtoAiFactory.Create(_settings, _repository, CrashLogger.Info);
// Expone _aiProvider come singleton accessibile ai VM
```

---

## Stima totale residua

| Fase | Complessità | Stima |
|---|---|---|
| F11 completare SemanticSearch + FindMismatches | media | 3h |
| F12 UI suggerimenti TaggingView | alta | 1 giornata |
| F13 UI status bar | bassa | 1h |
| F14 UI Settings AI | media | 3h |
| F15 Health Check AI | media | 3h |
| F16 Import pre-cache | bassa | 1h |
| F17 Startup wiring | bassa | 1h |
| **Totale residuo** | | **~2 giornate** |

---

## Prerequisiti utente (documentati)

1. Installare Ollama: https://ollama.com/download
2. Scaricare modello embedding:
   ```
   ollama pull nomic-embed-text
   ```
3. (Opzionale) Scaricare LLM per `SummarizeDescriptionAsync`:
   ```
   ollama pull llama3.2:3b
   ```
4. Abilitare AI in Settings → Avanzate → "Abilita funzioni AI"

**Fallback automatico**: se Ollama non è in esecuzione, il plugin mostra un warning discreto nello status bar e funziona al 100% senza AI (FTS5 ricerca + regole deterministiche).

---

## Privacy & sicurezza

- **Nessun dato esce dalla macchina**: Ollama gira in locale su 127.0.0.1:11434
- **Nessuna azione automatica**: ogni suggerimento richiede conferma utente
- **Opt-in**: default `AiEnabled=false`
- **Log trasparenti**: ogni errore Ollama loggato in `%AppData%\QtoPlugin\startup.log`

---

## Architettura file

```
QtoRevitPlugin.Core/
├── AI/
│   ├── IAiProvider.cs                ← interfacce
│   ├── AiDtos.cs                     ← DTOs
│   ├── NullAiProvider.cs             ← fallback
│   ├── AnomalyDetector.cs            ← z-score (no AI)
│   ├── EmbeddingEntry.cs             ← row model
│   ├── EmbeddingSerializer.cs        ← float[] ↔ byte[]
│   ├── CosineSimilarity.cs           ← utility
│   ├── QtoAiFactory.cs               ← DI
│   └── Ollama/
│       ├── OllamaEmbeddingProvider.cs
│       ├── OllamaTextModelProvider.cs
│       └── OllamaAiProvider.cs
├── Data/
│   └── QtoRepository.cs              ← +CRUD EmbeddingCache
└── Models/
    └── CmeSettings.cs                ← +AI fields

QtoRevitPlugin.Tests/
└── AI/
    ├── AnomalyDetectorTests.cs       (8 test)
    ├── EmbeddingSerializerTests.cs   (7 test)
    ├── NullAiProviderTests.cs        (8 test)
    ├── EmbeddingCacheRepositoryTests.cs (12 test)
    ├── CosineSimilarityTests.cs      (6 test)
    ├── OllamaProvidersHttpTests.cs   (11 test, MockHandler)
    └── QtoAiFactoryTests.cs          (5 test)
```

Test AI totali: **59/59 passing**.
Test suite globale: **285/285 passing**.
