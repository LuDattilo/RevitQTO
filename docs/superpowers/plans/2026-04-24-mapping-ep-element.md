# Mapping EP→Element — Sprint 11

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rendere la scheda Mapping completamente operativa: (A) assegnazione EP da famiglie Revit via PickEpDialog, (B) persistenza DB di RoomMappingConfigs e ManualItems, (C) hook "prompt preferiti al primo uso" dopo ogni AssignAsync.

**Architecture:** Tre task indipendenti ordinati per dipendenza: T1 (repo Locali+Manuali DB) è prerequisito di T2 (persistenza in MappingViewModel). T3 (AssignAsync Famiglie) è indipendente. T4 (prompt preferiti) dipende da T3. Nessuna modifica di schema DB — le tabelle RoomMappingConfigs e ManualQuantityEntries esistono già. Nessun nuovo file XAML richiesto: si modificano i file esistenti.

**Tech Stack:** C# netstandard2.0/net48/net8-windows, WPF, CommunityToolkit.Mvvm, SQLite+Dapper, Revit.Async, xUnit + FluentAssertions.

---

## File Map

| File | Azione | Responsabilità |
|---|---|---|
| `QtoRevitPlugin.Core/Data/IQtoRepository.cs` | Modifica | Aggiungi firma `InsertRoomMappingConfig` / `GetRoomMappingConfigs` / `UpdateRoomMappingConfig` / `DeleteRoomMappingConfig` |
| `QtoRevitPlugin.Core/Data/QtoRepository.cs` | Modifica | Implementa i 4 metodi RoomMapping + 1 metodo `UpdateManualItem` mancante |
| `QtoRevitPlugin.Tests/Sprint11/RoomMappingPersistenceTests.cs` | Crea | 5 test CRUD su RoomMappingConfig |
| `QtoRevitPlugin.Tests/Sprint11/ManualItemPersistenceTests.cs` | Crea | 3 test CRUD su ManualQuantityEntry |
| `QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs` | Modifica | SaveRoomMapping/Delete/ManualItem persistono su DB; Reload carica da DB |
| `QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs` | Modifica | Aggiunge `AssignFamilyTypeCommand` che apre PickEpDialog e chiama AssignAsync |
| `QtoRevitPlugin/UI/Views/MappingView.xaml` | Modifica | Bottone "Assegna EP…" collegato a `AssignFamilyTypeCommand` |

---

## Task 1: CRUD RoomMappingConfig su DB

**Contesto:** `RoomMappingConfigs` esiste già nello schema (`DatabaseSchema.RoomMappingConfigs`). Mancano i metodi nel repository.

**Files:**
- Modify: `QtoRevitPlugin.Core/Data/IQtoRepository.cs`
- Modify: `QtoRevitPlugin.Core/Data/QtoRepository.cs`
- Create: `QtoRevitPlugin.Tests/Sprint11/RoomMappingPersistenceTests.cs`

- [ ] **Step 1: Crea directory test e file test vuoto**

Crea `QtoRevitPlugin.Tests/Sprint11/RoomMappingPersistenceTests.cs`:

```csharp
using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint11
{
    public class RoomMappingPersistenceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _sessionId;

        public RoomMappingPersistenceTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"rm_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new WorkSession { ProjectPath = "p.rvt", ProjectName = "p" });
        }

        public void Dispose() { _repo.Dispose(); if (File.Exists(_dbPath)) File.Delete(_dbPath); }

        [Fact]
        public void InsertRoomMappingConfig_ReturnsPositiveId()
        {
            var cfg = new RoomMappingConfig
            {
                SessionId = _sessionId,
                EpCode = "A.01",
                EpDescription = "Test",
                Unit = "mq",
                Formula = "Area",
                TargetCategory = "Rooms",
                RoomNameFilter = ""
            };
            var id = _repo.InsertRoomMappingConfig(cfg);
            id.Should().BeGreaterThan(0);
        }

        [Fact]
        public void GetRoomMappingConfigs_ReturnsInsertedRows()
        {
            _repo.InsertRoomMappingConfig(new RoomMappingConfig { SessionId = _sessionId, EpCode = "A.01", EpDescription = "D", Unit = "mq", Formula = "Area", TargetCategory = "Rooms", RoomNameFilter = "" });
            _repo.InsertRoomMappingConfig(new RoomMappingConfig { SessionId = _sessionId, EpCode = "A.02", EpDescription = "D2", Unit = "ml", Formula = "Perimeter", TargetCategory = "Rooms", RoomNameFilter = "Piano" });
            var list = _repo.GetRoomMappingConfigs(_sessionId);
            list.Should().HaveCount(2);
            list.Select(r => r.EpCode).Should().BeEquivalentTo(new[] { "A.01", "A.02" });
        }

        [Fact]
        public void UpdateRoomMappingConfig_ChangesFormula()
        {
            var id = _repo.InsertRoomMappingConfig(new RoomMappingConfig { SessionId = _sessionId, EpCode = "B.01", EpDescription = "D", Unit = "mq", Formula = "Area", TargetCategory = "Rooms", RoomNameFilter = "" });
            var cfg = _repo.GetRoomMappingConfigs(_sessionId).First(r => r.Id == id);
            cfg.Formula = "Area * 2";
            _repo.UpdateRoomMappingConfig(cfg);
            _repo.GetRoomMappingConfigs(_sessionId).First(r => r.Id == id).Formula.Should().Be("Area * 2");
        }

        [Fact]
        public void DeleteRoomMappingConfig_RemovesRow()
        {
            var id = _repo.InsertRoomMappingConfig(new RoomMappingConfig { SessionId = _sessionId, EpCode = "C.01", EpDescription = "D", Unit = "mq", Formula = "Area", TargetCategory = "Rooms", RoomNameFilter = "" });
            _repo.DeleteRoomMappingConfig(id);
            _repo.GetRoomMappingConfigs(_sessionId).Should().BeEmpty();
        }

        [Fact]
        public void GetRoomMappingConfigs_IsolatedBySession()
        {
            var sessionB = _repo.InsertSession(new WorkSession { ProjectPath = "b.rvt", ProjectName = "b" });
            _repo.InsertRoomMappingConfig(new RoomMappingConfig { SessionId = _sessionId, EpCode = "X", EpDescription = "D", Unit = "mq", Formula = "Area", TargetCategory = "Rooms", RoomNameFilter = "" });
            _repo.InsertRoomMappingConfig(new RoomMappingConfig { SessionId = sessionB, EpCode = "Y", EpDescription = "D", Unit = "mq", Formula = "Area", TargetCategory = "Rooms", RoomNameFilter = "" });
            _repo.GetRoomMappingConfigs(_sessionId).Should().ContainSingle(r => r.EpCode == "X");
        }
    }
}
```

- [ ] **Step 2: Esegui test — devono FALLIRE (metodi non esistono)**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj" --filter "FullyQualifiedName~RoomMappingPersistenceTests"
```

Expected: errore di compilazione (metodi `InsertRoomMappingConfig` ecc. non esistono).

- [ ] **Step 3: Aggiungi firme a `IQtoRepository.cs`**

Leggi `QtoRevitPlugin.Core/Data/IQtoRepository.cs`. Trova la sezione dei metodi esistenti. Aggiungi alla fine dell'interfaccia (prima della chiusura `}`):

```csharp
    // RoomMappingConfigs (Sprint 11)
    int InsertRoomMappingConfig(RoomMappingConfig cfg);
    IReadOnlyList<RoomMappingConfig> GetRoomMappingConfigs(int sessionId);
    void UpdateRoomMappingConfig(RoomMappingConfig cfg);
    void DeleteRoomMappingConfig(int id);
```

- [ ] **Step 4: Implementa i 4 metodi in `QtoRepository.cs`**

Leggi `QtoRevitPlugin.Core/Data/QtoRepository.cs`. Trova la sezione ManualItems (intorno a riga 1315) per avere il pattern da seguire. Aggiungi subito dopo la sezione ManualItems:

```csharp
        // -----------------------------------------------------------------------
        // RoomMappingConfigs (Sprint 11)
        // -----------------------------------------------------------------------

        public int InsertRoomMappingConfig(RoomMappingConfig cfg)
        {
            const string sql = @"
INSERT INTO RoomMappingConfigs
(SessionId, EpCode, EpDescription, Unit, Formula, TargetCategory, RoomNameFilter, CreatedAt)
VALUES (@SessionId, @EpCode, @EpDescription, @Unit, @Formula, @TargetCategory, @RoomNameFilter, @CreatedAt);
SELECT last_insert_rowid();";
            return _conn.ExecuteScalar<int>(sql, new
            {
                cfg.SessionId,
                cfg.EpCode,
                cfg.EpDescription,
                cfg.Unit,
                cfg.Formula,
                cfg.TargetCategory,
                cfg.RoomNameFilter,
                CreatedAt = DateTime.UtcNow.ToString("o")
            });
        }

        public IReadOnlyList<RoomMappingConfig> GetRoomMappingConfigs(int sessionId)
        {
            const string sql = @"
SELECT Id, SessionId, EpCode, EpDescription, Unit, Formula, TargetCategory, RoomNameFilter
FROM RoomMappingConfigs
WHERE SessionId = @SessionId
ORDER BY Id;";
            return _conn.Query<RoomMappingConfig>(sql, new { SessionId = sessionId }).ToList();
        }

        public void UpdateRoomMappingConfig(RoomMappingConfig cfg)
        {
            const string sql = @"
UPDATE RoomMappingConfigs
SET EpCode=@EpCode, EpDescription=@EpDescription, Unit=@Unit,
    Formula=@Formula, TargetCategory=@TargetCategory, RoomNameFilter=@RoomNameFilter
WHERE Id=@Id;";
            _conn.Execute(sql, new { cfg.Id, cfg.EpCode, cfg.EpDescription, cfg.Unit, cfg.Formula, cfg.TargetCategory, cfg.RoomNameFilter });
        }

        public void DeleteRoomMappingConfig(int id)
        {
            _conn.Execute("DELETE FROM RoomMappingConfigs WHERE Id=@Id;", new { Id = id });
        }
