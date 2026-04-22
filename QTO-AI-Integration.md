# QTO Plugin – Integrazione AI
### Modulo opzionale · Riferimento: QTO-Plugin-Documentazione-v3.md

Questo documento descrive l'architettura, i modelli, il codice di integrazione e il piano di sviluppo del modulo AI del plug-in QTO. Il modulo è **interamente opzionale**: il plug-in funziona al 100% senza AI attiva.

---

## 1. Principi Guida

- **Graceful degradation**: se Ollama non è disponibile, ogni funzione AI cade silenziosamente sul fallback deterministico già presente nel core.
- **Nessuna azione automatica**: tutti i suggerimenti AI sono informativi. L'utente deve sempre confermare prima di qualsiasi scrittura sul modello Revit.
- **Interfaccia astratta**: tutta la logica applicativa usa `IQtoAiProvider`. Il backend (Ollama, API cloud, null) è intercambiabile senza toccare il resto del plug-in.
- **Privacy by default**: con Ollama tutto resta in locale. Nessun dato esce dalla macchina dell'utente.

---

## 2. Architettura del Modulo AI

```
Revit QTO Plugin
│
├── Core deterministico (sempre attivo)
│    ├── FTS5 search (listini)
│    ├── MeasurementRulesEngine
│    ├── HealthCheckEngine
│    ├── AnomalyDetector (z-score, senza AI)
│    └── ModelDiffService / RecheckService
│
└── AI Integration Layer (opzionale)
     │
     ├── IQtoAiProvider ◄──── punto di accesso unico
     │    ├── OllamaAiProvider    ← implementazione principale
     │    ├── OpenRouterAiProvider ← per prototipazione rapida / fallback cloud
     │    └── NullAiProvider      ← quando nessun backend è disponibile
     │
     ├── IEmbeddingProvider
     │    └── genera vettori float[] da testo
     │
     ├── ITextModelProvider
     │    └── genera testo (descrizioni brevi, spiegazioni)
     │
     └── EmbeddingCache (SQLite)
          └── vettori EP pre-calcolati, rigenerati solo al cambio listino
```

---

## 3. Interfacce Principali

### 3.1 IQtoAiProvider

Unico punto di accesso per tutta la logica applicativa e i ViewModel.

```csharp
public interface IQtoAiProvider
{
    bool IsAvailable { get; }

    /// <summary>
    /// Data una famiglia/tipo Revit, suggerisce le N voci EP semanticamente più vicine.
    /// </summary>
    Task<List<MappingSuggestion>> SuggestEpAsync(
        string familyName,
        string category,
        int topN = 3);

    /// <summary>
    /// Analizza le assegnazioni EP esistenti e segnala abbinamenti categoria/EP incoerenti.
    /// </summary>
    Task<List<SemanticMismatch>> FindSemanticMismatchesAsync(
        List<QtoAssignment> assignments);

    /// <summary>
    /// Genera una descrizione breve (10-15 parole) da una descrizione EP lunga.
    /// </summary>
    Task<string> SummarizeDescriptionAsync(string longDescription);

    /// <summary>
    /// Ricerca semantica nel listino (alternativa/complemento a FTS5).
    /// </summary>
    Task<List<PriceItem>> SemanticSearchAsync(
        string query,
        List<int> activeListIds,
        int topN = 10);
}
```

### 3.2 IEmbeddingProvider

```csharp
public interface IEmbeddingProvider
{
    bool IsAvailable { get; }
    Task<float[]> EmbedAsync(string text);
    Task<List<float[]>> EmbedBatchAsync(List<string> texts);
}
```

### 3.3 ITextModelProvider

```csharp
public interface ITextModelProvider
{
    bool IsAvailable { get; }
    Task<string> CompleteAsync(string prompt, int maxTokens = 100);
}
```

---

## 4. NullAiProvider — Fallback (da implementare subito)

Questa è la **prima implementazione da creare**, prima ancora di toccare Ollama. Garantisce che tutto il codice che usa `IQtoAiProvider` compili e funzioni senza AI.

