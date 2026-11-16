# Solar Insight

**Selbst hostbare PV-, Speicher- und Kostenanalyse für Sungrow-Anlagen (iSolarCloud)** – gebaut mit ASP.NET Core 10, React 19, Apache ECharts 6 und PostgreSQL 17.

👉 **[Live-Demo im Browser ausprobieren (GitHub Pages)](https://frederikt.github.io/SolarInsight/)** *(läuft komplett im Browser mit 24 Monaten synthetischen Beispieldaten – ohne Anmeldung und ohne Installation)*

![Übersicht mit synthetischen Demodaten](docs/demo-uebersicht.png)

---

## Was bietet Solar Insight?

- **Jetzt & Heute (Live-Übersicht):** Aktuelle Leistungen für Solar (AC/DC), Hausverbrauch, Netzbezug/Einspeisung, Batterieladung/-entladung und Ladezustand (SOC) mit intelligentem Cache (5-Minuten-Takt bei geöffneter Übersicht, 10-Minuten-Takt im Hintergrund).
- **Energiebilanz & Energieflüsse:** Sankey-Energieflussdiagramm und Aufschlüsselung *„Woher kam mein Haushaltsstrom?“* sowie *„Wohin ging mein PV-Ertrag?“* – lückenlos berechnet aus 15-Minuten-Intervallen und Sungrow-Tageswerten, ohne erfundene Aufteilungen.
- **Zeiträume & Langzeitvergleiche:** Tag, ISO-Kalenderwoche, Monat, Quartal, meteorologische Jahreszeiten, Jahr und *„Seit Datenbeginn“* mit Vorjahres-/Vorperiodenvergleich, Jahreskalender (inkl. Markierung von Ausfall- und Nachholtagen) sowie Monatsvergleich über mehrere Jahre.
- **Stromkosten & Ersparnis:** Monats- und Gesamtrechnung mit zeitlich abgrenzbaren Tarifen (Arbeitspreis, Einspeisevergütung, Grundpreis) inklusive Gegenrechnung *„Ohne PV-Anlage“* und *„Ohne Speicher“*.
- **Speicherzustand & Potenzialanalyse:**
  - *Speicherzustand über die Jahre:* Vollzyklen, Jahres-/Monats-Wirkungsgrad, Anteil am Hausverbrauch und automatische Kapazitätsschätzung aus SOC- und Zählerdaten.
  - *Was bringt ein größerer Speicher?* Tagesbilanzmodell mit Übertrag für +2,5 bis +15 kWh (oder eigene Größe) inkl. vermiedenem Netzbezug, Ersparnis und Amortisation.
  - *Überschussladen & detaillierte 15-Minuten-Simulation:* Kombination aus Speichererweiterung, Lastverschiebung und Zusatzverbrauch (z. B. Elektroauto / Wärmepumpe).
- **Datenqualität & historischer Batterie-Nachimport:** Übersicht aller vorhandenen Tageswerte, Intervalle und SOC-Stichproben je Monat sowie ein Nachimport-Werkzeug, das historische Batterie-Tageswerte rückwirkend aus den Mitternachts-Gesamtzählern des Sungrow-Wechselrichters rekonstruiert.
- **Hell-/Dunkel-Modus & Mobile-First:** Durchgängiges helles und dunkles Farbschema (inkl. Diagrammen), optimiert bis hinunter auf 375 px Bildschirmbreite.

---

## 1. Schnellstart: Lokal im Demo-Modus testen

Wenn du Solar Insight zunächst ohne eigene PV-Anlage lokal ausprobieren möchtest, genügt Docker mit Compose v2 (ca. 2 CPU-Kerne, 4 GB RAM):

### Linux / macOS (Bash)

```sh
git clone https://github.com/FrederikT/SolarInsight.git
cd SolarInsight
cp .env.example .env
mkdir -p secrets
openssl rand -base64 32 > secrets/database-password
docker compose up --build -d
```

### Windows (PowerShell)

```powershell
git clone https://github.com/FrederikT/SolarInsight.git
cd SolarInsight
Copy-Item .env.example .env
New-Item -ItemType Directory -Force secrets | Out-Null
$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
[IO.File]::WriteAllText("$PWD\secrets\database-password", [Convert]::ToBase64String($bytes))
docker compose up --build -d
```

Öffne anschließend **[http://localhost:8080](http://localhost:8080)**. Beim ersten Start erzeugt Solar Insight automatisch 24 Monate synthetische Fünf-Minuten-Messdaten (der Fortschritt ist unter *Datenstatus* sichtbar).

---

## 2. Eigene Sungrow-Anlage (iSolarCloud) einrichten

Um deine eigene Anlage über die offizielle Sungrow iSolarCloud Open API (EU-Gateway `https://gateway.isolarcloud.eu`) anzubinden, startest du Solar Insight im geschützten privaten Modus (`SOLAR_DEMO=false`).

### Schritt 2.1: Anwendung im Sungrow Developer Portal anlegen

1. Registriere dich kostenlos im **[Sungrow Developer Portal](https://developer-api.isolarcloud.com/)** (siehe auch die [Sungrow-EMEA-Kurzanleitung](https://academy.sungrow-emea.com/de/articles/331036-sungrow-s-api-developer-portal)).
2. Lege dort eine neue Anwendung für die **EU-Region** an und aktiviere die OAuth2-Autorisierung (*Authorization Bound*).
3. Trage als registrierte **Redirect-URL (Rücksprungadresse)** z. B. `http://127.0.0.1:8081` (oder deine eigene lokale Adresse) ein.
   *(Hinweis: Die Rücksprungadresse muss aus dem Internet nicht erreichbar sein – du kopierst nach der Einwilligung lediglich einmalig den `code=...`-Parameter aus der Adresszeile deines Browsers in Solar Insight).*
4. Nach der Freigabe deiner App im Sungrow-Portal erhältst du dort deinen **`appKey`**, deinen **`accessKey` (`x-access-key`)** sowie die **Einwilligungs-URL (Authorization Link)**.

### Schritt 2.2: Secret-Dateien anlegen

Lege im Projektordner den Unterordner `secrets/` an (dieser Ordner ist per `.gitignore` und `.dockerignore` vom Git-Repo ausgeschlossen) und erstelle drei Dateien:

1. **`secrets/database-password`** – ein zufälliges Passwort für die lokale PostgreSQL-Datenbank.
2. **`secrets/setup-password`** – dein gewünschtes Login-Passwort für die Solar-Insight-Oberfläche (**mindestens 14 Zeichen**). Nach dem ersten Start wird daraus ein sicherer Hash im State-Volume gespeichert; die Datei `setup-password` wird danach nicht mehr benötigt.
3. **`secrets/sungrow-app.json`** – deine Sungrow-App-Zugangsdaten in folgendem Format:

```json
{
  "appKey": "<Dein-Appkey-aus-dem-Sungrow-Developer-Portal>",
  "accessKey": "<Dein-x-access-key-aus-dem-Sungrow-Developer-Portal>",
  "redirect": "http://127.0.0.1:8081"
}
```

> **Wichtig unter Linux:** Setze die Berechtigungen so, dass nur dein Benutzer den Ordner betreten, der Container-Benutzer (`app`) die Dateien aber lesen kann:
> `chmod 700 secrets && chmod 644 secrets/*`

### Schritt 2.3: Private Instanz mit Docker Compose starten

Passe in deiner `.env` den Modus auf `SOLAR_DEMO=false` an (oder übergib die Variablen beim Start) und starte den Stack mit den Overlays `compose.owner.yaml` und `compose.sungrow.yaml`:

```sh
SOLAR_DEMO=false SOLAR_PORT=8081 \
  docker compose -p solar-private \
  -f compose.yaml -f compose.owner.yaml -f compose.sungrow.yaml \
  up --build -d
```

### Schritt 2.4: Anlage in der Oberfläche autorisieren und aktivieren

1. Öffne **[http://127.0.0.1:8081](http://127.0.0.1:8081)** und melde dich mit deinem Passwort aus `secrets/setup-password` an.
2. Wechsle zu **Einstellungen → Verbindung**.
3. Öffne in einem zweiten Browser-Tab den **Einwilligungslink** deiner Sungrow-Entwickleranwendung, melde dich mit deinem normalen **iSolarCloud-Eigentümerkonto** an und gib deine Anlage frei.
4. Dein Browser leitet dich auf `http://127.0.0.1:8081/?code=...` weiter. Kopiere den Wert hinter `code=` und füge ihn in Solar Insight unter **Autorisierungscode** ein → **Autorisieren**.
5. Klicke auf **Autorisierte Anlagen laden**, wähle deine Anlage aus (Standardzeitzone `Europe/Berlin` ist vorausgefüllt) und klicke auf **Anlage aktivieren**.
6. **Fertig!** Solar Insight plant automatisch den Erstimport der letzten 30 abgeschlossenen Tage ein und hält die Daten anschließend im Hintergrund aktuell. Unter **Einstellungen → Tarife** kannst du nun deine Strompreise und deine Einspeisevergütung eintragen.

---

## 3. Optional: Erweiterte Messpunkte für Hybridwechselrichter (Live-Daten, SOC & Zähler-Backfill)

Ohne weitere Konfiguration importiert Solar Insight automatisch die Standard-Tageswerte (PV-Ertrag, Hausverbrauch, Netzbezug, Einspeisung, Batterieladung/-entladung).

Wenn du einen **Sungrow-Hybridwechselrichter** (z. B. `SH10RT` / `SH10RT-V112`, Gerätetyp `14`) nutzt und zusätzlich **Live-Momentanwerte**, **Ladezustand (SOC)** sowie **15-Minuten-Zählerintervalle und historische Batterie-Tageswerte** abrufen möchtest:

1. Prüfe unter **Einstellungen → Diagnose und Messpunkte → Gerätemesspunkte lesend prüfen**, welche Messpunkte dein Wechselrichter liefert, und notiere dir deine **Anlagen-ID** (z. B. `123456`) und den **Geräte-Key** (z. B. `123456_14_1_1`).
2. Lege eine Datei `secrets/sungrow-mapping.json` an (Details in [docs/operations.md](docs/operations.md#betreiberprofil-für-messpunkte)):

```json
{
  "123456": {
    "pv": null,
    "load": 83106,
    "grid": null,
    "battery": null,
    "soc": null,
    "positiveGridIsImport": true,
    "positiveBatteryIsCharge": true,
    "instantaneous": true,
    "endStamped": false,
    "socPercent": false,
    "confirmedAcPaths": false,
    "deviceCounters": true,
    "stepMinutes": 15,
    "daily": {
      "pv": 83022, "load": 83118, "import": 83102, "export": 83072,
      "charge": 83322, "discharge": 83323
    },
    "live": {
      "deviceKey": "123456_14_1_1",
      "pv": null,
      "pvDc": 13003,
      "inverterActive": 13011,
      "load": 13119,
      "grid": 13149,
      "gridExport": 13121,
      "battery": 13126,
      "batteryDischarge": 13150,
      "soc": 13141,
      "positiveGridIsImport": true,
      "positiveBatteryIsCharge": true,
      "socPercent": false,
      "batteryExportExcluded": false,
      "confirmed": true
    }
  }
}
```

3. Binde die Datei per Compose-Override über `Sungrow__MappingFile: /etc/solar-insight/sungrow-mapping.json` schreibgeschützt ein und aktiviere die Anlage in den Einstellungen einmalig erneut.

---

## 4. Optional: Betrieb im Heimnetz oder auf einem eigenen Server / in der Cloud

Solar Insight ist ein normaler Docker-Compose-Stack und lässt sich auf viele Arten betreiben – zum Beispiel auf einem **Raspberry Pi / Homeserver (NAS, Proxmox)** im lokalen Netzwerk oder auf einem **kleinen VPS / Cloud-Server**:

- **Wichtig:** Betreibe für dieselbe Sungrow-Anlage immer nur **eine** aktive Instanz gleichzeitig, damit sich zwei Instanzen nicht gegenseitig die OAuth-Tokens oder API-Rate-Limits streitig machen.
- **HTTPS & Reverse Proxy:** Wenn du nicht nur über `localhost` zugreifst, setze `SOLAR_ALLOW_HTTP=false` und schalte einen TLS-Reverse-Proxy (z. B. Nginx, Caddy, Traefik, Cloudflare Tunnel oder Tailscale `serve`) davor. Ein fertiges Beispiel für **Oracle Cloud Always Free + Tailscale** liegt als Inspiration unter [`deploy/cloud/`](deploy/cloud/) und in [docs/cloud-deployment.md](docs/cloud-deployment.md) sowie [docs/cloud-setup.md](docs/cloud-setup.md).
- **Smart-Home-Integration im Heimnetz:** Über das optionale Overlay `compose.lan.yaml` (`GET /api/v1/lan/now` mit eigenem Bearer-Leseschlüssel) können Dienste wie Home Assistant oder ioBroker den aktuellen Netzbezug/Überschuss aus dem lokalen Cache auslesen, ohne zusätzliche Sungrow-Cloud-Abrufe auszulösen (siehe [docs/operations.md](docs/operations.md#lesender-heimnetz-zugang)).

---

## 5. Entwicklung, Tests und GitHub-Pages-Demo

```sh
dotnet restore src/Api/Api.csproj --locked-mode
dotnet build src/Api/Api.csproj -c Release --no-restore
dotnet test tests/Unit/Unit.csproj -c Release
# Für Integrationstests SOLAR_TEST_DB auf eine separate PostgreSQL-17-Testdatenbank setzen:
dotnet test tests/Integration/Integration.csproj -c Release

cd src/Web
npm ci
npm run typecheck
npm run lint
npm run build
npx playwright install chromium --only-shell
npm run test:e2e
```

### Statische Demo für GitHub Pages bauen und veröffentlichen

Der Workflow [`.github/workflows/pages.yml`](.github/workflows/pages.yml) baut bei jedem Push auf `main` mit `VITE_STATIC_DEMO=true` (`npm run build:pages`) eine rein statische Fassung der Demo und veröffentlicht sie automatisch auf GitHub Pages:

- **Einmalige Aktivierung in GitHub:** Öffne im GitHub-Repository **Settings → Pages** und stelle unter **Build and deployment → Source** auf **GitHub Actions** um.
- **Strikt isoliert:** Im normalen Build (`npm run build` / `Dockerfile`) ist `VITE_STATIC_DEMO` nicht gesetzt; Vite entfernt den statischen Demo-Adapter beim Bauen für Docker und die private Instanz vollständig per Dead-Code-Elimination.

---

## Weiterführende Dokumentation

- [Betrieb, Backups, Restore, Betreiberprofil & Heimnetz-API](docs/operations.md)
- [Architektur und Modellannahmen](docs/architecture.md)
- [Beispiel: Cloud-Deployment mit Docker & Tailscale](docs/cloud-deployment.md) · [Schritt-für-Schritt-Einrichtung](docs/cloud-setup.md)
- [Sungrow Open API Fähigkeiten und Messpunkte](docs/provider-capabilities.md)
- [Gepinnte Abhängigkeiten und Lizenzen](docs/dependencies.md) · [Umsetzungsstand & Abnahmenachweise](docs/implementation-status.md)

Solar Insight steht unter der [MIT-Lizenz](LICENSE). Copyright © 2026 Frederik Thuss.
