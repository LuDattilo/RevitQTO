# Listino — Fix IsActive + Voci Preferite

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sistemare il flag `IsActive` sul listino (persistenza + refresh ricerca) e aggiungere una lista "Preferiti" globale (UserLibrary) con pulsante ★ e sezione dedicata, accessibile come shortlist per tutti i computi.

**Architecture:** Due feature ortogonali in un unico plan perché condividono l'area UI (tab Listino).

- **Feature A (IsActive)**: il filtro SQL `AND pl.IsActive = 1` è già presente in tutte e 3 le query di ricerca (L1 esatta, L2 FTS5, L3 fuzzy). Il problema è alto: (1) `PriceListRow` nel VM è un DTO plain (no `INotifyPropertyChanged`) → il two-way binding sulla checkbox non notifica mai il VM; (2) `UpdatePriceListFlags` esiste nel repository ma non è chiamato da nessuno; (3) la cache fuzzy L3 non viene invalidata al toggle. Si converte `PriceListRow` a `ObservableObject`, si aggiunge un hook `OnIsActiveChanged` che chiama il repo + invalida la cache + rilancia la ricerca.

- **Feature B (Preferiti)**: nuova tabella `UserFavorites` in `UserLibrary.db` (globale, non per-sessione). Un `FavoritesRepository` metodi Add/Remove/GetAll/IsFavorite. VM espone `Favorites` ObservableCollection, `ToggleFavoriteCommand`, `UseInComputeCommand`. UI: bottone ★ nel pannello dettaglio (`Grid.Row="3"` in `SetupListinoView.xaml`), colonna stellina nella DataGrid risultati, Expander "I Miei Preferiti" sopra i risultati. Nessuno schema bump necessario se creata preventivamente come tabella v10 separata — PREFERENZA: bump schema `UserLibrary.db` a v10 con migration pulita.

**Tech Stack:** C# netstandard2.0 (Core) + net48/net8-windows (plugin), WPF + CommunityToolkit.Mvvm, SQLite + Dapper, xUnit + FluentAssertions.

---

## File Map

| File | Azione | Responsabilità |
|---|---|---|
| `QtoRevitPlugin.Core/Data/DatabaseSchema.cs` | Modifica | CurrentVersion 9→10. DDL `UserFavorites` + migration v9→v10 |
| `QtoRevitPlugin.Core/Data/DatabaseInitializer.cs` | Modifica | Block `if (dbVersion < 10)` con CREATE UserFavorites |
| `QtoRevitPlugin.Core/Models/UserFavorite.cs` | Crea | POCO: Id, PriceItemId (nullable), Code, Description, Unit, UnitPrice, ListName, ListId, AddedAt, Note |
| `QtoRevitPlugin.Core/Data/IQtoRepository.cs` | Modifica | Aggiunge `AddFavorite`, `RemoveFavorite`, `GetFavorites`, `IsFavorite` |
| `QtoRevitPlugin.Core/Data/QtoRepository.cs` | Modifica | Implementa CRUD UserFavorites |
| `QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs` | Modifica | `PriceListRow` diventa `ObservableObject`; aggiunge `Favorites`, `ToggleFavoriteCommand`, `SelectedFavorite`, `UseFavoriteInSearchCommand`; invalida cache fuzzy su IsActive toggle |
| `QtoRevitPlugin/UI/ViewModels/FavoriteRowVm.cs` | Crea | ViewModel di riga preferito (con flag `IsFavorite` osservabile per sync UI) |
| `QtoRevitPlugin/UI/Views/SetupListinoView.xaml` | Modifica | Expander preferiti + colonna ★ in DataGrid risultati + bottone ★ nel dettaglio |
| `QtoRevitPlugin/UI/ViewModels/BoolToStarConverter.cs` | Crea | Converter `true → "★", false → "☆"` per colonna DataGrid |
| `QtoRevitPlugin.Tests/Listino/UserFavoritesRepositoryTests.cs` | Crea | Test CRUD UserFavorites (add/remove idempotente, isFavorite, get ordered) |
| `QtoRevitPlugin.Tests/Listino/PriceListActiveToggleTests.cs` | Crea | Test che `GetActivePriceItems` cambia output al toggle `UpdatePriceListFlags` |
| `QtoRevitPlugin.Tests/Data/QtoRepositoryTests.cs` | Modifica | Aggiornare `Be(9)` → `Be(10)` |
| `QtoRevitPlugin.Tests/Sprint6/AuditFieldsMigrationTests.cs` | Modifica | `Assert.Equal(9` → `Assert.Equal(10` dove riferito alla versione schema |
| `QtoRevitPlugin.Tests/Sprint9/SchemaV5MigrationTests.cs` | Modifica | stessa cosa |

---

## Task 1: Schema v9→v10 — tabella UserFavorites