```csharp
public class NullAiProvider : IQtoAiProvider
{
    public bool IsAvailable => false;

    public Task<List<MappingSuggestion>> SuggestEpAsync(
        string familyName, string category, int topN = 3)
        => Task.FromResult(new List<MappingSuggestion>());

    public Task<List<SemanticMismatch>> FindSemanticMismatchesAsync(
        List<QtoAssignment> assignments)
        => Task.FromResult(new List<SemanticMismatch>());

    public Task<string> SummarizeDescriptionAsync(string longDescription)
        => Task.FromResult(string.Empty);

    public Task<List<PriceItem>> SemanticSearchAsync(
        string query, List<int> activeListIds, int topN = 10)
        => Task.FromResult(new List<PriceItem>());
}
```

---

## 5. OllamaAiProvider — Implementazione Principale

### 5.1 Prerequisiti utente

1. **Ollama installato**: https://ollama.com/download
2. **Modello embedding scaricato**:
   ```bash
   ollama pull nomic-embed-text
   ```
3. **Modello LLM (opzionale, per descrizioni brevi)**:
   ```bash
   ollama pull llama3.2:3b
   ```

Ollama gira come servizio locale su `http://localhost:11434` e non richiede configurazione aggiuntiva.

### 5.2 OllamaEmbeddingProvider

```csharp
public class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _client;
    private readonly string _model;

    public OllamaEmbeddingProvider(
        string baseUrl = "http://localhost:11434",
        string model   = "nomic-embed-text")
    {
        _model  = model;
        _client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _client.Timeout = TimeSpan.FromSeconds(30);
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                var resp = _client.GetAsync("/api/tags").Result;
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }
    }

    public async Task<float[]> EmbedAsync(string text)
    {
        var payload = new { model = _model, input = text };
        var json    = JsonSerializer.Serialize(payload);
        var resp    = await _client.PostAsync(
            "/api/embeddings",
            new StringContent(json, Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement
                  .GetProperty("embedding")
                  .EnumerateArray()
                  .Select(e => e.GetSingle())
                  .ToArray();
    }

    public async Task<List<float[]>> EmbedBatchAsync(List<string> texts)
    {
        var results = new List<float[]>();
        foreach (var text in texts)
            results.Add(await EmbedAsync(text)); // Ollama non ha batch nativo
        return results;
    }
}
```

### 5.3 OllamaTextModelProvider

```csharp
public class OllamaTextModelProvider : ITextModelProvider
{
    private readonly HttpClient _client;
    private readonly string _model;

    public OllamaTextModelProvider(
        string baseUrl = "http://localhost:11434",
        string model   = "llama3.2:3b")
    {
        _model  = model;
        _client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _client.Timeout = TimeSpan.FromSeconds(60);
    }

    public bool IsAvailable => true; // assumiamo stesso host di embedding

    public async Task<string> CompleteAsync(string prompt, int maxTokens = 100)
    {
        var payload = new
        {
            model  = _model,
            prompt = prompt,
            stream = false,
            options = new { num_predict = maxTokens }
        };
        var json = JsonSerializer.Serialize(payload);
        var resp = await _client.PostAsync(
            "/api/generate",
            new StringContent(json, Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("response").GetString() ?? string.Empty;
    }
}
```

---

## 6. EmbeddingCache (SQLite)

Gli embedding del prezzario vengono calcolati **una volta sola** al caricamento del listino e salvati nel DB. Vengono rigenerati solo se il listino cambia (versione/data di import).

### 6.1 Schema DB aggiuntivo

```sql
CREATE TABLE EmbeddingCache (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    PriceItemId INTEGER REFERENCES PriceItems(Id),
    ModelName   TEXT NOT NULL,          -- es. "nomic-embed-text"
    VectorBlob  BLOB NOT NULL,          -- float[] serializzato come bytes
    CreatedAt   DATETIME DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(PriceItemId, ModelName)
);
```

### 6.2 EmbeddingCacheService

