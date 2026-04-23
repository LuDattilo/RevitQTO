# CME UI Redesign — Home, Listino, Selezione, Fase

**Data:** 2026-04-23  
**Stato:** Design approvato, pronto per review spec  
**Ambito:** revisione UI/UX conservativa sull'architettura tecnica, meno conservativa sul primo flusso utente  
**Esclusioni:** nessuna implementazione in questa fase, nessun cambio di logica Revit-side oltre alla riallocazione dei controlli UI

---

## Obiettivo

Rendere il plugin piu' chiaro e coerente nel primo uso senza rompere i moduli gia' utili. In particolare:

1. sostituire il pannello iniziale passivo con una home operativa
2. mantenere `Listino` come modulo autonomo e flottante
3. portare `Fase` dentro `Selezione` come filtro vincolante del contesto operativo
4. aggiungere il `popout` anche a `Selezione`
5. chiarire il flusso reale: `Setup -> Listino -> Selezione -> Tagging -> Verifica -> Export`

---

## Vincoli approvati

### Moduli e popout

- La logica dei pannelli flottanti per `Listino` deve restare invariata.
- Anche `Selezione` deve poter essere aperta in finestra flottante.
- `Listino` deve restare utilizzabile come modulo autonomo.

### Fase

- `Fase` non deve piu' essere una tab separata.
- `Fase` deve vivere nel pannello filtri di `Selezione`.
- `Fase` deve essere molto visibile, ma resta parte del blocco filtri.
- `Fase` e' vincolante per filtro, selezione e tagging.
- Il cambio fase e' un `soft switch`.
- Il `soft switch` aggiorna automaticamente tutte le view `phase-bound`.

### View phase-bound

Le view considerate `phase-bound` sono:

- `Selezione`
- `Tagging/Mapping`
- `Preview`
- `Struttura Computo`

La view `Listino` non e' `phase-bound`.

### Listino, ricerca e preferiti

- Il passo `Listino` viene prima di `Selezione` nella logica generale.
- `Ricerca` e `Preferiti` sono funzionalita' fondamentali del modulo `Listino`.
- La ricerca deve essere `ibrida`.
- Senza listino attivo la ricerca opera sui `Preferiti`.
- Con listino attivo la ricerca opera su `Listino + Preferiti`.
- I `Preferiti` devono essere persistenti a doppio livello:
  - `Preferiti progetto`
  - `Preferiti personali`
- I due livelli devono essere salvati in file separati per garantire portabilita' e uso in sessioni separate.
- L'ambito di ricerca deve essere selezionato con un `selettore esplicito`.

---

## Verifica dominio Revit API

La scelta di rendere `Fase` vincolante e' coerente con il modello Revit.

- Gli elementi hanno `CreatedPhaseId` e, quando applicabile, `DemolishedPhaseId`.
- Lo stato di un elemento rispetto a una fase e' espresso in API tramite `ElementOnPhaseStatus`.
- Gli stati rilevanti includono `New`, `Existing`, `Demolished`, `Temporary`, `Future`.
- Nel codice attuale il plugin usa gia' `ElementPhaseStatusFilter` in `SelectionService` e `PhaseService` per filtrare e contare gli elementi.

Questo implica che la `Fase` non e' solo un metadato di sessione, ma un vero contesto temporale del computo. La UI deve rifletterlo in modo esplicito.

Riferimenti ufficiali Autodesk:

- [Phase — Revit API Developer Guide](https://help.autodesk.com/cloudhelp/2025/PTB/Revit-API/files/Revit_API_Developers_Guide/Revit_Geometric_Elements/Datum_and_Information_Elements/Revit_API_Revit_API_Developers_Guide_Revit_Geometric_Elements_Datum_and_Information_Elements_Phase_html.html)
- [Element.CreatedPhaseId](https://help.autodesk.com/view/RVT/2026/ENU/?guid=c6032e01-f7cb-b2ea-3312-697d14216a31)
- [ElementOnPhaseStatus](https://help.autodesk.com/view/RVT/2026/ENU/?guid=bfc481cc-11c8-de0b-1d71-7b2ffa711780)

---

## Problemi dell'UI attuale

### Pannello iniziale

- Il dock si apre con uno stato vuoto passivo.
- Le azioni utili sono nascoste dentro `Sessione`.
- Lo switcher comunica "app gia' pronta" ma le schede sono di fatto inutilizzabili senza sessione.
- La gerarchia e' debole: il primo utente non capisce subito il passo successivo.

### Fase separata da Selezione

- La UI attuale tratta `Fase` come workspace autonomo.
- La logica applicativa la usa gia' come filtro del contesto di selezione.
- Questo crea una separazione artificiale fra scelta della fase e query sugli elementi.

### Listino

- `Import`, `Ricerca` e `Sfoglia` convivono ma la priorita' logica non e' chiara.
- Il flusso generale non comunica bene che il listino precede la selezione.
- I preferiti non sono ancora trattati come corpus ricercabile a doppio livello.

---

## Strategia di redesign

### Linea guida

La revisione e' "meno conservativa" nel modello di onboarding, ma conserva i moduli esistenti dove gia' funzionano:

- shell del dock mantenuta
- `Listino` mantenuto come sottosistema
- `Mapping/Tagging` mantenuto come modulo
- `Export` mantenuto come finestra dedicata
- `popout` esteso, non sostituito

### Principio centrale

La home non deve piu' comportarsi come un empty state. Deve comportarsi come un `launchpad operativo` che porta ai moduli reali del plugin.

---

## Nuova architettura dell'esperienza

## 1. Home iniziale

### Scopo

Fornire un avvio task-first al posto della schermata vuota attuale.

### Azioni principali

- `Nuovo computo`
- `Apri computo esistente`
- `Riprendi ultimo`

### Workflow mostrato in home

1. `Setup progetto`
2. `Listino`
3. `Selezione`
4. `Tagging`
5. `Verifica`
6. `Export`

### Regole

- La home non e' un wizard bloccante.
- I moduli restano accessibili, ma il loro ordine viene reso esplicito.
- `Fase` non compare come step separato.
- Lo switcher puo' restare nella shell, ma con peso ridotto rispetto alla home.

### Copy di stato

La home deve comunicare in modo chiaro:

- se non esiste un computo aperto
- se manca un listino attivo
- se il contesto e' pronto per selezione/tagging

---

## 2. Setup progetto

### Scopo

Raccogliere e persistere i metadati del computo nel `.cme` e riusarli nell'export.

### Struttura

La schermata `Informazioni Progetto` resta form-based e contiene:

- denominazione opera
- committente
- impresa
- RUP
- direttore lavori
- luogo
- comune
- provincia
- data computo
- data prezzi
- riferimento prezzario
- CIG
- CUP
- ribasso

### Azioni

- `Eredita da Revit`
- `Salva`

Nessuna modifica di modello concettuale richiesta oltre al riallineamento visuale con la nuova home.

---

## 3. Modulo Listino

### Ruolo nel flusso

`Listino` viene prima di `Selezione`.

### Proprietà fondamentali

- modulo autonomo
- non dipendente dalla fase
- disponibile anche in finestra flottante

### Aree interne

Il modulo viene organizzato in 4 aree stabili:

1. `Importazione / attivazione listino`
2. `Ricerca`
3. `Preferiti`
4. `Dettaglio voce`

### Logica d'uso

- Se non esiste un listino attivo, la UI deve comunicarlo chiaramente.
- `Ricerca` resta disponibile per interrogare i preferiti.
- `Sfoglia listino` e le funzioni basate sul catalogo attivo dipendono da almeno un listino disponibile.

### Stato vuoto senza listino

Il modulo mostra:

- CTA per importare/attivare un listino
- ricerca ancora utilizzabile nei preferiti
- stato esplicito che spiega l'assenza del corpus `Listino attivo`

---

## 4. Ricerca ibrida

### Principio

La ricerca interroga dataset diversi in base all'ambito selezionato.

### Ambiti di ricerca

- `Tutti`
- `Listino attivo`
- `Preferiti progetto`
- `Preferiti personali`

### Comportamento

#### Senza listino attivo

- `Tutti` cerca in `Preferiti progetto + Preferiti personali`
- `Listino attivo` e' disabilitato

#### Con listino attivo

- `Tutti` cerca in `Listino attivo + Preferiti progetto + Preferiti personali`

### Motivazione

Questo evita un campo di ricerca "finto" quando non e' disponibile il corpus di listino, ma non impoverisce il modulo nei casi in cui i preferiti sono gia' un patrimonio utile.

---

## 5. Preferiti a doppio livello

### Tipologie

#### Preferiti progetto

- persistono con il progetto / sessione
- sono portabili
- servono a mantenere un set contestuale di voci frequenti o approvate per quel lavoro

#### Preferiti personali

- persistono trasversalmente alle sessioni
- rappresentano la libreria personale dell'utente

### Persistenza

- file distinti per progetto e personali
- struttura portabile e leggibile
- separazione intenzionale dal DB principale per non accoppiare eccessivamente la mobilita' della libreria ai dati di computo

### Implicazioni UI

Dal dettaglio di una voce devono essere disponibili azioni separate:

- `Aggiungi/Rimuovi da preferiti progetto`
- `Aggiungi/Rimuovi da preferiti personali`

---

## 6. Selezione come workspace principale

### Ruolo

`Selezione` diventa il principale workspace operativo dopo `Setup` e `Listino`.

### Disponibilità

- usabile nel dock
- apribile in `popout`

### Struttura

La schermata deve avere un pannello filtri dominante e una griglia risultati centrale.

### Filtro principale: Fase Revit attiva

Il primo blocco del pannello filtri e' `Fase Revit attiva`.

Vincoli:

- non e' una tab separata
- e' visibile dentro i filtri
- e' visivamente evidenziata piu' degli altri filtri
- e' il controllo principale del contesto operativo

### Altri filtri

- categoria
- nome
- eventuali filtri rapidi aggiuntivi

### Toolbar risultati

- `Isola in vista`
- `Nascondi`
- `Reset vista`
- `Aggiorna`

---

## 7. Modalità computo dentro Selezione

La sola scelta della fase non basta a spiegare l'intento di computazione. Serve un secondo controllo esplicito.

### Nuovo blocco UI

Subito sotto `Fase Revit attiva` compare `Modalita' computo`.

### Modalita' iniziali

- `Nuovo + Esistente`
- `Demolizioni`

### Motivazione

Nel dominio reale demolizioni e nuovo spesso vengono computati in momenti separati o in computi distinti. La UI deve renderlo esplicito.

### Comportamento

- La modalita' selezionata influisce sulle query di selezione.
- La fase resta vincolante.
- La modalita' evita ambiguita' fra contesto temporale e intento di computazione.

---

## 8. Soft switch di fase

### Regola

Il cambio fase non richiede conferma modale. E' un `soft switch`.

### Effetti

Al cambio fase si aggiornano automaticamente:

- `Selezione`
- `Tagging/Mapping`
- `Preview`
- `Struttura Computo`

### Esclusione

`Listino` non viene aggiornato dal cambio fase.

### Copy e feedback

Il cambio di fase deve essere visibile e tracciabile nella UI, con:

- etichetta di fase corrente
- stato contestuale aggiornato
- refresh dei contenuti dipendenti dal contesto

Nessuna conferma, ma nessun refresh silenzioso e opaco.

---

## 9. Tagging / Mapping

### Principio

`Tagging/Mapping` resta un modulo autonomo, ma diventa chiaramente `phase-bound`.

### Stato

La schermata deve mostrare in modo esplicito che sta lavorando nel contesto della fase attiva.

### Struttura conservata

Il modulo mantiene i tre sottosistemi gia' presenti:

- `Famiglie`
- `Locali`
- `Voci manuali`

### Comportamento

- al cambio fase si riallinea automaticamente
- la UI deve dichiarare che il contesto di fase influisce sul tagging

---

## 10. Preview e Struttura Computo

### Preview

`Preview` diventa una vista di verifica rapida e dashboard, ma va marcata come `phase-bound`.

### Struttura Computo

`Struttura Computo` resta la vista tree-based dei capitoli.

Regola:

- e' `phase-bound` quando mostra aggregazioni dipendenti dalla fase
- deve aggiornarsi automaticamente al cambio fase in quel caso

---

## 11. Export

### Ruolo

`Export` resta finestra dedicata.

### Input

- usa i metadati da `Informazioni Progetto`
- opera sul contesto di computo corrente

### Nessun cambio architetturale richiesto

Il redesign tocca soprattutto la leggibilita' del percorso che porta all'export, non il modulo export in se'.

---

## Impatto sui componenti esistenti

### Da conservare

- `QtoDockablePane` come shell
- `SetupView`
- `SetupListinoView`
- `CatalogBrowserWindow`
- `SelectionView`
- `MappingView`
- `PreviewView`
- `ComputoStructureView`
- `ExportWizardWindow`
- `PopoutWindow`

### Da riallineare

- home iniziale del dock
- gerarchia visiva dello switcher
- eliminazione della tab `Fasi` come workspace separato
- integrazione di `Fase` e `Modalita' computo` dentro `Selezione`
- estensione del pattern `popout` a `Selezione`
- stato e affordance del modulo `Listino`

---

## Mockup e riferimento visuale

Durante il brainstorming sono state validate mockup locali per:

- confronto `attuale` vs `proposta conservativa`
- confronto `conservativa guidata` vs `meno conservativa`
- tavola unica della direzione meno conservativa

Queste mockup fungono da riferimento qualitativo per il successivo implementation plan, ma non sono fonte normativa superiore a questa spec.

---

## Non-obiettivi

- non ridisegnare l'intero design system WPF
- non cambiare il modello dati del computo fuori dagli aspetti necessari ai preferiti persistenti
- non implementare ancora nuovi filtri parametrici complessi in `Selezione`
- non riscrivere `Tagging/Mapping` come nuovo modulo da zero
- non introdurre un wizard obbligatorio multi-step

---

## Rischi e mitigazioni

### Rischio 1: eccesso di onboarding

Se la home diventa troppo "guidata", puo' rallentare l'utente esperto.

Mitigazione:

- home task-first ma non bloccante
- moduli sempre raggiungibili

### Rischio 2: ambiguita' fase vs demolizioni

La sola fase puo' essere interpretata come filtro sufficiente anche quando il caso d'uso richiede distinzione demolizioni.

Mitigazione:

- blocco `Modalita' computo` esplicito

### Rischio 3: confusione nei preferiti

Due livelli di preferiti possono risultare opachi se la provenienza non e' visibile.

Mitigazione:

- badge chiari
- azioni separate nel dettaglio voce
- selettore esplicito di ambito

---

## Test e validazione richiesti nella fase successiva

### Validazione UX

- verificare il primo flusso: `Nuovo computo -> Setup -> Listino -> Selezione`
- verificare che il ruolo del `Listino` sia chiaro prima della selezione
- verificare che `Fase` dentro `Selezione` sia percepita come vincolante

### Validazione funzionale

- popout `Listino` invariato
- nuovo popout `Selezione`
- `soft switch` di fase con refresh corretto delle view `phase-bound`
- ricerca ibrida coerente con assenza/presenza di listino attivo
- preferiti progetto/personali persistiti in file distinti

---

## Output atteso della fase di implementazione

Il successivo implementation plan dovra' coprire almeno:

1. refactor della home iniziale del dock
2. rimozione della view `Fasi` come tab autonoma
3. integrazione di `Fase` e `Modalita' computo` in `Selezione`
4. aggiunta del `popout` a `Selezione`
5. redesign del modulo `Listino` con ricerca ibrida e preferiti a doppio livello
6. wiring del `soft switch` fase verso le view `phase-bound`

