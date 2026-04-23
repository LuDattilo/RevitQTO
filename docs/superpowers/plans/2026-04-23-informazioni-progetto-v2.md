# Informazioni Progetto v2 — Cascata Provincia/Comune + Eredita da Revit Configurabile

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sostituire i TextBox liberi `Comune`/`Provincia` nella scheda Informazioni Progetto con una selezione a cascata (DB ISTAT in `UserLibrary.db`) e rendere configurabile il pulsante "Eredita da Revit" tramite dialog di mappatura persistito nel `.cme`.

**Architecture:** Due feature indipendenti ma dispiegate insieme. Feature A (Cascata) introduce schema nuovo in `UserLibrary.db` (`comuni_italiani`), un `ComuniService` che wrappa Dapper raw, e refactor del ViewModel con `ObservableCollection<string>` per le ComboBox. Feature B (Eredita configurabile) aggiunge `RevitParamMapping` allo schema `.cme` (v8→v9), un `RevitParamMappingService` che enumera `ProjectInfo.Parameters` via Revit API, e un nuovo `RevitMappingDialog` modale.

**Tech Stack:** C# netstandard2.0 (Core) + net48/net8-windows (plugin), WPF, CommunityToolkit.Mvvm, SQLite+Dapper, EmbeddedResource CSV ISTAT, xUnit + FluentAssertions.

---

## File Map

| File | Azione | Responsabilità |
|---|---|---|
| `QtoRevitPlugin.Core/Resources/comuni_istat.csv` | Crea | Dataset ISTAT ~7.900 comuni (EmbeddedResource) |
| `QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj` | Modifica | Dichiara `<EmbeddedResource Include="Resources\comuni_istat.csv" />` |
| `QtoRevitPlugin.Core/Models/Comune.cs` | Crea | Model POCO (CodiceIstat, Nome, ProvinciaSigla, ProvinciaNome, Regione) |
| `QtoRevitPlugin.Core/Data/DatabaseSchema.cs` | Modifica | CurrentVersion 8→9. Aggiunge DDL `comuni_italiani` (UserLibrary) e `RevitParamMapping` (.cme). Migration v8→v9. |
| `QtoRevitPlugin.Core/Data/IQtoRepository.cs` | Modifica | Aggiunge GetProvince, GetComuniByProvincia, GetProvinciaByComune, ComuneExists, SeedComuniFromCsv, GetRevitParamMapping, UpsertRevitParamMapping |
| `QtoRevitPlugin.Core/Data/QtoRepository.cs` | Modifica | Implementa i metodi di IQtoRepository sopra |
| `QtoRevitPlugin.Core/Data/DatabaseInitializer.cs` | Modifica | Migration v8→v9 + chiamata a `SeedComuniFromCsvIfEmpty` su apertura UserLibrary |
| `QtoRevitPlugin/Services/UserLibraryManager.cs` | Modifica | Dopo inizializzazione, chiama seeding comuni leggendo CSV embedded |
| `QtoRevitPlugin.Core/Models/RevitParamMapping.cs` | Crea | Model POCO (SessionId, FieldKey, ParamName, IsBuiltIn) |
| `QtoRevitPlugin.Core/Models/ProjectInfoFieldKeys.cs` | Crea | Costanti `FieldKey`: "DenominazioneOpera", "Committente", ... |
| `QtoRevitPlugin/Services/ComuniService.cs` | Crea | Wrapper sopra IQtoRepository per UI (cache Province) |
| `QtoRevitPlugin/Services/RevitParamMappingService.cs` | Crea | BuiltInEntries + GetCustomParams + ReadValue + defaults mapping |
| `QtoRevitPlugin/UI/ViewModels/ProjectInfoViewModel.cs` | Modifica | Rimpiazza `Comune`/`Provincia` string con `ProvinciaSelezionata`/`ComuneSelezionato` + ObservableCollection + cascata |
| `QtoRevitPlugin/UI/Views/ProjectInfoView.xaml` | Modifica | TextBox Comune/Provincia → ComboBox cascata + TextBlock warning |
| `QtoRevitPlugin/UI/Views/ProjectInfoView.xaml.cs` | Modifica | Click: se mapping esiste → eredita diretto; SHIFT o ⚙ → apri MappingDialog |
| `QtoRevitPlugin/UI/Views/RevitMappingDialog.xaml` | Crea | Dialog modale con DataGrid FieldKey → ComboBox ParamName |
| `QtoRevitPlugin/UI/Views/RevitMappingDialog.xaml.cs` | Crea | Codebehind dialog |
| `QtoRevitPlugin/UI/ViewModels/RevitMappingDialogViewModel.cs` | Crea | VM del dialog: AvailableSources (BuiltIn+Custom), Rows ObservableCollection, SaveCommand |
| `QtoRevitPlugin.Tests/Informazioni/ComuniRepositoryTests.cs` | Crea | Test: seed CSV, GetProvince, GetComuniByProvincia, cascata, case-insensitive |
| `QtoRevitPlugin.Tests/Informazioni/RevitParamMappingRepositoryTests.cs` | Crea | Test: upsert mapping, get for session, default mapping |
| `QtoRevitPlugin.Tests/Informazioni/ProjectInfoViewModelCascateTests.cs` | Crea | Test: OnProvinciaChanged svuota Comune; OnComuneChanged auto-set Provincia; warning su valore inesistente |

---

## Task 1: Schema v8→v9 (UserLibrary + .cme)

**Files:**
- Modify: `QtoRevitPlugin.Core/Data/DatabaseSchema.cs`
- Modify: `QtoRevitPlugin.Core/Data/DatabaseInitializer.cs`

- [ ] **Step 1: Incrementa `CurrentVersion` da 8 a 9**

In `DatabaseSchema.cs` trova:
```csharp
public const int CurrentVersion = 8;
```
Sostituisci con:
```csharp
public const int CurrentVersion = 9;
```

- [ ] **Step 2: Aggiungi DDL `comuni_italiani` a `InitialStatements`**

In `DatabaseSchema.cs`, in fondo all'array `InitialStatements` (prima della `}` chiusura array), aggiungi:

```csharp
// v9 (Informazioni Progetto v2): dataset ISTAT comuni per cascata Provincia→Comune.
// Tabella vive solo in UserLibrary.db (seed da CSV embedded). Nel .cme viene
// creata ma rimane vuota (useremo sempre UserLibrary).
@"CREATE TABLE IF NOT EXISTS comuni_italiani (
    CodiceIstat     TEXT PRIMARY KEY,
    Comune          TEXT NOT NULL,
    ProvinciaSigla  TEXT NOT NULL,
    ProvinciaNome   TEXT NOT NULL,
    Regione         TEXT NOT NULL
);",
@"CREATE INDEX IF NOT EXISTS idx_comuni_prov ON comuni_italiani(ProvinciaSigla);",
@"CREATE INDEX IF NOT EXISTS idx_comuni_nome ON comuni_italiani(Comune COLLATE NOCASE);",

// v9: mapping configurabile parametri Revit → campi Informazioni Progetto.
@"CREATE TABLE IF NOT EXISTS RevitParamMapping (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId       INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
    FieldKey        TEXT NOT NULL,
    ParamName       TEXT,
    IsBuiltIn       INTEGER NOT NULL DEFAULT 0,
    SkipIfFilled    INTEGER NOT NULL DEFAULT 1,
    UNIQUE(SessionId, FieldKey)
);",
```

Nota: `SkipIfFilled` persiste la preferenza della checkbox "Non sovrascrivere campi già compilati" (default 1 = ON). È per-riga per permettere override granulari in futuro, ma nel dialog è una singola checkbox globale che scrive lo stesso valore su tutte le righe.

- [ ] **Step 3: Aggiungi costanti migration v8→v9**

In `DatabaseSchema.cs` dopo le costanti di v7→v8, aggiungi:

```csharp
public const string MigrateV8ToV9_CreateComuniItaliani =
    @"CREATE TABLE IF NOT EXISTS comuni_italiani (
        CodiceIstat     TEXT PRIMARY KEY,
        Comune          TEXT NOT NULL,
        ProvinciaSigla  TEXT NOT NULL,
        ProvinciaNome   TEXT NOT NULL,
        Regione         TEXT NOT NULL
    );";

public const string MigrateV8ToV9_IndexComuniProv =
    "CREATE INDEX IF NOT EXISTS idx_comuni_prov ON comuni_italiani(ProvinciaSigla);";

public const string MigrateV8ToV9_IndexComuniNome =
    "CREATE INDEX IF NOT EXISTS idx_comuni_nome ON comuni_italiani(Comune COLLATE NOCASE);";

public const string MigrateV8ToV9_CreateRevitParamMapping =
    @"CREATE TABLE IF NOT EXISTS RevitParamMapping (
        Id              INTEGER PRIMARY KEY AUTOINCREMENT,
        SessionId       INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
        FieldKey        TEXT NOT NULL,
        ParamName       TEXT,
        IsBuiltIn       INTEGER NOT NULL DEFAULT 0,
        SkipIfFilled    INTEGER NOT NULL DEFAULT 1,
        UNIQUE(SessionId, FieldKey)
    );";
```

- [ ] **Step 4: Registra migration in `DatabaseInitializer.MigrateIfNeeded`**

