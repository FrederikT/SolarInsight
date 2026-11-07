# Betrieb

## Health, Protokolle und Störungen

`GET /health/live` bestätigt den laufenden HTTP-Prozess. `GET /health/ready` prüft die Datenbankverbindung. Ein laufender historischer Import verhindert die Nutzung schon vorhandener Daten nicht. JSON-Protokolle enthalten Kategorien und technische Statusmeldungen. Provider-Tokens und Request-Bodies werden nicht protokolliert.

Bei 401 erneut lokal anmelden beziehungsweise die Sungrow-Autorisierung erneuern. Bei 429 das im tatsächlichen App-Paket erlaubte Kontingent prüfen und den Aufrufabstand erhöhen. Authentifizierungsfehler werden ohne Endlosschleife beendet. Historische Anfragen haben höchstens drei Versuche. Retry-After wird bis zwei Minuten berücksichtigt, darüber endet der Job mit sichtbarem Status. Ein neuer Import kann vom gewünschten Korrekturdatum aus gestartet werden.

Bei fehlenden Messpunkten bleiben die betroffenen Kanäle unbekannt. Einheitenabweichungen stoppen den Import, statt Werte umzudeuten. Änderungen an bestehenden Zuordnungen oder der Anlagenzeitzone werden nach dem ersten Datenimport abgelehnt. Sie benötigen eine bewusst geplante Datenmigration.

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