```

- [ ] **Step 5: Esegui test — devono PASSARE**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj" --filter "FullyQualifiedName~RoomMappingPersistenceTests"
```

Expected: 5 PASSED.

- [ ] **Step 6: Verifica suite completa**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj"
```

Expected: 448 + 5 = 453 passed, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add "QtoRevitPlugin.Core/Data/IQtoRepository.cs" "QtoRevitPlugin.Core/Data/QtoRepository.cs" "QtoRevitPlugin.Tests/Sprint11/RoomMappingPersistenceTests.cs"
git commit -m "feat(sprint11 T1): CRUD RoomMappingConfig su DB + 5 test"
```

---

## Task 2: CRUD ManualQuantityEntry — aggiunge UpdateManualItem

**Contesto:** `InsertManualItem` e `GetManualItems` esistono già. Manca `UpdateManualItem` e `DeleteManualItem` nell'interfaccia pubblica (il codice attuale usa solo in-memory). Aggiungiamo i due metodi mancanti + test.

**Files:**
- Modify: `QtoRevitPlugin.Core/Data/IQtoRepository.cs`
- Modify: `QtoRevitPlugin.Core/Data/QtoRepository.cs`
- Create: `QtoRevitPlugin.Tests/Sprint11/ManualItemPersistenceTests.cs`

- [ ] **Step 1: Crea file test**

Crea `QtoRevitPlugin.Tests/Sprint11/ManualItemPersistenceTests.cs`:

```csharp
using FluentAssertions;
using QtoRevitPlugin.Data;
using QtoRevitPlugin.Models;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QtoRevitPlugin.Tests.Sprint11
{
    public class ManualItemPersistenceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly QtoRepository _repo;
        private readonly int _sessionId;

        public ManualItemPersistenceTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"mi_{Guid.NewGuid():N}.db");
            _repo = new QtoRepository(_dbPath);
            _sessionId = _repo.InsertSession(new WorkSession { ProjectPath = "p.rvt", ProjectName = "p" });
        }

        public void Dispose() { _repo.Dispose(); if (File.Exists(_dbPath)) File.Delete(_dbPath); }

        [Fact]
        public void InsertAndGet_ManualItem_RoundTrips()
        {
            var item = new ManualQuantityEntry { SessionId = _sessionId, EpCode = "X.01", EpDescription = "Test", Unit = "mq", Quantity = 10.5, UnitPrice = 20.0, Notes = "nota" };
            var id = _repo.InsertManualItem(item);
            id.Should().BeGreaterThan(0);
            var loaded = _repo.GetManualItems(_sessionId);
            loaded.Should().ContainSingle();
            loaded[0].EpCode.Should().Be("X.01");
            loaded[0].Quantity.Should().BeApproximately(10.5, 0.001);
        }

        [Fact]
        public void UpdateManualItem_ChangesQuantityAndPrice()
        {
            var id = _repo.InsertManualItem(new ManualQuantityEntry { SessionId = _sessionId, EpCode = "Y.01", EpDescription = "D", Unit = "ml", Quantity = 5.0, UnitPrice = 10.0, Notes = "" });
            var item = _repo.GetManualItems(_sessionId).First(m => m.Id == id);
            item.Quantity = 99.9;
            item.UnitPrice = 50.0;
            _repo.UpdateManualItem(item);
            var reloaded = _repo.GetManualItems(_sessionId).First(m => m.Id == id);
            reloaded.Quantity.Should().BeApproximately(99.9, 0.001);
            reloaded.UnitPrice.Should().BeApproximately(50.0, 0.001);
        }

        [Fact]
        public void DeleteManualItem_RemovesRow()
        {
            var id = _repo.InsertManualItem(new ManualQuantityEntry { SessionId = _sessionId, EpCode = "Z.01", EpDescription = "D", Unit = "mq", Quantity = 1.0, UnitPrice = 1.0, Notes = "" });
            _repo.DeleteManualItem(id);
            _repo.GetManualItems(_sessionId).Should().BeEmpty();
        }
    }
}
```