In `DatabaseInitializer.cs` trova il blocco `if (currentVersion < 8)` (migration v7→v8). DOPO quella, PRIMA di aggiornare `SchemaInfo`, aggiungi:

```csharp
if (currentVersion < 9)
{
    // v8→v9: comuni_italiani (popolata solo in UserLibrary) + RevitParamMapping (solo .cme)
    conn.Execute(DatabaseSchema.MigrateV8ToV9_CreateComuniItaliani, transaction: tx);
    conn.Execute(DatabaseSchema.MigrateV8ToV9_IndexComuniProv, transaction: tx);
    conn.Execute(DatabaseSchema.MigrateV8ToV9_IndexComuniNome, transaction: tx);
    conn.Execute(DatabaseSchema.MigrateV8ToV9_CreateRevitParamMapping, transaction: tx);
}
```

- [ ] **Step 5: Aggiorna assertion di version nei test esistenti**

In `QtoRevitPlugin.Tests/Data/QtoRepositoryTests.cs`, `SchemaV5MigrationTests.cs`, `AuditFieldsMigrationTests.cs` — cerca assertion di tipo `CurrentVersion.Should().Be(8)` e aggiornale a `Be(9)`.

```bash
grep -rn "Be(8)" QtoRevitPlugin.Tests/Data/
```

- [ ] **Step 6: Build + test regression**

```bash
dotnet build "QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj" -c Debug
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj" --filter "FullyQualifiedName~Data"
```

Expected: 0 errors, tutti i test Data passing.

- [ ] **Step 7: Commit**

```bash
git add "QtoRevitPlugin.Core/Data/DatabaseSchema.cs" "QtoRevitPlugin.Core/Data/DatabaseInitializer.cs" "QtoRevitPlugin.Tests/Data/"
git commit -m "feat(infoproj v2 T1): schema v9 - comuni_italiani + RevitParamMapping"
```

---

## Task 2: Model `Comune` + Embedded CSV ISTAT

**Files:**
- Create: `QtoRevitPlugin.Core/Models/Comune.cs`
- Create: `QtoRevitPlugin.Core/Resources/comuni_istat.csv`
- Modify: `QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj`

- [ ] **Step 1: Crea model `Comune`**

Crea `QtoRevitPlugin.Core/Models/Comune.cs`:

```csharp
namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Entry del dataset ISTAT "Codici delle unità amministrative territoriali".
    /// Vive in UserLibrary.db (dati condivisi tra tutti i .cme) — seed da CSV embedded.
    /// </summary>
    public class Comune
    {
        public string CodiceIstat { get; set; } = "";
        public string Nome { get; set; } = "";
        public string ProvinciaSigla { get; set; } = "";
        public string ProvinciaNome { get; set; } = "";
        public string Regione { get; set; } = "";
    }
}
```

- [ ] **Step 2: Genera CSV ISTAT (subset ristretto per test / placeholder pieno per produzione)**

Crea `QtoRevitPlugin.Core/Resources/comuni_istat.csv` (UTF-8, separatore `;`). Header + subset rappresentativo per i test (produzione: sostituire con il dump ISTAT completo, stesso schema). Minimo richiesto — 20 comuni su 5 province:

```csv
CodiceIstat;Comune;ProvinciaSigla;ProvinciaNome;Regione
048017;Firenze;FI;Firenze;Toscana
048014;Empoli;FI;Firenze;Toscana
048032;Scandicci;FI;Firenze;Toscana
048031;Sesto Fiorentino;FI;Firenze;Toscana
058091;Roma;RM;Roma;Lazio
058100;Tivoli;RM;Roma;Lazio
015146;Milano;MI;Milano;Lombardia
015134;Legnano;MI;Milano;Lombardia
015202;Sesto San Giovanni;MI;Milano;Lombardia
063049;Napoli;NA;Napoli;Campania
063024;Ercolano;NA;Napoli;Campania
063050;Nola;NA;Napoli;Campania
001272;Torino;TO;Torino;Piemonte
001156;Moncalieri;TO;Torino;Piemonte
001219;Rivoli;TO;Torino;Piemonte
```

NOTA produzione: sostituire `comuni_istat.csv` con il dump ufficiale ISTAT completo (~7.900 righe, ~300 KB) prima del release. Lo schema colonne è quello sopra.

- [ ] **Step 3: Dichiara EmbeddedResource nel .csproj**

In `QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj`, dentro un `<ItemGroup>` (creane uno nuovo se necessario, accanto ai `<Compile>` esistenti), aggiungi:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\comuni_istat.csv" />
</ItemGroup>
```

- [ ] **Step 4: Build verifica embed**

```bash
dotnet build "QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj" -c Debug
```

Verifica manuale che l'embedded resource sia nell'assembly:

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj" --filter "FullyQualifiedName=BuildSanity" 2>&1 | head -5
```

- [ ] **Step 5: Commit**

```bash
git add "QtoRevitPlugin.Core/Models/Comune.cs" "QtoRevitPlugin.Core/Resources/comuni_istat.csv" "QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj"
git commit -m "feat(infoproj v2 T2): model Comune + dataset ISTAT embedded"
```

---

## Task 3: Repository methods comuni

**Files:**
- Modify: `QtoRevitPlugin.Core/Data/IQtoRepository.cs`
- Modify: `QtoRevitPlugin.Core/Data/QtoRepository.cs`
- Create: `QtoRevitPlugin.Tests/Informazioni/ComuniRepositoryTests.cs`

- [ ] **Step 1: Scrivi test PRIMA dell'implementazione**

Crea directory `QtoRevitPlugin.Tests/Informazioni/`.

Crea `QtoRevitPlugin.Tests/Informazioni/ComuniRepositoryTests.cs`:

```csharp
using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Informazioni
{
    public class ComuniRepositoryTests : System.IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;

        public ComuniRepositoryTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"cme_test_{System.Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            _repo.SeedComuniFromCsv(SampleCsv());
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void SeedComuni_PopulatesTable()
        {
            var province = _repo.GetProvince();
            province.Should().Contain("FI").And.Contain("RM");
        }

        [Fact]
        public void GetComuniByProvincia_FI_ReturnsFirenze()
        {
            var comuni = _repo.GetComuniByProvincia("FI");
            comuni.Should().Contain("Firenze").And.Contain("Empoli");
            comuni.Should().BeInAscendingOrder();
        }

        [Fact]
        public void GetComuniByProvincia_UnknownSigla_ReturnsEmpty()
        {
            _repo.GetComuniByProvincia("XX").Should().BeEmpty();
        }

        [Fact]
        public void GetProvinciaByComune_CaseInsensitive_ReturnsFI()
        {
            _repo.GetProvinciaByComune("firenze").Should().Be("FI");
            _repo.GetProvinciaByComune("FIRENZE").Should().Be("FI");
        }

        [Fact]
        public void ComuneExists_MatchIsCaseInsensitive()
        {
            _repo.ComuneExists("Milano").Should().BeTrue();
            _repo.ComuneExists("milano").Should().BeTrue();
            _repo.ComuneExists("Atlantide").Should().BeFalse();
        }

        [Fact]
        public void SeedComuni_IsIdempotent()
        {
            var before = _repo.GetProvince().Count;
            _repo.SeedComuniFromCsv(SampleCsv());
            _repo.GetProvince().Count.Should().Be(before);
        }

        private static string SampleCsv() => string.Join("\n",
            "CodiceIstat;Comune;ProvinciaSigla;ProvinciaNome;Regione",
            "048017;Firenze;FI;Firenze;Toscana",
            "048014;Empoli;FI;Firenze;Toscana",
            "058091;Roma;RM;Roma;Lazio",
            "015146;Milano;MI;Milano;Lombardia");
    }
}
```

- [ ] **Step 2: Aggiungi firme in `IQtoRepository`**

In `QtoRevitPlugin.Core/Data/IQtoRepository.cs`, dopo la sezione SOA, aggiungi:

```csharp
// Comuni ISTAT (v9 · UserLibrary.db). Dati di riferimento condivisi.
System.Collections.Generic.IReadOnlyList<string> GetProvince();
System.Collections.Generic.IReadOnlyList<string> GetComuniByProvincia(string provinciaSigla);
string? GetProvinciaByComune(string comuneNome);
bool ComuneExists(string comuneNome);
/// <summary>Inserisce i comuni dal CSV (separatore ';'). Idempotente: skip se già seedato.</summary>
void SeedComuniFromCsv(string csvContent);
```

- [ ] **Step 3: Esegui i test — aspettati FAIL**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj" --filter "FullyQualifiedName~ComuniRepositoryTests"
```

Expected: FAIL — metodi non implementati in `QtoRepository`.

- [ ] **Step 4: Implementa i metodi in `QtoRepository.cs`**

In `QtoRevitPlugin.Core/Data/QtoRepository.cs`, prima della chiusura della classe, aggiungi:

```csharp
// ── Comuni ISTAT (v9) ────────────────────────────────────────────

public IReadOnlyList<string> GetProvince()
{
    using var conn = OpenConnection();
    return conn.Query<string>(
        "SELECT DISTINCT ProvinciaSigla FROM comuni_italiani ORDER BY ProvinciaSigla")
        .ToList();
}