**Files:**
- Modify: `QtoRevitPlugin.Core/Data/DatabaseSchema.cs`
- Modify: `QtoRevitPlugin.Core/Data/DatabaseInitializer.cs`
- Modify: 3 test files che hardcodano `CurrentVersion = 9`

- [ ] **Step 1: Bumpa `CurrentVersion` a 10**

In `DatabaseSchema.cs`:
```csharp
public const int CurrentVersion = 10;
```

- [ ] **Step 2: Aggiungi DDL a `InitialStatements`**

```csharp
// v10 (Listino preferiti): lista preferiti utente in UserLibrary.db (globale).
// Vive anche nel .cme ma resta vuota (preferiti sono globali per utente).
// PriceItemId è NULL-able: se l'item viene cancellato dalla libreria, il preferito
// rimane con solo i dati storici (Code, Description, UnitPrice) per preservare
// la visibilità all'utente ("questo item non è più nel listino, rimuovi?").
@"CREATE TABLE IF NOT EXISTS UserFavorites (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    PriceItemId  INTEGER NULL,
    Code         TEXT NOT NULL,
    Description  TEXT NOT NULL DEFAULT '',
    Unit         TEXT NOT NULL DEFAULT '',
    UnitPrice    REAL NOT NULL DEFAULT 0,
    ListName     TEXT NOT NULL DEFAULT '',
    ListId       INTEGER NULL,
    AddedAt      TEXT NOT NULL,
    Note         TEXT NOT NULL DEFAULT '',
    UNIQUE(Code, ListId)
);",
@"CREATE INDEX IF NOT EXISTS idx_favorites_code ON UserFavorites(Code COLLATE NOCASE);",
```

Nota: `UNIQUE(Code, ListId)` permette di avere lo stesso Code su listini diversi (es. stesso codice DEI2024 e Regione Toscana 2025).

- [ ] **Step 3: Aggiungi costanti migration v9→v10**

Dopo `MigrateV8ToV9_CreateRevitParamMapping`:

```csharp
public const string MigrateV9ToV10_CreateUserFavorites =
    @"CREATE TABLE IF NOT EXISTS UserFavorites (
        Id           INTEGER PRIMARY KEY AUTOINCREMENT,
        PriceItemId  INTEGER NULL,
        Code         TEXT NOT NULL,
        Description  TEXT NOT NULL DEFAULT '',
        Unit         TEXT NOT NULL DEFAULT '',
        UnitPrice    REAL NOT NULL DEFAULT 0,
        ListName     TEXT NOT NULL DEFAULT '',
        ListId       INTEGER NULL,
        AddedAt      TEXT NOT NULL,
        Note         TEXT NOT NULL DEFAULT '',
        UNIQUE(Code, ListId)
    );";

public const string MigrateV9ToV10_IndexFavoritesCode =
    "CREATE INDEX IF NOT EXISTS idx_favorites_code ON UserFavorites(Code COLLATE NOCASE);";
```

- [ ] **Step 4: Registra migration in `DatabaseInitializer.MigrateIfNeeded`**

Dopo `if (dbVersion < 9) { ... }`:

```csharp
if (dbVersion < 10)
{
    // v9→v10: UserFavorites (popolata solo in UserLibrary).
    conn.Execute(DatabaseSchema.MigrateV9ToV10_CreateUserFavorites, transaction: tx);
    conn.Execute(DatabaseSchema.MigrateV9ToV10_IndexFavoritesCode, transaction: tx);
}
```

- [ ] **Step 5: Aggiorna assertion nei test**

Trova con Grep (`Be\(9\)` e `Assert\.Equal\(9`) nei test di `QtoRevitPlugin.Tests/Data/` e `Sprint6`/`Sprint9` e aggiorna a 10 (solo se il contesto è chiaramente schema version).

- [ ] **Step 6: Build + test regression**

```bash
cd "C:/Users/luigi.dattilo/OneDrive - GPA Ingegneria Srl/Documenti/RevitQTO"
dotnet build QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj -c Debug
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --filter "FullyQualifiedName~Data"
```

Expected: 0 errors, 23 passed.

- [ ] **Step 7: Commit**

```bash
git add QtoRevitPlugin.Core/Data/DatabaseSchema.cs QtoRevitPlugin.Core/Data/DatabaseInitializer.cs QtoRevitPlugin.Tests/
git commit -m "feat(listino T1): schema v10 - UserFavorites + indice su Code"
```

---

## Task 2: Model `UserFavorite` + repository CRUD

**Files:**
- Create: `QtoRevitPlugin.Core/Models/UserFavorite.cs`
- Modify: `QtoRevitPlugin.Core/Data/IQtoRepository.cs`
- Modify: `QtoRevitPlugin.Core/Data/QtoRepository.cs`
- Create: `QtoRevitPlugin.Tests/Listino/UserFavoritesRepositoryTests.cs`

- [ ] **Step 1: Crea model**

Crea `QtoRevitPlugin.Core/Models/UserFavorite.cs`:

```csharp
using System;

namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Voce preferita dell'utente, salvata nella UserLibrary.db (globale).
    /// Riflette una voce di listino al momento dell'aggiunta — se l'item
    /// originale viene cancellato, il preferito resta con i dati storici.
    /// </summary>
    public class UserFavorite
    {
        public int Id { get; set; }
        public int? PriceItemId { get; set; }
        public string Code { get; set; } = "";
        public string Description { get; set; } = "";
        public string Unit { get; set; } = "";
        public double UnitPrice { get; set; }
        public string ListName { get; set; } = "";
        public int? ListId { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
        public string Note { get; set; } = "";
    }
}
```

- [ ] **Step 2: Scrivi test PRIMA dell'implementazione**

Crea dir `QtoRevitPlugin.Tests/Listino/` e file `UserFavoritesRepositoryTests.cs`:

```csharp
using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Listino
{
    public class UserFavoritesRepositoryTests : System.IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;

        public UserFavoritesRepositoryTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"cme_fav_{System.Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void GetFavorites_EmptyByDefault()
        {
            _repo.GetFavorites().Should().BeEmpty();
        }

        [Fact]
        public void AddFavorite_InsertsRow()
        {
            _repo.AddFavorite(new UserFavorite
            {
                Code = "01.A01.001", Description = "Muro in laterizio",
                Unit = "m³", UnitPrice = 120.50, ListName = "Regione Toscana 2025", ListId = 1
            });

            var all = _repo.GetFavorites();
            all.Should().HaveCount(1);
            all[0].Code.Should().Be("01.A01.001");
            all[0].UnitPrice.Should().Be(120.50);
        }

        [Fact]
        public void AddFavorite_SameCodeAndListId_Idempotent()
        {
            var fav = new UserFavorite { Code = "A1", ListId = 1, Description = "x" };
            _repo.AddFavorite(fav);
            _repo.AddFavorite(fav); // seconda chiamata: no-op grazie a UNIQUE(Code, ListId)

            _repo.GetFavorites().Should().HaveCount(1);
        }

        [Fact]
        public void AddFavorite_SameCodeDifferentList_Allowed()
        {
            _repo.AddFavorite(new UserFavorite { Code = "A1", ListId = 1 });
            _repo.AddFavorite(new UserFavorite { Code = "A1", ListId = 2 });

            _repo.GetFavorites().Should().HaveCount(2);
        }

        [Fact]
        public void RemoveFavorite_ByIdCorrectly()
        {
            _repo.AddFavorite(new UserFavorite { Code = "A1", ListId = 1 });
            var id = _repo.GetFavorites().Single().Id;

            _repo.RemoveFavorite(id);

            _repo.GetFavorites().Should().BeEmpty();
        }

        [Fact]
        public void IsFavorite_ReturnsTrueForExistingCode()
        {
            _repo.AddFavorite(new UserFavorite { Code = "Z99", ListId = 7 });
            _repo.IsFavorite("Z99", 7).Should().BeTrue();
            _repo.IsFavorite("Z99", 8).Should().BeFalse();
            _repo.IsFavorite("Z100", 7).Should().BeFalse();
        }

        [Fact]
        public void GetFavorites_OrderedByAddedAtDesc()
        {
            _repo.AddFavorite(new UserFavorite { Code = "First", ListId = 1, AddedAt = System.DateTime.UtcNow.AddMinutes(-10) });
            _repo.AddFavorite(new UserFavorite { Code = "Last", ListId = 1, AddedAt = System.DateTime.UtcNow });

            var all = _repo.GetFavorites();
            all[0].Code.Should().Be("Last");
            all[1].Code.Should().Be("First");
        }
    }
}
```

- [ ] **Step 3: Firme in `IQtoRepository`**

Nel file `IQtoRepository.cs` dopo la sezione RevitParamMapping:

```csharp
// UserFavorites (v10 · UserLibrary.db). Lista preferiti utente globale.
System.Collections.Generic.IReadOnlyList<UserFavorite> GetFavorites();
int AddFavorite(UserFavorite fav);
void RemoveFavorite(int id);
bool IsFavorite(string code, int? listId);
```

- [ ] **Step 4: Run test — aspettati FAIL (compile error)**

```bash
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --filter "FullyQualifiedName~UserFavorites"
```

Expected: FAIL — i metodi non sono implementati.

- [ ] **Step 5: Implementa CRUD in QtoRepository**

In `QtoRepository.cs`:

```csharp
// ── UserFavorites (v10) ────────────────────────────────────────────

public IReadOnlyList<UserFavorite> GetFavorites()
{
    using var conn = OpenConnection();
    return conn.Query<UserFavorite>(
        @"SELECT Id, PriceItemId, Code, Description, Unit, UnitPrice,
                 ListName, ListId, AddedAt, Note
          FROM UserFavorites
          ORDER BY AddedAt DESC, Code")
        .ToList();
}

public int AddFavorite(UserFavorite fav)
{
    using var conn = OpenConnection();
    // INSERT OR IGNORE → idempotente grazie a UNIQUE(Code, ListId)
    conn.Execute(
        @"INSERT OR IGNORE INTO UserFavorites
          (PriceItemId, Code, Description, Unit, UnitPrice, ListName, ListId, AddedAt, Note)
          VALUES (@PriceItemId, @Code, @Description, @Unit, @UnitPrice, @ListName, @ListId, @AddedAt, @Note);",
        new
        {
            fav.PriceItemId,
            fav.Code,
            fav.Description,
            fav.Unit,
            fav.UnitPrice,
            fav.ListName,
            fav.ListId,
            AddedAt = fav.AddedAt.ToString("o"),
            fav.Note
        });

    return conn.ExecuteScalar<int>(
        "SELECT Id FROM UserFavorites WHERE Code = @Code AND IFNULL(ListId, -1) = IFNULL(@ListId, -1) LIMIT 1",
        new { fav.Code, fav.ListId });
}

public void RemoveFavorite(int id)
{
    using var conn = OpenConnection();
    conn.Execute("DELETE FROM UserFavorites WHERE Id = @Id", new { Id = id });
}

public bool IsFavorite(string code, int? listId)
{
    using var conn = OpenConnection();
    var n = conn.ExecuteScalar<int>(
        "SELECT COUNT(*) FROM UserFavorites WHERE Code = @Code AND IFNULL(ListId, -1) = IFNULL(@ListId, -1)",
        new { Code = code, ListId = listId });
    return n > 0;
}
```

- [ ] **Step 6: Run test — aspettati PASS**

```bash
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --filter "FullyQualifiedName~UserFavorites"
```

Expected: 7 passed.

- [ ] **Step 7: Commit**

```bash
git add QtoRevitPlugin.Core/Models/UserFavorite.cs QtoRevitPlugin.Core/Data/IQtoRepository.cs QtoRevitPlugin.Core/Data/QtoRepository.cs QtoRevitPlugin.Tests/Listino/UserFavoritesRepositoryTests.cs
git commit -m "feat(listino T2): model UserFavorite + CRUD repository + 7 test"
```

---

## Task 3: `PriceListRow` observable + persistenza toggle IsActive

**Files:**
- Modify: `QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs`
- Create: `QtoRevitPlugin.Tests/Listino/PriceListActiveToggleTests.cs`

Obiettivo: il toggle della checkbox IsActive nel DataGrid persiste nel DB e invalida la cache della ricerca fuzzy.

- [ ] **Step 1: Scrivi test che dimostra la persistenza**

Crea `QtoRevitPlugin.Tests/Listino/PriceListActiveToggleTests.cs`:

```csharp
using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Listino
{
    public class PriceListActiveToggleTests : System.IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;

        public PriceListActiveToggleTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"cme_active_{System.Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void UpdatePriceListFlags_DeactivatesList_ExcludesFromActiveItemsQuery()
        {
            // 2 listini, 1 voce ognuno
            var pl1 = new PriceList { Name = "L1", IsActive = true };
            var pl2 = new PriceList { Name = "L2", IsActive = true };
            _repo.InsertPriceList(pl1);
            _repo.InsertPriceList(pl2);
            _repo.InsertPriceItems(new[]
            {
                new PriceItem { Code = "A1", Description = "x", Unit = "m", UnitPrice = 1, PriceListId = pl1.Id },
                new PriceItem { Code = "A2", Description = "y", Unit = "m", UnitPrice = 2, PriceListId = pl2.Id }
            });

            _repo.GetAllActivePriceItems().Should().HaveCount(2);

            // Disattiva L2
            _repo.UpdatePriceListFlags(pl2.Id, isActive: false, priority: 0);

            _repo.GetAllActivePriceItems()
                .Should().ContainSingle()
                .Which.Code.Should().Be("A1");
        }

        [Fact]
        public void UpdatePriceListFlags_DeactivatesList_ExcludesFromCodeExactQuery()
        {
            var pl = new PriceList { Name = "L1", IsActive = true };
            _repo.InsertPriceList(pl);
            _repo.InsertPriceItems(new[]
            {
                new PriceItem { Code = "X42", Description = "x", Unit = "m", UnitPrice = 1, PriceListId = pl.Id }
            });

            _repo.FindByCodeExact("X42").Should().NotBeEmpty();

            _repo.UpdatePriceListFlags(pl.Id, isActive: false, priority: 0);

            _repo.FindByCodeExact("X42").Should().BeEmpty();
        }
    }
}
```

- [ ] **Step 2: Run test — aspettati PASS (query SQL già filtrate)**

```bash
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --filter "FullyQualifiedName~PriceListActiveToggle"
```

Expected: 2 passed — confermiamo che il filtro SQL funziona già; il problema è solo UI.

- [ ] **Step 3: Converti `PriceListRow` a `ObservableObject`**

Nel file `QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs` trova la definizione di `PriceListRow` (righe ~230-256).

