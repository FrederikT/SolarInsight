# Betrieb

## Health, Protokolle und Störungen

`GET /health/live` bestätigt den laufenden HTTP-Prozess. `GET /health/ready` prüft die Datenbankverbindung. Ein laufender historischer Import verhindert die Nutzung schon vorhandener Daten nicht. JSON-Protokolle enthalten Kategorien und technische Statusmeldungen. Provider-Tokens und Request-Bodies werden nicht protokolliert.

Bei 401 erneut lokal anmelden beziehungsweise die Sungrow-Autorisierung erneuern. Bei 429 das im tatsächlichen App-Paket erlaubte Kontingent prüfen und den Aufrufabstand erhöhen. Authentifizierungsfehler werden ohne Endlosschleife beendet. Historische Anfragen haben höchstens drei Versuche. Retry-After wird bis zwei Minuten berücksichtigt, darüber endet der Job mit sichtbarem Status. Ein neuer Import kann vom gewünschten Korrekturdatum aus gestartet werden.

Bei fehlenden Messpunkten bleiben die betroffenen Kanäle unbekannt. Einheitenabweichungen stoppen den Import, statt Werte umzudeuten. Änderungen an bestehenden Zuordnungen oder der Anlagenzeitzone werden nach dem ersten Datenimport abgelehnt. Sie benötigen eine bewusst geplante Datenmigration.

### Historischer Sungrow-Import ohne Daten

1. In iSolarCloud einen abgeschlossenen Tag und eine Stunde mit sichtbarer PV-Leistung wählen. Im Datenstatus für denselben Tag und dieselbe Stunde in der Anlagenzeitzone einen kurzen Testimport starten. Für Momentanwerte braucht die Berechnung benachbarte Werte im bestätigten Raster.
2. Bei `no-data` die Zeile `Sungrow-Minutenimport` im App-Protokoll prüfen. `Anlagenreihe Undefined` bedeutet, dass der Antwort kein Feld für die angefragte Anlage entnommen werden konnte. Nichtleere Arrays oder Objekte neben dem Messpunktwörterbuch zeigen, dass es weitere Strukturen zu prüfen gibt. Ein im Wörterbuch vorhandener Leistungspunkt belegt seine Einheit, aber keine historische Messreihe. Bei erneut leerer Antwort die historische Verfügbarkeit dieses Messpunkts für Anlage und App mit Sungrow prüfen.
3. Erst nach einem Test mit verwertbaren Intervallen ganze Tage importieren. Das Enddatum ist exklusiv. Die gemeinsame Abdeckung misst PV und Hausverbrauch im selben Intervall. Eine allein zugeordnete PV-Reihe kann daher bei 0 % gemeinsamer Abdeckung erfolgreich sein.

Die Diagnose enthält nur angefragte Messpunktnummern, Zeitfenster, Strukturtypen und Anzahlen. Fehlgeschlagene leere Testimporte blockieren einen neuen Testauftrag nicht.

## Rechte für Compose-Secrets

Dateibasierte Compose-Secrets werden eingebunden. Die Anwendung läuft als Benutzer app und muss die gemounteten Dateien lesen können. Auf einem POSIX-Host den Ordner secrets mit Modus 0700 schützen und die enthaltenen Dateien für das Container-Mapping mit Modus 0644 bereitstellen. Andere Hostbenutzer können den Ordner dadurch nicht betreten. Unter Windows den Ordner per ACL auf das Betreiberkonto und den Docker-Dienst begrenzen.

## TLS-Reverse-Proxy

Ein bestehender nginx-TLS-Server kann diese Location verwenden. Seine eigene IP muss im App-Parameter `Solar:TrustedProxy` eingetragen sein. Die Zertifikate und der Servername sind vom Betreiber bereitzustellen.

```nginx
location / {
    proxy_pass http://127.0.0.1:8080;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Proto https;
    proxy_set_header X-Forwarded-For $remote_addr;
}
```

`SOLAR_ALLOW_HTTP=false` und ein korrekt konfigurierter TLS-Proxy sind Voraussetzung für sichere externe Nutzung. Die Veröffentlichung erfolgt durch den Betreiber.

## Backup

Vor Updates App stoppen, damit Datenbank, Konfiguration und Schlüssel konsistent gesichert werden. Die Datenbank bleibt für pg_dump in Betrieb. Unter einer POSIX-Shell ausführen. Für andere Compose-Projekte denselben Projektnamen bei jedem Befehl ergänzen.