- [ ] **Step 2: Esegui test — devono fallire (UpdateManualItem/DeleteManualItem non esistono)**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj" --filter "FullyQualifiedName~ManualItemPersistenceTests"
```

Expected: errore compilazione.

- [ ] **Step 3: Aggiungi firme a `IQtoRepository.cs`**

Leggi `QtoRevitPlugin.Core/Data/IQtoRepository.cs`. Trova le firme `InsertManualItem`/`GetManualItems` esistenti. Aggiungile accanto:

```csharp
    void UpdateManualItem(ManualQuantityEntry item);
    void DeleteManualItem(int id);
```

- [ ] **Step 4: Implementa in `QtoRepository.cs`**

Leggi `QtoRevitPlugin.Core/Data/QtoRepository.cs`. Trova il metodo `InsertManualItem` (riga ~1328). Aggiungi subito dopo:

```csharp
        public void UpdateManualItem(ManualQuantityEntry item)
        {
            const string sql = @"
UPDATE ManualQuantityEntries
SET EpCode=@EpCode, EpDescription=@EpDescription, Unit=@Unit,
    Quantity=@Quantity, UnitPrice=@UnitPrice, Notes=@Notes
WHERE Id=@Id;";
            _conn.Execute(sql, new { item.Id, item.EpCode, item.EpDescription, item.Unit, item.Quantity, item.UnitPrice, item.Notes });
        }

        public void DeleteManualItem(int id)
        {
            _conn.Execute("DELETE FROM ManualQuantityEntries WHERE Id=@Id;", new { Id = id });
        }
```

- [ ] **Step 5: Esegui test — devono PASSARE**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj" --filter "FullyQualifiedName~ManualItemPersistenceTests"
```

Expected: 3 PASSED.

- [ ] **Step 6: Suite completa**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj"
```

Expected: 453 + 3 = 456 passed, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add "QtoRevitPlugin.Core/Data/IQtoRepository.cs" "QtoRevitPlugin.Core/Data/QtoRepository.cs" "QtoRevitPlugin.Tests/Sprint11/ManualItemPersistenceTests.cs"
git commit -m "feat(sprint11 T2): UpdateManualItem + DeleteManualItem su DB + 3 test"
```

---

## Task 3: Persistenza DB in MappingViewModel (Locali + Manuali)

**Contesto:** `SaveRoomMapping`, `DeleteRoomMapping`, `SaveManualItem`, `DeleteManualItem` in `MappingViewModel` usano solo `ObservableCollection` in-memory con commento "Sprint 5". Ora li facciamo persistere su DB. Il `Reload()` del VM deve caricare da DB all'avvio.

**Files:**
- Modify: `QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs`

Questo task è **solo UI/VM** — nessun test automatico aggiuntivo (le operazioni DB sono già coperte dai test T1/T2; il VM interagisce con Revit API quindi non è testabile headless).

- [ ] **Step 1: Leggi le prime 100 righe di `MappingViewModel.cs` per capire la struttura**

Leggi `QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs` righe 1-100 per vedere i campi `_repo`, `_sessionId`, `Reload()`.

- [ ] **Step 2: Aggiorna `Reload()` per caricare RoomMappings e ManualItems da DB**

Trova il metodo `Reload()` in `MappingViewModel.cs`. Alla fine del metodo, dopo il codice esistente che popola FamilyTypes/Categories, aggiungi:

```csharp
            // Carica da DB (Sprint 11): ora i dati sopravvivono al riavvio del plugin
            if (_repo != null && _sessionId > 0)
            {
                RoomMappings.Clear();
                foreach (var cfg in _repo.GetRoomMappingConfigs(_sessionId))
                    RoomMappings.Add(RoomMappingConfigVm.FromModel(cfg));

                ManualItems.Clear();
                foreach (var item in _repo.GetManualItems(_sessionId))
                    ManualItems.Add(ManualItemVm.FromModel(item));
            }
```

**Nota:** `RoomMappingConfigVm.FromModel` e `ManualItemVm.FromModel` sono da aggiungere (Step 3).

- [ ] **Step 3: Aggiungi metodi factory `FromModel` alle classi DTO**

Leggi `MappingViewModel.cs` righe 582-677 per vedere `RoomMappingConfigVm` e `ManualItemVm`.

Aggiungi dentro `RoomMappingConfigVm` (dopo le proprietà esistenti):