Sostituiscila con (mantenendo stessi nomi proprietà per non rompere i binding):

```csharp
/// <summary>
/// Riga del DataGrid "Listini attivi" — osservabile per propagare il toggle
/// IsActive al repository (persistenza + invalidazione cache ricerca).
/// </summary>
public partial class PriceListRow : ObservableObject
{
    private readonly SetupViewModel _owner;
    public int Id { get; }
    public string Name { get; }
    public string Source { get; }
    public string Region { get; }
    public int RowCount { get; }
    public int Priority { get; }
    public string ImportedAtShort { get; }

    [ObservableProperty] private bool _isActive;

    public PriceListRow(SetupViewModel owner, PriceList model, int rowCount)
    {
        _owner = owner;
        Id = model.Id;
        Name = model.Name;
        Source = model.Source;
        Region = model.Region;
        Priority = model.Priority;
        RowCount = rowCount;
        ImportedAtShort = model.ImportedAt.ToString("yyyy-MM-dd");
        _isActive = model.IsActive; // set tramite field: non scatena OnIsActiveChanged al load
    }

    partial void OnIsActiveChanged(bool value)
    {
        _owner.OnPriceListActiveToggled(this, value);
    }
}
```

Nota: usa il constructor di `PriceListRow` e la proprietà `IsActive` con field-set iniziale (`_isActive = model.IsActive`) per evitare di scatenare il partial method durante la creazione iniziale dalla query.

Verifica il punto di creazione nel SetupViewModel (probabilmente `RefreshPriceLists`) e aggiorna l'invocazione passando `this` come `owner` e i parametri corretti:

```csharp
// in RefreshPriceLists, dove si crea PriceListRow:
new PriceListRow(this, pl, rowCount)
```

- [ ] **Step 4: Aggiungi l'hook `OnPriceListActiveToggled` nel SetupViewModel**

Aggiungi il metodo pubblico al `SetupViewModel`:

```csharp
/// <summary>
/// Invocato da PriceListRow quando la checkbox IsActive viene togglata.
/// Persiste lo stato nel repository e invalida la cache della ricerca.
/// </summary>
public void OnPriceListActiveToggled(PriceListRow row, bool newValue)
{
    try
    {
        var repo = QtoRevitPlugin.Application.QtoApplication.Instance?.SessionManager?.Repository;
        if (repo == null) return;
        repo.UpdatePriceListFlags(row.Id, newValue, row.Priority);

        // Invalida il cache fuzzy L3 (se esiste) e rilancia la ricerca corrente
        _searchService?.InvalidateFuzzyCache();
        if (!string.IsNullOrWhiteSpace(SearchQuery))
            ExecuteSearch();
    }
    catch (System.Exception ex)
    {
        QtoRevitPlugin.Services.CrashLogger.WriteException("SetupViewModel.OnPriceListActiveToggled", ex);
    }
}
```

Se `PriceItemSearchService.InvalidateFuzzyCache()` non esiste, aggiungilo al servizio (vedi Step 5).

- [ ] **Step 5: Aggiungi `InvalidateFuzzyCache` al `PriceItemSearchService`**

In `QtoRevitPlugin.Core/Search/PriceItemSearchService.cs`:

```csharp
/// <summary>
/// Svuota la cache della ricerca fuzzy L3 — da chiamare quando cambia il set
/// di listini attivi o la libreria viene modificata.
/// </summary>
public void InvalidateFuzzyCache()
{
    _fuzzyCache = null; // o la tua logica di clear della cache
}
```

Se la cache è un field diverso, usa il nome reale. Se non c'è cache (la query L3 è eseguita ogni volta), il metodo può essere vuoto con commento esplicativo.

- [ ] **Step 6: Build + test**

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -c "Debug R25"
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --filter "FullyQualifiedName~Listino"
```

Expected: 0 errors, 9 passed (7 favorites + 2 active toggle).

- [ ] **Step 7: Commit**

```bash
git add QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs QtoRevitPlugin.Core/Search/PriceItemSearchService.cs QtoRevitPlugin.Tests/Listino/PriceListActiveToggleTests.cs
git commit -m "fix(listino T3): IsActive toggle persiste nel DB e invalida cache ricerca"
```

---

## Task 4: ViewModel Preferiti — collezione, comandi, toggle

**Files:**
- Create: `QtoRevitPlugin/UI/ViewModels/FavoriteRowVm.cs`
- Modify: `QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs`

- [ ] **Step 1: Crea `FavoriteRowVm`**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// Riga UI della sezione Preferiti. Espone `IsFavorite` osservabile per
    /// sincronizzare l'icona ★/☆ nella griglia risultati di ricerca quando
    /// l'utente aggiunge/rimuove un preferito.
    /// </summary>
    public partial class FavoriteRowVm : ObservableObject
    {
        public UserFavorite Model { get; }
        public int Id => Model.Id;
        public string Code => Model.Code;
        public string Description => Model.Description;
        public string Unit => Model.Unit;
        public double UnitPrice => Model.UnitPrice;
        public string UnitPriceFormatted => Model.UnitPrice.ToString("#,##0.00 €");
        public string ListName => Model.ListName;
        public string AddedAtShort => Model.AddedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        [ObservableProperty] private bool _isFavorite = true;

        public FavoriteRowVm(UserFavorite model) => Model = model;
    }
}
```

