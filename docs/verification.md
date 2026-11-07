# Lokale Verifikation

Stand 7. November 2026, Europe/Berlin. Ausgeführt auf Windows x64 mit portablem .NET SDK 10.0.403, Node.js 24.15.0, Chromium Headless Shell und PostgreSQL 17.11. Testanwendungen und Datenbank waren ausschließlich an Loopback gebunden. Der Testbestand enthielt nur synthetische Daten.

| Prüfung | Ergebnis |
| --- | --- |
| NuGet-Restore mit --locked-mode | Erfolgreich für API, Unit und Integration |
| .NET Release-Build und Publish | Erfolgreich, ohne Compilerwarnungen |
| npm ci, TypeScript, ESLint und Vite | Erfolgreich |
| npm audit --omit=dev | Keine bekannten Schwachstellen gemeldet |
| xUnit-Domäne und Analyse | 19 bestanden |
| xUnit-PostgreSQL, Providervertrag und Schlüssel-Restore | 13 bestanden |
| Playwright | 3 bestanden |
| Frische Datenbanken und Migrationen | Demo und private Instanz starteten mit Readiness 200. Demo importierte 210.240 Intervalle in 53 Fenstern. |
| Anwendungsneustart | Messrevision und Datenabdeckung blieben erhalten. Lokale Anmeldung funktionierte weiterhin ohne Setup-Secret. |
| PostgreSQL-Backup und Restore | pg_dump und pg_restore auf leere Zieldatenbank. Anmeldung, Anlagenkonfiguration und Tarif über HTTP geprüft. |
| OpenAPI | Die JSON-Beschreibung wurde vom Release-Prozess geliefert. Analyse-, Tarif- und Simulationspfade waren enthalten. |
| Compose 2.40.3 | Basis und kombinierte Overlays mit config --quiet validiert. Alle vier gepinnten Image-Tags lieferten HTTP 200 in den offiziellen Registries. Der lokale Engine-Socket fehlte. |
| Sungrow-Live-Abnahme | Offen. Vertragstests verwenden ausdrücklich synthetische HTTP-Antworten und Messpunkte. |

Vite meldet einen Größenhinweis für den ECharts-haltigen Bundle-Chunk von etwa 878 kB vor Kompression. Der Szenarioeditor wird separat geladen. Der Build war erfolgreich.

## Tatsächlich ausgeführte Befehle

Mit lokalem SDK-Pfad und mit separater PostgreSQL-Testverbindung wurden diese Projektbefehle ausgeführt.

```sh
dotnet restore src/Api/Api.csproj --locked-mode
dotnet build src/Api/Api.csproj -c Release --no-restore
dotnet publish src/Api/Api.csproj -c Release --no-restore
dotnet restore tests/Unit/Unit.csproj --locked-mode
dotnet test tests/Unit/Unit.csproj -c Release --no-restore
dotnet restore tests/Integration/Integration.csproj --locked-mode
dotnet test tests/Integration/Integration.csproj -c Release --no-restore
```

Für den Publish lag das gebaute Vite-Frontend unter src/Api/wwwroot. Der veröffentlichte Prozess wurde mit getrennten Datenbanken und Zustandsordnern für Demo und privaten Modus gestartet. Mit Playwright wurden Anwendung, Download und API-Autorisierung gegen diese Prozesse geprüft.

```sh
cd src/Web
npm ci
npm run typecheck
npm run lint
npm run build
npm audit --omit=dev
npx playwright install chromium --only-shell
npm run test:e2e
```

Für die lokale Wiederherstellung wurden die Testinstanz-Daten mit PostgreSQL 17.11 als Custom-Dump gesichert und in eine leere Datenbank zurückgespielt. Der Zustandsordner inklusive Schlüsselring wurde separat kopiert. Der gestartete Zielprozess erhielt kein Setup-Passwort.

```sh
pg_dump -Fc -f private.dump solar_final_private
pg_restore --exit-on-error -d solar_final_restore private.dump
docker compose --env-file .env.example config --quiet
docker compose --env-file .env.example -f compose.yaml -f compose.owner.yaml -f compose.sungrow.yaml config --quiet
```

## Leistung

Referenzsystem Intel Core i7-10700K, 16 logische CPUs und 32 GiB RAM. Der lokale API- und PostgreSQL-Prozess liefen auf demselben Host. Nach je einer Aufwärmanfrage wurden pro Leseendpunkt 25 aufeinanderfolgende Anfragen einschließlich JSON-Parsing gemessen. Simulationszeiten enthalten Jobannahme und Polling mit 250 ms Abstand. Die Messwerte gelten für diesen Rechner.

| Ablauf | Gemessen | Ziel |
| --- | ---: | ---: |
| Monatsübersicht Oktober 2026, p95 | 89 ms | < 2 s |
| Tagesdetail 25. Oktober 2026 mit 25 Stunden, p95 | 6 ms | < 2 s |
| 12 Monate, reine Speichererweiterung | 1,39 s | < 5 s |
| 12 Monate, Speicher, Verschiebung und Zusatzbedarf kombiniert | 2,40 s | < 60 s |

Reproduzierbar mit `node tests/E2E/performance.mjs` und `SOLAR_E2E_URL` auf die laufende Demo-Instanz.