```sh
mkdir -p backups
docker compose stop app
docker compose exec -T db pg_dump -U solar -d solar -Fc > backups/solar.dump
docker compose run --rm --no-deps --user 0 --entrypoint tar app -czf - -C /state . > backups/state.tar.gz
docker compose run --rm --no-deps --user 0 --entrypoint tar app -czf - -C /keys . > backups/keys.tar.gz
docker compose start app
```

Zusätzlich die serverseitigen Secret-Dateien und die Konfiguration separat verschlüsselt sichern. Datenbankdump, State- und Schlüsselarchiv sind gemeinsam wiederherzustellen. Ohne passendes Schlüsselmaterial sind geschützte Provider-Tokens unbrauchbar. Ein Backup mit gültigen Tokens muss wie ein Zugangsschlüssel geschützt werden.

## Restore auf eine leere Instanz

Einen eigenen Compose-Projektnamen verwenden und dieselben Datenbank-/App-Secret-Dateien bereitstellen. Die App vor dem Restore nicht starten. Ziel muss eine leere Datenbank sein. Diese Befehle löschen keine bestehende Instanz.

```sh
docker compose -p solar-restored up -d db
docker compose -p solar-restored exec -T db pg_restore --exit-on-error -U solar -d solar < backups/solar.dump
docker compose -p solar-restored run --rm --no-deps --user 0 --entrypoint tar app -xzf - -C /state < backups/state.tar.gz
docker compose -p solar-restored run --rm --no-deps --user 0 --entrypoint tar app -xzf - -C /keys < backups/keys.tar.gz
docker compose -p solar-restored up -d app
```

Der wiederhergestellte Stack benötigt für einen parallelen Test einen anderen `SOLAR_PORT`. Archive erhalten die ursprünglichen Dateirechte und Eigentümer. Anschließend Readiness, lokalen Login, Anlagenkonfiguration, Tarife und eine historische Auswertung prüfen. Ein echter Provider-Token kann zwischenzeitlich abgelaufen oder widerrufen sein und muss dann erneuert werden.

## Updates und Migrationen

1. Vollständiges Backup anlegen.
2. Release oder gewünschten Commit beziehen.
3. Änderungen und Migrationshinweise lesen.
4. `docker compose up --build -d` starten.
5. Readiness, Importstatus, historische Kennzahlen und Anmeldung prüfen.

Migrationen werden beim Start angewendet. Der versionierte EF-Modellsnapshot ermöglicht Folgeänderungen. Eine Versionsrückkehr mit inkompatiblem Schema erfolgt über das vor dem Update angelegte Backup. Rohmesswerte werden nicht automatisch entfernt.

## Ressourcen

Ausgelegt für mindestens zwei Kerne und 4 GB RAM. Die Anwendung begrenzt API-Requests pro IP, zwei parallele Rechenjobs, acht wartende Jobs, 32 kurzlebige Ergebnisse und 60 Sekunden Rechenzeit pro Job. Zeitraumabfragen sind auf 732 Tage, Simulationen auf 366 Tage begrenzt. Tagesdaten bleiben klein. Langzeitdiagramme erhalten Tages-/Monatsaggregate.



## Live-Übersicht und gezielte Historienabstimmung

Im Datenstatus zuerst **Dauer schätzen** und anschließend **Import einplanen** wählen. Die Basisdauer nutzt Abruffenster, Auflösung und den konfigurierten Mindestabstand. Antwortzeiten, Zeitzonenwechsel, Wiederholungen und zusätzliche Authentifizierungsaufrufe können den Lauf verlängern. Während des Imports zeigt der Balken den Anteil zwischen Beginn und Ende bis zum letzten gespeicherten Checkpoint. Ab zwei abgeschlossenen Fenstern nutzt die Restzeit das beobachtete Tempo einschließlich Wartezeiten. Fenster, leere Antworten, Minutenintervalle und Tageswerte werden getrennt gezählt. Bei vor dieser Migration begonnenen Aufträgen gelten die neuen Zähler ab dem ersten nach der Migration verarbeiteten Fenster. **Import abbrechen** beendet einen ausstehenden oder laufenden Auftrag. Bereits abgeschlossene Fenster bleiben erhalten.