- [ ] **Step 2: Aggiungi ObservableCollection + comandi al `SetupViewModel`**

Usings da aggiungere:
```csharp
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Models;
using System.Collections.ObjectModel;
```

Field + property:

```csharp
public ObservableCollection<FavoriteRowVm> Favorites { get; } = new ObservableCollection<FavoriteRowVm>();

[ObservableProperty] private FavoriteRowVm? _selectedFavorite;

/// <summary>
/// True se la voce selezionata nei risultati ricerca è già nei preferiti.
/// Aggiornata da OnSelectedSearchResultChanged.
/// </summary>
[ObservableProperty] private bool _isSelectedResultFavorite;
```

- [ ] **Step 3: Metodo `LoadFavorites()` invocato al refresh/iniziale**

```csharp
public void LoadFavorites()
{
    var repo = QtoRevitPlugin.Application.QtoApplication.Instance?.UserLibraryManager?.Repository;
    if (repo == null) return;

    Favorites.Clear();
    foreach (var f in repo.GetFavorites())
        Favorites.Add(new FavoriteRowVm(f));

    RefreshIsSelectedResultFavorite();
}

private void RefreshIsSelectedResultFavorite()
{
    var sel = SelectedSearchResult;
    IsSelectedResultFavorite = sel != null &&
        Favorites.Any(f => f.Code == sel.Code && f.Model.ListId == sel.ListId);
}

partial void OnSelectedSearchResultChanged(PriceItemRow? value)
    => RefreshIsSelectedResultFavorite();
```

