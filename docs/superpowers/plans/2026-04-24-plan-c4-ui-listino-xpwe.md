# Plan C-4 — UI Elenco Prezzi potenziato (17 campi XPWE)

> **Contesto:** quinto sotto-progetto della spec `2026-04-24-modulo-computi-primus-xpwe-design.md`. Dipende da C-0 (schema v12), C-2 (ChapterService).

**Goal:** Estendere `PriceItem` C# con i 17 campi XPWE aggiunti a v12 della tabella. Arricchire `SetupListinoView` per mostrare/modificare le colonne chiave (Prezzo1..5, SpCap/Cap/SbCap, incidenze). Zero breaking changes sull'import esistente.

**Architecture:**
- Estensione additiva di `PriceItem` (nuove property, nessuna rinominata/rimossa)
- Dapper popola automaticamente le nuove property dalle colonne DB v12 esistenti
- DataGrid `SetupListinoView` mostra 5 nuove colonne principali (Prezzo1, IncMDO, IncMAT, IncSIC, Capitolo)
- Editor popup "Dettagli voce" per modifica completa di tutti i 17 campi
- Il vecchio `UnitPrice` resta come property sinonimo di `Prezzo1` (entrambi mappati allo stesso valore DB: la colonna `UnitPrice` resta primaria, `Prezzo1` è new-style)

**Tech Stack:** C# (model), XAML (DataGrid + dialog), MVVM CommunityToolkit.

**Vincoli:**
- Mantengo `UnitPrice` funzionante per tutta la codebase esistente (import parsers, export, altri VM)
- `Prezzo1` riceve lo stesso valore di `UnitPrice` automaticamente (via Insert) — il flusso di import carica `UnitPrice` come oggi
- I parametri XPWE (Articolo, Tariffa, DesRidotta) sono **solo lettura** in questa fase — valorizzati dal deserializer C-1 quando importeremo da XPWE in C-7

**File impattati:**
- Modify: `QtoRevitPlugin.Core/Models/PriceItem.cs` (aggiunta 17 property)
- Modify: `QtoRevitPlugin.Core/Data/QtoRepository.cs` (solo se serve SELECT esplicita delle nuove colonne — ma Dapper con `SELECT *` le prende auto)
- Modify: `QtoRevitPlugin/UI/Views/SetupListinoView.xaml` (aggiunta colonne DataGrid)
- Opz: `QtoRevitPlugin/UI/Views/PriceItemEditDialog.xaml(.cs)` (editor completo 17 campi)

---

## Task 1: Estensione modello PriceItem

**Files:**
- Modify: `QtoRevitPlugin.Core/Models/PriceItem.cs`

- [ ] **Step 1: Leggere file attuale** (già fatto nel plan header)

- [ ] **Step 2: Aggiungere property nuove al fondo della classe (prima della `}`)**

```csharp
// ============================================================
// Plan C-4 (schema v12): campi XPWE aggiuntivi per compliance PriMus.
// Backward-compat: UnitPrice resta primario, Prezzo1 lo replica.
// Dapper popola queste property dalle colonne v12 esistenti.
// ============================================================

/// <summary>Articolo XPWE (secondo livello codice, opzionale).</summary>
public string? Articolo { get; set; }

/// <summary>Tariffa XPWE (terzo livello codice, opzionale).</summary>
public string? Tariffa { get; set; }

/// <summary>Prezzo listino 1 (lordo). Alias di UnitPrice per compat XPWE.</summary>
public double Prezzo1 { get; set; }

/// <summary>Prezzo listino 2 (tipicamente netto ribassato).</summary>
public double Prezzo2 { get; set; }

/// <summary>Prezzo listino 3.</summary>
public double Prezzo3 { get; set; }

/// <summary>Prezzo listino 4.</summary>
public double Prezzo4 { get; set; }

/// <summary>Prezzo listino 5.</summary>
public double Prezzo5 { get; set; }

/// <summary>FK → ChapterNode.Id (Level=SpCap).</summary>
public int? SpCapId { get; set; }

/// <summary>FK → ChapterNode.Id (Level=Cap).</summary>
public int? CapId { get; set; }

/// <summary>FK → ChapterNode.Id (Level=SbCap).</summary>
public int? SbCapId { get; set; }

/// <summary>FK → WbsNode.Id (Kind=WbsCap).</summary>
public int? WbsCapNodeId { get; set; }

/// <summary>Incidenza manodopera (%).</summary>
public double IncMDO { get; set; }

/// <summary>Incidenza materiali (%).</summary>
public double IncMAT { get; set; }

/// <summary>Incidenza sicurezza (%).</summary>
public double IncSIC { get; set; }

/// <summary>Tipo risorsa XPWE (0=default, 1-5 = MDO/MAT/ATT/ecc.).</summary>
public int TipoRisorsa { get; set; }

/// <summary>Flags XPWE (bitmask). Default 512 = voce standard.</summary>
public int Flags { get; set; } = 512;

/// <summary>Configurazione quantità XPWE.</summary>
public string? CnfQt { get; set; }

/// <summary>Indirizzo internet (riferimento esterno).</summary>
public string? AdrInternet { get; set; }

/// <summary>Data EP XPWE (DD/MM/YYYY, null se Excel zero).</summary>
public string? DataEP { get; set; }
```

