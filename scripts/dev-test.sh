#!/usr/bin/env bash
# dev-test.sh — chiude Revit, ricompila, rilancia.
# Usage: ./scripts/dev-test.sh [path-to-rvt]
#   path-to-rvt opzionale: apre il file dato dopo il boot.
set -e

REVIT_EXE="/c/Program Files/Autodesk/Revit 2025/Revit.exe"
PROJECT="QtoRevitPlugin/QtoRevitPlugin.csproj"

# 1. Kill Revit se running (ignora errore se non è in esecuzione)
taskkill //IM Revit.exe //F 2>/dev/null || true
sleep 1

# 2. Build (auto-deploy attivo via MSBuild target)
echo "→ Build + deploy…"
dotnet build "$PROJECT" -f net8.0-windows --verbosity minimal | tail -5
if [ $? -ne 0 ]; then
    echo "✗ Build fallito"
    exit 1
fi

# 3. Rilancia Revit (opzionale: con file di test)
if [ -n "$1" ]; then
    echo "→ Avvio Revit 2025 con $1"
    "$REVIT_EXE" "$1" &
else
    echo "→ Avvio Revit 2025"
    "$REVIT_EXE" &
fi

echo "Revit avviato (PID $!). Quando chiudi Revit manualmente o runni di nuovo lo script, ricompila."
