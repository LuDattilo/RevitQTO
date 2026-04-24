# Plan 1 — Fix contrasto grafico PickEpDialog

> **Contesto:** primo sotto-progetto della spec `2026-04-24-tagging-refactor-cme-redazione-design.md`. Standalone, bassissimo rischio, precede il refactor architetturale per poter testare le schede con UI leggibile.

**Goal:** Correggere i problemi di contrasto nella `PickEpDialog` (card "QUANTITÀ PER ISTANZA" + banda "ANTEPRIMA") in modo che radio label e testi di anteprima siano leggibili su sfondo chiaro.

**Architecture:** Swap di brush sbagliati (`ChromeTextBrush` / `ChromeTextDimBrush`, pensati per chrome scuro) con i brush corretti per fondi chiari (`InkDefaultBrush` / `InkMutedBrush`). Tocco solo `PickEpDialog.xaml`, zero logica, zero C#.

**Tech Stack:** WPF, DynamicResource, tema `QtoTheme.xaml` (già definito, no nuovi token).

**Riferimenti brush:**
- `ChromeText = #ECEFF1` (quasi bianco) — da usare SOLO su fondi scuri
- `ChromeTextDim = #B0BEC5` (grigio chiaro) — stesso discorso
- `InkDefault` (nero) — testo principale su fondi chiari ✓
- `InkMuted` (grigio scuro) — testo secondario su fondi chiari ✓
- `BrandAccentDeep` (teal scuro) — per testo su banda `BrandAccentSoft` teal pallido

**File impattati:** SOLO `QtoRevitPlugin/UI/Views/PickEpDialog.xaml`. Il file `QtoTheme.xaml` NON va modificato — i brush esistono già.

---

## Task 1: Fix label "QUANTITÀ PER ISTANZA" e hint categoria

**Files:**
- Modify: `QtoRevitPlugin/UI/Views/PickEpDialog.xaml:202-211`

- [ ] **Step 1: Aprire il file in editor**

- [ ] **Step 2: Sostituire il Foreground delle due TextBlock**

Vecchio blocco (righe 202-211):
```xml
<TextBlock Text="QUANTITÀ PER ISTANZA"
           FontFamily="{DynamicResource FontMono}"
           FontSize="9" FontWeight="Bold"
           Foreground="{DynamicResource ChromeTextDimBrush}"
           VerticalAlignment="Center" />
<TextBlock x:Name="CategoryHintBlock"
           Margin="8,0,0,0"
           FontSize="10" FontStyle="Italic"
           Foreground="{DynamicResource ChromeTextDimBrush}"
           VerticalAlignment="Center" />
```

Nuovo:
```xml
<TextBlock Text="QUANTITÀ PER ISTANZA"
           FontFamily="{DynamicResource FontMono}"
           FontSize="9" FontWeight="Bold"
           Foreground="{DynamicResource InkMutedBrush}"
           VerticalAlignment="Center" />
<TextBlock x:Name="CategoryHintBlock"
           Margin="8,0,0,0"
           FontSize="10" FontStyle="Italic"
           Foreground="{DynamicResource InkMutedBrush}"
           VerticalAlignment="Center" />
```

- [ ] **Step 3: Commit intermedio NO — commit unico a fine plan.**

## Task 2: Fix radio buttons "Conteggio / Area / Volume / Lunghezza"

**Files:**
- Modify: `QtoRevitPlugin/UI/Views/PickEpDialog.xaml:214-233`

- [ ] **Step 1: Sostituire Foreground di tutti e 4 i RadioButton**

Vecchio (righe 214-233):
```xml
<RadioButton x:Name="RadioCount" GroupName="QtyMode"
             Content="Conteggio (cad.)"
             Checked="OnQtyModeChanged"
             Margin="0,0,14,0" FontSize="11"
             Foreground="{DynamicResource ChromeTextBrush}" />
<RadioButton x:Name="RadioArea" GroupName="QtyMode"
             Content="Area (m²)"
             Checked="OnQtyModeChanged"
             Margin="0,0,14,0" FontSize="11"
             Foreground="{DynamicResource ChromeTextBrush}" />
<RadioButton x:Name="RadioVolume" GroupName="QtyMode"
             Content="Volume (m³)"
             Checked="OnQtyModeChanged"
             Margin="0,0,14,0" FontSize="11"
             Foreground="{DynamicResource ChromeTextBrush}" />
<RadioButton x:Name="RadioLength" GroupName="QtyMode"
             Content="Lunghezza (m)"
             Checked="OnQtyModeChanged"
             FontSize="11"
             Foreground="{DynamicResource ChromeTextBrush}" />
```

Nuovo: replace-all del testo `Foreground="{DynamicResource ChromeTextBrush}"` con `Foreground="{DynamicResource InkDefaultBrush}"` **solo** nelle righe 214-233 (limitato al blocco RadioButton — se ci sono altre occorrenze di `ChromeTextBrush` più avanti verranno gestite nei task successivi).

- [ ] **Step 2: Verificare visivamente dopo il build che le 4 label appaiano in nero leggibile**