Nach einem erfolgreich abgeschlossenen Import ganzer Tage untersucht der Hintergrunddienst abgeschlossene Kalendertage in der Anlagenzeitzone. Minutenlücken werden im bestätigten Raster in zulässige Fenster gebündelt. Fehlende Tagesaggregate werden separat nachgeladen. Der Dienst prüft vor jedem Abruf erneut die Datenbank und ergänzt bei gleicher Quelle, Auflösung und Zeitgrenze nur fehlende Kanäle. Bei abweichender Qualität ist ein vollständiger Ersatz mit identischen Zeitgrenzen erforderlich. Er priorisiert aktuelle Live-Abrufe. Nach drei erfolglosen Versuchen erscheint die Lücke als offen. Ein späterer manueller Import kann sie schließen. Offene Lücken werden lokal erneut geprüft, ohne weitere Cloud-Aufrufe auszulösen. Standardwerte sind ein täglicher Scan und 24 Stunden zwischen Wiederholungen. `SUNGROW_HISTORY_RETRY_HOURS` erlaubt 24 bis 48 Stunden.

Unter **Einstellungen → Live-Messpunkte prüfen** ein Gerät und die jeweiligen Messpunkte konfigurieren. Die Probe vor Bestätigung mit iSolarCloud vergleichen. Fehlende Kanäle bleiben unbekannt. Für alle Tabs und den Heimnetz-Endpunkt gibt es einen gemeinsamen Cache je App-Instanz. Die Übersicht signalisiert alle 30 Sekunden Sichtbarkeit und liest den Cache. Die Sichtbarkeitsfrist endet nach 70 Sekunden ohne Signal. Der Hintergrundtakt liegt standardmäßig bei 600 Sekunden. Der 30–60-Sekunden-Takt bei sichtbarer Übersicht gilt für Cache und Aktivität. Für die aktuelle Sungrow-Quelle gilt dabei die dokumentierte Untergrenze von 300 Sekunden zwischen Live-Cloud-Abfragen. Der Untergrenzenwert kann erhöht werden. Bei Fehlern verzögert ein gestaffelter Wiederanlauf die nächste Abfrage und beachtet `Retry-After`. Ein bestätigter Messwert wird nach standardmäßig 900 Sekunden als veraltet angezeigt.

Der Tagesverlauf unterscheidet gespeicherte Momentwerte von historischen Intervallwerten. Historische Intervalle haben für einen abgedeckten Kanal und Zeitraum Vorrang. Ein unverifizierter oder fehlender Kanal wird dadurch nicht ersetzt. Die Live-Stichproben und ihre Lücken bleiben gespeichert. Momentwerte fließen nicht in Energie, Tarife oder Simulation ein. Der Sonnenüberschuss erscheint nur bei gemessener Einspeisung, gemeinsamem plausiblen Fluss und bestätigtem Ausschluss von Batterieexport. Die historische Sungrow-Route liefert nur abgeschlossene Tage. Der laufende Tag zeigt daher normalerweise Live-Stichproben.

### Lesender Heimnetz-Zugang

Der Endpunkt `GET /api/v1/lan/now` ist standardmäßig deaktiviert. Für den Betrieb das Overlay `-f compose.lan.yaml` zur privaten Compose-Konfiguration ergänzen. `SOLAR_LAN_ENABLED=true`, `SOLAR_LAN_NETWORK` als konkretes CIDR des Heimnetzes und `SOLAR_LAN_KEY_SECRET` als Pfad zu einer lokalen Datei mit einem separaten zufälligen Leseschlüssel setzen. Ein 32-Byte-Zufallswert in Hex erfüllt die Dateivorgabe. Der Client übergibt `Authorization: Bearer <Leseschlüssel>`. Schlüssel gehören ausschließlich in die Secret-Datei und den Smart-Home-Secret-Speicher.

Der Compose-Port bleibt an Loopback gebunden. Für Geräte im Heimnetz den Pfad über einen lokalen HTTPS-Reverse-Proxy freigeben und nur den konkreten Proxy als `SOLAR_TRUSTED_PROXY` konfigurieren. Er muss die Clientadresse als `X-Forwarded-For` weiterreichen und fremde Forwarding-Header verwerfen. Die API prüft diese Adresse gegen das konfigurierte CIDR. Der vorhandene HTTPS-Schutz gilt auch für den Heimnetz-Endpunkt. Der Schlüssel gewährt keinen Zugriff auf Einstellungen oder Importaktionen.

Die Antwort enthält unter `grid` vorzeichenbehaftete kW (`value`), `direction` (`import`, `export`, `idle` oder `unknown`), `measuredAt`, `ageSeconds` und `quality`. Bei `unknown` ist `value` null. Positive Netzleistung ist Bezug, negative Leistung Einspeisung. Aufrufe lesen ausschließlich den Cache oder dessen Datenbankstand nach einem Neustart.
