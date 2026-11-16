# Cloud-Betrieb der privaten Instanz

Stand: 2026-11-16. Status: **In Betrieb.** Die private Instanz läuft auf einer Oracle-Always-Free-VM (A1, 2 OCPU/12 GB, Frankfurt) und ist nur über Tailscale erreichbar (`https://solar.<tailnet>.ts.net`). Der Cutover ist am 2026-11-16 um 00:55 erfolgt. Die lokale Instanz auf 8081 ist gestoppt, ihre Datenbank bleibt als Rückfallebene unverändert. Instanzspezifische Werte wie IP, Tailnet-Name und Schlüsselpfade stehen nur lokal in `.local/cloud-instance.md`, nicht im Repository.

Dieses Dokument enthält keine Secrets. Es wird bei jedem Schritt fortgeschrieben, damit ein neuer Agent übernehmen kann. Den Fortschritt hält das [Übergabeprotokoll](#übergabeprotokoll) am Ende fest.

## Rahmen und Freigabegrenzen

Ohne ausdrückliche Freigabe der Betreiberin bzw. des Betreibers gilt:

- keine Cloud-Ressourcen anlegen und nichts Kostenpflichtiges;
- keine Schreibzugriffe auf die private Datenbank (lesend nur über `.local/owner-instance/csv-db.ps1 -Sql <datei>`);
- nichts pushen, nichts mergen und keine destruktiven Git-Befehle.

`.env`, `secrets/`, `data/` und die Secret-Dateien unter `.local/owner-instance/` werden nur gelesen, wenn es nötig ist. Ihre Werte erscheinen nie in Doku, Log oder Chat.

- **Passwörter:** Anmeldungen in der App, in der Oracle-Konsole, bei Cloudflare oder Tailscale nimmt die Betreiberin bzw. der Betreiber selbst vor. Ein Agent gibt keine Passwörter ein.
- **Git:** Basis für die Cloud ist der neueste Stand von `fix/analytics-handover` (3d688c7). Die Cloud-Konfiguration entsteht nach der Freigabe auf einem eigenen Branch (`feat/cloud-oracle`), der davon abzweigt.

## Phase 1 · Bestandsaufnahme

### Laufende Instanzen

| Instanz | Port | Betrieb | Datenbank | Startweg |
|---|---|---|---|---|
| Demo | 8080 | Docker Compose (`compose.yaml`) | Container `postgres:17.11-bookworm`, Demo-Daten | `.local/ci-deploy-demo.cmd` |
| Privat | 8081 | **ohne Docker**: portables .NET unter `.local/dotnet` und App-Ordner unter `.local/owner-instance/app-*` | portables PostgreSQL 17.11 (Windows x64), Daten in `.local/owner-instance/postgres`, `127.0.0.1:55433` | `.local/ci-deploy-private.ps1` → `start.ps1` |

Für die Migration wichtig: Die private Instanz läuft nicht in Docker. Sie verwendet die Datenbank **`postgres`**, Compose dagegen `solar`. Das Cluster wurde mit `--locale=C --encoding=UTF8` initialisiert.

### Private Datenbank (lesend ermittelt am 2026-11-15)

- PostgreSQL 17.11 (x86_64-windows). Gesamtgröße **14 MB**. Erweiterung nur `plpgsql`.
- 5 EF-Migrationen, die neueste ist `20261110184446_AutomaticDeviceCounters`.
- Tabellen mit Größe nach Statistik: `Aggregates` 7667 Zeilen, `LiveMeasurements` 5636, `LivePolls` 483, `HistoryGaps` 79, `LiveGaps` 11, `Plants` 2. Dazu kommen `Readings`, `HistoryScans`, `Imports`, `Connections`, `Devices`, `Prices` und `Configurations`. Diese Zeilenzahlen sind Schätzwerte aus `pg_stat`. Den Vergleich bei der Migration führt `count(*)` aus.

### Laufzeitzustand außerhalb der Datenbank

Alles hier muss zusammen mit dem Datenbankdump migriert werden.

| Element | Lokal | Rolle | Cloud |
|---|---|---|---|
| `state/owner.hash` | `.local/owner-instance/state/` | Passwort-Hash der lokalen Anmeldung. Der Session-Stamp wird davon abgeleitet. | Volume `state` |
| `state/keys/` | `.local/owner-instance/state/keys/` (1 Schlüsseldatei) | ASP.NET Data-Protection-Schlüssel. Damit sind die Sungrow-Tokens in `Connections` verschlüsselt. | Volume `keys` (im Image ist `Solar__Keys=/keys` gesetzt) |
| `database-password` | `.local/owner-instance/` | DB-Passwort | Secret-Datei auf der VM, **neu erzeugen** |
| `sungrow-app.json` | `.local/owner-instance/` | Sungrow-App-Schlüssel und registrierte Rücksprungadresse (`http://127.0.0.1:8081`) | Secret-Datei `sungrow_app` |
| `sungrow-mapping-soc.json` | `.local/owner-instance/` | aktives Betreiberprofil der Messpunkte | schreibgeschützte Datei, `Sungrow__MappingFile` |
| `erstpasswort.txt` | `.local/owner-instance/` | Erstpasswort, wird wegen vorhandenem `owner.hash` nicht mehr gelesen | **nicht migrieren** |

Zum Sungrow-OAuth: Der Autorisierungscode wird in der App eingegeben (`POST /api/v1/provider/authorize`). Die registrierte Rücksprungadresse `http://127.0.0.1:8081` muss deshalb nicht erreichbar sein und kann in der Cloud bleiben. Gehen die Schlüssel unter `keys/` verloren, sind die gespeicherten Tokens unbrauchbar, und Sungrow muss neu autorisiert werden.

### Container-Build

- **Dockerfile:** dreistufig (Node 24.15 → .NET SDK 10.0.403 → aspnet 10.0.14). Läuft als Benutzer `app`. Port 8080, Volumes `/state` und `/keys`.
- **ARM64:** Alle Basis-Images sind Multi-Arch. Im `package-lock.json` sind die nativen linux-arm64-Pakete enthalten (`@rolldown/binding-linux-arm64-gnu`, `lightningcss-linux-arm64-gnu`). Ein Build auf der ARM-VM ist damit vorgesehen, aber noch nicht getestet (Phase 7).
- **Compose:** `compose.yaml` mit `db` und `app`, DB-Healthcheck, Log-Rotation 10 MB × 3, App-Port nur an `127.0.0.1`. Overlays sind `compose.owner.yaml` (Setup-Passwort), `compose.sungrow.yaml` (Sungrow-App-Datei und Taktung) und `compose.lan.yaml` (Heimnetz-Leseendpunkt).
- **Bisher fehlt:** Reverse Proxy, App-Healthcheck, Ressourcenlimits, internes Netz für die DB und Härtung (`read_only`, `cap_drop`, `no-new-privileges`).

Die App bringt dafür schon mit:

- `Solar:AllowHttp=false` erzwingt HTTPS für `/api`. Hinter einem Proxy muss dieser `X-Forwarded-Proto: https` setzen.
- `Solar:TrustedProxy` akzeptiert genau eine IP-Adresse. Der Proxy braucht deshalb eine feste IP im Compose-Netz.
- Die Readiness unter `GET /health/ready` prüft die Datenbank.

## Phase 2 · Hosting-Empfehlung

### Oracle Cloud Always Free: geeignet

Laut [Oracle-Dokumentation](https://docs.oracle.com/en-us/iaas/Content/FreeTier/resourceref.htm), abgerufen am 2026-11-15:

- **Ampere A1 (VM.Standard.A1.Flex):** insgesamt **2 OCPU und 12 GB RAM** (früher 4/24), in der Home-Region. Nötig sind nach `docs/operations.md` mindestens 2 Kerne und 4 GB. Geplant ist **eine VM mit 2 OCPU und 12 GB**.
- **Block Volume:** 200 GB für Boot- und Datenvolumes zusammen, 5 Volume-Backups. Ein Boot-Volume mit 50 GB genügt, die DB hat 14 MB.
- **Object Storage:** 20 GB kombiniert bei reinem Always-Free-Konto (10/10/10 GB bei Pay-as-you-go), 50.000 Requests pro Monat. Das reicht für verschlüsselte Offsite-Backups.
- **Ausgehender Traffic:** 10 TB pro Monat laut Oracle-Free-Tier-Seite. Der Bedarf liegt weit darunter.

Risiken:

1. **Rückforderung bei Leerlauf:** Oracle kann Always-Free-Instanzen zurückfordern, wenn über 7 Tage das 95. Perzentil der CPU unter 20 %, die Netzwerkauslastung unter 20 % und bei A1 der Speicher unter 20 % liegt. Diese App ist fast immer im Leerlauf und erfüllt die Kriterien daher voraussichtlich.
   - Gegenmaßnahme: Das Konto auf **Pay-as-you-go** umstellen. Always-Free-Ressourcen bleiben laut Oracle kostenlos, und Leerlauf-Rückforderungen werden nach verbreiteter Erfahrung für Always-Free-only-Konten berichtet. Dazu ein **Budget-Alarm bei 1 €**, damit nichts unbemerkt Kosten erzeugt.
   - Unabhängig davon: tägliche externe Backups und ein dokumentierter Neuaufbau (Phase 6), damit eine Rückforderung keinen Datenverlust bedeutet.
   - Künstliche CPU-Last wird nicht empfohlen.
2. **Kapazitätsmangel:** Bei „Out of host capacity“ in der Home-Region später erneut versuchen oder eine andere Availability Domain wählen. Die Home-Region ist nach der Registrierung fest. Empfohlen ist eine EU-Region, etwa Frankfurt, wegen Datenschutz und der Nähe zum Sungrow-EU-Gateway.
3. **Kontingente wurden 2026 bereits halbiert:** Weitere Kürzungen sind möglich. Deshalb bleibt der Stack portabel (reines Docker Compose).
4. **Kreditkarte:** Für die Registrierung nötig. Ein Fehlklick kann kostenpflichtige Ressourcen anlegen. Deshalb nur als „Always Free Eligible“ markierte Shapes und Images verwenden und den Budget-Alarm setzen.

Alternativen werden nicht weiter verfolgt, da Oracle geeignet ist. Zur Einordnung: Hetzner CAX11 kostet etwa 4 € pro Monat, ein eigener Raspberry Pi verursacht Stromkosten. Beide sind nicht kostenlos.

### Zugang von außen: Entscheidung erforderlich

Die App hat genau einen Faktor, das lokale Passwort mit mindestens 14 Zeichen. Login-Rate-Limit: 8 Versuche je 5 Minuten und IP. Für den Internetbetrieb wird eine vorgeschaltete Zugangskontrolle empfohlen.

| Variante | Offene Ports | Domain nötig | Zweiter Faktor | Zugriff von | Kosten |
|---|---|---|---|---|---|
| **A · Tailscale** (`tailscale serve`, HTTPS-Zertifikat für `*.ts.net`) | keine | nein | Gerät muss im Tailnet sein | eigene Geräte mit Tailscale-Client | 0 € (Personal-Plan) |
| **B · Cloudflare Tunnel + Access** (E-Mail-Einmalcode) | keine | ja, Zone bei Cloudflare | Access-Richtlinie | jeder Browser | 0 € bei vorhandener Domain, sonst ca. 10 € pro Jahr |
| C · Caddy öffentlich mit Let's Encrypt | 80/443 | ja (oder freier DynDNS) | nein, nur App-Passwort | jeder Browser | 0 € |

**Empfehlung:** Variante **A**, wenn nur eigene Geräte zugreifen. Sie braucht keine Domain und öffnet keine Ports. Variante **B**, wenn der Zugriff aus beliebigen Browsern nötig ist und eine Domain vorhanden ist. Von **C** wird abgeraten. In allen Varianten ist eingehend höchstens SSH offen, und auch das nur von der eigenen IP oder über Tailscale.

## Phase 3 · Sicherheitsprüfung (nur aktueller Stand, keine Git-History)

Geprüft wurden die getrackten Dateien (`git ls-files`, Suche nach Schlüsselmustern), die Ignore-Dateien, `Program.cs`, `Security.cs`, `LiveProvider.cs`, `SungrowDiscovery.cs`, Compose, das Dockerfile und die lokalen Startskripte.

| # | Befund | Bewertung | Maßnahme |
|---|---|---|---|
| S1 | In den getrackten Dateien kein Secret gefunden. `.env`, `secrets/`, `.local/` und `data/` sind in `.gitignore`. Getrackt ist nur `.env.example` ohne Werte. | ok | – |
| S2 | In `.dockerignore` fehlt `data/`. Die privaten CSV-Exporte gehen in den Build-Kontext, aber nicht ins Image, weil nur `src/` kopiert wird. | niedrig | `data` ergänzen (Branch `feat/cloud-oracle`) |
| S3 | `erstpasswort.txt` liegt noch lokal, obwohl `owner.hash` existiert. | niedrig (lokal) | nicht migrieren. Nach Bestätigung, dass das aktuelle Passwort bekannt ist, löscht die Betreiberin bzw. der Betreiber die Datei selbst. |
| S4 | Die Data-Protection-Schlüssel liegen unverschlüsselt im Volume `keys` (kein `ProtectKeysWith`). | mittel | OCI-Volumes sind ruhend verschlüsselt. Backups von `keys`/`state` nur verschlüsselt (age) und getrennt vom Schlüssel ablegen. |
| S5 | Bei Internetzugang gibt es nur einen Faktor (Passwort). Das Rate-Limit gilt je IP und bremst verteilte Versuche nicht. | mittel | vorgeschaltete Zugangskontrolle (Variante A oder B) |
| S6 | `MapOpenApi()` liefert `/openapi/v1.json` ohne Anmeldung aus. Es enthält die Endpunktliste, keine Daten. | niedrig | in Produktion am Proxy sperren oder nur im Demo/Development-Modus mappen |
| S7 | Kein HSTS-Header | niedrig | Proxy setzt `Strict-Transport-Security` (bei B übernimmt das Cloudflare) |
| S8 | `TrustedProxy` erlaubt genau eine IP. Bei falscher IP fehlen `IsHttps` und die Client-IP. | Betriebsrisiko | feste Proxy-IP im Compose-Netz (`172.30.0.10`) |
| S9 | Die private Instanz läuft lokal mit `AllowHttp=true`. | ok lokal (Loopback) | Cloud: `SOLAR_ALLOW_HTTP=false` |
| S10 | Images sind per Tag gepinnt, nicht per Digest. | niedrig | optional per Digest pinnen. Updates sind bewusste Commits. |
| S11 | Positiv: Container läuft nicht als root. Secrets kommen als Dateien, nicht als Umgebungsvariablen. Cookies sind `HttpOnly`, `SameSite=Strict` und bei HTTPS `Secure`. CSRF ist auf allen schreibenden Routen aktiv. CSP ist gesetzt. Die Request-Größe ist auf 32 KB begrenzt. Provider-Tokens werden nicht geloggt. | ok | – |
| S12 | Die App braucht ausgehenden Internetzugang (Sungrow-API). Die DB braucht keinen. | Design | DB nur im internen Netz |

## Phase 4 · Migrationsplan

Die Quelle ist das lokale portable PostgreSQL 17.11 (Datenbank `postgres`). Ziel ist `postgres:17.11-bookworm` auf linux/arm64 mit der Datenbank `solar`. Das Custom-Format von `pg_dump` ist plattformunabhängig. Die Hauptversionen von Quelle und Ziel sind gleich.

### 4.1 Vorbereitung ohne Schreibzugriff auf die private DB

1. Die VM ist aufgebaut und der Stack läuft (Phase 5), zunächst mit leerer Datenbank und **ohne** gestartete App. Das Ziel wird mit `POSTGRES_INITDB_ARGS="--encoding=UTF8 --locale=C"` initialisiert. Das entspricht der Quelle und vermeidet Sortierunterschiede.
2. Probelauf: Einen frischen Dump mit `pg_dump -Fc` anfertigen, wie in `backup-before-battery.ps1` (rein lesend). Auf der VM in eine **Wegwerf-DB** einspielen und den Abgleich nach 4.3 ausführen. Danach die Wegwerf-DB verwerfen.

### 4.2 Kontrollierter Cutover

Zwei Instanzen dürfen nicht gleichzeitig Sungrow abfragen, wegen der Rate-Limits und weil die Datenbestände sonst auseinanderlaufen.

1. Ankündigen und Zeitfenster wählen, am besten abends ohne PV-Erzeugung.
2. **Lokale App stoppen** (Prozess auf 8081). Das portable PostgreSQL läuft weiter.
3. Abschlussdump:
   - `pg_dump -Fc -d postgres` nach `.local/owner-instance/cutover-<zeit>.dump`;
   - `pg_restore -l` prüfen, wie im bestehenden Backup-Skript;
   - SHA-256-Summe bilden.
4. Den lokalen Abgleich nach 4.3 auf der Quelle ausführen und das Ergebnis lokal speichern.
5. Dump, `state/owner.hash`, `state/keys/`, `sungrow-app.json` und das Mapping per `scp` auf die VM kopieren. Der Weg läuft über SSH bzw. Tailscale. Danach die Prüfsumme kontrollieren.
6. Auf der VM einspielen: `pg_restore --exit-on-error --no-owner --role=solar -d solar`. Dann `owner.hash` und die Schlüssel in die Volumes `state` und `keys` legen. Eigentümer ist `app`, die UID wird aus dem Image ermittelt.
7. Den Abgleich nach 4.3 auf dem Ziel ausführen und mit der Quelle vergleichen. **Erst wenn alles übereinstimmt**, die App starten.
8. App starten. Readiness, Anmeldung, Übersicht und Datenstatus prüfen. Die Sungrow-Verbindung muss als „authorized“ erscheinen. Falls die Tokens abgelaufen sind, autorisiert die Betreiberin bzw. der Betreiber neu.
9. Die Lücke zwischen Stopp und Start schließen die Hintergrunddienste mit `fillOnly`. Live-Stichproben aus diesem Zeitraum fehlen dauerhaft. Das wird in Kauf genommen und als Lücke ausgewiesen.
10. Die lokale Instanz bleibt **gestoppt, aber unverändert** als Rückfallebene. Das gilt mindestens 14 Tage, nichts wird gelöscht.

**Rückfall:** Cloud-App stoppen und lokal `start.ps1` ausführen. Der lokale Stand entspricht dann dem Cutover-Zeitpunkt, die Hintergrunddienste füllen nach.

**AGENTS.md:** Die Regel, nach jeder Implementierung 8081 neu zu starten, widerspricht dem Cloud-Betrieb, weil dann zwei Instanzen Sungrow abfragen. Nach dem Cutover muss sie auf „private Instanz = Cloud“ umgestellt werden. Die Betreiberin bzw. der Betreiber entscheidet, wie die Regel künftig lautet.

### 4.3 Abgleich Quelle ↔ Ziel

Das Skript `deploy/cloud/verify.sql` entsteht auf dem Feature-Branch. Es ist rein lesend und läuft in einer `READ ONLY`-Transaktion. Quelle und Ziel müssen in allen Punkten übereinstimmen:

- `count(*)` je Tabelle (alle 14 Tabellen einschließlich `__EFMigrationsHistory`);
- `min/max` von `Start`/`End` bzw. Zeitstempel in `Readings`, `Aggregates` und `LiveMeasurements`;
- Kennzahlen: Summen der `Kwh` in `Aggregates` je Messgröße (`Point`), `Origin`, `Revision` und Monat;
- Anzahl der `Aggregates` mit Revision `sungrow-day-2` und mit `sungrow-device-counter-day-v1`;
- `Plants` und `Configurations` (Id, Zone, Zeitpunkt der Änderung), `Connections` (nur Anzahl und Status, kein Token-Inhalt);
- die neueste EF-Migration;
- fachliche Stichprobe über die API mit demselben Zeitraum lokal und in der Cloud (nach dem Start): Autarkiegrad, Eigenverbrauchsquote, PV, Hausverbrauch und Netzbezug.

## Phase 5 · Produktions-Compose (umgesetzt auf `feat/cloud-oracle`)

Umgesetzt sind:

- `deploy/cloud/compose.cloud.yaml`, `tailscale-serve.json`, `cloud.env.example`, `verify.sql`, `backup.sh`, `restore-check.sh` und `systemd/solar-backup.{service,timer}`;
- die Sicherheitskorrekturen S2 (`data` in `.dockerignore`), S6 (OpenAPI nur im Demo- und Development-Modus) und S8 (feste Proxy-IP; eine ungültige `Solar:TrustedProxy` bricht den Start ab, statt still ignoriert zu werden).

Ergebnisse stehen im [Übergabeprotokoll](#übergabeprotokoll), die manuelle Einrichtung in [cloud-setup.md](cloud-setup.md).

Abweichungen vom Entwurf:

- Die App hat zusätzlich die feste IP `172.30.0.20`. Tailscale `serve` leitet direkt dorthin weiter.
- Weil die IP fest ist, kann kein zweiter App-Container starten, solange die App läuft. Backups nutzen deshalb `compose exec`, nicht `compose run`. `compose run app` (z. B. Restore nach `docs/operations.md`) nur bei gestoppter App.
- Die Data-Protection-Schlüssel haben Modus 0600. Backups lesen sie als Benutzer `app`, root ohne Capabilities könnte das nicht.
- Tailscale tritt bevorzugt über einen Login-Link aus dem Container-Log bei. Die Datei `tailscale.env` darf leer sein, ein Auth-Key ist optional.

Die Dateien entstehen auf `feat/cloud-oracle` unter `deploy/cloud/`. Der bestehende Basis-Stack wird nur überlagert:

```sh
docker compose -p solar -f compose.yaml -f compose.owner.yaml -f compose.sungrow.yaml \
  -f deploy/cloud/compose.cloud.yaml --env-file deploy/cloud/cloud.env up -d --build --wait
```

Geplanter Inhalt von `compose.cloud.yaml`:

- **Netze:**
  - `backend` mit `internal: true`, nur `db` und `app`;
  - `edge` mit festem Subnetz `172.30.0.0/24`, `app` und Proxy. Der Proxy hat die feste IP `172.30.0.10`, gesetzt als `SOLAR_TRUSTED_PROXY`. Die DB ist ausschließlich in `backend`.
- **Proxy je Variante:**
  - **A:** Container `tailscale/tailscale` (gepinnte Version) im Netz `edge` mit `tailscale serve` auf `http://app:8080`. Der Auth-Key liegt als Secret-Datei vor.
  - **B:** Container `cloudflare/cloudflared` (gepinnt). Der Tunnel-Token liegt als Secret-Datei vor, die Access-Richtlinie wird in Cloudflare von der Betreiberin bzw. dem Betreiber angelegt.
  - Optional bei C: `caddy` mit HSTS und `/openapi` gesperrt.
- **app:**
  - `SOLAR_DEMO=false`, `SOLAR_ALLOW_HTTP=false`, `Sungrow__MappingFile` (Datei schreibgeschützt eingebunden);
  - Healthcheck auf `/health/ready`, z. B. per `dotnet`-freiem Aufruf über ein kleines Prüfprogramm oder `wget`. Das aspnet-Image enthält kein curl, das wird beim Umsetzen geklärt.
  - `restart: unless-stopped`, `read_only: true` mit `tmpfs: /tmp`, `cap_drop: [ALL]`, `security_opt: [no-new-privileges:true]`;
  - Limits `mem_limit: 1g`, `cpus: 1.5`, `pids_limit: 256`.
- **db:**
  - `POSTGRES_INITDB_ARGS`, `restart: unless-stopped`;
  - Limits `mem_limit: 1g`, `shm_size: 256m`;
  - keine Ports, nur Netz `backend`.
- **Logs:** alle Dienste mit `json-file`, 10 MB × 3 (wie bisher).
- **Secrets:** Dateien unter `/opt/solar/secrets/` (Ordner 0700, Dateien 0644 wegen des Container-Mappings, siehe `docs/operations.md`): `database-password` (neu erzeugt), `setup-password` (nur Platzhalter, da `owner.hash` migriert wird), `sungrow-app.json`, `sungrow-mapping.json` und das Proxy-Token.
- **Port:** `127.0.0.1:8080` bleibt für Diagnose per SSH-Tunnel. Er ist von außen nicht erreichbar.
- **Härtung des Hosts:**
  - Ubuntu 24.04 LTS (aarch64, „Always Free Eligible“) mit `unattended-upgrades`;
  - SSH nur mit Schlüssel, kein Passwort-Login;
  - OCI-Security-List: eingehend nur SSH von der eigenen IP (oder gar nichts bei Tailscale-SSH);
  - Docker aus dem offiziellen Repository mit Log-Rotation in `daemon.json`.

Zusätzlich auf dem Branch:

- `data` in `.dockerignore` (S2);
- `/openapi` in Produktion sperren (S6);
- `deploy/cloud/verify.sql`, `deploy/cloud/backup.sh` und `deploy/cloud/restore-check.sh`;
- `.local/owner-instance/export-for-cloud.ps1` bleibt lokal, weil `.local` nicht getrackt ist. Es erstellt den lesenden Dump mit Prüfsumme.

## Phase 6 · Betrieb

### Updates

1. `backup.sh` ausführen, siehe unten.
2. Optional vor größeren Updates ein Boot-Volume-Backup in OCI (5 frei).
3. Die VM hat keinen GitHub-Zugang. Vom PC aus `git bundle create solar-main.bundle main` ausführen, das Bundle per `scp` nach `/tmp/` kopieren und auf der VM als `solar` Folgendes ausführen: `git -C /opt/solar/app fetch /tmp/solar-main.bundle main:refs/remotes/bundle/main && git -C /opt/solar/app merge --ff-only bundle/main && chmod -R go+rX /opt/solar/app`. Auf der VM wird nie selbst entwickelt.
4. Das Image mit dem Commit-Hash taggen (`SOLAR_IMAGE_TAG`). Das vorige Image bleibt erhalten.
5. `docker compose ... up -d --build --wait`. Danach Readiness, Anmeldung, Datenstatus und eine historische Kennzahl prüfen.

### Rollback

- **Ohne Schemaänderung:** das vorige Image-Tag starten.
- **Mit Schemaänderung:** die App stoppen, in eine leere DB den Dump vor dem Update einspielen (Ablauf aus `docs/operations.md`, „Restore auf eine leere Instanz“) und dann das vorige Tag starten.

### Backups

Bisher gibt es nur manuelle Dumps. Geplant ist ein `systemd`-Timer, täglich um 03:30:

1. `pg_dump -Fc` des Containers, dazu `state` und `keys` als tar. Die App läuft weiter. Die Schlüssel ändern sich selten. Für ein konsistentes Paket aus Dump und State genügt es, weil `owner.hash` und die Schlüssel nur bei Passwortwechsel bzw. Schlüsselrotation geschrieben werden.
2. Mit **age** an einen öffentlichen Schlüssel verschlüsseln. Der private Schlüssel liegt **nur offline** bei der Betreiberin bzw. dem Betreiber, nicht auf der VM.
3. Lokal 7 Tage aufbewahren.
4. Extern:
   - Upload in einen privaten OCI-Object-Storage-Bucket (Always Free, 20 GB) mit Lebenszyklus 30 Tage;
   - **zweites Ziel außerhalb von Oracle**, damit eine Konto- oder Ressourcensperre nicht alles trifft. Vorschlag: Abholung per `scp` durch den lokalen Rechner (Windows-Aufgabe, wöchentlich). Alternativ rclone zu einem vorhandenen Cloud-Speicher. Das wählt die Betreiberin bzw. der Betreiber.
5. Monatlich eine **Wiederherstellungsprobe**: `restore-check.sh` spielt das letzte Backup in einen Wegwerf-Stack ein (eigener Projektname, kein Port nach außen) und vergleicht die Ergebnisse von `verify.sql`.

### Rückforderung freier Ressourcen und Kontoverlust

- **Vorbeugung:** Pay-as-you-go mit Budget-Alarm. Oracle-E-Mails zu Leerlauf oder Kürzung beachten.
- **Neuaufbau:** Neue VM nach Phase 5 aufsetzen und das letzte externe Backup einspielen. Ziel: in unter einer Stunde wieder lauffähig. Ohne Cloud lässt sich lokal zurückkehren: `start.ps1` mit einem Restore in das portable PostgreSQL. Das ist ein Schreibzugriff und braucht eine Freigabe.
- **Fristen:** Datenstand höchstens 24 Stunden alt. Fehlende Sungrow-Tage füllt der Hintergrundabgleich nach. Die Sungrow-Historie ist dabei nur begrenzt verfügbar (siehe `docs/provider-capabilities.md`).

## Phase 7 · Validierung (nach Umsetzung)

- [x] Build auf linux/arm64 erfolgreich. `dotnet test` und die Web-Checks laufen lokal grün.
- [x] Stack `healthy`. `GET /health/ready` liefert intern und über den Proxy `ready`.
- [x] `/api/v1/auth/session` meldet `demo=false`. Die Anmeldung funktioniert über HTTPS, über HTTP lehnt die API ab.
- [x] Mit falscher Proxy-IP schlägt der HTTPS-Check fehl (Gegenprobe zu S8).
- [x] Die DB ist von außen und aus dem Netz `edge` nicht erreichbar.
- [x] Der Abgleich nach 4.3 stimmt in allen Punkten zwischen Quelle und Ziel überein.
- [x] Die Sungrow-Verbindung ist aktiv, und Live-Werte laufen ein. Der nächste abgeschlossene Tag wird importiert.
- [x] Backup-Timer gelaufen, externe Kopie vorhanden, Wiederherstellungsprobe grün (regelmäßige automatische Abholung auf den PC steht noch als Betriebsentscheidung offen).
- [x] Neustart der VM: Der Stack kommt ohne Eingriff wieder hoch (`restart: unless-stopped`, `live-restore`).
- [x] `docs/implementation-status.md` ist ergänzt.

## Phase 8 · Übergabe

### Übergabeprotokoll

| Datum | Schritt | Ergebnis |
|---|---|---|
| 2026-11-15 | Phasen 1–3 ausgewertet, Plan 4–8 erstellt. DB-Kennzahlen lesend ermittelt. Oracle-Kontingente in der Primärquelle geprüft. | erledigt. Keine Cloud-Ressourcen, kein Push, kein Schreibzugriff. |
| 2026-11-15 | Freigabe Hosting und Zugang | **freigegeben:** Oracle Cloud Always Free, **Always-Free-only (kein Pay-as-you-go)**, Zugang **Variante A (Tailscale)**. Das Rückforderungsrisiko bleibt bestehen. Gegenmaßnahmen: tägliche externe Backups, zweites Ziel außerhalb von Oracle, dokumentierter Neuaufbau. Object Storage im Always-Free-only-Konto: 20 GB kombiniert. |
| 2026-11-15 | Git-Konsolidierung | PR #2 war bereits gemergt. `fix/analytics-handover` hat 6 neue, ungepushte Commits (4376262…3d688c7, nach PR #2). Keine Stashes, keine anderen Worktrees. Geprüft auf 3d688c7: API-Build ohne Fehler, Unit 47/47, Integration 42/42 (separate Test-PostgreSQL 55432), Web typecheck/lint/build grün. Push, PR und Merge nach `main` warten auf Freigabe. |
| 2026-11-15 | Worktree `G:\CodingProjects\SolarInsight-cloud`, Branch `feat/cloud-oracle` ab 3d688c7 | Konfiguration und Sicherheitskorrekturen umgesetzt (Phase 5) |
| 2026-11-15 | Lokale Prüfung (Docker Desktop, amd64, Projekt `solar-cloudtest`, Dummy-Secrets, danach entfernt) | Image gebaut, `db` und `app` healthy mit `read_only` und `cap_drop`. `/health/ready` → ready. HTTP `/api` → 400. Über Proxy-IP 172.30.0.10 mit `X-Forwarded-Proto: https` → 200 (`demo=false`), über 172.30.0.31 → 400. `/openapi/v1.json` → 404. DB aus `edge` nicht erreichbar, DB ohne Internet, App erreicht `gateway.isolarcloud.eu:443`. DB-Collation `C`/UTF8. Ungültige `TrustedProxy` → Startabbruch. tar von `state`/`keys` als `app`. `restore-check.sh` und `verify.sql` auf Container-PG ok. `verify.sql` lesend gegen die private DB ok. **Gefunden und behoben:** `compose run app` scheitert bei laufender App an der festen IP, `backup.sh` nutzt jetzt `exec`. |
| 2026-11-15 | ARM64-Build (`docker buildx --platform linux/arm64`, QEMU) | erfolgreich. Das Image startet als `aarch64` und enthält `Api.dll` und `wwwroot`. Unit-Tests im Worktree 47/47. |
| 2026-11-15 | Git-Integration | Vor dem Push geprüft: keine Secrets. Eine echte Sungrow-Gerätekennung in `implementation-status.md` wurde durch einen Platzhalter ersetzt (d42715f, ohne History-Umschreibung, Altstand bleibt für die spätere History-Bereinigung). [PR #3](https://github.com/FrederikT/SolarInsight/pull/3) war CI-grün und wurde nach `main` gemergt (fcfce07). Danach wurde `main` in `feat/cloud-oracle` gemergt. Erneut geprüft: Build 0 Fehler/0 Warnungen, Unit 47/47, Integration 42/42, Web typecheck/lint/build grün, Cloud-Stack-Checks alle wie erwartet. |
| 2026-11-15 | Nicht lokal prüfbar | Tailscale `serve` (Weiterleitung, `X-Forwarded-Proto`, Zertifikat) braucht einen Account. `backup.sh` als Ganzes braucht Linux mit `age`. Prüfung in Phase 7. |
| 2026-11-16 | VM | Die Betreiberin bzw. der Betreiber hat sie angelegt: Ubuntu 24.04 aarch64, A1.Flex 2 OCPU/12 GB, AD-1, VCN aus dem Assistenten „VCN with Internet Connectivity“, öffentliche IPv4. Die SSH-Regel ist auf die Heim-IP beschränkt. Kosten laut „Free Tier“ 0. Die Kostenschätzung des Formulars (1,85 € pro Monat für das Boot-Volume) ist ein Listenpreis ohne Always-Free-Abzug. |
| 2026-11-16 | Host | Updates, unattended-upgrades (Reboot 04:30 UTC), SSH nur per Schlüssel, Docker 29.9.3 und Compose v5.7.1 (Log-Rotation, live-restore), Benutzer `solar`, `/opt/solar/{app,secrets,backups,backup}`. Code per `git bundle`. Secrets auf der VM neu erzeugt. |
| 2026-11-16 | Stack mit leerer DB | db und app healthy. Auf der VM geprüft: HTTPS-Zwang, Proxy-Vertrauen, OpenAPI 404, DB ohne Edge- und Internetzugang, App erreicht Sungrow, Read-only-Dateisystem, Collation C, iptables lässt nur 22 durch. |
| 2026-11-16 | Tailscale | Beitritt per Login-Link, Ablauf der Anmeldung abgeschaltet, MagicDNS und HTTPS-Zertifikate aktiv. Vom PC aus: gültiges Zertifikat, `/api/v1/auth/session` → 200 (`X-Forwarded-Proto` von `tailscale serve` wird akzeptiert). **Fehler behoben:** Ein Klon mit `umask 077` machte `tailscale-serve.json` unlesbar für den Container ohne Capabilities (Neustart-Schleife). Abhilfe: `chmod -R go+rX /opt/solar/app`, siehe Updates. Der Host selbst ist nicht im Tailnet, SSH läuft deshalb weiter über die öffentliche IP, beschränkt auf die Heim-IP. |
| 2026-11-16 00:50 | Probemigration ohne Sungrow | Rein lesender Export. Die leere DB (nur Standardanlage) verworfen, `pg_restore --no-owner --role=solar`, `owner.hash` nach `/state`, Keys nach `/keys`. `verify.sql` identisch (337 Zeilen). Sungrow in der Cloud vorübergehend per Override abgeschaltet (Token-Konflikt vermeiden). Login mit dem bisherigen Passwort bestätigt. |
| 2026-11-16 00:55 | **Cutover** | Lokale App 8081 gestoppt, finaler Export, Restore, `verify.sql` identisch (337 Zeilen). Echte Sungrow-App-Datei und Mapping installiert, Override entfernt. Erster Cloud-Live-Abruf `confirmed`, Verbindung `authorized`, keine Neuautorisierung nötig. |
| 2026-11-16 | Backups | Der age-Schlüssel wurde im RAM der VM erzeugt. Der private Teil liegt nur auf dem PC der Betreiberin bzw. des Betreibers, die VM hat nur den Empfänger. Timer `solar-backup.timer` täglich 03:30 UTC. Erstes Backup erstellt und geprüft: im RAM entschlüsselt, Prüfsummen ok, Restore in einen Wegwerf-Container identisch mit der Live-DB (ohne laufende Live-Tabellen). Erste externe Kopie auf dem PC (Prüfsumme identisch). Die Migrationskopien auf der VM sind gelöscht. |
| offen | Betrieb | Regelmäßige externe Abholung der Backups auf den PC (Aufgabe in der Windows-Aufgabenplanung, Entscheidung der Betreiberin bzw. des Betreibers). Tailscale auf dem Host, falls die SSH-Regel entfallen soll. Kostenprüfung nach 1–2 Tagen. Ende der Testphase beobachten. |

### Offene Entscheidungen

1. ~~Hosting~~ entschieden: Oracle A1, Always-Free-only. Der Budget-Alarm wird trotzdem empfohlen, als Schutz vor Fehlklicks.
2. ~~Zugang~~ entschieden: Variante A, Tailscale. Die Tailscale-Anmeldung und den Auth-Key erledigt die Betreiberin bzw. der Betreiber selbst.
3. Zweites Backup-Ziel außerhalb von Oracle (erste Kopie liegt auf dem PC; regelmäßige Aufgabenplanung offen).
4. ~~Neue Fassung der AGENTS.md-Regel zur privaten Instanz auf 8081 nach dem Cutover~~ erledigt in `AGENTS.md` und `CLAUDE.md` (6e54d2f).
