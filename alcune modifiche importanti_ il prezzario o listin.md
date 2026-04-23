<img src="https://r2cdn.perplexity.ai/pplx-full-logo-primary-dark%402x.png" style="height:64px;margin-right:32px"/>

# alcune modifiche importanti: il prezzario o listino deve essere su un pannello separato che si attiva con un pulsante a fianco di quello che attiva il programma, la logica ON OFF deve essere la setssa del pannello CME. Il pannello del listino deve prevedere una sezione dei preferiti che sono le voci che utilizzerò o intendo utilizzare nel computo per il progetto. Potrei voler salvare i preferiti in un file separato da salvare per altro computo simile. l'assegnazione delle quantità deve avvenire secondo questa logica: seleziono la voce di prezzario sia tra i preferiti sia nel pannello del prezzario, seleziono gli oggetti attraverso la logica dei filtri o manualmente o comuwnei attraverso la logica implementata per i comandi di revit, e assegnano le quantità degli oggetti selezionati alla voce di prezzario, oppure pura seleziono gli oggetti e poi la voce di prezzario e quindi le quantità alla voce. la logica di selezione del parametro quantitivo, metri quadri, metri, unità etc. deve poter essere salvata in un file json per futuri utilizzi. attenzione particolare prima di tutto al fatto che il modello potrebbe essere stato mostrato tra una sessione di revit e una ltra per cui all'inizio del compito, se parto da un file cme esistente, non nuovo file quindi, deve esserci la possibilità a scelta dell'utente di fare un check per capire se sono state fatte nodifiche, elementi eliminati o cambiati, e accettare o meno el modifiche al cme previa verifica anche attraverso selezione in revit degli elementi modificati con evidenza della voce di prezzario cui quelli elementi sono stati assegnati nel cme. chiara la logica delle modifiche?

Tutto molto chiaro. Prendo nota di queste specifiche nuove e le documento in modo organizzato.

***

## Recap Specifiche — Sessione 22/04 sera

### 1. Architettura UI: due pannelli separati

Il ribbon Revit avrà **due pulsanti distinti**:


| Pulsante | Pannello | Logica ON/OFF |
| :-- | :-- | :-- |
| `[QTO Computo]` | Pannello CME principale | Toggle DockablePane (già implementato) |
| `[Prezzario]` | Pannello Prezzario separato | Stessa logica toggle DockablePane |

I due pannelli sono **indipendenti** (possono essere aperti entrambi contemporaneamente) ma comunicano: selezionare una voce nel Prezzario la propaga automaticamente nel pannello CME come voce attiva.

***

### 2. Pannello Prezzario — struttura interna

```
┌─ PANNELLO PREZZARIO ─────────────────────────────────┐
│ [Listino attivo: Toscana 2025/1 ▼]  [Cambia listino] │
│                                                       │
│ 🔍 Cerca voce...    [Cerca]                          │
│ ─────────────────────────────────────────────────────│
│ ⭐ PREFERITI                        [Salva set ▼]    │
│   ▸ A.02.001 – Muratura mattoni     €  42,00 /m²     │
│   ▸ B.01.003 – Intonaco civile      €  18,50 /m²     │
│   ▸ D.04.010 – NP – Cappotto 10cm  €  65,00 /m²     │
│   [+ Aggiungi preferito]  [Rimuovi]                  │
│ ─────────────────────────────────────────────────────│
│ 📋 TUTTE LE VOCI                                     │
│   Cap. A – Opere Strutturali                         │
│     ▸ A.01.001 – Scavo a mano…     €  12,00 /mc     │
│     ▸ A.02.001 – Muratura…         €  42,00 /m²     │
│   Cap. B – Finiture                                  │
│     …                                               │
│ ─────────────────────────────────────────────────────│
│ [✓ ASSEGNA AGLI ELEMENTI SELEZIONATI]               │
└─────────────────────────────────────────────────────┘
```

**Set di preferiti**: salvabile come file `.json` nella cartella del progetto o in una cartella libera dell'utente. Riutilizzabile per computi simili (es. "Preferiti_Ristrutturazione_Civile.json").

***

### 3. Flusso di Assegnazione Quantità (doppia direzione)

Il processo supporta due ordini equivalenti:

**Modalità A — Prima la voce, poi gli elementi:**

1. Seleziono voce EP nei Preferiti o nel Prezzario → diventa "voce attiva"
2. Seleziono elementi in Revit (filtri, manuale, etc.)
3. Premo `[ASSEGNA]` → quantità degli elementi selezionati assegnate alla voce attiva

**Modalità B — Prima gli elementi, poi la voce:**

1. Seleziono elementi in Revit
2. Seleziono voce EP nel pannello
3. Premo `[ASSEGNA]` → stessa operazione

In entrambi i casi, il parametro quantitativo (m², m, m³, n.) viene determinato dalla **Regola di Mappatura** salvata in JSON.

***

### 4. Regole di Mappatura — file JSON per categoria

```json
{
  "MappingRules": [
    {
      "RevitCategory": "OST_Walls",
      "DefaultParam": "Area",
      "AllowedParams": ["Area", "Volume", "Length", "Count"],
      "CustomFormula": null,
      "UnitDisplay": "m²",
      "RoundingDecimals": 2,
      "VuotoPerPieno": true
    },
    {
      "RevitCategory": "OST_Floors",
      "DefaultParam": "Area",
      "AllowedParams": ["Area", "Volume", "Count"],
      "CustomFormula": null,
      "UnitDisplay": "m²",
      "RoundingDecimals": 2,
      "VuotoPerPieno": false
    }
  ]
}
```