```csharp
public class EmbeddingCacheService
{
    private readonly QtoRepository _repo;
    private readonly IEmbeddingProvider _embed;
    private Dictionary<int, float[]> _cache = new();

    public async Task EnsureCacheAsync(
        List<PriceItem> items,
        string modelName,
        IProgress<int> progress = null)
    {
        var missing = items
            .Where(i => !_repo.HasEmbedding(i.Id, modelName))
            .ToList();

        for (int i = 0; i < missing.Count; i++)
        {
            var item  = missing[i];
            var text  = $"{item.Code} {item.Description} {item.Chapter}";
            var vec   = await _embed.EmbedAsync(text);
            _repo.SaveEmbedding(item.Id, modelName, SerializeVector(vec));
            progress?.Report(i + 1);
        }
    }

    public void LoadCacheInMemory(List<int> priceItemIds, string modelName)
    {
        _cache = _repo.GetEmbeddings(priceItemIds, modelName)
                      .ToDictionary(e => e.PriceItemId,
                                    e => DeserializeVector(e.VectorBlob));
    }

    // Calcolo embedding prezzario: ~5s per 20.000 voci su CPU moderna
    private byte[] SerializeVector(float[] v)
    {
        var bytes = new byte[v.Length * 4];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private float[] DeserializeVector(byte[] b)
    {
        var v = new float[b.Length / 4];
        Buffer.BlockCopy(b, 0, v, 0, b.Length);
        return v;
    }
}
```

---

## 7. Funzionalità AI nel Dettaglio

### 7.1 Smart Mapping (Suggerimento EP da Famiglia Revit)

```csharp
public async Task<List<MappingSuggestion>> SuggestEpAsync(
    string familyName, string category, int topN = 3)
{
    if (!_embeddingProvider.IsAvailable || _cache.Count == 0)
        return new List<MappingSuggestion>();

    var queryText = $"{category} {familyName}";
    var queryVec  = await _embeddingProvider.EmbedAsync(queryText);

    return _cache
        .Select(kv => new
        {
            PriceItemId = kv.Key,
            Score       = CosineSimilarity(queryVec, kv.Value)
        })
        .Where(x => x.Score > 0.65)          // soglia: abbinamento ragionevole
        .OrderByDescending(x => x.Score)
        .Take(topN)
        .Select(x => new MappingSuggestion
        {
            PriceItem = _repo.GetPriceItem(x.PriceItemId),
            Score     = x.Score,
            Label     = $"{x.Score:P0} match"
        })
        .ToList();
}

private static float CosineSimilarity(float[] a, float[] b)
{
    float dot = 0, magA = 0, magB = 0;
    for (int i = 0; i < a.Length; i++)
    {
        dot  += a[i] * b[i];
        magA += a[i] * a[i];
        magB += b[i] * b[i];
    }
    return dot / (MathF.Sqrt(magA) * MathF.Sqrt(magB) + 1e-8f);
}
```

**Dove appare nella UI**: pannello laterale nella TaggingView. Quando l'utente seleziona elementi Revit, compare un box "Suggerimenti AI" con 3 voci EP con punteggio, cliccabili per avviare l'assegnazione (che rimane soggetta a conferma).

### 7.2 Ricerca Semantica nel Listino

Complemento alla ricerca FTS5: quando FTS5 restituisce pochi o nessun risultato (es. sinonimi, terminologia diversa), la ricerca semantica viene attivata automaticamente.

```csharp
public async Task<List<PriceItem>> SemanticSearchAsync(
    string query, List<int> activeListIds, int topN = 10)
{
    var queryVec = await _embeddingProvider.EmbedAsync(query);

    return _cache
        .Where(kv => activeListIds.Contains(
            _repo.GetPriceItem(kv.Key).PriceListId))
        .Select(kv => new
        {
            Item  = _repo.GetPriceItem(kv.Key),
            Score = CosineSimilarity(queryVec, kv.Value)
        })
        .Where(x => x.Score > 0.60)
        .OrderByDescending(x => x.Score)
        .Take(topN)
        .Select(x => x.Item)
        .ToList();
}
```

**Logica di fallback nella TaggingView**:

```csharp
// Prima prova FTS5 (sempre)
var ftsResults = _repo.Search(query, activeListIds);

// Se FTS5 ha pochi risultati, completa con ricerca semantica
if (ftsResults.Count < 5 && _aiProvider.IsAvailable)
{
    var semanticResults = await _aiProvider.SemanticSearchAsync(query, activeListIds);
    ftsResults = ftsResults
        .Union(semanticResults)
        .DistinctBy(i => i.Id)
        .ToList();
}
```

### 7.3 Incoerenza Semantica Categoria/EP (Health Check AI)