```csharp
        public static RoomMappingConfigVm FromModel(RoomMappingConfig cfg) => new RoomMappingConfigVm
        {
            Id = cfg.Id,
            EpCode = cfg.EpCode,
            EpDescription = cfg.EpDescription,
            Unit = cfg.Unit,
            Formula = cfg.Formula,
            TargetCategory = cfg.TargetCategory == "MEPSpaces" ? RoomTargetCategory.MEPSpaces : RoomTargetCategory.Rooms,
            RoomNameFilter = cfg.RoomNameFilter ?? ""
        };

        public RoomMappingConfig ToModel(int sessionId) => new RoomMappingConfig
        {
            Id = Id,
            SessionId = sessionId,
            EpCode = EpCode,
            EpDescription = EpDescription,
            Unit = Unit,
            Formula = Formula,
            TargetCategory = TargetCategory == RoomTargetCategory.MEPSpaces ? "MEPSpaces" : "Rooms",
            RoomNameFilter = RoomNameFilter
        };
```

Aggiungi dentro `ManualItemVm`:

```csharp
        public static ManualItemVm FromModel(ManualQuantityEntry item) => new ManualItemVm
        {
            Id = item.Id,
            EpCode = item.EpCode,
            EpDescription = item.EpDescription,
            Unit = item.Unit,
            Quantity = (decimal)item.Quantity,
            UnitPrice = (decimal)item.UnitPrice,
            Notes = item.Notes ?? "",
            IsNew = false
        };

        public ManualQuantityEntry ToModel(int sessionId) => new ManualQuantityEntry
        {
            Id = Id,
            SessionId = sessionId,
            EpCode = EpCode,
            EpDescription = EpDescription,
            Unit = Unit,
            Quantity = (double)Quantity,
            UnitPrice = (double)UnitPrice,
            Notes = Notes
        };
```

- [ ] **Step 4: Aggiorna `SaveRoomMapping` per persistere su DB**

Trova `SaveRoomMapping()` in `MappingViewModel.cs`. Sostituisci il blocco `if (EditingRoomMapping.Id == 0)` con:

```csharp
            if (EditingRoomMapping.Id == 0)
            {
                if (_repo != null && _sessionId > 0)
                {
                    var model = EditingRoomMapping.ToModel(_sessionId);
                    var newId = _repo.InsertRoomMappingConfig(model);
                    EditingRoomMapping.Id = newId;
                }
                RoomMappings.Add(EditingRoomMapping);
                RoomStatus = $"Aggiunta formula «{EditingRoomMapping.EpCode}».";
            }
            else
            {
                if (_repo != null)
                    _repo.UpdateRoomMappingConfig(EditingRoomMapping.ToModel(_sessionId));
                var existing = RoomMappings.FirstOrDefault(r => r.Id == EditingRoomMapping.Id);
                if (existing != null)
                    RoomMappings[RoomMappings.IndexOf(existing)] = EditingRoomMapping;
                RoomStatus = $"Aggiornata formula «{EditingRoomMapping.EpCode}».";
            }
```

- [ ] **Step 5: Aggiorna `DeleteRoomMapping` per persistere su DB**

Trova `DeleteRoomMapping()`. Aggiungi prima della `RoomMappings.Remove(...)`:

```csharp
            if (_repo != null && SelectedRoomMapping.Id > 0)
                _repo.DeleteRoomMappingConfig(SelectedRoomMapping.Id);
```

- [ ] **Step 6: Aggiorna `SaveManualItem` per persistere su DB**

Trova `SaveManualItem()`. Sostituisci il blocco `if (EditingManualItem.IsNew)`:

```csharp
            if (EditingManualItem.IsNew)
            {
                if (_repo != null && _sessionId > 0)
                {
                    var model = EditingManualItem.ToModel(_sessionId);
                    var newId = _repo.InsertManualItem(model);
                    EditingManualItem.Id = newId;
                }
                EditingManualItem.IsNew = false;
                ManualItems.Add(EditingManualItem);
                ManualStatus = $"Aggiunta voce «{EditingManualItem.EpCode}».";
            }
            else
            {
                if (_repo != null)
                    _repo.UpdateManualItem(EditingManualItem.ToModel(_sessionId));
                var existing = ManualItems.FirstOrDefault(m => m.Id == EditingManualItem.Id);
                if (existing != null)
                    ManualItems[ManualItems.IndexOf(existing)] = EditingManualItem;
                ManualStatus = $"Aggiornata voce «{EditingManualItem.EpCode}».";
            }
```

- [ ] **Step 7: Aggiorna `DeleteManualItem` (o metodo equivalente) per persistere su DB**

Trova il metodo che cancella `SelectedManualItem` dalla lista (cerca `ManualItems.Remove`). Aggiungi prima della Remove:

```csharp
            if (_repo != null && SelectedManualItem.Id > 0)
                _repo.DeleteManualItem(SelectedManualItem.Id);
```

- [ ] **Step 8: Build verifica (non ci sono test automatici per questo task — è VM WPF)**

```bash
dotnet build "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj"
```

Expected: 0 errors.