public IReadOnlyList<string> GetComuniByProvincia(string provinciaSigla)
{
    using var conn = OpenConnection();
    return conn.Query<string>(
        "SELECT Comune FROM comuni_italiani WHERE ProvinciaSigla = @Sigla ORDER BY Comune",
        new { Sigla = provinciaSigla })
        .ToList();
}

public string? GetProvinciaByComune(string comuneNome)
{
    using var conn = OpenConnection();
    return conn.QueryFirstOrDefault<string>(
        "SELECT ProvinciaSigla FROM comuni_italiani WHERE Comune = @Nome COLLATE NOCASE LIMIT 1",
        new { Nome = comuneNome });
}

public bool ComuneExists(string comuneNome)
{
    using var conn = OpenConnection();
    var count = conn.ExecuteScalar<int>(
        "SELECT COUNT(*) FROM comuni_italiani WHERE Comune = @Nome COLLATE NOCASE",
        new { Nome = comuneNome });
    return count > 0;
}

public void SeedComuniFromCsv(string csvContent)
{
    using var conn = OpenConnection();
    var already = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM comuni_italiani");
    if (already > 0) return; // idempotente

    using var tx = conn.BeginTransaction();
    var lines = csvContent.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
    for (int i = 1; i < lines.Length; i++) // skip header
    {
        var cols = lines[i].Split(';');
        if (cols.Length < 5) continue;
        conn.Execute(
            @"INSERT OR IGNORE INTO comuni_italiani
              (CodiceIstat, Comune, ProvinciaSigla, ProvinciaNome, Regione)
              VALUES (@C, @N, @PS, @PN, @R)",
            new
            {
                C = cols[0].Trim(),
                N = cols[1].Trim(),
                PS = cols[2].Trim(),
                PN = cols[3].Trim(),
                R = cols[4].Trim()
            },
            tx);
    }
    tx.Commit();
}
```

- [ ] **Step 5: Esegui i test — aspettati PASS**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj" --filter "FullyQualifiedName~ComuniRepositoryTests"
```

Expected: 6 passed.

- [ ] **Step 6: Commit**

```bash
git add "QtoRevitPlugin.Core/Data/IQtoRepository.cs" "QtoRevitPlugin.Core/Data/QtoRepository.cs" "QtoRevitPlugin.Tests/Informazioni/ComuniRepositoryTests.cs"
git commit -m "feat(infoproj v2 T3): repository methods comuni ISTAT + 6 test"
```

---

## Task 4: Seeding automatico UserLibrary al primo avvio

**Files:**
- Modify: `QtoRevitPlugin/Services/UserLibraryManager.cs`

- [ ] **Step 1: Leggi lo stato attuale di UserLibraryManager**

Apri `QtoRevitPlugin/Services/UserLibraryManager.cs` e individua il punto dove l'`QtoRepository` viene creato per la prima volta sull'UserLibrary (cerca `new QtoRepository(path)`).

- [ ] **Step 2: Aggiungi helper `SeedComuniFromEmbeddedResource`**

Nella classe `UserLibraryManager` aggiungi:

```csharp
private static void SeedComuniFromEmbeddedResource(QtoRepository repo)
{
    // Legge comuni_istat.csv dalle embedded resources del Core assembly.
    var coreAssembly = typeof(QtoRepository).Assembly;
    const string resName = "QtoRevitPlugin.Resources.comuni_istat.csv";
    using var stream = coreAssembly.GetManifestResourceStream(resName);
    if (stream == null)
    {
        QtoRevitPlugin.Services.CrashLogger.WriteLine(
            $"[UserLibraryManager] Embedded resource '{resName}' non trovata — skip seed comuni.");
        return;
    }
    using var reader = new System.IO.StreamReader(stream);
    var csv = reader.ReadToEnd();
    try
    {
        repo.SeedComuniFromCsv(csv);
    }
    catch (System.Exception ex)
    {
        QtoRevitPlugin.Services.CrashLogger.WriteException("UserLibraryManager.SeedComuni", ex);
    }
}
```

- [ ] **Step 3: Chiama il seed nell'init di UserLibrary**

Nel metodo che crea/apre `UserLibrary.db` (subito dopo `new QtoRepository(path)` e dopo la migration), aggiungi la chiamata:

```csharp
SeedComuniFromEmbeddedResource(repo);
```

- [ ] **Step 4: Build**

```bash
dotnet build "QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj" -c Debug
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add "QtoRevitPlugin/Services/UserLibraryManager.cs"
git commit -m "feat(infoproj v2 T4): seed comuni ISTAT al primo avvio UserLibrary"
```

---

## Task 5: `ComuniService` wrapper UI-side

**Files:**
- Create: `QtoRevitPlugin/Services/ComuniService.cs`

- [ ] **Step 1: Crea il service**

Crea `QtoRevitPlugin/Services/ComuniService.cs`:

```csharp
using QtoRevitPlugin.Data;
using System.Collections.Generic;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Wrapper UI-friendly sopra IQtoRepository per leggere i comuni ISTAT
    /// dall'UserLibrary. Usato da ProjectInfoViewModel per popolare le ComboBox
    /// cascata Provincia→Comune. Non cachea la lista province (consultazione
    /// locale, query ordine ~50ms max su 7.900 righe).
    /// </summary>
    public class ComuniService
    {
        private readonly IQtoRepository _userLibrary;

        public ComuniService(IQtoRepository userLibrary)
        {
            _userLibrary = userLibrary;
        }

        public IReadOnlyList<string> GetProvince() =>
            _userLibrary.GetProvince();

        public IReadOnlyList<string> GetComuniByProvincia(string sigla) =>
            string.IsNullOrWhiteSpace(sigla)
                ? new List<string>()
                : _userLibrary.GetComuniByProvincia(sigla);

        public string? GetProvinciaByComune(string comune) =>
            string.IsNullOrWhiteSpace(comune)
                ? null
                : _userLibrary.GetProvinciaByComune(comune);

        public bool ComuneExists(string nome) =>
            !string.IsNullOrWhiteSpace(nome) && _userLibrary.ComuneExists(nome);
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build "QtoRevitPlugin/QtoRevitPlugin.csproj" -c "Debug R25"
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add "QtoRevitPlugin/Services/ComuniService.cs"
git commit -m "feat(infoproj v2 T5): ComuniService wrapper UI-side"
```

---

## Task 6: `ProjectInfoViewModel` cascata Provincia/Comune

**Files:**
- Modify: `QtoRevitPlugin/UI/ViewModels/ProjectInfoViewModel.cs`
- Create: `QtoRevitPlugin.Tests/Informazioni/ProjectInfoViewModelCascateTests.cs`

- [ ] **Step 1: Scrivi test PRIMA del refactor VM**

Crea `QtoRevitPlugin.Tests/Informazioni/ProjectInfoViewModelCascateTests.cs`:

```csharp
using FluentAssertions;
using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI.ViewModels;
using System.Collections.Generic;
using Xunit;

namespace QtoRevitPlugin.Tests.Informazioni
{
    public class ProjectInfoViewModelCascateTests
    {
        private class FakeComuniService : ComuniService
        {
            public FakeComuniService() : base(null!) { }
            public override IReadOnlyList<string> GetProvince() => new[] { "FI", "MI", "RM" };
            public override IReadOnlyList<string> GetComuniByProvincia(string sigla) => sigla switch
            {
                "FI" => new[] { "Empoli", "Firenze", "Scandicci" },
                "MI" => new[] { "Legnano", "Milano" },
                "RM" => new[] { "Roma", "Tivoli" },
                _ => System.Array.Empty<string>()
            };
            public override string? GetProvinciaByComune(string c) => c switch
            {
                "Firenze" => "FI", "Milano" => "MI", "Roma" => "RM", _ => null
            };
            public override bool ComuneExists(string n) => GetProvinciaByComune(n) != null;
        }

        [Fact]
        public void OnProvinciaChanged_RepopulatesComuniAndClearsSelection()
        {
            var vm = new ProjectInfoViewModel(new FakeComuniService());
            vm.ProvinciaSelezionata = "FI";

            vm.ComuniFiltrati.Should().Contain("Firenze").And.Contain("Scandicci");
            vm.IsComuneEnabled.Should().BeTrue();
            vm.ComuneSelezionato.Should().BeEmpty();
        }

        [Fact]
        public void OnProvinciaCleared_DisablesComune()
        {
            var vm = new ProjectInfoViewModel(new FakeComuniService());
            vm.ProvinciaSelezionata = "FI";
            vm.ProvinciaSelezionata = "";

            vm.IsComuneEnabled.Should().BeFalse();
            vm.ComuniFiltrati.Should().BeEmpty();
        }

        [Fact]
        public void OnComuneChanged_AutoSetsProvincia()
        {
            var vm = new ProjectInfoViewModel(new FakeComuniService());
            vm.ComuneSelezionato = "Milano";
            vm.ProvinciaSelezionata.Should().Be("MI");
        }

        [Fact]
        public void SetComuneFromRevit_UnknownValue_SetsWarning()
        {
            var vm = new ProjectInfoViewModel(new FakeComuniService());
            vm.SetComuneFromRevit("Atlantide");

            vm.ComuneSelezionato.Should().Be("Atlantide");
            vm.ComuneWarning.Should().Contain("non trovato");
        }

        [Fact]
        public void SetComuneFromRevit_KnownValue_NoWarning()
        {
            var vm = new ProjectInfoViewModel(new FakeComuniService());
            vm.SetComuneFromRevit("Firenze");

            vm.ComuneSelezionato.Should().Be("Firenze");
            vm.ComuneWarning.Should().BeEmpty();
            vm.ProvinciaSelezionata.Should().Be("FI");
        }
    }
}
```

