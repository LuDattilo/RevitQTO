# Backlog — Prossimo sprint (dopo 2026-04-23)

> Raccolta dei task rimasti aperti al termine della sessione del 23 aprile 2026.
> Tutti i task qui hanno backend/models già implementati; manca solo collegamento UI
> o refactor strutturale. Nessuno è critico per l'uso quotidiano del plugin.

---

## TRACK 1 — Informazioni Progetto: selector inline + Shared Parameters

**Stato**: backend completo e testato, UI da costruire.

**Pezzi già in repo**:
- `QtoRevitPlugin.Core/Models/ProjectInfoFieldKeys.cs` + 7 test
- `QtoRevitPlugin.Core/Models/RevitParamMapping.cs`
- `QtoRevitPlugin.Core/Services/SharedParameterFileHelper.cs` + 7 test
- `QtoRevitPlugin.Core/Data/IQtoRepository.cs` + `QtoRepository.cs`: `GetRevitParamMappings` / `UpsertRevitParamMapping` / `DeleteRevitParamMapping` + 9 test
- `QtoRevitPlugin/Services/RevitParamEnumeratorService.cs`: enumera BuiltIn + Shared di ProjectInformation
- `QtoRevitPlugin/Services/SharedParameterWriterService.cs`: crea SP + binding a ProjectInformation (transazione Revit)

**Da fare (UI)**:

1. **`AddSharedParameterDialog`** (Window WPF modale)
   - TextBox nome parametro (prefisso `CME_<FieldKey>` precompilato editabile)
   - RadioButton: "File SP del progetto corrente" vs "File CME dedicato"
   - Bottoni Annulla / "Crea e aggiungi"
   - Al Click Crea: chiama `SharedParameterWriterService.CreateAndBindProjectInfoParam`

2. **`ProjectInfoFieldRow` UserControl** (riutilizzabile 11 volte)
   - Label (DisplayName del FieldKey)
   - TextBox valore editabile
   - ComboBox dropdown "sorgente" con:
     - `(manuale)`
     - Parametri BuiltIn di ProjectInformation
     - Parametri custom/shared esistenti
     - `(+ Aggiungi parametro condiviso…)` → apre `AddSharedParameterDialog`
   - Binding bidirezionale: al cambio dropdown carica valore da Revit; al cambio TextBox l'utente scrive a mano

3. **Refactor `ProjectInfoView.xaml`** con 11 istanze di `ProjectInfoFieldRow`

4. **VM `ProjectInfoViewModel`**:
   - Al load, per ogni FieldKey: carica mapping da `GetRevitParamMappings(sessionId)`
   - Per ogni mapping non-null: legge valore da Revit via `RevitParamEnumeratorService.ReadValue(doc, paramName)`
   - Popola TextBox se mapping presente e campo vuoto
   - Al cambio dropdown: salva mapping via `UpsertRevitParamMapping`, rilegge valore da Revit

5. **Rimozione bottone legacy "⬇ Eredita da Revit"** dalla view (il selector inline lo sostituisce completamente)

**Stima**: 600-900 righe (UI + VM), ~1 pomeriggio con testing in Revit reale.

**Non blockante**: oggi l'utente può ancora usare "⬇ Eredita da Revit" hardcoded (funziona per Name/ClientName/Address + ricerca custom di `CME_RUP`/`CME_DL`/ecc.).

---

## TRACK 2 — Prompt preferiti al primo uso

**Richiesta utente** (2026-04-23): *"Per ogni voce di listino che viene usata per la prima volta e non è già presente nei preferiti, compare un messaggio 'Vuoi salvare nei preferiti? Sì/No' e quindi viene caricata nei preferiti."*

**Stato**: NON implementabile oggi perché richiede un flusso di assegnazione EP→elemento che al momento **non esiste nella UI**. L'unico `InsertAssignment` vive in `QtoRevitPlugin/UI/ViewModels/CatalogBrowserViewModel.cs:356` ma quel VM non è più raggiungibile dopo il refactor `48427ea` (CatalogBrowser ora embed SetupListinoView).

**Piano quando sarà pronta la scheda Mapping**:

1. Nel VM della scheda Mapping (o dove nascerà il comando "Assegna EP a elemento"):
   ```csharp
   public void OnAssignEpCode(Element revitElement, string epCode, int priceListId)
   {
       var repo = GetActiveSessionRepo();
       var userLib = GetActiveUserLibraryRepo();

       // 1. Crea l'assignment
       var assignment = new QtoAssignment { /* ... */ EpCode = epCode };
       repo.InsertAssignment(assignment);

       // 2. Check se è il primo uso di questo EpCode per la sessione corrente
       var usedNow = repo.GetUsedEpCodes(sessionId);  // metodo già esistente
       // Se è già usato prima di questa insert, usedNow lo conteneva già → non primo uso

       // 3. Se primo uso E non già nei preferiti → prompt
       if (!userLib.IsFavorite(epCode, priceListId))
       {
           var td = new TaskDialog("CME – Voce nuova")
           {
               MainInstruction = $"Salvare «{epCode}» nei preferiti?",
               MainContent = "Le voci preferite sono accessibili rapidamente dalla scheda Listino, anche tra progetti.",
               CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
               DefaultButton = TaskDialogResult.Yes
           };
           if (td.Show() == TaskDialogResult.Yes)
           {
               userLib.AddFavorite(new UserFavorite
               {
                   PriceItemId = /* lookup */,
                   Code = epCode,
                   // ... altri campi
               });
           }
       }
   }
   ```