- [ ] **Step 9: Suite completa**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj"
```

Expected: 456 passed, 0 failed.

- [ ] **Step 10: Commit**

```bash
git add "QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs"
git commit -m "feat(sprint11 T3): MappingViewModel persiste RoomMappings+ManualItems su DB (era in-memory)"
```

---

## Task 4: AssignFamilyTypeCommand — assegnazione EP da Tab Famiglie

**Contesto:** Tab Famiglie mostra `FamilyTypeRow` (family+type+instanceCount). Il bottone "Assegna EP…" (in `MappingView.xaml`) non è ancora connesso a nessun command. Dobbiamo:
1. Aggiungere `AssignFamilyTypeCommand` al VM che apre `PickEpDialog` (già esistente: `QtoRevitPlugin/UI/Views/PickEpDialog.xaml`)
2. Raccogliere gli elementi Revit della FamilyTypeRow selezionata
3. Chiamare `AssignmentService.AssignEp()` via `Revit.Async.RevitTask.RunAsync`
4. Aggiornare lo status

**Files:**
- Modify: `QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs`
- Modify: `QtoRevitPlugin/UI/Views/MappingView.xaml`

- [ ] **Step 1: Leggi `PickEpDialog` e `PickEpDialogViewModel` per capire l'API**

```bash
# Trova i file rilevanti
grep -rn "class PickEpDialog\|class PickEpDialogViewModel\|SelectedItem\|SelectedPriceItem" QtoRevitPlugin/UI/Views/PickEpDialog.xaml.cs QtoRevitPlugin/UI/ViewModels/PickEpDialogViewModel.cs 2>/dev/null | head -20
```

Expected output: mostra come il dialog restituisce l'item selezionato.

- [ ] **Step 2: Aggiungi `AssignFamilyTypeCommand` a `MappingViewModel`**

Leggi `QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs`. Cerca il blocco Tab 1 (Famiglie). Trova il metodo `RefreshFamilyTypes()`. Aggiungi subito PRIMA di `RefreshFamilyTypes()`:

```csharp
        /// <summary>
        /// Apre il dialog di selezione EP (PickEpDialog), poi assegna l'EP selezionato
        /// a tutti gli elementi Revit della FamilyTypeRow scelta, via AssignmentService.
        /// </summary>
        [RelayCommand]
        private async Task AssignFamilyTypeAsync()
        {
            if (SelectedFamilyTypeRow == null)
            {
                FamilyStatus = "Seleziona prima una riga famiglia.";
                return;
            }

            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var session = QtoApplication.Instance?.SessionManager?.ActiveSession;
            var userCtx = QtoApplication.Instance?.UserContext ?? new WindowsUserContext();
            if (repo == null || session == null)
            {
                FamilyStatus = "Nessuna sessione aperta.";
                return;
            }

            // Apre il dialog di scelta EP
            var dlgVm = new PickEpDialogViewModel();
            var dlg = new QtoRevitPlugin.UI.Views.PickEpDialog(dlgVm);
            if (dlg.ShowDialog() != true || dlgVm.SelectedItem == null)
                return;

            var pickedItem = dlgVm.SelectedItem;

            FamilyStatus = $"Assegnazione «{pickedItem.Code}» a {SelectedFamilyTypeRow.InstanceCount} elementi…";

            try
            {
                await Revit.Async.RevitTask.RunAsync(app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null) return;

                    var session2 = QtoApplication.Instance?.SessionManager?.ActiveSession;
                    var repo2 = QtoApplication.Instance?.SessionManager?.Repository;
                    if (session2 == null || repo2 == null) return;

                    // Raccoglie tutti gli elementi della FamilyType selezionata
                    var familyName = SelectedFamilyTypeRow.Family;
                    var typeName = SelectedFamilyTypeRow.Type;

                    var collector = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                        .OfCategory(SelectedFamilyCategory!.Bic)
                        .WhereElementIsNotElementType()
                        .ToElements();

                    var targets = collector
                        .Where(el =>
                        {
                            var key = ExtractFamilyTypeKey(el, doc);
                            return key.HasValue && key.Value.Family == familyName && key.Value.Type == typeName;
                        })
                        .ToList();

                    if (targets.Count == 0) return;

                    var svc = new QtoRevitPlugin.Services.AssignmentService(repo2);
                    var request = new QtoRevitPlugin.Services.AssignmentRequest
                    {
                        SessionId = session2.Id,
                        EpCode = pickedItem.Code,
                        EpDescription = pickedItem.Description ?? "",
                        Unit = pickedItem.Unit ?? "",
                        UnitPrice = (double)(pickedItem.UnitPrice ?? 0m),
                        Source = QtoRevitPlugin.Models.AssignmentSource.RevitElement,
                        CreatedBy = userCtx.UserName,
                        TargetElements = targets
                    };

                    svc.AssignEp(request);
                });

                FamilyStatus = $"Assegnate {SelectedFamilyTypeRow.InstanceCount} istanze a «{pickedItem.Code}» ✓";
            }
            catch (Exception ex)
            {
                QtoRevitPlugin.Services.CrashLogger.WriteException("AssignFamilyTypeAsync", ex);
                FamilyStatus = $"Errore: {ex.Message}";
            }
        }
