# Solar Insight – Hinweise für Claude Code

Verbindliche Arbeitsregeln stehen in [AGENTS.md](AGENTS.md) (Backlog, Umsetzungsstand, lokale Bereitstellung). Diese Datei ergänzt sie und ersetzt sie nicht. Bei Widersprüchen gilt `AGENTS.md`.

## Architektur

- **Backend:** ASP.NET Core 10 (`src/Api`), Minimal-API unter `/api/v1`. Schichten: `Domain` (Energie-, Kalender-, Tarif- und Dispatch-Kern ohne Abhängigkeiten), `Application` (Analysen: `Analysis`, `EnergyInsights`, `EnergyBalance`, `StorageInsights`, `StoragePotential`, `Potential`), `Infrastructure` (EF Core + PostgreSQL, Sungrow-Adapter, Importe), `Api` (Endpunkte, Hintergrunddienste, Sicherheit).
- **Frontend:** React 19 + ECharts 6 + Vite (`src/Web`). Seiten in `main.tsx`, Übersicht in `Overview.tsx`, Flüsse/Speicher in `EnergyInsights.tsx`, Potenzial in `StoragePotential.tsx` und `Scenario.tsx`.
- **Daten:** `Readings` (Intervalle, UTC, kWh je Intervall), `Aggregates` (Sungrow-Tageswerte, `Origin = provider`; Zählerlücken `counter-gap`), `LiveMeasurements` (Momentanwerte und historische SOC-Stichproben `sungrow-device-history-soc-v1`, SOC als Anteil 0–1).
- `Queries.Read` lädt einen Zeitraum als `DataView`. Historische SOC-Stichproben werden dort an Intervalle mit fehlendem SOC angehängt (Wert am Intervallende).

## Fachliche Regeln

- Autarkiegrad = (Hausverbrauch − Netzbezug) / Hausverbrauch. Eigenverbrauchsquote = (PV − Einspeisung) / PV. Beide nur über Tage bzw. Intervalle, für die beide Größen vorliegen. Die gepaarte Abdeckung wird mit ausgegeben (`AutarkyCoverage`, `RetentionCoverage`).
- Die Energiebilanz (`EnergyBalance`) wählt je Kalendertag genau eine Quelle: vollständige, plausible Intervalle oder Sungrow-Tageswerte. Doppelzählungen sind so ausgeschlossen. Ohne Batteriekanäle bleibt „PV direkt oder Speicher“ als nicht trennbarer Anteil getrennt ausgewiesen.
- `ConfirmedAcPaths` kennzeichnet bestätigte AC-Messpfade. Es sperrt keine Bilanz, die aus Messwerten schließt, wird aber angezeigt. Die Intervallsimulation (`Potential`) setzt es weiterhin voraus.
- Gemessen, berechnet, geschätzt und nicht verfügbar müssen in API und UI unterscheidbar bleiben.

## Befehle (lokal, portable Tools unter `.local/`)

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.local"; $env:NUGET_PACKAGES = "$PWD\.local\nuget"
.local\dotnet\dotnet.exe build src/Api/Api.csproj -c Release
.local\dotnet\dotnet.exe test tests/Unit/Unit.csproj -c Release
# Integrationstests brauchen SOLAR_TEST_DB (eigene Test-PostgreSQL, nie die private Datenbank)
cd src/Web; ..\..\.local\node-v24.15.0-win-x64\npm.cmd run typecheck; ...\npm.cmd run lint; ...\npm.cmd run build
```

Bereitstellung: Demo auf Port 8080 über Docker Compose (`.local/ci-deploy-demo.cmd`), private Instanz auf Port 8081 über `.local/ci-deploy-private.ps1` bzw. `.local/owner-instance/start.ps1` (portables PostgreSQL auf 127.0.0.1:55433). Readiness: `GET /health/ready`.

## Vorsicht

- `.env`, `secrets/`, `.local/` und `data/` (private Sungrow-CSV-Exporte) nie committen oder auslesen, wenn es nicht nötig ist.
- Keine Schreibzugriffe auf die private Datenbank ohne ausdrückliche Freigabe. Lesende Analysen gehen über `.local/owner-instance/csv-db.ps1 -Sql <datei>`.
- Ein manueller Nachimport (`Import.cs`) überschreibt Tageswerte desselben Tages mit API-Werten. Hintergrundabgleich, Zählerimporte und der Batterie-Nachimport aus Mitternachtszählern (`BatteryHistory`, Revision `sungrow-device-counter-day-v1`) schreiben nur fehlende Werte (`fillOnly`).
- Arbeite auf Feature-Branches, nicht auf `main`.