- [ ] **Step 2: Refactor `ProjectInfoViewModel` — rimuovi `Comune` string, aggiungi cascata**

In `QtoRevitPlugin/UI/ViewModels/ProjectInfoViewModel.cs`:

1. Aggiungi using `System.Collections.ObjectModel;` e `QtoRevitPlugin.Services;` se mancanti.

2. Rimuovi (o lascia `[System.Obsolete]`) le property:
```csharp
[ObservableProperty] private string _comune = "";
[ObservableProperty] private string _provincia = "";
```

3. Aggiungi:

```csharp
private readonly ComuniService? _comuniService;

[ObservableProperty] private string _provinciaSelezionata = "";
[ObservableProperty] private string _comuneSelezionato = "";
[ObservableProperty] private string _comuneWarning = "";
[ObservableProperty] private bool _isComuneEnabled = false;

public ObservableCollection<string> Province { get; } = new ObservableCollection<string>();
public ObservableCollection<string> ComuniFiltrati { get; } = new ObservableCollection<string>();

public ProjectInfoViewModel(ComuniService comuniService) : this()
{
    _comuniService = comuniService;
    foreach (var p in _comuniService.GetProvince())
        Province.Add(p);
}

partial void OnProvinciaSelezionataChanged(string value)
{
    ComuniFiltrati.Clear();
    IsComuneEnabled = !string.IsNullOrEmpty(value);
    if (!IsComuneEnabled || _comuniService == null) return;
    foreach (var c in _comuniService.GetComuniByProvincia(value))
        ComuniFiltrati.Add(c);
    if (!ComuniFiltrati.Contains(ComuneSelezionato))
        ComuneSelezionato = "";
}

partial void OnComuneSelezionatoChanged(string value)
{
    if (string.IsNullOrEmpty(value) || _comuniService == null) return;
    var prov = _comuniService.GetProvinciaByComune(value);
    if (prov != null && prov != ProvinciaSelezionata)
        ProvinciaSelezionata = prov;
    ComuneWarning = "";
}

/// <summary>Setter usato dal flusso Eredita da Revit: non valida contro DB, ma mostra warning se il valore non è canonico.</summary>
public void SetComuneFromRevit(string rawValue)
{
    ComuneSelezionato = rawValue;
    if (_comuniService == null) return;
    ComuneWarning = _comuniService.ComuneExists(rawValue)
        ? ""
        : $"⚠ \"{rawValue}\" non trovato nel DB comuni — verifica la grafia";
}
```

4. Se `Load(ProjectInfo)` esiste, aggiungi mapping `ComuneSelezionato = info.Comune; ProvinciaSelezionata = info.Provincia;` e in `ToProjectInfo()` aggiorna `Comune = ComuneSelezionato; Provincia = ProvinciaSelezionata;`.

5. Il constructor senza parametri (usato da XAML designer) deve restare (`public ProjectInfoViewModel() {}`) così il DataContext da XAML non rompe.

- [ ] **Step 3: Esegui test — aspettati PASS**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj" --filter "FullyQualifiedName~ProjectInfoViewModelCascate"
```

Expected: 5 passed.

- [ ] **Step 4: Commit**

```bash
git add "QtoRevitPlugin/UI/ViewModels/ProjectInfoViewModel.cs" "QtoRevitPlugin.Tests/Informazioni/ProjectInfoViewModelCascateTests.cs"
git commit -m "feat(infoproj v2 T6): cascata Provincia/Comune in ProjectInfoViewModel + 5 test"
```

---

## Task 7: `ProjectInfoView.xaml` — sostituisci TextBox con ComboBox cascata

**Files:**
- Modify: `QtoRevitPlugin/UI/Views/ProjectInfoView.xaml`

- [ ] **Step 1: Leggi il blocco attuale `Comune`/`Provincia`**

Apri `QtoRevitPlugin/UI/Views/ProjectInfoView.xaml` e individua le due TextBox (Comune + Provincia MaxLength=2).

- [ ] **Step 2: Sostituisci con ComboBox + warning TextBlock**

Rimpiazza il gruppo `<TextBlock Text="Provincia"/><TextBox ...MaxLength="2".../>` e `<TextBlock Text="Comune"/><TextBox .../>` con:

```xml
<StackPanel Grid.Row="X" Grid.Column="0" Margin="0,4,8,0">
    <TextBlock Text="Provincia" FontSize="11" Foreground="{DynamicResource InkMutedBrush}"/>
    <ComboBox IsEditable="True" IsTextSearchEnabled="True"
              ItemsSource="{Binding Province}"
              SelectedItem="{Binding ProvinciaSelezionata, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
              Text="{Binding ProvinciaSelezionata, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
              FontSize="11"/>
</StackPanel>
<StackPanel Grid.Row="X" Grid.Column="1" Margin="0,4,0,0">
    <TextBlock Text="Comune" FontSize="11" Foreground="{DynamicResource InkMutedBrush}"/>
    <ComboBox IsEditable="True" IsTextSearchEnabled="True"
              ItemsSource="{Binding ComuniFiltrati}"
              SelectedItem="{Binding ComuneSelezionato, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
              Text="{Binding ComuneSelezionato, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
              IsEnabled="{Binding IsComuneEnabled}"
              FontSize="11"/>
    <TextBlock Text="{Binding ComuneWarning}" FontSize="10" FontStyle="Italic"
               Foreground="#D17F00" Margin="0,2,0,0"
               Visibility="{Binding ComuneWarning, Converter={x:Static vm:NonEmptyToVisibilityConverter.Instance}}"/>
