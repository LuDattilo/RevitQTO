# RevitQTO — CME · Computo Metrico Estimativo

[![CI](https://github.com/LuDattilo/RevitQTO/actions/workflows/ci.yml/badge.svg)](https://github.com/LuDattilo/RevitQTO/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Revit 2025](https://img.shields.io/badge/Revit-2025-blue)](https://www.autodesk.com/products/revit/overview)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)

Plug-in Autodesk Revit per la redazione del **Computo Metrico Estimativo** conforme al D.Lgs. 36/2023 (Nuovo Codice degli Appalti italiano). Workflow file-based `.cme`, tre sorgenti di quantità, export XPWE verso PriMus.

## Feature principali

- **3 sorgenti di quantità**: (A) famiglie Revit con multi-EP, (B) Room/Space con formula NCalc, (C) voci manuali svincolate dal modello
- **Analisi prezzi**: CT (Manodopera + Materiali + Noli + Trasporti) × SG 13–17% × Utile 10% secondo D.Lgs. 36/2023 All. II.14
- **Workflow NP**: Bozza → Concordato → Approvato (RUP)
- **Export XPWE** per import diretto in PriMus, più Excel (.xlsx) con foglio analisi NP, TSV per SA, Delta report
- **Health Check** con matrice 6 stati + AnomalyDetector z-score
- **Filtri vista nativi Revit**: CME_Taggati / CME_Mancanti / CME_Anomalie
- **Persistenza doppia**: SQLite locale (cache performante) + Extensible Storage del modello (verità autoritativa)

## Stack tecnologico

| Layer | Tech |
|---|---|
| Host | Autodesk Revit 2025 (net8.0-windows) |
| Fallback | Revit 2022–2024 (net48) |
| UI | WPF + MVVM via `CommunityToolkit.Mvvm` |
| Async | `Revit.Async` wrapper su `IExternalEventHandler` |
| Persistenza locale | SQLite via `Microsoft.Data.Sqlite` + `Dapper` |
| Formule | `NCalc2` (previsto Sprint 2) |
| Excel | `ClosedXML` (previsto Sprint 5) |
| Test | xUnit + FluentAssertions |

## Struttura progetto

```
RevitQTO/
├── QtoRevitPlugin/              Progetto addin Revit (net8.0-windows + net48)
│   ├── Application/             IExternalApplication + registrazione pane + ribbon
│   ├── Commands/                IExternalCommand (LaunchQtoCommand)
│   ├── UI/                      WPF Views, ViewModels, DockablePane
│   ├── Services/                SessionManager, AutoSave, RecoveryService, CrashLogger
│   └── Theme/                   QtoTheme.xaml — design tokens
├── QtoRevitPlugin.Core/         Logica pura C# (netstandard2.0) — nessuna dipendenza Revit
│   ├── Models/                  QtoResult, PriceItem, NuovoPrezzo, WorkSession, etc.
│   └── Data/                    Schema SQLite + QtoRepository
└── QtoRevitPlugin.Tests/        xUnit test del Core (net8.0)
```

## Build & Deploy

### Prerequisiti

- Visual Studio 2022 17.8+ oppure `dotnet` SDK 8.0
- Autodesk Revit 2025 installato in `C:\Program Files\Autodesk\Revit 2025\` (per il build del target `net8.0-windows`)
- Variabile MSBuild `$(RevitDir2025)` deve risolvere al path API Revit (di default: `C:\Program Files\Autodesk\Revit 2025`)

### Build da CLI

```bash
# Build + deploy automatico in %AppData%\Autodesk\Revit\Addins\2025\QtoRevitPlugin
dotnet build QtoRevitPlugin\QtoRevitPlugin.csproj -f net8.0-windows -c Debug

# Solo build, niente deploy
dotnet build QtoRevitPlugin\QtoRevitPlugin.csproj -f net8.0-windows -c Debug /p:DeployToRevit=false

# Test Core (no Revit needed)
dotnet test QtoRevitPlugin.Tests
```

Il target MSBuild `DeployToRevit2025` copia output + `.addin` manifest direttamente nella cartella addin di Revit 2025. Basta riavviare Revit per vedere le modifiche.

## Stato sviluppo

Piano: 11 sprint su 24 settimane. Sprint 0-1 completati (UI autocontenuta, SQLite, file-based `.cme`). Consultare la documentazione interna per la roadmap dettagliata.

## Autore

**Luigi Dattilo** — [luigi.dattilo@gpapartners.com](mailto:luigi.dattilo@gpapartners.com)

## Licenza

[MIT](LICENSE) © 2026 Luigi Dattilo