## Task 3: Fix banda ANTEPRIMA

**Files:**
- Modify: `QtoRevitPlugin/UI/Views/PickEpDialog.xaml:239-280` (tutta la banda anteprima)

Contesto: la banda ha `Background="{DynamicResource BrandAccentSoftBrush}"` (teal pallido `#B2EBF2`). Testo attuale usa `ChromeTextDimBrush` (grigio chiaro) → invisibile. Va cambiato in `BrandAccentDeepBrush` (teal scuro, contrasto AAA su teal pallido).

- [ ] **Step 1: Leggere la sezione completa per capire quanti TextBlock ci sono**

Run:
```
Read PickEpDialog.xaml offset=239 limit=45
```

- [ ] **Step 2: Per ogni TextBlock dentro la banda (Grid.Row="5") sostituire:**
  - `Foreground="{DynamicResource ChromeTextDimBrush}"` → `Foreground="{DynamicResource BrandAccentDeepBrush}"`
  - `Foreground="{DynamicResource ChromeTextBrush}"` → `Foreground="{DynamicResource InkDefaultBrush}"` (per il contenuto dinamico `PreviewQuantityBlock`, che va in nero pieno per massimo contrasto)

Esempio di trasformazione per il label ANTEPRIMA (riga 250-255):
```xml
<TextBlock Grid.Column="0"
           Text="ANTEPRIMA"
           FontFamily="{DynamicResource FontMono}"
           FontSize="9" FontWeight="Bold"
           Foreground="{DynamicResource BrandAccentDeepBrush}"
           VerticalAlignment="Center" />
```

Esempio per PreviewQuantityBlock (riga 256-262):
```xml
<TextBlock Grid.Column="1"
           x:Name="PreviewQuantityBlock"
           Margin="8,0,0,0"
           FontSize="11"
           FontWeight="SemiBold"
           Foreground="{DynamicResource InkDefaultBrush}"
           VerticalAlignment="Center"
           Text="—" />
```

Nota: aggiungo `FontWeight="SemiBold"` al valore dinamico per rinforzare la leggibilità (la quantità è l'informazione chiave).

- [ ] **Step 3: Stesso trattamento per l'eventuale terzo TextBlock (prezzo totale, se presente) — se ha uno sfondo/foreground che va in collisione, applicare lo stesso swap**

## Task 4: Build di verifica

- [ ] **Step 1: Chiudi Revit** (per rilasciare Dapper.dll / le DLL del plugin)

- [ ] **Step 2: Build**

Run:
```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows -c Debug -v q 2>&1 | grep -E "error|Errori" | tail -5
```

Expected output:
```
    Errori: 0
```

Se appare un errore CS/MSB3231 diverso da 0, STOP e diagnostica prima di procedere.

- [ ] **Step 3: Apri Revit, carica un progetto di test, apri il plugin, apri PickEpDialog**

Verifica a vista:
- Label "QUANTITÀ PER ISTANZA" deve essere **leggibile** (grigio scuro, non chiaro)
- 4 radio button devono avere label **ben visibili** (nere)
- Banda ANTEPRIMA: "ANTEPRIMA" in teal scuro, quantità in nero semibold, leggibile

## Task 5: Commit

- [ ] **Step 1: Stage only il file modificato**

Run:
```bash
git add QtoRevitPlugin/UI/Views/PickEpDialog.xaml
```

- [ ] **Step 2: Commit con messaggio**

Run:
```bash
git commit -m "$(cat <<'EOF'
fix(ui): contrasto PickEpDialog leggibile su fondo chiaro

- Radio QUANTITÀ PER ISTANZA: Foreground ChromeText→InkDefault
- Label card e hint: ChromeTextDim→InkMuted
- Banda ANTEPRIMA: ChromeText/Dim→BrandAccentDeep + InkDefault semibold
  sul valore dinamico, per contrasto AAA su BrandAccentSoft

I brush Chrome* sono pensati per chrome scuro; su PanelSub e
BrandAccentSoft producono un contrasto ~1.2:1 (WCAG fail).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 3: Verifica**

Run:
```bash
git log -1 --oneline
```

Expected: messaggio "fix(ui): contrasto PickEpDialog leggibile su fondo chiaro".

---

## Self-review checklist

- [x] Ogni step ha il codice esatto da sostituire, non solo descrizione
- [x] Tutti i brush citati esistono già nel tema (verificato via `grep` su `QtoTheme.xaml:85-110`)
- [x] Nessun placeholder, nessun "TODO"
- [x] Build step con output atteso
- [x] Commit finale senza toccare altri file (`git add` con path specifico)
- [x] No effetti collaterali su altre View (il change è scope-limitato a `PickEpDialog.xaml`)

## Scope NON incluso in questo plan

- Refactor architetturale Tagging → Redazione CME (plan 2-5)
- Fix contrasto in altre view (se `ChromeTextBrush` è usato male altrove, va in plan separati)
- Cambio del tema o aggiunta nuovi token colore