Trova assegnazioni EP semanticamente "strane" rispetto alla categoria dell'elemento. Eseguito nel pannello Health Check come layer aggiuntivo rispetto ai controlli deterministici.

```csharp
public async Task<List<SemanticMismatch>> FindSemanticMismatchesAsync(
    List<QtoAssignment> assignments)
{
    var mismatches = new List<SemanticMismatch>();

    foreach (var a in assignments)
    {
        var categoryVec = await _embeddingProvider.EmbedAsync(
            $"{a.Category} {a.FamilyName}");
        
        if (!_cache.TryGetValue(a.PriceItemId, out var epVec))
            continue;

        float similarity = CosineSimilarity(categoryVec, epVec);

        if (similarity < 0.45) // abbinamento improbabile
        {
            var suggestions = await SuggestEpAsync(a.FamilyName, a.Category);
            mismatches.Add(new SemanticMismatch
            {
                UniqueId        = a.UniqueId,
                Category        = a.Category,
                FamilyName      = a.FamilyName,
                EpCode          = a.EpCode,
                EpDescription   = a.EpDescription,
                Similarity      = similarity,
                Suggestions     = suggestions
            });
        }
    }
    return mismatches;
}
```

**Soglie indicative**:

| Score cosine | Interpretazione |
|---|---|
| > 0,75 | Abbinamento molto probabile |
| 0,60 – 0,75 | Abbinamento ragionevole |
| 0,45 – 0,60 | Zona grigia, da verificare |
| < 0,45 | Abbinamento improbabile → segnala mismatch |

### 7.4 Generazione Descrizione Breve EP

Eseguita una volta al caricamento del listino per tutte le voci senza `ShortDesc`. Risultati salvati nel DB.

```csharp
public async Task<string> SummarizeDescriptionAsync(string longDescription)
{
    if (!_textModel.IsAvailable) return string.Empty;

    var prompt = $"""
        Riassumi questa descrizione tecnica di una voce di computo in massimo 12 parole.
        Descrizione: {longDescription}
        Risposta (solo la descrizione breve, senza punteggiatura finale):
        """;

    return await _textModel.CompleteAsync(prompt, maxTokens: 30);
}
```

### 7.5 Anomaly Detection (Statistica, senza AI)

Funziona **sempre**, senza Ollama. Individua valori di quantità statisticamente anomali rispetto agli altri elementi dello stesso gruppo EP.

```csharp
public class AnomalyDetector
{
    public List<QuantityAnomaly> Detect(List<QtoAssignment> assignments)
    {
        var anomalies = new List<QuantityAnomaly>();

        foreach (var group in assignments.GroupBy(a => a.EpCode))
        {
            var quantities = group.Select(a => a.Quantity).ToList();
            if (quantities.Count < 3) continue; // campione troppo piccolo

            double mean = quantities.Average();
            double std  = Math.Sqrt(
                quantities.Select(q => Math.Pow(q - mean, 2)).Average());

            if (std < 0.001) continue; // tutti uguali, nessuna anomalia

            foreach (var a in group)
            {
                double z = Math.Abs(a.Quantity - mean) / std;
                if (z > 2.5)
                {
                    anomalies.Add(new QuantityAnomaly
                    {
                        UniqueId    = a.UniqueId,
                        EpCode      = a.EpCode,
                        Quantity    = a.Quantity,
                        Mean        = mean,
                        StdDev      = std,
                        ZScore      = z,
                        Severity    = z > 3.5 ? Severity.Alta : Severity.Media,
                        Message     = $"Quantità {a.Quantity:F2} anomala " +
                                      $"(media gruppo: {mean:F2}, z={z:F1})"
                    });
                }
            }
        }
        return anomalies;
    }
}
```

---

## 8. Modelli Raccomandati

| Modello | Dimensione | Scopo | Comando pull |
|---|---|---|---|
| `nomic-embed-text` | ~274 MB | Embedding (Smart Mapping, ricerca semantica) | `ollama pull nomic-embed-text` |
| `mxbai-embed-large` | ~670 MB | Embedding qualità superiore (vocabolario tecnico) | `ollama pull mxbai-embed-large` |
| `llama3.2:3b` | ~2 GB | Generazione descrizioni brevi | `ollama pull llama3.2:3b` |
| `phi4-mini` | ~2,5 GB | Generazione + ragionamento (più preciso) | `ollama pull phi4-mini` |