Chiama `LoadFavorites()` dal constructor (dopo l'inizializzazione attuale) o da `RefreshPriceLists()`.

- [ ] **Step 4: Comando `ToggleFavorite`**

```csharp
[RelayCommand]
private void ToggleFavorite()
{
    var sel = SelectedSearchResult;
    if (sel == null) return;

    var repo = QtoRevitPlugin.Application.QtoApplication.Instance?.UserLibraryManager?.Repository;
    if (repo == null) return;

    try
    {
        if (IsSelectedResultFavorite)
        {
            // Rimuovi
            var existing = Favorites.FirstOrDefault(f => f.Code == sel.Code && f.Model.ListId == sel.ListId);
            if (existing != null)
            {
                repo.RemoveFavorite(existing.Id);
                Favorites.Remove(existing);
            }
        }
        else
        {
            // Aggiungi
            var fav = new UserFavorite
            {
                PriceItemId = sel.Id,
                Code = sel.Code,
                Description = sel.Description,
                Unit = sel.Unit,
                UnitPrice = sel.UnitPrice,
                ListName = sel.ListName,
                ListId = sel.ListId,
                AddedAt = System.DateTime.UtcNow
            };
            fav.Id = repo.AddFavorite(fav);
            Favorites.Insert(0, new FavoriteRowVm(fav));
        }
        RefreshIsSelectedResultFavorite();
    }
    catch (System.Exception ex)
    {
        QtoRevitPlugin.Services.CrashLogger.WriteException("SetupViewModel.ToggleFavorite", ex);
    }
}
```

Nota: questo presuppone che `PriceItemRow` esponga `Id` e `ListId`. Verifica nel file SetupViewModel.cs; se mancano, aggiungili (sono trivial get-only su `PriceItem.Id` / `PriceItem.PriceListId`).

- [ ] **Step 5: Comando `UseFavoriteInSearch`**

Permette di "caricare" un preferito nella ricerca (la voce diventa il SelectedSearchResult).

```csharp
[RelayCommand]
private void UseFavoriteInSearch()
{
    if (SelectedFavorite == null) return;
    // Popola SearchQuery con il Code — la ricerca si lancia automaticamente
    SearchQuery = SelectedFavorite.Code;
}
```

- [ ] **Step 6: Comando `RemoveFavorite`**

```csharp
[RelayCommand]
private void RemoveFavorite(FavoriteRowVm row)
{
    if (row == null) return;
    var repo = QtoRevitPlugin.Application.QtoApplication.Instance?.UserLibraryManager?.Repository;
    if (repo == null) return;
    try
    {
        repo.RemoveFavorite(row.Id);
        Favorites.Remove(row);
        RefreshIsSelectedResultFavorite();
    }
    catch (System.Exception ex)
    {
        QtoRevitPlugin.Services.CrashLogger.WriteException("SetupViewModel.RemoveFavorite", ex);
    }
}
```

- [ ] **Step 7: Build**

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -c "Debug R25"
```

Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add QtoRevitPlugin/UI/ViewModels/FavoriteRowVm.cs QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs
git commit -m "feat(listino T4): ViewModel preferiti - Favorites collection + ToggleFavorite + Use/Remove commands"
```

---

## Task 5: UI `SetupListinoView.xaml` — expander preferiti + colonna ★ + bottone dettaglio

**Files:**
- Modify: `QtoRevitPlugin/UI/Views/SetupListinoView.xaml`
- Create: `QtoRevitPlugin/UI/ViewModels/BoolToStarConverter.cs`

- [ ] **Step 1: Crea il converter**

Crea `QtoRevitPlugin/UI/ViewModels/BoolToStarConverter.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows.Data;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>Converter WPF: bool IsFavorite → "★" (true) o "☆" (false).</summary>
    public class BoolToStarConverter : IValueConverter
    {
        public static readonly BoolToStarConverter Instance = new BoolToStarConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? "★" : "☆";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
```

- [ ] **Step 2: Aggiungi namespace vm a SetupListinoView.xaml (se non già presente)**

Assicurati che nel root `<UserControl>` ci sia:
```xml
xmlns:vm="clr-namespace:QtoRevitPlugin.UI.ViewModels"
```

- [ ] **Step 3: Aggiungi Expander "I Miei Preferiti" sopra la sezione risultati**

Individua dove inizia il DataGrid dei SearchResults nel XAML (cerca `ItemsSource="{Binding SearchResults}"`).

Poco sopra, aggiungi:

```xml
<Expander Header="★ I Miei Preferiti" IsExpanded="False"
          Margin="0,6,0,0"
          Background="{DynamicResource PanelSubBrush}"
          BorderBrush="{DynamicResource EdgeLightBrush}" BorderThickness="1">
    <DataGrid ItemsSource="{Binding Favorites}"
              SelectedItem="{Binding SelectedFavorite, Mode=TwoWay}"
              AutoGenerateColumns="False" CanUserAddRows="False" CanUserDeleteRows="False"
              HeadersVisibility="Column" FontSize="11"
              MaxHeight="160" GridLinesVisibility="None"
              IsReadOnly="True">
        <DataGrid.Columns>
            <DataGridTextColumn Header="Codice"      Binding="{Binding Code}"              Width="130" FontFamily="Consolas"/>
            <DataGridTextColumn Header="Descrizione" Binding="{Binding Description}"       Width="*"/>
            <DataGridTextColumn Header="U.M."        Binding="{Binding Unit}"              Width="50"/>
            <DataGridTextColumn Header="Prezzo"      Binding="{Binding UnitPriceFormatted}" Width="80"/>
            <DataGridTextColumn Header="Listino"     Binding="{Binding ListName}"          Width="110" FontSize="10"/>
            <DataGridTextColumn Header="Aggiunto"    Binding="{Binding AddedAtShort}"      Width="115" FontSize="10"/>
        </DataGrid.Columns>
        <DataGrid.RowStyle>
            <Style TargetType="DataGridRow">
                <Setter Property="ContextMenu">
                    <Setter.Value>
                        <ContextMenu>
                            <MenuItem Header="Usa nella ricerca"
                                      Command="{Binding DataContext.UseFavoriteInSearchCommand,
                                          RelativeSource={RelativeSource AncestorType=UserControl}}"/>
                            <MenuItem Header="Rimuovi dai preferiti"
                                      Command="{Binding DataContext.RemoveFavoriteCommand,
                                          RelativeSource={RelativeSource AncestorType=UserControl}}"
                                      CommandParameter="{Binding}"/>
                        </ContextMenu>
                    </Setter.Value>
                </Setter>
            </Style>
        </DataGrid.RowStyle>
    </DataGrid>
</Expander>
```

- [ ] **Step 4: Aggiungi bottone ★ nel pannello dettaglio voce**

Individua il pannello dettaglio (`Grid.Row="3"`, `DataContext="{Binding SelectedSearchResult}"`).

Accanto al prezzo (cerca `UnitPriceFormatted`), aggiungi:

```xml
<Button Grid.Column="X"
        Content="{Binding DataContext.IsSelectedResultFavorite,
                  RelativeSource={RelativeSource AncestorType=UserControl},
                  Converter={x:Static vm:BoolToStarConverter.Instance}}"
        FontSize="20" Foreground="Goldenrod"
        Background="Transparent" BorderThickness="0"
        Padding="8,2" Margin="4,0,0,0"
        ToolTip="Aggiungi/Rimuovi dai preferiti"
        Command="{Binding DataContext.ToggleFavoriteCommand,
                  RelativeSource={RelativeSource AncestorType=UserControl}}"/>
```

Dove `Grid.Column="X"` è la colonna successiva al prezzo nel dettaglio. Se il pannello non è un Grid multi-colonna, wrappa prezzo + bottone in uno StackPanel orizzontale.

- [ ] **Step 5: Aggiungi colonna ★ alla DataGrid SearchResults**

Nel DataGrid dei risultati ricerca (righe ~143-147), inserisci COME PRIMA COLONNA:

```xml
<DataGridTemplateColumn Header="" Width="30" IsReadOnly="True">
    <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
            <TextBlock HorizontalAlignment="Center"
                       FontSize="14" Foreground="Goldenrod"
                       Text="{Binding IsFavoriteInLibrary,
                              Converter={x:Static vm:BoolToStarConverter.Instance}}"/>
        </DataTemplate>
    </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>
```

Nota: `PriceItemRow` deve esporre `IsFavoriteInLibrary` (bool che riflette presenza nei preferiti). Aggiungerlo nel VM:

```csharp
public partial class PriceItemRow : ObservableObject
{
    // ... esistente ...
    [ObservableProperty] private bool _isFavoriteInLibrary;
}
```

E al refresh dei search results, popolare:

```csharp
// Dopo aver ricostruito SearchResults in ExecuteSearch:
var favCodes = new HashSet<(string code, int? listId)>(
    Favorites.Select(f => (f.Code, f.Model.ListId)));
foreach (var r in SearchResults)
    r.IsFavoriteInLibrary = favCodes.Contains((r.Code, r.ListId));
```

- [ ] **Step 6: Sync UI dopo toggle favorite**

Modifica il `ToggleFavorite` (Task 4 Step 4) per aggiornare anche la colonna nella griglia:

```csharp
// In fondo a ToggleFavorite, dopo RefreshIsSelectedResultFavorite():
if (SelectedSearchResult != null)
    SelectedSearchResult.IsFavoriteInLibrary = IsSelectedResultFavorite;
```

- [ ] **Step 7: Build**

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -c "Debug R25"
```

Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add QtoRevitPlugin/UI/Views/SetupListinoView.xaml QtoRevitPlugin/UI/ViewModels/BoolToStarConverter.cs QtoRevitPlugin/UI/ViewModels/SetupViewModel.cs
git commit -m "feat(listino T5): UI preferiti - Expander + colonna star + bottone dettaglio"
```

---

## Task 6: Build finale + full suite

- [ ] **Step 1: Full test suite**

```bash
cd "C:/Users/luigi.dattilo/OneDrive - GPA Ingegneria Srl/Documenti/RevitQTO"
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --nologo
```

Expected: 175 baseline + 9 (7 favorites + 2 active toggle) = **184 passed**, 0 failed.

- [ ] **Step 2: Build Core Release**

```bash
dotnet build QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj -c Release
```

Expected: 0 errors.

- [ ] **Step 3: Build plugin Debug R24 (net48)**

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -c "Debug R24"
```

Expected: 0 errors (cross-target check).

- [ ] **Step 4: Commit finale**

```bash
git commit --allow-empty -m "chore(listino): build finale - 184 test passing"
```

- [ ] **Step 5: Verifica manuale Revit**

Eseguita dall'utente:

1. Apri la scheda Listino
2. Conferma colonna "Attivo" come prima (già applicato in commit 2ba790d)
3. Disattiva un listino → la sua voce non deve più comparire nella ricerca
4. Riattiva → ricompare
5. Cerca una voce, selezionala, clicca ★ → appare nell'Expander "I Miei Preferiti"
6. Riclicca ★ → sparisce dai preferiti
7. Click destro su un preferito → "Usa nella ricerca" imposta la SearchQuery col codice del preferito
8. Click destro → "Rimuovi dai preferiti" funziona

---

## Note implementative

### Cache ricerca fuzzy
Il `PriceItemSearchService.InvalidateFuzzyCache()` è un "best effort": se il service non ha cache interna, il metodo è no-op. L'ExecuteSearch() rilanciato dopo il toggle garantisce comunque il refresh UI.

### PriceItemRow — proprietà che devono esistere
Il plan assume che `PriceItemRow` esponga `Id`, `ListId`, `Code`, `Description`, `Unit`, `UnitPrice`, `ListName`. Se qualcuna manca (in particolare `Id`/`ListId`), aggiungerle come get-only dal `PriceItem` model.

### UserLibrary.db vs .cme
- `UserFavorites` esiste tecnicamente in ENTRAMBI i DB (la tabella è in `InitialStatements`), ma usata SOLO in UserLibrary tramite `QtoApplication.Instance.UserLibraryManager.Repository`. Nel `.cme` la tabella resta vuota — è la scelta più semplice per evitare di avere schema divergenti.

### Test coverage
184 test: 175 baseline + 9 nuovi (7 `UserFavoritesRepositoryTests` + 2 `PriceListActiveToggleTests`). I test di UI (expander, colonna star) sono verificati manualmente.

### Ordine di esecuzione consigliato
T1 → T2 → T3 → T4 → T5 → T6. T3 (fix IsActive) è indipendente da T2 (preferiti DB) — possono essere invertiti se serve prioritizzare il fix.