</StackPanel>
```

Sostituisci `X` con il `Grid.Row` originale dei due campi.

- [ ] **Step 3: Aggiungi converter `NonEmptyToVisibilityConverter` se non esiste**

Crea `QtoRevitPlugin/UI/ViewModels/NonEmptyToVisibilityConverter.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>Converter WPF: stringa non vuota → Visible, altrimenti Collapsed.</summary>
    public class NonEmptyToVisibilityConverter : IValueConverter
    {
        public static readonly NonEmptyToVisibilityConverter Instance = new NonEmptyToVisibilityConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
```

- [ ] **Step 4: Build XAML**

```bash
dotnet build "QtoRevitPlugin/QtoRevitPlugin.csproj" -c "Debug R25"
```

Expected: 0 errors, 0 XAML warnings nuovi.

- [ ] **Step 5: Commit**

```bash
git add "QtoRevitPlugin/UI/Views/ProjectInfoView.xaml" "QtoRevitPlugin/UI/ViewModels/NonEmptyToVisibilityConverter.cs"
git commit -m "feat(infoproj v2 T7): ComboBox cascata Provincia/Comune in ProjectInfoView"
```

---

## Task 8: Model + Schema `RevitParamMapping` + Field Keys

**Files:**
- Create: `QtoRevitPlugin.Core/Models/RevitParamMapping.cs`
- Create: `QtoRevitPlugin.Core/Models/ProjectInfoFieldKeys.cs`

- [ ] **Step 1: Crea model `RevitParamMapping`**

Crea `QtoRevitPlugin.Core/Models/RevitParamMapping.cs`:

```csharp
namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Associazione FieldKey (es. "Committente") → ParamName Revit (es. "ClientName").
    /// Persistita nella tabella RevitParamMapping del .cme, key (SessionId, FieldKey).
    /// <para><see cref="ParamName"/> null = "nessun mapping" (il bottone Eredita salterà il campo).</para>
    /// </summary>
    public class RevitParamMapping
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public string FieldKey { get; set; } = "";
        public string? ParamName { get; set; }
        public bool IsBuiltIn { get; set; }
        /// <summary>True = non sovrascrivere il campo se già valorizzato (default).</summary>
        public bool SkipIfFilled { get; set; } = true;
    }
}
```

- [ ] **Step 2: Crea costanti `ProjectInfoFieldKeys`**

Crea `QtoRevitPlugin.Core/Models/ProjectInfoFieldKeys.cs`:

```csharp
namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Identificatori stabili dei campi della scheda Informazioni Progetto.
    /// Usati come FieldKey in RevitParamMapping — NON cambiarli senza migration.
    /// </summary>
    public static class ProjectInfoFieldKeys
    {
        public const string DenominazioneOpera   = "DenominazioneOpera";
        public const string Committente          = "Committente";
        public const string Impresa              = "Impresa";
        public const string Rup                  = "RUP";
        public const string DirettoreLavori      = "DirettoreLavori";
        public const string Luogo                = "Luogo";
        public const string Comune               = "Comune";
        public const string Provincia            = "Provincia";
        public const string Cig                  = "CIG";
        public const string Cup                  = "CUP";
        public const string RiferimentoPrezzario = "RiferimentoPrezzario";

        public static readonly string[] All = new[]
        {
            DenominazioneOpera, Committente, Impresa, Rup, DirettoreLavori,
            Luogo, Comune, Provincia, Cig, Cup, RiferimentoPrezzario
        };

        /// <summary>Etichetta UI human-readable per FieldKey.</summary>
        public static string DisplayNameFor(string fieldKey) => fieldKey switch
        {
            DenominazioneOpera   => "Denominazione opera",
            Committente          => "Committente",
            Impresa              => "Impresa appaltatrice",
            Rup                  => "RUP",
            DirettoreLavori      => "Direttore dei Lavori",
            Luogo                => "Luogo (via/piazza)",
            Comune               => "Comune",
            Provincia            => "Provincia",
            Cig                  => "CIG",
            Cup                  => "CUP",
            RiferimentoPrezzario => "Riferimento prezzario",
            _                    => fieldKey
        };
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build "QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj" -c Debug
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add "QtoRevitPlugin.Core/Models/RevitParamMapping.cs" "QtoRevitPlugin.Core/Models/ProjectInfoFieldKeys.cs"
git commit -m "feat(infoproj v2 T8): model RevitParamMapping + ProjectInfoFieldKeys"
```

---

## Task 9: Repository CRUD `RevitParamMapping`

**Files:**
- Modify: `QtoRevitPlugin.Core/Data/IQtoRepository.cs`
- Modify: `QtoRevitPlugin.Core/Data/QtoRepository.cs`
- Create: `QtoRevitPlugin.Tests/Informazioni/RevitParamMappingRepositoryTests.cs`

- [ ] **Step 1: Scrivi test PRIMA**

Crea `QtoRevitPlugin.Tests/Informazioni/RevitParamMappingRepositoryTests.cs`:

```csharp
using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Informazioni
{
    public class RevitParamMappingRepositoryTests : System.IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _sessionId;

        public RevitParamMappingRepositoryTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"cme_mapping_{System.Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new Session { Name = "Test", CreatedAt = System.DateTime.UtcNow });
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void GetMapping_EmptyBySession_ReturnsEmpty()
        {
            _repo.GetRevitParamMapping(_sessionId).Should().BeEmpty();
        }

        [Fact]
        public void UpsertMapping_InsertsNewRow()
        {
            _repo.UpsertRevitParamMapping(new RevitParamMapping
            {
                SessionId = _sessionId,
                FieldKey = ProjectInfoFieldKeys.Committente,
                ParamName = "ClientName",
                IsBuiltIn = true
            });

            var all = _repo.GetRevitParamMapping(_sessionId);
            all.Should().HaveCount(1);
            all[0].ParamName.Should().Be("ClientName");
            all[0].IsBuiltIn.Should().BeTrue();
        }

        [Fact]
        public void UpsertMapping_UpdatesExistingRow()
        {
            _repo.UpsertRevitParamMapping(new RevitParamMapping
            {
                SessionId = _sessionId, FieldKey = ProjectInfoFieldKeys.Rup,
                ParamName = "CME_RUP", IsBuiltIn = false
            });
            _repo.UpsertRevitParamMapping(new RevitParamMapping
            {
                SessionId = _sessionId, FieldKey = ProjectInfoFieldKeys.Rup,
                ParamName = "Responsabile", IsBuiltIn = false
            });

            var all = _repo.GetRevitParamMapping(_sessionId);
            all.Should().HaveCount(1);
            all[0].ParamName.Should().Be("Responsabile");
        }

        [Fact]
        public void UpsertMapping_NullParamName_MeansNoMapping()
        {
            _repo.UpsertRevitParamMapping(new RevitParamMapping
            {
                SessionId = _sessionId, FieldKey = ProjectInfoFieldKeys.Impresa,
                ParamName = null, IsBuiltIn = false
            });
            var all = _repo.GetRevitParamMapping(_sessionId);
            all.Single().ParamName.Should().BeNull();
        }

        [Fact]
        public void UpsertMapping_SkipIfFilled_PersistsFlag()
        {
            _repo.UpsertRevitParamMapping(new RevitParamMapping
            {
                SessionId = _sessionId, FieldKey = ProjectInfoFieldKeys.Luogo,
                ParamName = "Address", IsBuiltIn = true, SkipIfFilled = false
            });
            var all = _repo.GetRevitParamMapping(_sessionId);
            all.Single().SkipIfFilled.Should().BeFalse();

            _repo.UpsertRevitParamMapping(new RevitParamMapping
            {
                SessionId = _sessionId, FieldKey = ProjectInfoFieldKeys.Luogo,
                ParamName = "Address", IsBuiltIn = true, SkipIfFilled = true
            });
            _repo.GetRevitParamMapping(_sessionId).Single().SkipIfFilled.Should().BeTrue();
        }
    }
}
```

- [ ] **Step 2: Aggiungi firme IQtoRepository**

In `QtoRevitPlugin.Core/Data/IQtoRepository.cs`:

```csharp
// Revit param mapping (v9): persistenza scelte dialog Eredita da Revit.
System.Collections.Generic.IReadOnlyList<RevitParamMapping> GetRevitParamMapping(int sessionId);
void UpsertRevitParamMapping(RevitParamMapping mapping);
```

- [ ] **Step 3: Run test — aspettati FAIL**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj" --filter "FullyQualifiedName~RevitParamMappingRepository"
```

Expected: FAIL.

- [ ] **Step 4: Implementa in QtoRepository**

In `QtoRevitPlugin.Core/Data/QtoRepository.cs`:

```csharp
// ── RevitParamMapping (v9) ────────────────────────────────────────

public IReadOnlyList<RevitParamMapping> GetRevitParamMapping(int sessionId)
{
    using var conn = OpenConnection();
    return conn.Query<RevitParamMapping>(
        @"SELECT Id, SessionId, FieldKey, ParamName, IsBuiltIn, SkipIfFilled
          FROM RevitParamMapping
          WHERE SessionId = @SessionId
          ORDER BY FieldKey",
        new { SessionId = sessionId })
        .ToList();
}

public void UpsertRevitParamMapping(RevitParamMapping mapping)
{
    using var conn = OpenConnection();
    conn.Execute(
        @"INSERT INTO RevitParamMapping (SessionId, FieldKey, ParamName, IsBuiltIn, SkipIfFilled)
          VALUES (@SessionId, @FieldKey, @ParamName, @IsBuiltIn, @SkipIfFilled)
          ON CONFLICT(SessionId, FieldKey) DO UPDATE SET
             ParamName = excluded.ParamName,
             IsBuiltIn = excluded.IsBuiltIn,
             SkipIfFilled = excluded.SkipIfFilled;",
        new
        {
            mapping.SessionId,
            mapping.FieldKey,
            mapping.ParamName,
            IsBuiltIn = mapping.IsBuiltIn ? 1 : 0,
            SkipIfFilled = mapping.SkipIfFilled ? 1 : 0
        });
}
```

- [ ] **Step 5: Run test — aspettati PASS**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj" --filter "FullyQualifiedName~RevitParamMappingRepository"
```

Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add "QtoRevitPlugin.Core/Data/IQtoRepository.cs" "QtoRevitPlugin.Core/Data/QtoRepository.cs" "QtoRevitPlugin.Tests/Informazioni/RevitParamMappingRepositoryTests.cs"
git commit -m "feat(infoproj v2 T9): CRUD RevitParamMapping + 4 test"
```

---

## Task 10: `RevitParamMappingService` (enumerazione parametri + default + read)

**Files:**
- Create: `QtoRevitPlugin/Services/RevitParamMappingService.cs`

- [ ] **Step 1: Crea il service**

Crea `QtoRevitPlugin/Services/RevitParamMappingService.cs`:

```csharp
using Autodesk.Revit.DB;
using QtoRevitPlugin.Models;
using System.Collections.Generic;
using System.Linq;

namespace QtoRevitPlugin.Services
{
    /// <summary>Entry riga del dropdown "Parametro Revit sorgente" nel MappingDialog.</summary>
    public class RevitParamEntry
    {
        public string DisplayName { get; set; } = "";
        public string ParamName { get; set; } = "";
        public bool IsBuiltIn { get; set; }
    }

    /// <summary>
    /// Servizio che:
    /// 1. Enumera i parametri disponibili su <c>Document.ProjectInformation</c> (BuiltIn + custom/shared).
    /// 2. Produce il mapping di default (Name→DenominazioneOpera, ClientName→Committente, ...).
    /// 3. Legge il valore di un parametro dato il ParamName salvato.
    /// </summary>
    public static class RevitParamMappingService
    {
        // Parametri BuiltIn di ProjectInformation sempre disponibili.
        public static readonly IReadOnlyList<RevitParamEntry> BuiltInEntries = new[]
        {
            new RevitParamEntry { DisplayName = "Name — Nome progetto",          ParamName = "Name",                   IsBuiltIn = true },
            new RevitParamEntry { DisplayName = "ClientName — Committente",      ParamName = "ClientName",             IsBuiltIn = true },
            new RevitParamEntry { DisplayName = "Address — Indirizzo",           ParamName = "Address",                IsBuiltIn = true },
            new RevitParamEntry { DisplayName = "BuildingName — Nome edificio",  ParamName = "BuildingName",           IsBuiltIn = true },
            new RevitParamEntry { DisplayName = "Number — Numero progetto",      ParamName = "Number",                 IsBuiltIn = true },
            new RevitParamEntry { DisplayName = "Author — Autore",               ParamName = "Author",                 IsBuiltIn = true },
            new RevitParamEntry { DisplayName = "IssueDate — Data emissione",    ParamName = "IssueDate",              IsBuiltIn = true },
            new RevitParamEntry { DisplayName = "Status — Stato",                ParamName = "Status",                 IsBuiltIn = true },
            new RevitParamEntry { DisplayName = "OrganizationName",              ParamName = "OrganizationName",       IsBuiltIn = true },
            new RevitParamEntry { DisplayName = "OrganizationDescription",       ParamName = "OrganizationDescription", IsBuiltIn = true },
        };

        /// <summary>Parametri custom/shared letti dal documento corrente (filtro StorageType=String).
        /// DisplayName include suffisso "(parametro custom)" per distinguerli dai BuiltIn nel dialog.</summary>
        public static IReadOnlyList<RevitParamEntry> GetCustomParams(ProjectInfo pi)
        {
            if (pi == null) return System.Array.Empty<RevitParamEntry>();
            var builtInNames = new System.Collections.Generic.HashSet<string>(
                BuiltInEntries.Select(e => e.ParamName), System.StringComparer.OrdinalIgnoreCase);

            return pi.Parameters
                .Cast<Parameter>()
                .Where(p => !builtInNames.Contains(p.Definition.Name) && p.StorageType == StorageType.String)
                .Select(p => new RevitParamEntry
                {
                    DisplayName = $"{p.Definition.Name}  (parametro custom)",
                    ParamName = p.Definition.Name,
                    IsBuiltIn = false
                })
                .OrderBy(e => e.DisplayName, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Ritorna BuiltIn + Custom in un'unica lista già ordinata (BuiltIn prima, custom ordinati alfabeticamente).
        /// Usato dal MappingDialog come ItemsSource unico.</summary>
        public static IReadOnlyList<RevitParamEntry> GetAllParams(ProjectInfo pi) =>
            BuiltInEntries.Concat(GetCustomParams(pi)).ToList();

        /// <summary>Legge il valore di un parametro dato ParamName salvato e flag IsBuiltIn.</summary>
        public static string? ReadValue(ProjectInfo pi, string paramName, bool isBuiltIn)
        {
            if (pi == null || string.IsNullOrEmpty(paramName)) return null;
            if (isBuiltIn)
            {
                return paramName switch
                {
                    "Name"                    => pi.Name,
                    "ClientName"              => pi.ClientName,
                    "Address"                 => pi.Address,
                    "BuildingName"            => pi.BuildingName,
                    "Number"                  => pi.Number,
                    "Author"                  => pi.Author,
                    "IssueDate"               => pi.IssueDate,
                    "Status"                  => pi.Status,
                    "OrganizationName"        => pi.OrganizationName,
                    "OrganizationDescription" => pi.OrganizationDescription,
                    _                         => null
                };
            }
            var p = pi.LookupParameter(paramName);
            if (p == null || !p.HasValue) return null;
            return p.AsString() ?? p.AsValueString();
        }

        /// <summary>Mapping di default quando l'utente non ha ancora salvato nulla.</summary>
        public static IReadOnlyList<RevitParamMapping> GetDefaultMapping(int sessionId) => new[]
        {
            New(sessionId, ProjectInfoFieldKeys.DenominazioneOpera,   "Name",        true),
            New(sessionId, ProjectInfoFieldKeys.Committente,          "ClientName",  true),
            New(sessionId, ProjectInfoFieldKeys.Luogo,                "Address",     true),
            New(sessionId, ProjectInfoFieldKeys.Rup,                  "CME_RUP",     false),
            New(sessionId, ProjectInfoFieldKeys.DirettoreLavori,      "CME_DL",      false),
            New(sessionId, ProjectInfoFieldKeys.Impresa,              "CME_Impresa", false),
            New(sessionId, ProjectInfoFieldKeys.Cig,                  "CME_CIG",     false),
            New(sessionId, ProjectInfoFieldKeys.Cup,                  "CME_CUP",     false),
            New(sessionId, ProjectInfoFieldKeys.Comune,               "CME_Comune",  false),
            New(sessionId, ProjectInfoFieldKeys.Provincia,            "CME_Provincia", false),
            New(sessionId, ProjectInfoFieldKeys.RiferimentoPrezzario, null,           false),
        };

        private static RevitParamMapping New(int sid, string key, string? param, bool bi) =>
            new RevitParamMapping { SessionId = sid, FieldKey = key, ParamName = param, IsBuiltIn = bi };
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build "QtoRevitPlugin/QtoRevitPlugin.csproj" -c "Debug R25"
```

Expected: 0 errors (cross-target: il `LookupParameter` funziona su net48 e net8).

- [ ] **Step 3: Commit**

```bash
git add "QtoRevitPlugin/Services/RevitParamMappingService.cs"
git commit -m "feat(infoproj v2 T10): RevitParamMappingService + default mapping"
```

---

## Task 11: `RevitMappingDialog` (XAML + ViewModel)

**Files:**
- Create: `QtoRevitPlugin/UI/Views/RevitMappingDialog.xaml`
- Create: `QtoRevitPlugin/UI/Views/RevitMappingDialog.xaml.cs`
- Create: `QtoRevitPlugin/UI/ViewModels/RevitMappingDialogViewModel.cs`

- [ ] **Step 1: Crea ViewModel del dialog**

Crea `QtoRevitPlugin/UI/ViewModels/RevitMappingDialogViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>Riga del DataGrid: un campo Informazioni Progetto + il ParamName scelto.</summary>
    public partial class MappingRowVm : ObservableObject
    {
        public string FieldKey { get; }
        public string FieldDisplay { get; }
        [ObservableProperty] private RevitParamEntry? _selectedSource;

        public MappingRowVm(string fieldKey, RevitParamEntry? selected)
        {
            FieldKey = fieldKey;
            FieldDisplay = ProjectInfoFieldKeys.DisplayNameFor(fieldKey);
            _selectedSource = selected;
        }
    }

    public partial class RevitMappingDialogViewModel : ObservableObject
    {
        /// <summary>Entry "(nessuno)" sempre in testa alle sorgenti disponibili.</summary>
        private static readonly RevitParamEntry NoneEntry = new RevitParamEntry
        { DisplayName = "(nessuno)", ParamName = "", IsBuiltIn = false };

        public ObservableCollection<RevitParamEntry> AvailableSources { get; } = new ObservableCollection<RevitParamEntry>();
        public ObservableCollection<MappingRowVm> Rows { get; } = new ObservableCollection<MappingRowVm>();

        [ObservableProperty] private bool _noOverwriteFilledFields = true;

        /// <summary>Risultato del dialog: "Accept" esegue eredita + salva, "SaveOnly" salva mappatura senza eseguire, "Cancel" annulla.</summary>
        public enum DialogOutcome { Cancel, SaveOnly, Accept }

        [ObservableProperty] private DialogOutcome _result = DialogOutcome.Cancel;

        public RevitMappingDialogViewModel(
            IReadOnlyList<RevitParamEntry> builtIn,
            IReadOnlyList<RevitParamEntry> custom,
            IReadOnlyList<RevitParamMapping> savedMappings)
        {
            AvailableSources.Add(NoneEntry);
            foreach (var e in builtIn) AvailableSources.Add(e);
            foreach (var e in custom) AvailableSources.Add(e);

            // Se una qualsiasi riga salvata ha SkipIfFilled=false, rifletti nella checkbox globale (UX single-toggle).
            if (savedMappings.Any() && savedMappings.All(m => !m.SkipIfFilled))
                NoOverwriteFilledFields = false;

            foreach (var fk in ProjectInfoFieldKeys.All)
            {
                var saved = savedMappings.FirstOrDefault(m => m.FieldKey == fk);
                RevitParamEntry? selected = saved?.ParamName == null
                    ? NoneEntry
                    : AvailableSources.FirstOrDefault(e =>
                        e.ParamName == saved.ParamName && e.IsBuiltIn == saved.IsBuiltIn)
                      ?? NoneEntry;
                Rows.Add(new MappingRowVm(fk, selected));
            }
        }

        [RelayCommand]
        private void Accept(System.Windows.Window window)
        {
            Result = DialogOutcome.Accept;
            window.DialogResult = true;
            window.Close();
        }

        [RelayCommand]
        private void SaveOnly(System.Windows.Window window)
        {
            Result = DialogOutcome.SaveOnly;
            window.DialogResult = true;
            window.Close();
        }

        [RelayCommand]
        private void Cancel(System.Windows.Window window)
        {
            Result = DialogOutcome.Cancel;
            window.DialogResult = false;
            window.Close();
        }

        /// <summary>Converte lo stato UI in mapping persistibili. Il flag <c>NoOverwriteFilledFields</c>
        /// viene applicato a tutte le righe (UX a checkbox singola).</summary>
        public IReadOnlyList<RevitParamMapping> BuildMappings(int sessionId) =>
            Rows.Select(r => new RevitParamMapping
            {
                SessionId = sessionId,
                FieldKey = r.FieldKey,
                ParamName = string.IsNullOrEmpty(r.SelectedSource?.ParamName) ? null : r.SelectedSource!.ParamName,
                IsBuiltIn = r.SelectedSource?.IsBuiltIn ?? false,
                SkipIfFilled = NoOverwriteFilledFields
            }).ToList();
    }
}
```