- [ ] **Step 3: Build Core**

```bash
dotnet build QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj -c Debug -v q
```

Atteso: 0 errori.

## Task 2: Verifica Dapper popola correttamente

**Files:** (nessuno da modificare) — solo verifica runtime tramite unit test

Dapper auto-mappa colonne DB → property C# per nome. Le nuove colonne (`Prezzo2..5`, `SpCapId`, ecc.) sono scritte nello schema v12 nelle `InitialStatements` di `DatabaseSchema` — quindi `SELECT * FROM PriceItems` le ritorna. Dapper ignora silenziosamente colonne con nomi senza property C# corrispondenti, e viceversa — quindi il nostro caso "property ci sono, colonne ci sono" funziona out-of-the-box.

- [ ] **Step 1: Aggiungere test in DomainServicesTests.cs (o nuovo file)**

```csharp
[Fact]
public void PriceItem_Xpwe_RoundtripViaRawSql()
{
    var path = UniquePath();
    try
    {
        var (repo, _) = NewRepoWithSession(path);
        // Insert con tutti i campi v12 via SQL raw
        SqliteConnection.ClearAllPools();
        using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO PriceLists (Name, IsActive, Priority, RowCount) VALUES ('L', 1, 0, 0);";
                cmd.ExecuteNonQuery();
            }
            int listId;
            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "SELECT last_insert_rowid();"; listId = Convert.ToInt32(cmd.ExecuteScalar()); }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO PriceItems
                    (PriceListId, Code, Description, Unit, UnitPrice,
                     Articolo, Prezzo1, Prezzo2, IncMDO, IncMAT, IncSIC, Flags)
                    VALUES (@l, 'X', 'desc', 'mc', 50.0,
                            'ART-1', 50.0, 45.0, 30.5, 55.0, 2.5, 512);";
                cmd.Parameters.AddWithValue("@l", listId);
                cmd.ExecuteNonQuery();
            }
        }
        SqliteConnection.ClearAllPools();

        // Rileggi tramite repository (Dapper)
        using var repo2 = new QtoRepository(path);
        var items = repo2.GetPriceItems(new[] { 1 });
        items.Should().ContainSingle();
        var pi = items[0];
        pi.Code.Should().Be("X");
        pi.UnitPrice.Should().Be(50.0);
        pi.Articolo.Should().Be("ART-1");
        pi.Prezzo1.Should().Be(50.0);
        pi.Prezzo2.Should().Be(45.0);
        pi.IncMDO.Should().BeApproximately(30.5, 0.001);
        pi.IncMAT.Should().BeApproximately(55.0, 0.001);
        pi.IncSIC.Should().BeApproximately(2.5, 0.001);
        pi.Flags.Should().Be(512);

        repo.Dispose();
    }
    finally { SafeDelete(path); }
}
```

- [ ] **Step 2: Run test**

```bash
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --filter "FullyQualifiedName~PriceItem_Xpwe" -v quiet
```

Atteso: verde.

## Task 3: UI SetupListinoView — colonne XPWE in DataGrid

**Files:**
- Modify: `QtoRevitPlugin/UI/Views/SetupListinoView.xaml`

Strategia: aggiungere colonne **readonly** per i nuovi campi chiave, visibili solo se hanno valore. L'edit completo si fa tramite editor dialog (Task 4, opzionale).

- [ ] **Step 1: Leggere SetupListinoView.xaml per individuare il DataGrid delle voci**

```
Grep "DataGrid" in SetupListinoView.xaml
```

- [ ] **Step 2: Identificare il DataGrid dei PriceItems (potrebbe essere nella parte ricerca ibrida o preferiti)**

Il layout attuale ha:
- Tabella listini in alto (PriceLists)
- Tabs sotto: Ricerca ibrida / Preferiti progetto / Preferiti personali — tutti mostrano `PriceItem`

Aggiungo colonne alle **3 DataGrid** che listano PriceItem. Pattern: `<DataGridTextColumn Header="Cap" Binding="{Binding SuperChapter}" Width="60"/>` dopo le colonne base.