2. **Opzione "Non chiedere più"**: aggiungere checkbox "Non mostrare per nuove voci" nel TaskDialog, salvare in `SettingsService` e rispettare il flag.

**Precondizione**: implementare prima il flusso assegnazione EP→Element dalla UI (probabilmente come MappingView + command `AssignCommand`).

---

## TRACK 3 — Debiti tecnici backend

### 3.1 ListId → PublicId su UserFavorites (schema v11)

**Rischio architetturale** (code review 2026-04-23, issue #1):
`UserFavorites.ListId` è un FK all'Id AUTOINCREMENT di `PriceLists`. Se un altro PC rigenera la UserLibrary con Id diversi, i preferiti puntano a listini sbagliati o orfani. Il campo `ListName` (snapshot) non basta come identificatore stabile.

**Fix**: aggiungere `PriceListPublicId TEXT` (GUID stabile) accanto a `ListId`. `PriceLists.PublicId` esiste già dal schema v3. Migration:
1. Schema v11: `ALTER TABLE UserFavorites ADD COLUMN PriceListPublicId TEXT;`
2. Backfill: `UPDATE UserFavorites SET PriceListPublicId = (SELECT PublicId FROM PriceLists WHERE PriceLists.Id = UserFavorites.ListId);`
3. Nuovo `UNIQUE(Code, PriceListPublicId)` accanto al vecchio `UNIQUE(Code, ListId)` durante il periodo di transizione
4. Il model `UserFavorite` aggiunge `string? PriceListPublicId`, non rompe API esistenti

**Impatto UI**: il binding è su `ListId` (int) nei VM — **tocca ViewModels**, quindi non si può fare senza coordinarsi con chi modifica l'interfaccia.

**Stima**: 4-6h con test completi.

### 3.2 Refactor `DatabaseInitializer.MigrateIfNeeded` in `class Migrations`

**Osservazione** (code review 2026-04-23, nitpick):
Metodo monolitico di ~140 righe con guardie difensive intrecciate a step numerati. "Al limite del gestibile, al prossimo schema bump merita refactor in `private static class Migrations` con metodi `ToV8(conn, tx)`, `ToV9(...)` etc."

**Rischio**: BASSO (refactor senza cambio logica, ogni migrazione testabile in isolation).
**Urgenza**: BASSA (funziona, solo cleanup).
**Prerequisito**: fare PRIMA del prossimo bump schema (v11).

**Stima**: 2-3h con test di verifica bit-by-bit delle migrazioni esistenti.

### 3.3 Estrai `FavoritesSectionViewModel` da `SetupViewModel`

**Osservazione** (code review 2026-04-23):
`SetupViewModel.cs` a ~700 righe con 3 responsabilità distinte: listini, ricerca, preferiti. Estrarre una `FavoritesSectionViewModel` per le linee 256-598 (collection `Favorites`, commands `ToggleFavorite` / `AddFavoriteFromDrop` / `RemoveUnusedFavorites` / `UseFavoriteInSearch`, metodi helper `RefreshFavoritesUsage` / `RefreshFavoritesHeader` / `SyncFavoriteFlagsOnSearchResults`).

**Rischio**: MEDIO (tocca ViewModel, richiede aggiornare binding XAML).
**Urgenza**: BASSA fino al prossimo grosso feature sui preferiti.

---

## Tabella riassuntiva priorità

| # | Task | Complessità | Blockante | Suggerita dopo |
|---|---|---|---|---|
| T1 | InfoProj selector inline + SP dialog UI | Alta | No | Quando hai tempo per test Revit reale |
| T2 | Prompt preferiti al primo uso | Media | Sì, serve scheda Mapping prima | Dopo T4 |
| T3.1 | ListId → PublicId migration | Alta | No | Prima del sync multi-utente |
| T3.2 | Migrations class refactor | Bassa | No | Prima del prossimo bump schema |
| T3.3 | FavoritesSectionViewModel extract | Media | No | Prima di nuove feature preferiti |
| T4 | **Scheda Mapping EP→Element** (nuova feature) | Alta | Sì, blocca T2 | Prossimo sprint |

---

## Commit di oggi che compongono il backend

Riferimenti git utili per il prossimo sprint:
- `c29e5be` — backend InfoProj (Models + repo CRUD + enumerator)
- `cf5caf9` — fix DataGrid preferiti stretch + UserLibrary guard
- `d174ecb` — rebrand RevitCortex + Copia CME
- `ef190f7` — tracking "usato nel computo" + `GetUsedEpCodes`
- `8d856a0`/`ad04dce` — UserFavorites CRUD + transazione AddFavorite

**Test suite attuale**: 226/226 passing (27 nuovi oggi coperti backend).
