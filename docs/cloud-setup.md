# Einrichtung Oracle Cloud und Tailscale: Schritt für Schritt

Gehört zu [cloud-deployment.md](cloud-deployment.md). Stand: 2026-11-15.

Die Leitplanken stehen in [cloud-deployment.md](cloud-deployment.md#rahmen-und-freigabegrenzen) und gelten unverändert. Für diese Einrichtung heißt das:

- Oracle wird **nur im Always-Free-Kontingent** genutzt: kein Pay-as-you-go, keine kostenpflichtigen Ressourcen.
- Vor der ausdrücklichen Freigabe gilt außerdem: keine Cloud-Ressourcen, keine Migration produktiver Daten und kein Stoppen laufender Dienste.

Kennzeichnung der Aufgaben:

- **[DU]**: nur du persönlich (Registrierung, Zahlungsdaten, Passwörter, MFA, Konsolen-Klicks).
- **[AGENT]**: kann der Agent nach deiner Freigabe selbst ausführen, sobald ein Zugang existiert, etwa SSH-Schlüssel auf deinem PC oder SSH zur VM.
- **[SPÄTER]**: erst möglich, wenn Accounts und VM eingerichtet sind. Auch diese Schritte brauchen eine eigene Freigabe.

## A · Aufgaben für dich

### A1 · Oracle-Cloud-Account

1. <https://www.oracle.com/cloud/free/> → **Start for free**.
2. Land, Name und E-Mail eingeben, die E-Mail bestätigen und ein starkes Passwort setzen (Passwortmanager).
3. **Home Region** wählen: **Germany Central (Frankfurt)**. Alternativ eine andere EU-Region, z. B. Netherlands Northwest (Amsterdam).
   - Die Home-Region lässt sich später **nicht** ändern.
   - Always-Free-VMs gibt es nur dort.
   - Frankfurt ist oft ausgelastet („Out of host capacity“). Das ist ein Grund zu warten, aber kein Grund für eine andere Region, wenn dir Frankfurt wichtig ist.
4. Adresse und Kreditkarte zur Identitätsprüfung angeben. Oracle reserviert dabei einen kleinen Betrag und gibt ihn wieder frei.
5. Nach dem Login die **MFA** für den Account einrichten (Oracle fordert das in der Regel beim ersten Login an), mit einer Authenticator-App.
6. **Nicht anklicken:** „Upgrade to Pay As You Go“ bzw. „Upgrade your account“.
7. Die ersten 30 Tage laufen als Testphase mit Guthaben. Danach wird der Account automatisch Always-Free-only, und nicht freie Ressourcen werden gelöscht.

   **Erstelle von Anfang an nur Ressourcen mit dem Label „Always Free-eligible“.**

### A2 · Budget-Alarm als Schutz vor Fehlklicks

Konsole → **Billing & Cost Management → Budgets → Create Budget**:

- Monatlich 1 (EUR bzw. Account-Währung) auf das Root-Compartment;
- Alert Rule „Actual Spend“ bei 100 % mit E-Mail an dich.

Ein Budget kostet nichts und ändert nichts am Kontotyp.

### A3 · VM anlegen (erst nach Freigabe „Cloud-Ressourcen erstellen“)

Konsole → **Compute → Instances → Create instance**:

| Feld | Wert |
|---|---|
| Name | `solar` |
| Placement | beliebige Availability Domain. Bei „Out of host capacity“ eine andere AD probieren oder später erneut. |
| Image | **Canonical Ubuntu 24.04** (aarch64), Label „Always Free-eligible“ |
| Shape | **Ampere → VM.Standard.A1.Flex**, **2 OCPU, 12 GB** (Label „Always Free-eligible“) |
| Networking | „Create new virtual cloud network“ und „Create new public subnet“, **Assign a public IPv4 address: ja** (für ausgehende Verbindungen zu Sungrow und zum Einrichten per SSH) |
| SSH keys | **Paste public key**: Inhalt von `~/.ssh/solar_oci_ed25519.pub` (siehe B1) |
| Boot volume | Standard 50 GB, keine zusätzlichen Block Volumes. Bleibt unter 200 GB frei. |

Nach dem Erstellen:

1. Notiere die **öffentliche IP**. Sie ist nicht geheim, gehört aber auch nicht ins Repository.
2. **Networking → Virtual cloud networks → (VCN) → Security Lists → Default**: Die Regel für SSH (TCP 22) auf `0.0.0.0/0` auf **deine eigene IP** einschränken (`<deine-IP>/32`). Andere eingehende Regeln anlegst du nicht. Nach erfolgreichem Tailscale-Test (C3) entfernst du die SSH-Regel ganz.

Folgendes legst du **nicht** an:

- Load Balancer, NAT Gateway;
- zusätzliche Block Volumes über 200 GB gesamt;
- Autonomous Database;
- Ressourcen in anderen Regionen;
- Shapes ohne Always-Free-Label.

### A4 · Tailscale-Account

1. <https://login.tailscale.com/start> → mit einem bestehenden Identitätsanbieter anmelden, z. B. Google, Microsoft, GitHub oder Apple. Dort sollte MFA aktiv sein. Gewählt wird der kostenlose Personal-Plan.
2. Tailscale auf **PC und Smartphone** installieren und mit demselben Konto anmelden.
3. Admin-Konsole → **DNS**: **MagicDNS** einschalten und **HTTPS Certificates** aktivieren.
   - Hinweis: Zertifikatsnamen wie `solar.<tailnet>.ts.net` erscheinen in öffentlichen Certificate-Transparency-Logs.
   - Deshalb einen neutralen Hostnamen verwenden, Standard `solar`.
4. Admin-Konsole → **Access controls**: Die Standardrichtlinie (alle eigenen Geräte untereinander) genügt für eine Person. **Funnel** nicht aktivieren, die Konfiguration schaltet ihn ausdrücklich ab.
5. Die VM tritt dem Tailnet bei (C2), entweder über einen Login-Link, den der Agent dir aus dem Container-Log nennt (empfohlen, kein Schlüssel nötig), oder über einen Einmal-Auth-Key, den **du** erzeugst und selbst auf der VM ablegst. Danach in der Admin-Konsole beim Gerät `solar` **Disable key expiry** wählen, sonst läuft die Anmeldung nach 180 Tagen ab.

### A5 · Zweites Backup-Ziel festlegen (Entscheidung)

Vorschlag: Dein Windows-PC holt wöchentlich die verschlüsselten Backups per `scp` über Tailscale ab. Die Kopie liegt dann außerhalb von Oracle und kostet nichts. Alternativ ist ein vorhandener Cloud-Speicher über rclone möglich.

### A6 · Altlasten lokal

`.local/owner-instance/erstpasswort.txt` löschst du selbst, sobald sicher ist, dass du dein aktuelles App-Passwort kennst. Die Datei wird nicht migriert.

## B · Aufgaben, die der Agent nach Freigabe übernehmen kann

Jeder Schritt braucht deine Freigabe im Chat.

| # | Schritt | Voraussetzung |
|---|---|---|
| B1 | SSH-Schlüssel `~/.ssh/solar_oci_ed25519` auf deinem PC erzeugen (`ssh-keygen -t ed25519`). Der private Schlüssel bleibt auf dem PC, den öffentlichen fügst du in A3 ein. | keine |
| B2 | age-Schlüssel für Backups erzeugen (`age-keygen`). Der private Schlüssel bleibt **offline** bei dir, idealerweise zusätzlich im Passwortmanager. Nur der öffentliche Empfänger kommt auf die VM. | age lokal installiert (portabel nach `.local/`) |
| B3 | VM härten: Updates, `unattended-upgrades`, SSH nur per Schlüssel, Benutzer `solar`, Docker CE aus dem offiziellen Repository mit Log-Rotation in `daemon.json` | VM aus A3, SSH-Zugang |
| B4 | Code auf die VM bringen: `git bundle` des freigegebenen Commits per `scp`. So liegen keine GitHub-Zugangsdaten auf der VM. Danach `/opt/solar/app` anlegen. | B3 |
| B5 | Secrets anlegen: `database-password` neu auf der VM erzeugen, `sungrow-app.json` und das Mapping per `scp` übertragen, ohne sie anzuzeigen (Ordner 0700, Dateien 0644). | B4 |
| B6 | Stack bauen und mit **leerer** Datenbank starten. Readiness, Netze und Härtung prüfen (Phase 7). | B5 |
| B7 | Backup-Timer installieren (`deploy/cloud/systemd/`) und einen Testlauf mit `restore-check.sh` machen. | B2, B6 |

## C · Erst nach eingerichteten Accounts und VM, jeweils mit eigener Freigabe

| # | Schritt | Wer |
|---|---|---|
| C1 | Probemigration: frischer, rein lesender Dump der privaten DB → Wegwerf-DB auf der VM → `verify.sql` vergleichen. Das überträgt **produktive Daten** in die Cloud und braucht deshalb deine Freigabe. | AGENT |
| C2 | Tailscale-Beitritt der VM: Der Agent startet den Container und nennt dir den Login-Link, **du** öffnest ihn und bestätigst. Danach `https://solar.<tailnet>.ts.net` vom PC testen. | DU + AGENT |
| C3 | SSH-Regel in der Security List entfernen. SSH läuft danach über Tailscale. | DU |
| C4 | Cutover nach [cloud-deployment.md, 4.2](cloud-deployment.md#42-kontrollierter-cutover). Dabei wird die **lokale Instanz auf 8081 gestoppt**, das braucht deine ausdrückliche Freigabe. | AGENT |
| C5 | In der Cloud-App anmelden, Sungrow-Verbindung prüfen und bei Bedarf neu autorisieren (Code selbst eingeben). | DU |
| C6 | Validierung Phase 7, wöchentliche Backup-Abholung auf dem PC einrichten, AGENTS.md-Regel zu 8081 anpassen | AGENT (Regeltext: deine Entscheidung) |
