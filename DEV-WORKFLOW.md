# Dev Workflow — QTO Revit Plugin

Guida per ridurre il dev-loop di test su Revit 2025.

---

## Setup una tantum

### 1. Visual Studio (2022 17.8+) — F5 lancia Revit automaticamente

Già configurato via `QtoRevitPlugin/Properties/launchSettings.json`. Due profili disponibili dal dropdown accanto al bottone Play:

- **Revit 2025** — lancia Revit vuoto
- **Revit 2025 + file test** — lancia Revit aprendo `%USERPROFILE%\Documents\QtoTest.rvt`

Per usarlo:
1. Seleziona `QtoRevitPlugin` come Startup Project
2. Imposta il framework target a `net8.0-windows` nel dropdown configurations
3. Premi **F5** → Revit parte con l'addin appena compilato + debugger attaccato

### 2. Rider / VS Code

Stesso `launchSettings.json` funziona. Configura la Run/Debug configuration con "Executable external" → Revit.exe.

### 3. File di test locale (raccomandato)

Crea un `.rvt` di test **fuori da OneDrive** per evitare latenza di sync:

```
%USERPROFILE%\Documents\QtoTest.rvt
```

Minimale: Revit → Nuovo → Template architettonico → salva con quel nome.
Il profilo "Revit 2025 + file test" lo apre in automatico.

### 4. Revit — impedisci apertura automatica dell'ultimo file

Revit 2025 → `File → Options → User Interface`:
- ☐ Disabilita *"Show Home page at startup"* (se attivo e lento)
- Sotto `Recent Files on the Home page` → imposta a 0

Revit → `File → Options → General`:
- ☐ *"Notifications: Check for updates"* → disattiva durante dev

---

## Il nuovo dev-loop

### Modifica logica pura (Models, Engine, Parser, Repository)

Nessun Revit necessario. Esegui i test xUnit:

```bash
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj
```

VS: Test Explorer → Run All. Rider: tasto destro sul progetto Tests → Run Unit Tests.

**Tempo ciclo**: ~2 secondi.

### Modifica UI (XAML, ViewModel, comandi, ribbon, DockablePane)

Richiede Revit. Ciclo:

1. Modifica file
2. **Ctrl+Shift+B** (build) → deploy automatico fa il resto
3. **Chiudi** Revit (se aperto da F5 precedente: Shift+F5 in VS)
4. **F5** → Revit si riavvia con le DLL nuove + debugger attaccato
5. Clicca "Avvia QTO" e testa

**Tempo ciclo**: ~10-20 secondi (con file di test locale).

### Debug con breakpoint

Con F5:
- Breakpoint in `OnStartup` → si ferma al boot Revit
- Breakpoint in `LaunchQtoCommand.Execute` → si ferma al click ribbon
- Breakpoint in ViewModel → si ferma quando la view lo invoca
- **Modifiche during-session**: body di metodi (no XAML, no firme, no nuovi tipi) supportate via Edit & Continue — applica modifica e premi Continua.

---

## Ricaricare un addin senza riavviare Revit (per comandi isolati)

Per modifiche a singoli `IExternalCommand` **senza UI** (no ribbon, no DockablePane), puoi usare **Autodesk Add-In Manager** (gratuito dall'App Store):

1. Revit → *App Store* → installa `AddIn Manager`
2. Dopo il build → clicca `AddIn Manager` nel ribbon → `Load` → seleziona la DLL
3. La DLL viene caricata in runtime → lancia i comandi senza riavviare

**Limite**: funziona solo per `IExternalCommand` isolati. Il nostro plugin usa `IExternalApplication` con DockablePane registrato in `OnStartup` → l'Add-In Manager non è utile nel nostro caso perché l'architettura principale richiede sempre il riavvio.

**Quando può servire**: se in Sprint 2+ scrivi un comando utility slegato dal pane principale (es. "Export XPWE standalone"), testalo con Add-In Manager senza riavviare.

---

## Comandi di emergenza

### Forza redeploy anche se cache "skip"

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows --no-incremental
```

### Disabilita temporaneamente il deploy automatico

```bash
dotnet build QtoRevitPlugin/QtoRevitPlugin.csproj -f net8.0-windows /p:DeployToRevit=false
```

Utile se vuoi testare solo la compilazione senza toccare la cartella addin.

### Rimuovi completamente dalla cartella Revit (pulizia)

```bash
rm -rf "$APPDATA/Autodesk/Revit/Addins/2025/QtoRevitPlugin"
rm -f  "$APPDATA/Autodesk/Revit/Addins/2025/QtoRevitPlugin.addin"
```

### Chiudi Revit da linea di comando

```bash
taskkill /IM Revit.exe /F
```

Utile se Revit rimane "appeso" dopo un crash del plugin.

---

## Note importanti

- **Revit blocca le DLL**: se Revit è in esecuzione, il deploy automatico fallirà con errore "Il file è in uso". Chiudi Revit prima del build.
- **AutoSave Revit**: disattiva `File → Options → General → Autosave every N minutes` durante sviluppo. Evita interruzioni.
- **OneDrive**: se il `.rvt` di test è su OneDrive, aggiungi il path a *Impostazioni OneDrive → Sempre su questo dispositivo* per evitare re-download.