```

- [ ] **Step 3: Verifica che `SelectedFamilyTypeRow` esista come proprietà osservabile**

Cerca in `MappingViewModel.cs`:
```bash
grep -n "SelectedFamilyTypeRow\|FamilyTypeRow" QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs | head -10
```

Se `SelectedFamilyTypeRow` non esiste come `[ObservableProperty]`, aggiungila nella sezione campi Tab Famiglie:

```csharp
        [ObservableProperty] private FamilyTypeRow? _selectedFamilyTypeRow;
```

- [ ] **Step 4: Collega il bottone in `MappingView.xaml`**

Leggi `QtoRevitPlugin/UI/Views/MappingView.xaml`. Cerca il bottone "Assegna EP…" nel tab Famiglie (probabilmente `Content="Assegna EP…"` o simile). Collegalo al command:

```xml
<Button Content="Assegna EP…"
        Command="{Binding AssignFamilyTypeAsyncCommand}"
        IsEnabled="{Binding SelectedFamilyTypeRow, Converter={StaticResource NullToBoolConverter}}"
        Margin="0,0,4,0" Padding="6,2"/>
```

Se il bottone non esiste, aggiungilo alla toolbar del tab Famiglie (nella `StackPanel` o `WrapPanel` dei bottoni della tab).

Assicurati anche che la `DataGrid` delle famiglie abbia:
```xml
SelectedItem="{Binding SelectedFamilyTypeRow, Mode=TwoWay}"
```

- [ ] **Step 5: Build verifica**

```bash
dotnet build "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj"
```

Expected: 0 errors.

- [ ] **Step 6: Suite completa**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj"
```

Expected: 456 passed, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add "QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs" "QtoRevitPlugin/UI/Views/MappingView.xaml"
git commit -m "feat(sprint11 T4): AssignFamilyTypeCommand — EP da PickEpDialog → elementi Revit via AssignmentService"
```

---

## Task 5: Prompt preferiti al primo uso

**Contesto:** Dopo ogni `AssignEp` riuscito, verificare se il codice EP è già nei preferiti. Se no, mostrare `TaskDialog` "Vuoi salvare nei preferiti?". Il setting "Non chiedere più" viene salvato in `SettingsService`.

**Files:**
- Modify: `QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs`
- Modify: oppure aggiungi metodo `void MaybePromptAddFavorite(string epCode, string listName, int? listId)` a `MappingViewModel`

- [ ] **Step 1: Leggi `SettingsService` per capire come salvare un flag booleano**

```bash
grep -n "class SettingsService\|Get\|Set\|bool\|SuppressPrompt\|DoNotAsk" QtoRevitPlugin.Core/Services/SettingsService.cs 2>/dev/null | head -20
# oppure
grep -rn "class SettingsService" QtoRevitPlugin/ QtoRevitPlugin.Core/ --include="*.cs" | head -5
```

- [ ] **Step 2: Aggiungi `SuppressNewFavoritePrompt` a `SettingsService` se non esiste**

Leggi `SettingsService`. Se ha un metodo generico `Get<T>(string key, T def)` / `Set<T>(string key, T value)`, usa quello. Altrimenti aggiungi:

```csharp
        public bool SuppressNewFavoritePrompt
        {
            get => Get("SuppressNewFavoritePrompt", false);
            set => Set("SuppressNewFavoritePrompt", value);
        }