- [ ] **Step 2: Crea XAML del dialog**

Crea `QtoRevitPlugin/UI/Views/RevitMappingDialog.xaml`:

```xml
<Window x:Class="QtoRevitPlugin.UI.Views.RevitMappingDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="📥 Eredita da Revit — Mappatura parametri"
        Height="560" Width="720"
        WindowStartupLocation="CenterOwner"
        ResizeMode="CanResizeWithGrip">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <TextBlock Grid.Row="0"
                   Text="Scegli da quale parametro Revit ereditare ogni campo. La mappatura viene salvata nel .cme corrente."
                   TextWrapping="Wrap" Margin="0,0,0,10"/>

        <DataGrid Grid.Row="1" ItemsSource="{Binding Rows}"
                  AutoGenerateColumns="False" CanUserAddRows="False" CanUserDeleteRows="False"
                  HeadersVisibility="Column" RowHeight="30">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Campo QTO" Binding="{Binding FieldDisplay}" IsReadOnly="True" Width="220"/>
                <DataGridTemplateColumn Header="Parametro Revit sorgente" Width="*">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <ComboBox ItemsSource="{Binding DataContext.AvailableSources, RelativeSource={RelativeSource AncestorType=Window}}"
                                      SelectedItem="{Binding SelectedSource, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                      DisplayMemberPath="DisplayName"/>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
            </DataGrid.Columns>
        </DataGrid>

        <CheckBox Grid.Row="2" Content="Non sovrascrivere campi già compilati"
                  IsChecked="{Binding NoOverwriteFilledFields, Mode=TwoWay}"
                  Margin="0,8,0,0"/>

        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,10,0,0">
            <Button Content="Annulla" Width="90" Margin="0,0,8,0"
                    Command="{Binding CancelCommand}"
                    CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"/>
            <Button Content="💾 Salva mappatura" Width="150" Margin="0,0,8,0"
                    ToolTip="Salva la mappatura senza eseguire l'import dei valori"
                    Command="{Binding SaveOnlyCommand}"
                    CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"/>
            <Button Content="✅ Eredita" Width="110" IsDefault="True"
                    ToolTip="Salva la mappatura ed esegue l'import dei valori dai parametri Revit"
                    Command="{Binding AcceptCommand}"
                    CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 3: Crea codebehind**

Crea `QtoRevitPlugin/UI/Views/RevitMappingDialog.xaml.cs`:

```csharp
using QtoRevitPlugin.UI.ViewModels;
using System.Windows;

namespace QtoRevitPlugin.UI.Views
{
    public partial class RevitMappingDialog : Window
    {
        public RevitMappingDialogViewModel VmTyped => (RevitMappingDialogViewModel)DataContext;

