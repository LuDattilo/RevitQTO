# CME UI Redesign Board

**Data:** 2026-04-23  
**Scopo:** tavola unica di verifica UI per un secondo sviluppatore  
**Riferimento visivo:** PNG salvato nel workspace locale

---

## Contesto

Questa tavola rappresenta la direzione meno conservativa concordata per il pannello CME / QTO, ma senza rompere i vincoli architetturali gia` presenti:

- `Listino` resta un modulo autonomo.
- `Listino` mantiene il comportamento flottante tramite `Popout`.
- `Selezione` mantiene il comportamento flottante tramite `Popout`.
- `Fase` non e` una tab separata.
- `Fase` vive dentro `Selezione` ed e` vincolante per filtro e selezione.
- Il cambio fase e` un `soft switch`.
- `Listino` non dipende dalla fase.
- La ricerca del listino e` ibrida.
- I preferiti sono persistenti su due livelli: progetto e personali.

La tavola e` pensata per una verifica tra sviluppatori: serve a controllare gerarchia, flusso, stati e relazioni fra schermate, non a definire pixel finali.

---

## Lettura della tavola

La composizione va letta da sinistra a destra.

1. `Home` iniziale task-first.
2. `Setup progetto`.
3. `Listino` con ricerca ibrida e popout.
4. `Selezione` con `Fase Revit attiva` e `Modalita' computo`.
5. `Tagging / Preview / Export` come flusso successivo e dipendente dal contesto.

La fascia superiore mostra il workflow generale:

`Setup -> Listino -> Selezione -> Tagging -> Verifica -> Export`

La legenda in basso separa:

- pannelli docked
- pannelli popout
- flusso principale sequenziale
- relazioni / note
- navigazione chiave

---

## 1. Home iniziale task-first

### Obiettivo

Sostituire l'empty state passivo con una home operativa.

### Contenuto

- titolo del prodotto
- stato del progetto attivo
- stato sintetico del computo
- metriche rapide
- attivita` rapide:
  - `Setup progetto`
  - `Listino`
  - `Selezione`
  - `Tagging`
  - `Verifica`
  - `Export`

### Regola di UX

La home non deve sembrare una shell vuota con pochi bottoni. Deve comunicare che il workflow parte da qui.

---

## 2. Setup progetto

### Obiettivo

Raccogliere i metadati del computo e prepararli per export e contesto progetto.

### Elementi mostrati

- navigazione laterale interna
- scheda `Generale`
- campi anagrafici e descrittivi
- pulsanti `Ripristina predefiniti`, `Annulla`, `Salva`

### Messaggio chiave

`Setup` e` indipendente dalla fase e serve da base dati del progetto.

---

## 3. Listino

### Obiettivo

Rendere chiaro che `Listino` e` un modulo autonomo, sempre accessibile e con popout.

### Elementi mostrati

- selettore listino attivo
- selettore versione / riferimento
- preferito a stella
- menu overflow
- schede:
  - `Ricerca ibrida`
  - `Preferiti progetto`
  - `Preferiti personali`
- campo ricerca con ambito esplicito
- griglia risultati
- pulsante `Apri in finestra`

### Regole di comportamento

- se non c'e` un listino attivo, la ricerca continua a funzionare sui preferiti
- il listino resta indipendente dalla fase
- i preferiti devono essere separati per scopo e file

### Popout

La vista popup e` visivamente identica nel contenuto essenziale, ma separata dalla shell principale.

Questo deve rimanere vero anche dopo l'integrazione con il resto dell'architettura.

---

## 4. Selezione

### Obiettivo

Trasformare `Selezione` nel workspace operativo principale per il contesto di computo.

### Elementi mostrati

- titolo `Selezione`
- `Fase Revit attiva` in alto nel pannello filtri
- selettore fase
- `Modalita' computo`
- filtri secondari:
  - categoria
  - famiglia
  - stati elementi
- opzioni aggiuntive
- riepilogo elementi correnti
- pulsanti `Cancella`, `Aggiorna`, `Applica`

### Regole di comportamento

- `Fase` non e` un tab esterno
- `Fase` e` il primo filtro del pannello
- la modalita` di computo distingue `Nuovo + Esistente` e `Demolizioni`
- il cambio fase aggiorna le view dipendenti senza conferma modale

### Popout

La versione flottante deve restare un vero workspace parallelo, non un duplicato decorativo.

---

## 5. Tagging / Preview / Export

### Obiettivo

Mostrare che il flusso successivo alla selezione esiste, ma non confonde il primo impatto.

### Contenuto

- scheda `Tagging` con contatori e associazione
- scheda `Preview` come verifica rapida
- scheda `Verifica` come controllo coerenze
- card separate per `Preview` ed `Export`

### Regola di UX

La parte finale del workflow deve apparire come prosecuzione naturale del contesto di selezione, non come area scollegata.

---

## Vincoli di integrazione

Il mockup e` pensato per integrarsi con l'architettura attuale senza rompere il lavoro parallelo sull'AI.

- Nessun cambio ai contratti AI.
- Nessuna dipendenza del layout UI dalla logica AI.
- Le policy di ricerca e stato devono restare deterministiche.
- Il refactor della shell non deve invalidare il modulo `Listino` o il popout.
- `Selezione` deve continuare a dipendere da fase e modalita` computo in modo esplicito.

---

## Criteri di verifica

Un secondo sviluppatore puo` usare questa tavola per rispondere a queste domande:

- La home iniziale comunica un avvio operativo chiaro?
- `Listino` e` ancora un modulo autonomo e flottante?
- `Selezione` mostra `Fase` nel posto giusto?
- Il cambio fase e` leggibile come `soft switch`?
- La separazione tra `Listino`, `Selezione` e `Tagging/Export` e` coerente?
- I preferiti e la ricerca ibrida sono comprensibili come modello?

Se una di queste risposte e` no, la UI non e` ancora allineata.