```

- [ ] **Step 3: Aggiungi `MaybePromptAddFavorite` in `MappingViewModel`**

Dopo il metodo `AssignFamilyTypeAsync`, aggiungi:

```csharp
        /// <summary>
        /// Se l'EP non è già nei preferiti personali e l'utente non ha soppresso il prompt,
        /// mostra TaskDialog "Vuoi salvare nei preferiti?".
        /// Chiamato dopo ogni assegnazione riuscita.
        /// </summary>
        private void MaybePromptAddFavorite(string epCode, string epDescription, string unit, decimal unitPrice, string listName, int? listId)
        {
            var settings = QtoApplication.Instance?.Settings;
            if (settings != null && settings.SuppressNewFavoritePrompt) return;

            var favRepo = new FileFavoritesRepository(FileFavoritesRepository.GetDefaultGlobalDir());
            // Controlla se già nei preferiti (cerca per code)
            var favorites = favRepo.LoadPersonal();
            if (favorites.Any(f => f.EpCode == epCode)) return;

            var result = Autodesk.Revit.UI.TaskDialog.Show(
                "CME – Voce nuova",
                $"Vuoi salvare «{epCode}» nei preferiti?\n\n" +
                $"{epDescription}  [{unit}]",
                Autodesk.Revit.UI.TaskDialogCommonButtons.Yes | Autodesk.Revit.UI.TaskDialogCommonButtons.No,
                Autodesk.Revit.UI.TaskDialogResult.No);

            if (result == Autodesk.Revit.UI.TaskDialogResult.Yes)
            {
                var fav = new FavoriteItem
                {
                    EpCode = epCode,
                    EpDescription = epDescription,
                    Unit = unit,
                    UnitPrice = unitPrice,
                    ListName = listName,
                    Scope = FavoriteScope.Personal
                };
                favRepo.AddToPersonal(fav);
            }
        }
```

- [ ] **Step 4: Chiama `MaybePromptAddFavorite` alla fine di `AssignFamilyTypeAsync` (nel blocco riuscito)**

Trova nel metodo `AssignFamilyTypeAsync` la riga:
```csharp
FamilyStatus = $"Assegnate {SelectedFamilyTypeRow.InstanceCount} istanze a «{pickedItem.Code}» ✓";
```

Aggiungila subito dopo:

```csharp
            RunOnUi(() => MaybePromptAddFavorite(
                pickedItem.Code,
                pickedItem.Description ?? "",
                pickedItem.Unit ?? "",
                pickedItem.UnitPrice ?? 0m,
                pickedItem.ListName ?? "",
                pickedItem.ListId));
```

**Nota:** `RunOnUi` è disponibile da `ViewModelBase`. `MappingViewModel` eredita già da `ViewModelBase`.

- [ ] **Step 5: Adatta le firme se `FileFavoritesRepository` non ha `LoadPersonal`/`AddToPersonal`**

Verifica:
```bash
grep -n "LoadPersonal\|AddToPersonal\|LoadAll\|AddFavorite\|FavoriteItem" QtoRevitPlugin.Core/Data/FileFavoritesRepository.cs | head -20
```

Se i metodi si chiamano diversamente, adatta la chiamata in `MaybePromptAddFavorite` al metodo effettivo. Non inventare nuovi metodi — usa quelli già esistenti.

- [ ] **Step 6: Build verifica**

```bash
dotnet build "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj"
```

Expected: 0 errors.

- [ ] **Step 7: Suite completa**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj"
```

Expected: 456 passed, 0 failed.

- [ ] **Step 8: Commit**

```bash
git add "QtoRevitPlugin/UI/ViewModels/MappingViewModel.cs"
git commit -m "feat(sprint11 T5): prompt preferiti al primo uso dopo AssignFamilyType"
```

---

## Task 6: Build finale + verifica

- [ ] **Step 1: Suite completa Debug**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj"
```

Expected: 456 passed, 0 failed.

- [ ] **Step 2: Build Core Release**

```bash
dotnet build "QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj" -c Release
```

Expected: 0 errori.

- [ ] **Step 3: Suite Release**

```bash
dotnet test "QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj" -c Release
```

Expected: 456 passed.

---

## Note implementative

### Quando testare in Revit

**Testare in Revit dopo T3+T4 completati** (non prima): le T1/T2 sono solo backend testabili headless. Il test Revit reale ha senso quando:
1. Apri il file `.rvt` → sessione carica RoomMappings e ManualItems da DB (T3)
2. Tab Famiglie → seleziona una riga → "Assegna EP…" → dialog si apre → confermi → elementi assegnati (T4)
3. Tab Locali → aggiungi formula → chiudi Revit → riapri → formula ancora lì (T3)

**Verifica minima in Revit:**
- Crea 2 formule locali → chiudi + riapri Revit → le formule ci sono ancora → ✅
- Seleziona FamilyType → Assegna EP → controlla la tab "Assegnazioni" del DockablePane → voci presenti → ✅

### Cross-target net48/net8

- `RoomMappingConfig.TargetCategory` è `string` (non enum) nel model Core per compatibilità Dapper.
- Il mapping `"Rooms"/"MEPSpaces"` ↔ `RoomTargetCategory` enum avviene nei metodi `FromModel`/`ToModel`.
- Nessuna feature C# 9+ usata — tutto `class` con property standard.

### Dipendenze tra task

```
T1 (CRUD RoomMapping DB) ──┐
T2 (CRUD ManualItem DB)  ──┼──> T3 (VM persistenza) ──> T4 (Assign) ──> T5 (Prompt)
```

T1 e T2 possono essere eseguiti in parallelo.