Il file si salva come `QTO_MappingRules.json` nella cartella `%AppData%\QtoPlugin\` (globale, riusabile tra progetti) o nella cartella del progetto (locale, sovrascrive la globale).

***

### 5. Avvio da file CME esistente — Model Diff Check

Questa è la parte più delicata. Al caricamento di un `.cme` esistente, il plugin deve:

**Step 1 — Dialog di benvenuto:**

```
┌─ Apri CME esistente ────────────────────────┐
│ File: Edificio_A_v3.cme                     │
│ Ultima sessione: 18/04/2026 14:32           │
│                                             │
│ Vuoi eseguire un CHECK delle modifiche      │
│ al modello Revit rispetto all'ultima        │
│ sessione di computo?                        │
│                                             │
│  [✓ Sì, verifica modifiche]  [Salta]       │
└────────────────────────────────────────────┘
```

**Step 2 — ModelDiff Analysis** (se l'utente sceglie "Sì"):

Il plugin confronta:

- **ElementId** presenti nell'ES / nel CME vs ElementId esistenti nel modello attuale
- **Hash geometrico** (UniqueId + parametri chiave) per rilevare modifiche di forma/dimensione

Produce tre liste:


| Lista | Contenuto | Colore |
| :-- | :-- | :-- |
| 🟢 Invariati | Elementi presenti e identici | — (non mostrati) |
| 🟡 Modificati | Geometria/parametri cambiati | Giallo in modello |
| 🔴 Eliminati | ElementId non più esistenti nel modello | Rosso nel report |
| 🔵 Nuovi | ElementId presenti nel modello ma non nel CME | Blu in modello |

**Step 3 — Pannello di Riconciliazione:**

```
┌─ MODIFICHE MODELLO RILEVATE ─────────────────────────────────┐
│ Rispetto alla sessione del 18/04/2026:                       │
│                                                              │
│ 🔴 ELIMINATI (3 elementi)                                    │
│   [→] Muro ID 112345  → era assegnato a: A.02.001 Muratura  │
│   [→] Muro ID 112346  → era assegnato a: A.02.001 Muratura  │
│   [→] Solaio ID 98712 → era assegnato a: C.01.002 Solaio lat│
│                                                              │
│ 🟡 MODIFICATI (2 elementi) — quantità cambiate              │
│   [→] Muro ID 112350  Area: 12,4 m² → 15,2 m²  (+2,8 m²)  │
│        assegnato a: A.02.001 Muratura                        │
│   [→] Finestra ID 44210  Count invariato, posizione cambiata │
│                                                              │
│ 🔵 NUOVI (5 elementi) — non ancora computati                │
│   [→] Muro ID 115001  Area: 8,3 m²  (non assegnato)         │
│   [→] Muro ID 115002  Area: 6,1 m²  (non assegnato)         │
│   [+ altri 3 elementi...]                                    │
│                                                              │
│ Per ogni riga: [Seleziona in Revit] [Isola] [Accetta] [Ignora]│
│                                                              │
│ [✓ Accetta TUTTO]  [✗ Ignora TUTTO]  [Revisione manuale ▼]  │
└──────────────────────────────────────────────────────────────┘
```

**Step 4 — Applicazione modifiche al CME:**

- **Elementi eliminati accettati**: le voci EP corrispondenti vengono marcate come "quantità ridotta" e i totali ricalcolati.
- **Elementi modificati accettati**: le quantità nel CME vengono aggiornate con i nuovi valori.
- **Nuovi elementi**: rimangono nel pool "da computare" e compaiono nel Health Check come ❌ Non computati.
- **Ignorati**: rimangono nel CME con i valori originali ma con un flag `[⚠ Dati non aggiornati]` visibile nel report.

***

### 6. Implementazione tecnica — punti chiave

**Snapshot per il ModelDiff:**
La sessione `.cme` deve salvare, per ogni elemento assegnato:

```json
{
  "ElementId": 112345,
  "UniqueId": "a1b2c3d4-...",
  "SnapshotQty": 12.4,
  "SnapshotHash": "sha256:8f3a...",  // hash di UniqueId + parametri chiave
  "AssignedEP": ["A.02.001"],
  "LastUpdated": "2026-04-18T14:32:00Z"
}
```

**Hash geometrico leggero:**

```csharp
private string ComputeElementHash(Element elem)
{
    var sb = new StringBuilder();
    sb.Append(elem.UniqueId);
    sb.Append(elem.LookupParameter("Area")?.AsDouble().ToString("F6"));
    sb.Append(elem.LookupParameter("Volume")?.AsDouble().ToString("F6"));
    sb.Append(elem.GetPhaseStatus(doc.ActiveProjectPhase).ToString());
    return Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(sb.ToString())))[..12];
}
```

**Regola generale sulla gestione modifiche:**

- Il CME originale non viene mai modificato automaticamente senza conferma esplicita dell'utente.
- Tutte le azioni del pannello di riconciliazione sono reversibili fino al salvataggio esplicito.
- Il log delle modifiche accettate viene scritto nel `.cme` con timestamp.
<span style="display:none">[^1][^2]</span>

<div align="center">⁂</div>

[^1]: startup.log

[^2]: questa-relazione-tecnica-delin-jhiAKXPWRju_kKlzHrqbcQ.md