        public RevitMappingDialog(RevitMappingDialogViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
```

- [ ] **Step 4: Build**

```bash
dotnet build "QtoRevitPlugin/QtoRevitPlugin.csproj" -c "Debug R25"
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add "QtoRevitPlugin/UI/Views/RevitMappingDialog.xaml" "QtoRevitPlugin/UI/Views/RevitMappingDialog.xaml.cs" "QtoRevitPlugin/UI/ViewModels/RevitMappingDialogViewModel.cs"
git commit -m "feat(infoproj v2 T11): RevitMappingDialog modale con DataGrid mappatura"
```

---

## Task 12: Integrazione pulsante "Eredita da Revit"

**Files:**
- Modify: `QtoRevitPlugin/UI/Views/ProjectInfoView.xaml.cs`
- Modify: `QtoRevitPlugin/UI/Views/ProjectInfoView.xaml` (solo aggiunta bottone ⚙)

- [ ] **Step 1: XAML — aggiungi bottone ⚙ accanto a "Eredita da Revit"**

In `ProjectInfoView.xaml`, trova il Button "Eredita da Revit" (cerca `OnImportFromRevitClick`). Wrappalo in uno StackPanel orizzontale aggiungendo accanto il bottone config:

```xml
<StackPanel Orientation="Horizontal">
    <Button Content="📥 Eredita da Revit" Click="OnImportFromRevitClick" Padding="10,4" Margin="0,0,4,0"/>
    <Button Content="⚙" Click="OnConfigureMappingClick" Padding="6,4" ToolTip="Configura mappatura parametri"/>
</StackPanel>
```

- [ ] **Step 2: Codebehind — sostituisci `OnImportFromRevitClick` con flusso configurabile**

In `QtoRevitPlugin/UI/Views/ProjectInfoView.xaml.cs`, sostituisci il metodo `OnImportFromRevitClick` esistente con:

```csharp
private void OnImportFromRevitClick(object sender, RoutedEventArgs e)
{
    // Se SHIFT è premuto → forza apertura dialog
    bool forceDialog = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
    InheritFromRevit(forceDialog);
}

private void OnConfigureMappingClick(object sender, RoutedEventArgs e)
{
    InheritFromRevit(forceDialog: true);
}

private void InheritFromRevit(bool forceDialog)
{
    if (!(DataContext is ProjectInfoViewModel vm)) return;
    var uiApp = QtoRevitPlugin.Application.QtoApplication.Instance?.UiApplication;
    var doc = uiApp?.ActiveUIDocument?.Document;
    var pi = doc?.ProjectInformation;
    if (pi == null)
    {
        TaskDialog.Show("Informazioni", "Nessun documento Revit attivo.");
        return;
    }

    var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
    var repo = QtoApplication.Instance?.SessionManager?.Repository;
    if (session == null || repo == null) return;

    var saved = repo.GetRevitParamMapping(session.Id);
    bool noMappingYet = saved.Count == 0;

    // Se non c'è mapping salvato → default precompilato
    if (noMappingYet) saved = RevitParamMappingService.GetDefaultMapping(session.Id);

    // Apri dialog se forzato oppure se è la prima volta
    bool executeImport;
    if (forceDialog || noMappingYet)
    {
        var dialogVm = new RevitMappingDialogViewModel(
            RevitParamMappingService.BuiltInEntries,
            RevitParamMappingService.GetCustomParams(pi),
            saved);
        var dialog = new RevitMappingDialog(dialogVm) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return; // Cancel

        // Persisti sempre il mapping scelto (sia su "Salva mappatura" sia su "Eredita")
        var newMappings = dialogVm.BuildMappings(session.Id);
        foreach (var m in newMappings) repo.UpsertRevitParamMapping(m);
        saved = newMappings;

        // Esegue l'import solo se l'utente ha cliccato "Eredita" (non "Salva mappatura")
        executeImport = dialogVm.Result == RevitMappingDialogViewModel.DialogOutcome.Accept;
        if (!executeImport)
        {
            TaskDialog.Show("Eredita da Revit", "Mappatura salvata. Clicca di nuovo \"Eredita\" per applicare i valori.");
            return;
        }
    }
    else
    {
        executeImport = true; // click singolo con mapping già salvato
    }

    // Applica i valori — SkipIfFilled è per-riga (persistito dal dialog)
    int applied = 0, skipped = 0;
    foreach (var m in saved)
    {
        if (string.IsNullOrEmpty(m.ParamName)) continue;
        var val = RevitParamMappingService.ReadValue(pi, m.ParamName!, m.IsBuiltIn);
        if (string.IsNullOrWhiteSpace(val)) continue;
        if (ApplyFieldValue(vm, m.FieldKey, val!, m.SkipIfFilled)) applied++;
        else skipped++;
    }

    TaskDialog.Show("Eredita da Revit", $"Ereditati {applied} campi, saltati {skipped}.");
}

/// <summary>Scrive il valore sul ViewModel per il FieldKey dato. Ritorna true se applicato, false se skippato (no-overwrite + già pieno).</summary>
private static bool ApplyFieldValue(ProjectInfoViewModel vm, string fieldKey, string value, bool noOverwrite)
{
    bool IsEmpty(string s) => string.IsNullOrWhiteSpace(s);
    switch (fieldKey)
    {
        case ProjectInfoFieldKeys.DenominazioneOpera:
            if (noOverwrite && !IsEmpty(vm.DenominazioneOpera)) return false;
            vm.DenominazioneOpera = value; return true;
        case ProjectInfoFieldKeys.Committente:
            if (noOverwrite && !IsEmpty(vm.Committente)) return false;
            vm.Committente = value; return true;
        case ProjectInfoFieldKeys.Impresa:
            if (noOverwrite && !IsEmpty(vm.Impresa)) return false;
            vm.Impresa = value; return true;
        case ProjectInfoFieldKeys.Rup:
            if (noOverwrite && !IsEmpty(vm.Rup)) return false;
            vm.Rup = value; return true;
        case ProjectInfoFieldKeys.DirettoreLavori:
            if (noOverwrite && !IsEmpty(vm.DirettoreLavori)) return false;
            vm.DirettoreLavori = value; return true;
        case ProjectInfoFieldKeys.Luogo:
            if (noOverwrite && !IsEmpty(vm.Luogo)) return false;
            vm.Luogo = value; return true;
        case ProjectInfoFieldKeys.Comune:
            if (noOverwrite && !IsEmpty(vm.ComuneSelezionato)) return false;
            vm.SetComuneFromRevit(value); return true; // warning soft se non in DB
        case ProjectInfoFieldKeys.Provincia:
            if (noOverwrite && !IsEmpty(vm.ProvinciaSelezionata)) return false;
            vm.ProvinciaSelezionata = value; return true;
        case ProjectInfoFieldKeys.Cig:
            if (noOverwrite && !IsEmpty(vm.Cig)) return false;
            vm.Cig = value; return true;
        case ProjectInfoFieldKeys.Cup:
            if (noOverwrite && !IsEmpty(vm.Cup)) return false;
            vm.Cup = value; return true;
        case ProjectInfoFieldKeys.RiferimentoPrezzario:
            if (noOverwrite && !IsEmpty(vm.RiferimentoPrezzario)) return false;
            vm.RiferimentoPrezzario = value; return true;
        default: return false;
    }
}
```

Aggiungi using mancanti in cima al file:
```csharp
using Autodesk.Revit.UI;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using QtoRevitPlugin.UI.ViewModels;
using System.Windows.Input;
```

Rimuovi il vecchio metodo `TryImportCustomParam` (non più usato).

- [ ] **Step 3: Build**

```bash
dotnet build "QtoRevitPlugin/QtoRevitPlugin.csproj" -c "Debug R25"
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add "QtoRevitPlugin/UI/Views/ProjectInfoView.xaml" "QtoRevitPlugin/UI/Views/ProjectInfoView.xaml.cs"
git commit -m "feat(infoproj v2 T12): bottone Eredita configurabile (dialog su SHIFT o ⚙)"
```

---

## Task 13: Wire-up `ComuniService` nel DataContext di `ProjectInfoView`

**Files:**
- Modify: `QtoRevitPlugin/UI/Views/SetupView.xaml.cs` (o dove viene creato il DataContext di ProjectInfoView)
- Modify: `QtoRevitPlugin/UI/Views/ProjectInfoView.xaml` (rimuovi `DataContext` automatico se presente)

- [ ] **Step 1: Trova dove viene creato `ProjectInfoViewModel`**

```bash
grep -rn "new ProjectInfoViewModel" "QtoRevitPlugin/"
```

Può essere in `SetupView.xaml.cs`, in un DataTemplate nel XAML, o nel constructor di `ProjectInfoView.xaml.cs`.

- [ ] **Step 2: Modifica la creazione per iniettare `ComuniService`**

Nel punto individuato al Step 1, sostituisci:
```csharp
new ProjectInfoViewModel()
```
con:
```csharp
new ProjectInfoViewModel(
    new ComuniService(QtoApplication.Instance.UserLibraryManager.Repository))
```

Aggiungi using `using QtoRevitPlugin.Services;` se manca.

Se la creazione avviene in XAML (es. `<vm:ProjectInfoViewModel/>` come DataContext implicito), spostala in code-behind così da poter iniettare il service. In `ProjectInfoView.xaml.cs`:

```csharp
public ProjectInfoView()
{
    InitializeComponent();
    var repo = QtoRevitPlugin.Application.QtoApplication.Instance?.UserLibraryManager?.Repository;
    if (repo != null)
        DataContext = new ProjectInfoViewModel(new QtoRevitPlugin.Services.ComuniService(repo));
    else
        DataContext = new ProjectInfoViewModel(); // fallback designer
}
```

E nel XAML rimuovi `<UserControl.DataContext><vm:ProjectInfoViewModel/></UserControl.DataContext>` se presente.

- [ ] **Step 3: Verifica che `QtoApplication.UserLibraryManager.Repository` esponga `IQtoRepository`**

```bash
grep -n "UserLibraryManager" "QtoRevitPlugin/Application/QtoApplication.cs"
grep -n "Repository" "QtoRevitPlugin/Services/UserLibraryManager.cs"
```

Se il field è privato, aggiungi una property pubblica:
```csharp
public IQtoRepository Repository => _repo; // in UserLibraryManager
```

- [ ] **Step 4: Build**

```bash
dotnet build "QtoRevitPlugin/QtoRevitPlugin.csproj" -c "Debug R25"
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add "QtoRevitPlugin/UI/Views/ProjectInfoView.xaml" "QtoRevitPlugin/UI/Views/ProjectInfoView.xaml.cs" "QtoRevitPlugin/Services/UserLibraryManager.cs"
git commit -m "feat(infoproj v2 T13): wire-up ComuniService in ProjectInfoView DataContext"
```

---

## Task 14: Build finale + full suite

- [ ] **Step 1: Run full test suite in Debug**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj"
```

Expected: baseline (175 pre-feature) + 16 nuovi test (6 comuni + 5 mapping + 5 cascata VM) = **191 passed**, 0 failed.

- [ ] **Step 2: Build Core Release**

```bash
dotnet build "QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj" -c Release
```

Expected: 0 errors.

- [ ] **Step 3: Build plugin Debug R24 (cross-target net48)**

```bash
dotnet build "QtoRevitPlugin/QtoRevitPlugin.csproj" -c "Debug R24"
```

Expected: 0 errors (verifica che `LookupParameter`, `ProjectInfo.Parameters.Cast<Parameter>()` compilino anche su net48).

- [ ] **Step 4: Commit finale**

```bash
git commit --allow-empty -m "chore(infoproj v2): build finale verificato - 190 test passing, cross-target OK"
```

- [ ] **Step 5: Verifica manuale in Revit (separata)**

Eseguita dall'utente con Visual Studio:

```
msbuild QtoRevitPlugin.sln /p:Configuration="Debug R25" /t:Build
```

Poi in Revit 2025:
1. Apri un modello con `ProjectInformation` con parametri custom (`CME_RUP`, `CME_Comune`, ecc.)
2. Apri la scheda Informazioni Progetto
3. Verifica che la ComboBox Provincia contenga le sigle (dovrebbe essere almeno FI/MI/RM/NA/TO con il dataset minimo)
4. Seleziona "FI" → Comune si popola con Firenze/Empoli/…
5. Click "📥 Eredita da Revit" (prima volta) → si apre il dialog mappatura con default precompilati
6. Clicca "Eredita" → i campi vengono popolati
7. Click "📥" una seconda volta → **non** apre il dialog, eredita diretto (no-overwrite ON)
8. Click "⚙" → apre il dialog di configurazione
9. Cambia un mapping (es. "Committente" → un param custom), salva, eredita → valore nuovo applicato
10. Verifica che digitando un Comune inesistente (via SetComuneFromRevit) appaia il warning arancione

---

## Note implementative cross-target

### Dipendenze
- Nessuna nuova dipendenza NuGet — tutto con API già disponibili (Dapper, SQLite, CommunityToolkit.Mvvm, Revit API).

### Cross-target compatibility
- `RevitParamMappingService` usa `Cast<Parameter>()` e `LookupParameter` — disponibili su net48+.
- `switch expression` (C# 8+) è supportato sia su net48 (con LangVersion≥8) sia su net8. Verifica `<LangVersion>` nel `.csproj`.
- CSV parsing manuale (Split) — nessuna dipendenza da `CsvHelper`, per non inquinare il Core.

### Threading
- `UpsertRevitParamMapping` è chiamato dal thread UI dopo il dialog — safe (transazione SQLite sincrona).
- `ComuniService.GetComuniByProvincia` su 7.900 righe è ~30ms — OK sul thread UI, no debounce necessario.

### UX & data flow
- **Prima apertura .cme nuovo**: `RevitParamMapping` vuoto → click Eredita apre il dialog con default precompilati → salva = persiste.
- **Apertura successiva**: click Eredita riusa mapping salvato (no dialog).
- **Forzatura dialog**: SHIFT+click oppure tasto ⚙.
- **No-overwrite checkbox ON by default**: appaga casi di aggiornamento parziale senza perdere input manuale.

### Test coverage target
191 test passing: 175 baseline + 16 nuovi:
- 6 `ComuniRepositoryTests`
- 5 `RevitParamMappingRepositoryTests` (include test per `SkipIfFilled`)
- 5 `ProjectInfoViewModelCascateTests`

I test di `RevitMappingDialog` (UI puro) e di `RevitParamMappingService.GetCustomParams` (richiede Revit API doc) sono verificati manualmente.

### Discrepanze risolte rispetto alla spec originale
- **Naming colonne DB**: spec usava snake_case (`field_key`, `is_builtin`), plan usa **PascalCase** per coerenza con il resto dello schema `.cme` (`SessionId`, `ProjectInfo`, `SoaCategories`).
- **`UserLibraryInitializer`**: la spec lo nomina, ma nel codebase esiste `UserLibraryManager.cs`. Il plan estende `UserLibraryManager` con `SeedComuniFromEmbeddedResource` invece di creare una nuova classe.
- **`Query<T>`/`QueryScalar<T>` generici**: la spec li mostra nel service, ma il pattern del codebase è repository entity-specific. Il plan usa metodi dedicati (`GetProvince`, `GetComuniByProvincia`, ecc.) nel `IQtoRepository`.
- **Flag `SkipIfFilled` per-riga**: persistito in DB (spec §3.6), UI single-checkbox scrive lo stesso valore su tutte le righe al build, ma la struttura dati supporta override futuri per-campo senza migration.
- **Bottone "💾 Salva mappatura"**: presente nella spec §3.3, aggiunto al plan con l'enum `DialogOutcome { Cancel, SaveOnly, Accept }`.