Le 3 colonne XPWE da aggiungere (dopo `Prezzo` e prima di `Listino`):
- `Prezzo2` · colonna Width=70 · Header "P.2"
- `IncMDO` · Width=50 · Header "MDO%" · StringFormat `0.#`
- `IncSIC` · Width=50 · Header "SIC%" · StringFormat `0.#`

Decisione: NON aggiungere tutte e 17 le colonne — il DataGrid diventa illeggibile. Le 3 sopra coprono i casi d'uso più comuni (alternativa netto, costo manodopera, sicurezza).

- [ ] **Step 3: Lettura file + aggiunta colonne**

Leggere il file, trovare i `<DataGrid.Columns>` dove ci sono binding `{Binding UnitPrice}` o `{Binding Prezzo}`, e aggiungere subito dopo:

```xml
<DataGridTextColumn Header="P.2" Width="70" IsReadOnly="True"
                    Binding="{Binding Prezzo2, StringFormat=\{0:N2\}}"/>
<DataGridTextColumn Header="MDO%" Width="55" IsReadOnly="True"
                    Binding="{Binding IncMDO, StringFormat=\{0:N1\}}"/>
<DataGridTextColumn Header="SIC%" Width="55" IsReadOnly="True"
                    Binding="{Binding IncSIC, StringFormat=\{0:N1\}}"/>
```

Se l'utente trova le colonne scomode (3 colonne extra = 180px), possiamo sempre renderle nascondibili in un follow-up.

- [ ] **Step 4: Build WPF**

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug -v q
```

Atteso: 0 errori.

## Task 4 (opzionale, se Task 3 è ok): Editor "Dettagli voce" popup

**Files:**
- Create: `QtoRevitPlugin/UI/Views/PriceItemEditDialog.xaml(.cs)`
- Create: `QtoRevitPlugin/UI/ViewModels/PriceItemEditViewModel.cs`

Dialog popup triggered da doppio-click su una riga del DataGrid. Mostra tutti i 17 campi XPWE editabili in un form multi-sezione:

1. **Identificazione**: Code, Articolo, Tariffa, Description, ShortDesc, Unit
2. **Prezzi**: Prezzo1..5 (5 textbox in griglia)
3. **Classificazione**: SpCap/Cap/SbCap (3 ComboBox che filtrano per Level, popolati da `ChapterService.GetAll`)
4. **Incidenze**: IncMDO, IncMAT, IncSIC
5. **Extra**: TipoRisorsa, Flags, CnfQt, AdrInternet, DataEP
6. **OK / Annulla**

Salvataggio: aggiungere metodo `QtoRepository.UpdatePriceItem(PriceItem)` (oggi non esiste) con UPDATE su tutte le 17 colonne.

Siccome questo task è corposo (~200 righe XAML + 150 di VM) **lo faccio separato in C-4.1 se serve**. Per ora fermiamoci a Task 3 (visibilità colonne).

- [ ] **Step 1: Stimare se procedere**

Se l'utente testa Task 3 e dice "basta così, edit lo faremo dopo" → **SKIP Task 4**, commit, prossimo plan.

Se l'utente dice "voglio editare i prezzi dalla UI ora" → **procediamo con Task 4** come sotto-task C-4.1.

## Task 5: Test regressione piena

- [ ] **Step 1: Full test suite**

```bash
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --no-build -v quiet
```

Atteso: **485 superati** (484 precedenti + 1 nuovo di Task 2).

## Task 6: Commit

```bash
git add QtoRevitPlugin.Core/Models/PriceItem.cs \
       QtoRevitPlugin/UI/Views/SetupListinoView.xaml \
       QtoRevitPlugin.Tests/Computi/DomainServicesTests.cs
git commit -m "feat(ui): PriceItem + Listino DataGrid con campi XPWE v12 (Plan C-4)"
```

---

## Self-review

- [x] Backward-compat: `UnitPrice` e `Code/Description/Unit` restano intatti
- [x] Import esistenti (Excel/CSV parsers) non toccati — riempiono ancora solo i campi v11
- [x] Dapper mapping automatico (property e colonne stesso nome)
- [x] DataGrid con solo 3 colonne extra (P.2, MDO%, SIC%) per non saturare la UI
- [x] Editor completo dialog rimandato a C-4.1 se l'utente lo richiede

## Scope NON incluso

- Editor dialog completo 17 campi → C-4.1
- ComboBox SpCap/Cap/SbCap per assegnazione capitolo → C-4.1
- UpdatePriceItem nel repository → C-4.1
- Colonne Prezzo3, Prezzo4, Prezzo5 visibili in DataGrid → se serve in futuro, toggle da menu contestuale
- Migrazione auto `SuperChapter` (stringa v11) → `SpCapId` FK v12: rimandato (servirebbe matching euristico che è fragile)