**Raccomandazione di partenza**: `nomic-embed-text` (embedding) + `llama3.2:3b` (testo). Totale ~2,3 GB, funzionano su 8 GB RAM senza GPU dedicata.

---

## 9. Registrazione in DI / Factory

```csharp
public static class QtoAiFactory
{
    public static IQtoAiProvider Create(QtoSettings settings)
    {
        if (!settings.AiEnabled)
            return new NullAiProvider();

        var embeddingProvider = new OllamaEmbeddingProvider(
            settings.OllamaBaseUrl,
            settings.EmbeddingModel);

        if (!embeddingProvider.IsAvailable)
        {
            Logger.Warn("Ollama non disponibile, AI disabilitata.");
            return new NullAiProvider();
        }

        var textProvider = new OllamaTextModelProvider(
            settings.OllamaBaseUrl,
            settings.TextModel);

        var cache = new EmbeddingCacheService(repository, embeddingProvider);

        return new OllamaAiProvider(embeddingProvider, textProvider, cache, repository);
    }
}
```

Configurazione (`QtoSettings`):

```json
{
  "AiEnabled": true,
  "OllamaBaseUrl": "http://localhost:11434",
  "EmbeddingModel": "nomic-embed-text",
  "TextModel": "llama3.2:3b",
  "SmartMappingThreshold": 0.65,
  "MismatchThreshold": 0.45
}
```

---

## 10. Check all'avvio del Plug-in

```csharp
public void CheckAiStatus(QtoSettings settings)
{
    if (!settings.AiEnabled) return;

    bool ollamaUp = _embeddingProvider.IsAvailable;

    if (!ollamaUp)
    {
        // Notifica discreta nella status bar del DockablePane
        _previewPaneVM.AiStatus = "AI non disponibile (Ollama offline)";
        _aiProvider = new NullAiProvider();
    }
    else
    {
        _previewPaneVM.AiStatus = $"AI attiva · {settings.EmbeddingModel}";
        _aiProvider = QtoAiFactory.Create(settings);

        // Pre-carica embedding per i listini attivi
        Task.Run(() => _cacheService.EnsureCacheAsync(
            _activePriceItems,
            settings.EmbeddingModel));
    }
}
```

---

## 11. Piano di Sviluppo – Sprint AI (da aggiungere al planning principale)

### Sprint 9 (già previsto) – Ampliato

| Task | Stima |
|---|---|
| Definire interfacce `IQtoAiProvider`, `IEmbeddingProvider`, `ITextModelProvider` | 0,5 gg |
| Implementare `NullAiProvider` | 0,5 gg |
| Schema `EmbeddingCache` nel DB + `EmbeddingCacheService` | 1 gg |
| `OllamaEmbeddingProvider` (HTTP client + serializzazione) | 1 gg |
| `OllamaTextModelProvider` | 0,5 gg |
| `OllamaAiProvider` con Smart Mapping + ricerca semantica | 1,5 gg |
| Anomaly Detection (z-score, senza Ollama) → Health Check | 1 gg |
| Mismatch semantico categoria/EP → Health Check AI | 1 gg |
| Generazione ShortDesc all'import listino | 0,5 gg |
| UI: pannello "Suggerimenti AI" in TaggingView | 1 gg |
| UI: badge AI in DockablePane + check disponibilità | 0,5 gg |
| Settings: toggle AI + URL Ollama + modelli | 0,5 gg |
| Test e calibrazione soglie cosine | 1 gg |
| **Totale** | **~10 giorni lavorativi** |

---

## 12. Riferimenti

- Ollama API Reference: https://github.com/ollama/ollama/blob/main/docs/api.md
- Modelli embedding disponibili: https://ollama.com/search?c=embedding
- Microsoft.Extensions.AI (alternativa a HTTP raw): https://learn.microsoft.com/en-us/dotnet/ai/
- Semantic Kernel + Ollama: https://devblogs.microsoft.com/semantic-kernel/
- nomic-embed-text: https://ollama.com/library/nomic-embed-text
- Cosine similarity .NET: calcolo manuale (nessuna dipendenza esterna necessaria)
