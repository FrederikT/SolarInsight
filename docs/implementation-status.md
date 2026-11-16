# Umsetzungsstand

## Anforderungen und offene Arbeit

| Bereich | Anforderungen | Stand |
| --- | --- | --- |
| Kernumfang | [Solar Insight Spezifikation](../SolarInsight_Spezifikation.md) | A01–A24 umgesetzt und geprüft. A24 mit Testdaten. Die Sungrow-Abnahme A25 ist offen. Die Einzelprüfungen stehen unten. |
| Kalender, Energieflüsse und Speicher | [Bugs und Feature-Anforderungen](../SolarInsight_Bugs_und_Features.md) | BUG-01–04 und FEATURE-01–05 umgesetzt. Der Abgleich der Messpunkte an einer echten Anlage bleibt Teil von A25. |
| Import, Live-Daten und Heimnetz | [Sungrow-Import](import-todos.md) | Erledigte Anforderungen und die offene Anlagenprüfung stehen in der Checkliste. |
| Bedienung, Setup und automatische Importe | [Backlog](backlog.md) | UX-01 bis UX-10, CI-01, LIVE-01, LIVE-02, POT-01, POT-02, KPI-01, FLOW-01, BAT-01, BAT-02, DATA-03 bis DATA-07 umgesetzt und geprüft. UX-11 (einschließlich Kopfzeilen-Logo und Tab-Icon) und COST-01 sind umgesetzt und mit der Demo geprüft (für COST-01 fehlen auf der privaten Anlage die Betreibertarife). DATA-01 und DATA-02 sind teilgeprüft. OPS-01 ist in der Cloud in Betrieb. DATA-08 ist offen, siehe [Datenverfügbarkeit](data-availability.md). |
| Repository-Lizenz | [Solar Insight Spezifikation](../SolarInsight_Spezifikation.md) | [MIT-Lizenz](../LICENSE) mit Copyright-Vermerk für Frederik Thuss. |

## Umsetzungsliste

1. Datenvertrag, Versionen, Compose, Migration und Provider-Erkundung (F-01–04, D-01–02, A01–03, A25).
2. Vertikaler Demo-Pfad und Kalenderanalysen (D-03–05, U-01–03, B-01, A04, A07–08).
3. Tarife, Einstellungen, Import und Datenqualität (K-01–03, U-05, A05–06).
4. Bilanzkern, Referenzvalidierung, Ereignisse und Kapazitätskurve (S-01–06, E-01–02, A09–13, A20).
5. Kombinierbare Szenarien, Exporte und Amortisation (V-01–02, Z-01–03, A14–19).
6. Absicherung, Browserprüfung, Backup/Restore und Betriebsübergabe (U-06, B-02–03, A21–25).


## Lieferstand

- Fundament, Backend, PostgreSQL-Migrationen, Compose, Lockfiles und CI sind vorhanden. Der lokale Release-Publish enthält das gebaute Frontend und läuft gegen PostgreSQL 17.
- Demoimport, Zeitraumübersicht, Tagesdetail, Heatmap, Ereignisse, Datenstatus, Anlagenparameter und Tarife verwenden die gemeinsamen serverseitigen Analysepfade.
- Speichererweiterung, Kapazitätskurve, Verschiebung, Zusatzbedarf, Kostenvergleich, begrenzte Rechenjobs und Exporte sind implementiert.
- Der private Betriebsmodus besitzt lokalen Zugang, CSRF-Schutz, getrennte Schlüsselhaltung und den dokumentierten Sungrow-Leseadapter.
- Die Provider-Freigabe, echte Messpunktprobe und kontobezogenen Grenzwerte bleiben vor Live-Abnahme offen.
- Docker Compose wurde lokal mit frischen Demo- und privaten Testvolumes geprüft. Die Restore-Prüfung wurde separat mit portablem PostgreSQL ausgeführt.

## Abnahme je Kriterium

| ID | Stand | Evidenz |
| --- | --- | --- |
| A01 | Geprüft | Docker-Image neu gebaut. Frischer Compose-Start auf leeren Volumes mit erfolgreicher Migration und abgeschlossenem synthetischem Import. |
| A02 | Geprüft | PostgreSQL-Test für Duplikate, unveränderte Summe, Korrektur, neue Revision und Löschung betroffener integrierter Aggregate. Ergebnisse tragen die Revision und ihr Abruf verwirft veraltete Revisionen. |
| A03 | Geprüft | PostgreSQL-Checkpoint, Wiederaufnahme mit neuem Kontext, Rollback eines unterbrochenen Fensters, 429 mit Retry-After, 401 ohne Retry und Stopp nach drei Timeouts. |
| A04 | Geprüft | 4 kW über 15 Minuten ergeben 1 kWh. Trapez, Lücken, Messpunkt-Einheiten und Reset separat geprüft. |
| A05 | Geprüft | Unabhängige xUnit-Referenz für 5,00 EUR Bezug, 0,32 EUR Erlös und 4,68 EUR netto. |
| A06 | Geprüft | Überlappung, negative Preise, Tariflücke, zeitanteilige Grenze und Grundpreis über Monatswechsel. Privater HTTP-Test prüft den Überlappungsschutz. |
| A07 | Geprüft | 23 und 25 Stunden in Europe/Berlin mit eindeutigen UTC-Intervallen. Winter 2025/26, ISO-Vorjahreswoche und Schaltjahrgrenze. |
| A08 | Geprüft | Qualitätsfixture, null statt Nullenergie, Abdeckungsquote, Lückensegment mit SOC-Reset und Sperre regulärer Simulation. |
| A09 | Geprüft | Speicherloses Referenzintervall mit 2 kWh Direktverbrauch und 3 kWh Export. |
| A10 | Geprüft | 2 kWh Ladung, 1,8 kWh Bestand, 3 kWh Export und 0,2 kWh Verlust. |
| A11 | Geprüft | 1,62 kWh Entladung, 1,38 kWh Import und 0,18 kWh Verlust. |
| A12 | Geprüft | Deterministische Zufallstests für Energie, Bestand, Leistung, Zusatzlastzuteilung und Ausschluss gleichzeitiger Ladung/Entladung. |
| A13 | Geprüft | Identische Läufe liefern Delta null. Absoluter Anfangsbestand bleibt gleich. Ein positiver Endbestand erzeugt keine Gutschrift. |
| A14 | Geprüft | Tagesenergie, Quell- und Zielzeit, Leistung, Schritte und Tagesbudget. Nullbudget bleibt wirkungslos. |
| A15 | Geprüft | Referenzbeispiel senkt Bezug und Export von je 2 auf je 1 kWh. |
| A16 | Geprüft | Überschuss-only begrenzt Zusatzbedarf und erhält Speicherbestand sowie Haushaltsbezug. |
| A17 | Geprüft | Vollständige Deckung, Zuteilung nach Herkunft, Eingabefehler bei unerreichbarem Ziel und Nullbedarf. |
| A18 | Geprüft | Kombinierte Bilanz in xUnit und kombinierter Lauf im Browser. |
| A19 | Geprüft | Unter zwölf Monaten keine Zahl. Null- und Negativersparnis bei vollständigem Jahr zeigen keine statische Amortisation. |
| A20 | Geprüft | Ereignistest für lückenfreie Dauer und Zahlenwerte. Playwright öffnet den belegenden Tag oder Zeitraum. |
| A21 | Geprüft | HTTP-Tests für Demo-Schreibschutz, gesperrten Providerzugriff und getrennten Zugriff auf Job, Export und Abbruch. |
| A22 | Geprüft | Privates HTTP-Login, CSRF, anonyme Sperre, Änderungen nach Login und Logout. Testpasswort fehlt in geprüften Antworten, Export und lokalen Testlogs. |
| A23 | Geprüft | Playwright wechselt Zeitraum, öffnet Tag, kehrt zurück, vergrößert Speicher, kombiniert Module und exportiert CSV und JSON. Layoutprüfung bei 375 px. |
| A24 | Geprüft mit Testdaten | pg_dump und pg_restore auf leere Datenbank. Restore von Anlagen-/Tarifdaten, State und Schlüsseln. Erneuter privater Login ohne Setup-Secret. Schlüssel-Restore entschlüsselt eine synthetische Provider-Tokenfixture. |
| A25 | Teilgeprüft | EU-App, Eigentümerautorisierung, reale Live-Kanäle und historische Zählerproben sind belegt. Offen bleiben die separate AC-PV-Messung, das tatsächliche App-Kontingent und die verfügbare Rückschau. [Neuere Prüfnachweise](#data-01-und-data-02--gerätezähler-und-getrennte-live-leistungen). |

## Fortsetzung

1. Die Restpunkte der Sungrow-Abnahme und die Nutzung gespeicherter Stichproben für den laufenden Tag klären. [Abnahme und nächste Schritte](today-acceptance.md).

Die manuelle OAuth-Codeübergabe, bekannte Modellgrenzen und Schlüsselhaltung sind in der Architektur und Betriebsdokumentation beschrieben. Messungen und Befehle stehen in verification.md.

## Nachtrag zur Aufgabenliste vom 7. November 2026

- BUG-01–04 verwenden direkte Kalenderwahlen. Bestehende URL-Parameter bleiben gültig. Kalendergrenzen bleiben unabhängig von der Verfügbarkeit von Messdaten bestehen.
- FEATURE-01–03 verwenden plausible gemeinsame Intervallwerte und eine kombinierte Energiebilanz. Bei bestätigten AC-Pfaden stammt PV-Direktverbrauch aus den jeweiligen Restgleichungen des Haushalts und der PV-Erzeugung. Der aktuelle Messvertrag stellt keinen separat bestätigten Direktfluss bereit. Fehlende Kanäle und größere Abweichungen schließen das Intervall aus. Die Toleranz beträgt 0,05 kW multipliziert mit der tatsächlichen Intervalllänge, zuzüglich 0,000000001 kWh für Rundung. Kleine negative Reste werden auf null begrenzt und ein verbleibender Bilanzrest wird ausgewiesen. Anteile beziehen sich auf die Energie der auswertbaren Intervalle.
- FEATURE-04–05 nutzen lokale volle Stundenfenster. Nacht ist 22:00 bis 06:00 Uhr. Auswertbarkeit wird je Kanal oder Kanalkombination separat bestimmt. Ein Fenster zählt pro Tag einmal, wenn mindestens ein gemeinsames Intervall die Bedingung erfüllt. In der wiederholten Herbststunde fließen beide UTC-Stunden in Zeitdauer und Mittel ein, der Kalendertag zählt einmal. Die übersprungene Stunde im Frühjahr trägt nicht zum Nenner bei. Die SOC-Kurve für mehrere Tage ist zeitgewichtet und auf das nutzbare Fenster bezogen. Im Tagesdetail bleibt der gemeldete SOC sichtbar. Zustände gelten auf Ebene des gemeldeten Messintervalls.
- Die Dauer je Nacht zählt erfasste Zeit an der unteren Ladegrenze ab 22:00 Uhr bis zum ersten nachweisbaren PV-Intervall am selben oder folgenden Kalendertag. Eine Lücke, fehlender SOC oder das Ende der verfügbaren Auswahl kann den Nachweis begrenzen. Die Anzeige kennzeichnet dies.
- Sungrow-Live-Abnahme bleibt offen. Die neuen Ansichten verwenden vorhandene Demodaten und unabhängige deterministische Fixtures. Es wurden keine unbestätigten Sungrow-Messpunktzuordnungen ergänzt.

- Ladegrenzenaussagen und nutzbarer SOC verwenden je Intervall die damals gültige Speicherkonfiguration. Fehlt eine eindeutige Zuordnung, bleibt der jeweilige Speicherzustand unbekannt. Gemessener Netzbezug und gemeldeter SOC bleiben sichtbar.
- Lokal ausgeführt und bestanden sind der .NET-Release-Build, ESLint, TypeScript/Vite, 24 Unit-Tests, 13 PostgreSQL-Integrationstests und vier Playwright-Tests. Der neue Browserfall prüft unter anderem den unvollständigen Herbst, die direkte Kalenderwahl, KW 53 am Jahreswechsel sowie den Uhrzeit-Drilldown mit Rücksprung.


## Sungrow-Import und Live-Übersicht

| Abnahme | Stand |
| --- | --- |
| Fortschritt, Restzeit und Vorschau | Prozent aus dem Checkpoint. Fenster, leere Antworten und tatsächlich geschriebene Werte getrennt. Prognose aus der beobachteten Laufzeit. Browser- und Integrationsfixtures bestanden. |
| Gezielter Abbruch | Ausstehende und laufende Aufträge enden dauerhaft als abgebrochen. Fertige Fenster bleiben bestehen. Ein weiterer Zeitraum kann sofort eingeplant werden. PostgreSQL-Rennfall und Browserbedienung geprüft. |
| Cache, Neustart und Lücken | Sichtbarkeitsfrist pro Tab, ein geteilter Cache, Quellabstand und Wiederholungen beachtet. Deduplizierung, fehlende Kanäle und Ausfall über lokale Mitternacht geprüft. |
| Historienabstimmung | Begrenzte tägliche Scans, beständige Warteschlange und erneute Prüfung vor jedem Abruf. Nach drei unvollständigen Versuchen bleibt die Lücke offen. PostgreSQL-Restart und leere Antworten geprüft. |
| Jetzt, Heute und Heimnetz | Flussrichtung, Gleichzeitigkeit, Bilanz, DST, kanalweiser Vorrang der Historie und Aufbewahrung der Live-Stichproben geprüft. HTTP-Abruf ohne und mit getrenntem Leseschlüssel geprüft. |
| Autorisierte Sungrow-Anlage | Offen. Messpunkte, Vorzeichen, Zeitstempel, Gleichzeitigkeit, tatsächliche Aktualisierung und gebuchtes Kontingent mit iSolarCloud abgleichen. Die öffentliche Herstellerempfehlung begrenzt den Cloud-Takt auf frühestens fünf Minuten. |

Der Release-Build, die EF-Modellprüfung, ESLint, TypeScript/Vite, 24 Unit-Tests, 33 PostgreSQL-Integrations- und Vertragstests sowie sechs Playwright-Tests sind lokal durchgelaufen. Die Compose-Konfiguration einschließlich der Sungrow- und Heimnetz-Overlays wurde geprüft. Die Anwendung lief für die Browser- und HTTP-Tests als lokaler Release-Build mit PostgreSQL. Der Heimnetz-Smoke-Test bestätigte 401 bei fehlendem oder falschem Schlüssel, 200 beim lesenden Zugriff mit Schlüssel, 401 für den Zugriff auf Eigentümerrouten mit Leseschlüssel und 404 für den deaktivierten Demo-Endpunkt.


## Erneute Abnahme und lokale Bereitstellung

Am 8. November 2026 wurde die laufende lokale Demo auf Port 8080 mit Docker Compose aus dem aktuellen Quellstand neu gebaut. Compose ersetzte den App-Container und verwendete die vorhandene Datenbank weiter. Der Readiness-Endpunkt meldete HTTP 200. Die Monatsübersicht meldete 100 % Datenabdeckung.

Die private Instanz auf Port 8081 war zusammen mit ihrer lokalen PostgreSQL-Instanz gestoppt. Beide wurden mit der vorhandenen Datenbank und Konfiguration gestartet. Die App verwendet einen neuen Release-Build. Der Readiness-Endpunkt meldete HTTP 200, der Sitzungsendpunkt bestätigte den privaten Modus und der anonyme Zugriff auf die Anlagendaten erhielt HTTP 401.

Docker Compose hat das aktuelle Image gebaut und die bestehende Demo auf Port 8080 aktualisiert. Die private Instanz auf Port 8081 erhielt denselben Quellstand als .NET Release-Publish. Vor ihrer ersten Migration wurde ein lokaler Datenbankdump erstellt. Beide Instanzen meldeten HTTP 200 auf `/health/ready`.

Zusätzlich starteten getrennte Compose-Projekte mit frischen Volumes für Demo und privaten Testbetrieb. Die Demo schloss den synthetischen Import ab. Bestanden sind 25 Unit-Tests, sechs Playwright-Tests gegen diese Compose-Projekte, ESLint, TypeScript und der Release-Build. Der neue Speichertest prüft, dass dieselbe SOC-Messung vor und nach einem Wechsel der Ladegrenze unterschiedlich eingeordnet wird.

Die erneute Sungrow-Live-Abnahme bleibt offen. Die automatische Freigabeprüfung hat die lesende Abfrage privater Anlagendaten bis zu einer ausdrücklichen Betreiberfreigabe abgelehnt.

## Backlog vom 8. November 2026

### UX-01 · Oberfläche und Datenstatus

Verbindung, letzter erfolgreicher Abruf und Importzustand stehen vor den technischen Details. Die Ansicht zeigt bei fehlender Einrichtung und Importfehlern den passenden nächsten Schritt. Der letzte erfolgreiche Abruf berücksichtigt Import, historischen Abgleich und Live-Cache. Details zu Import, Geräten, Messpunkten und Datenlücken sind aufklappbar. Die Übersicht unterscheidet fehlende Minutenwerte von verfügbaren Tageswerten.

Playwright prüfte alle vier Hauptansichten bei 1400 und 375 px sowie das private Setup bei 375 px, einschließlich Fehlermeldung zur erneuten Autorisierung und Abrufstatus. Die Tests verwenden Demo- und Provider-Fixtures.

### UX-02 · Setup und Betreiberprofil

Das Setup fragt Autorisierung, Anlagenwahl und Zeitzone ab. Für neue Anlagen sind dokumentierte Tagesmesspunkte und Europe/Berlin voreingestellt. Bestehende Zuordnungen bleiben erhalten. Für Minuten-, Live- und EMS-Kanäle ohne bestätigtes Profil zeigt die Diagnose den fehlenden Kanal und den Grund. Bestätigte Sonderzuordnungen werden über die serverseitige [Betreiberdatei](operations.md#betreiberprofil-für-messpunkte) angewendet.

Der normale Setup-Endpunkt weist mitgesendete Zuordnungen zurück. Der frühere Schreibzugriff auf Live-Zuordnungen ist entfernt. Playwright prüfte beide Fälle an der privaten HTTP-Instanz. Die PostgreSQL- und Sungrow-Vertragstests verwendeten eine synthetische Autorisierung, Anlagenliste und ein bestätigtes Betreiberprofil als Fixture. Die vollständige Abnahme an einer echten Anlage gehört weiterhin zu A25.

### UX-03 · Automatischer Erstimport

Die Anlagenaktivierung legt den Import standardmäßig für 30 abgeschlossene Kalendertage in derselben Transaktion an. Wiederholte Bestätigung und Neustart behalten den Auftrag. Auch ein abgebrochener Auftrag wird bei erneuter Bestätigung nicht dupliziert. Leere Tage am Anfang stoppen den automatischen Erstimport nicht. Nach erfolgreichem Erstimport übernimmt der vorhandene historische Abgleich. Nachimport, kurzer Testimport für Minutenprofile und Abbruch bleiben erreichbar.

Die PostgreSQL-Tests prüften den Auftrag über wiederholte Aktivierungen und neu erzeugte Providerinstanzen, Abbruch, Profiländerung und den Import nach leeren Anfangstagen. Playwright prüfte Vorschau, sichtbaren Fortschritt und Abbruch.

### CI-01 · Pipeline

Ein lokaler Testlauf auf leerem PostgreSQL scheiterte bei paralleler Initialisierung mit SQLSTATE `42701` an der bereits angelegten Spalte `Readings.Quality`. Ein sitzungsgebundener PostgreSQL-Advisory-Lock serialisiert jetzt Migration und Initialdaten über die einzelnen EF-Transaktionen hinweg. Der erneute Lauf auf leerer Testdatenbank war erfolgreich.

Lokal bestanden der .NET Release-Publish, die EF-Modellprüfung, TypeScript, ESLint und Vite-Build, 25 Unit-Tests, 36 PostgreSQL- und Vertragstests sowie 9 Playwright-Tests gegen getrennte lokale Demo- und private Testinstanzen. Der Demo-Neustart erhielt die vollständige Monatsabdeckung. Der PostgreSQL-Restore in eine leere Datenbank enthielt die erwarteten Anlagen- und Tarifdaten. Diese Prüfung lief mit lokalem PostgreSQL und dem Release-Publish.

Das [GitHub-Jobprotokoll zum Migrationsfehler](https://github.com/FrederikT/SolarInsight/actions/runs/42470317220/job/128699313378) bestätigt SQLSTATE `42701` vor dem Fix. Der [aktuelle Lauf für `d5139c5`](https://github.com/FrederikT/SolarInsight/actions/runs/42645352279/job/129257022188) besteht Backend, Frontend und den Start beider Compose-Stacks. Playwright meldet in Übersicht und Tagesdetail eine Dokumentbreite von 388 statt 375 Pixeln. Die Restore-Prüfung wurde dadurch übersprungen.

Im lokalen Linux-Chromium lässt sich der Überlauf mit 125 % Schriftgröße reproduzieren. Der lange KPI-Name „Eigenverbrauchsquote“ vergrößerte die innere Grid-Spalte über die Karte hinaus. Die Spalte darf jetzt schrumpfen und lange Wörter werden bei Bedarf umgebrochen. Der neue Regressionstest prüft die Karten und die Dokumentbreite bei 375 Pixeln mit vergrößerter Schrift. Der GitHub-Workflow bleibt unverändert.

Am 10. November 2026 wurden die [lokale Demo](http://127.0.0.1:8080) per Docker Compose und die [private Instanz](http://127.0.0.1:8081) per .NET Release-Publish neu gebaut und gestartet. Beide behalten ihre Datenbank und Konfiguration, melden `ready` und liefern dieselbe korrigierte CSS-Datei aus. TypeScript, ESLint und Vite-Build liefen erfolgreich durch.

Alle 10 Playwright-Tests liefen bei der ursprünglichen lokalen Layoutprüfung mit Linux-Chromium gegen die Demo und eine isolierte private Compose-Testinstanz durch. Der Restore der privaten Testdatenbank in eine leere PostgreSQL-Datenbank ergab die erwartete Anlage und einen Tarif.

[GitHub Actions, Lauf 42785223950](https://github.com/FrederikT/SolarInsight/actions/runs/42785223950) bestätigte den Anwendungsstand `7793acc`. Der Job `verify` bestand Backend, PostgreSQL, Frontend, frischen Compose-Start, Browser- und Zugriffstests sowie Neustart und Restore.

## Übersicht und Datenprüfung vom 10. November 2026

### UX-04 und UX-05 · Kennzahlen und Zeitreihen

Die Übersicht trennt gemeinsame Tages- und Minutenabdeckung. Unter dem Info-Symbol stehen die Erläuterungen zu Quellen und Näherungen. Bei vollständigen Tageswerten für PV und Verbrauch benennt der Hinweis konkret die unvollständige Minutenreihe. Fehlende Eingangsgrößen, zu wenige Daten und fehlende Bestätigung von Energiepfaden werden bei den Quoten unterschieden, soweit sich der Grund aus dem Bericht ableiten lässt. Berechenbare Quoten aus unvollständigen Intervallreihen werden als vorläufig markiert. Live-Stichproben werden weiterhin nicht als Energiedaten für die Tagesquote verwendet.

Einzelne Tage zeigen im Zeitverlauf kW und SOC in Prozent. Der Verlauf benennt fehlende Reihen. Die Heatmap zeigt einen Leerzustand für den gewählten Kanal und behandelt gemessene Nullwerte als Werte. Auf dem Desktop wurde die Maximalbreite angehoben. Das Diagramm für den laufenden Tag wird erst nach dem Aufklappen geladen.

### DATA-01 · Lokaler November-Bestand

Eine schreibgeschützte Abfrage der privaten PostgreSQL-Datenbank bestätigte folgende Daten.

| Tag | Sungrow-Tageskanäle | Historische Intervalle | Gemeinsame PV-/Verbrauchsintervalle |
| --- | --- | --- | --- |
| 1. November | PV, Last, Bezug, Einspeisung, Ladung, Entladung | 49 mit ausschließlich Last | 0 |
| 4. November | PV, Last, Bezug, Einspeisung, Ladung, Entladung | 34 mit ausschließlich Last | 0 |
| 6. November | PV, Last, Bezug, Einspeisung, Ladung, Entladung | 96 mit ausschließlich Last | 0 |

Damit sind die Tagesaggregate am 6. November vorhanden und die 0 % gemeinsamer Minutenabdeckung sind korrekt. Autorisierte Stichproben vom 10. November lieferten für alle drei abgefragten Tage alle sechs Tageskanäle. Im 15-Minuten-Mittagsfenster lieferten Last (83106) und Anlagenwirkleistung (83033) am 1., 4. und 6. November jeweils 6, 0 und 9 Werte. Die Anlagenwirkleistung ist kein bestätigter PV-Kanal. Die geprüften Kandidaten für PV (83067), alternative Last (83052), Netz (83549), Batterie (83238) und SOC (83129) lieferten in allen drei Fenstern null Werte.

Die erfolgreiche Anfrage bestätigt den Zugriff auf den Endpunkt. Eine leere Messpunktreihe unterscheidet ohne zusätzlichen Providerbeleg fehlende Punktberechtigung und ausbleibende Quellwerte nicht. Die gespeicherten Lastintervalle am 4. November belegen Daten außerhalb oder unabhängig vom abgefragten Mittagsfenster. Für PV, Netz, Batterie und SOC fehlt eine bestätigte historische Zuordnung. Es gab daher keinen Profilwechsel und keinen Nachimport derselben Zeiträume.

### DATA-02 · Live-Profil und Anzeige

Das private Profil enthält Live-Zuordnungen für Last, Netz und Batterieleistung. Die lokale Datenbank bestätigte aufeinanderfolgende Live-Zeitstempel in Fünf-Minuten-Abständen. PV und SOC fehlen im Profil. Die Anzeige unterscheidet fehlende Zuordnung, fehlenden Wert, veralteten Wert und Abruffehler. Bestätigte 0 % SOC erscheinen als „Leer · 0 %“. SOC bezeichnet den Ladezustand. Laden und Entladen stehen bei der Batterieleistung. Der Gleichzeitvergleich der gemeldeten Wattwerte mit iSolarCloud steht aus.

Die private Statusabfrage am 10. November bestätigte die drei zugeordneten Kanäle. Die Freigabe zum Lesen des offenen iSolarCloud-Browserfensters lief ab. Für den Gegenvergleich fehlen deshalb zeitgleiche Portalwerte und für PV und SOC ein bestätigter Messpunkt beziehungsweise die bestätigte Skalierung. Das aktive Profil blieb erhalten.

### DATA-01 und DATA-02 · Nachprüfung am 10. November

Die private API bestätigte den abgeschlossenen Import für den 9. November (Europe/Berlin) ohne Fehler und ohne geänderte Datensätze. Im Bestand liegen sechs Sungrow-Tageskanäle und 83 von 96 möglichen Viertelstunden mit Hausverbrauch vor. PV, Netzbezug, Einspeisung, Ladung, Entladung und SOC fehlen im Minutenverlauf. Die aktive Minutenhistorie fragt ausschließlich Lastpunkt `83106` ab.

Die autorisierte, lesende historische Tagesprobe für denselben Tag lieferte HTTP 200, alle sechs Tageskanäle sowie je neun Minutenstichproben für Last `83106` und Anlagenwirkleistung `83033`. PV `83067`, alternative Last `83052`, Netz `83549`, Batterie `83238` und SOC `83129` lieferten im geprüften Mittagsfenster keine Werte. Der Erfolg des Endpunkts erlaubt noch keine Aussage über die Freigabe einzelner Messpunkte.

Am 10. November umfasste der lokale Tagesverlauf je 16 Live-Stichproben für Last, Netz und Batterie. Die jüngste Quellmessung dieser Stichprobe stammt von 10:45 Uhr und wurde gegen 10:49 Uhr abgerufen. PV und SOC waren ohne Zuordnung. Die bestehende Live-Probe unterstützt keine zusätzlichen Kandidaten. Profil und gespeicherte Historie wurden durch die Diagnose nicht geändert. Die Gerätehistorie und die lesende Live-Kandidatenprobe bleiben als nächste Prüfungen offen.

### DATA-01 und DATA-02 · Gerätequelle und Lastlücke

Die Geräteliste der privaten Instanz weist einen Sungrow SH10RT-V112 vom Typ 14 aus. Die bestehende EU-App war für eine Gerätehistorienprobe auf diesem Gerät autorisiert. Am 9. November 2026 von 11:00 bis 13:00 Uhr (Europe/Berlin) lieferte die Gerätehistorie neun Quellzeitpunkte im 15-Minuten-Raster:

| Kanal | Punkt | Ergebnis | Stichprobe um 11:00 Uhr |
| --- | --- | --- | --- |
| Hausverbrauch | 13119 | 9 Werte in W | 653 W |
| Netzbezug / Einspeisung | 13149 / 13121 | je 9 Werte in W | 13 / 0 W |
| Batterieladung / -entladung | 13126 / 13150 | je 9 Werte in W | 2.675 / 0 W |
| SOC | 13141 | 9 Rohwerte, Einheit leer | 0,378 roh |
| DC-Gesamtleistung | 13003 | 9 Werte in W | 3.437 W |
| Wechselrichter-Wirkleistung | 13011 | 9 Werte in W | 640 W |

Die Wechselrichter-Wirkleistung liegt bei dieser Stichprobe nahe am Hausverbrauch abzüglich Netzbezug. Der SOC-Rohwert kann erst mit einem Portalwert desselben Zeitpunkts sicher skaliert werden. Der Gerätevertrag belegt das Raster, während die Integration der Gerätewerte zu Energien noch eine Bestätigung der Intervallsemantik benötigt. Das Profil und die Historie wurden durch die Proben nicht geändert.

Die gespeicherten Tageswerte stimmen mit den iSolarCloud-Referenzen aus dem Backlog überein: PV 13,6 kWh und Last 16,1 kWh. Die 13 fehlenden Lastintervalle beginnen im Viertelstundenraster um 16:15 bis einschließlich 19:15 Uhr. Im Abfragefenster 16:15–19:15 Uhr lieferten Anlagen- und Gerätequelle jeweils nur einen Lastwert um 16:15 Uhr (539 W am Anlagenpunkt 83106, 465 W am Gerätepunkt 13119). Die Anlagenprobe über 19:15–19:45 Uhr lieferte erst um 19:30 und 19:45 Uhr wieder Lastwerte. Das aktive Anlagenprofil integriert Momentanwerte und benötigt beide Intervallgrenzen. Damit erklärt die Quelllücke alle 13 fehlenden Intervalle. Eine Wiederholung des Imports mit derselben Datenlage würde sie nicht schließen.

Der Betreiberabgleich braucht einen Portalverlauf desselben Geräts am 9. November gegen 11:00–13:00 Uhr und eine zeitgleiche Live-Stichprobe mit PV, Last, Netzrichtung, Batterierichtung und von null verschiedenem SOC. Alternativ kann der Betreiber den Zugriff auf das angemeldete Portal für den Vergleich freigeben. Die App-Freigabe für die geprüfte Gerätehistorie liegt bereits vor. Die Anlagen-Live-Route weist laut Vertrag keinen vergleichbaren Quellzeitstempel aus und eignet sich damit nicht als Beleg für eine zeitgleiche AC-Flussbilanz.

Die autorisierte Live-Probe am 10. November um 13:56:14 Uhr lieferte den Gerätezeitstempel 13:55 Uhr (Europe/Berlin). Am selben Gerät meldete Sungrow 683 W Hausverbrauch, 3 W Netzbezug, 0 W Einspeisung, 648 W Batterieladung, 0 W Entladung, 1.398 W DC-Gesamtleistung und 680 W Wechselrichter-Wirkleistung. Punkt 13141 lieferte den SOC-Rohwert 0,842 mit leerer Einheit. Die Wirkleistung plus Netzbezug ergibt in dieser Stichprobe den Hausverbrauch. Für SOC und PV bleibt der Portalabgleich vor einer Zuordnung erforderlich.

### DATA-01 und DATA-02 · Abgleich im angemeldeten iSolarCloud-Portal

Am 10. November 2026 um 14:14 Uhr (Europe/Berlin) lieferte die lesende Live-Probe den Gerätezeitstempel 14:10 Uhr. Die Geräteansicht in iSolarCloud zeigte dieselben 558 W Wirkleistung, 400 W Batterieladung, 0 W Entladung, 12 W Netzbezug und 0 W Einspeisung. Das Portal zeigte 85 % SOC zum API-Rohwert 0,852. Diese übereinstimmenden Leistungswerte stützen die Zuordnung zur selben Stichprobe. Die Geräteansicht zeigte keinen eigenen Messzeitstempel. Der Vergleich bestätigt die SOC-Skalierung als Anteil von 0 bis 1 für dieses Gerät. Die separate Anlagenübersicht zeigte abweichende Werte ohne sichtbaren Quellzeitstempel.

Die Portal-Gerätekurve für den 9. November zeigte um 11:00 Uhr 3,4 kW DC-Gesamtleistung, 0,6 kW Wirkleistung, 2,7 kW Batterieladung und 0 kW Entladung. Das stimmt bei Portalrundung mit 3.437, 640, 2.675 und 0 W der Gerätehistorie überein. Auch die Unterbrechung nach 16:15 Uhr war in der Gerätekurve sichtbar. Der Vergleich belegt das Viertelstundenraster, aber keine Mittelwertbildung. Der AC-PV-Messort ist weiter offen. Betreiberprofil und Historie wurden bei diesem Abgleich nicht geändert.

### DATA-01 und DATA-02 · Betreiberprofil und offene Quellen

Das bestehende private Betreiberprofil wurde um Live-Punkt `13141` ergänzt. `socPercent: false` übernimmt den bestätigten Anteil von 0 bis 1. Dieselbe Anlage wurde über das reguläre Setup erneut aktiviert. Der lokale Mapping-Endpunkt bestätigte die Zuordnung. Die historischen Messpunktregeln wurden dabei beibehalten. Die Betreiberdatei wird beim privaten Start eingebunden und bleibt außerhalb des Repositorys.

Der reguläre Abruf lieferte anschließend über `/api/v1/live/now` einen bestätigten SOC von 86,3 % für 14:25 Uhr (Europe/Berlin). Hausverbrauch, Netz und Batterie blieben bestätigt. Die Demo auf Port 8080 wurde per Compose neu gebaut und gestartet. Die private Instanz auf Port 8081 wurde per Release-Publish neu gebaut und mit derselben Datenbank und Konfiguration gestartet. Beide meldeten `ready`. ESLint, TypeScript, Vite, 25 Unit-Tests und 38 Integrations- und Vertragstests bestanden.

Die Gerätehistorienprobe von 11:00 bis 13:00 Uhr (Europe/Berlin) lieferte am 1., 4. und 6. November 2026 je 6, 0 und 9 Rohwerte pro Punkt. Dies gilt für Last, Bezug, Einspeisung, beide Batterierichtungen, SOC, DC-Gesamtleistung und Wechselrichter-Wirkleistung. Die Leistungspunkte wurden in Watt geliefert. Die SOC-Reihe hatte keine Einheit im Provider-Wörterbuch. Die leere Reihe am 4. November ist auf das geprüfte Fenster beschränkt. Alle drei Anfragen waren autorisiert. Ein Nachimport aus dieser Quelle bleibt offen, bis der Provider Momentanwert oder Intervallmittel und den Zeitbezug bestätigt.

Die Portal-Geräteliste enthält einen Hybridwechselrichter, die zugehörige Batterie und das Kommunikationsmodul. Ein separater PV-Wechselrichter oder PV-Produktionszähler ist dort nicht registriert. Die physische Installation ist damit nicht vollständig belegt. Der SH10RT kann PV- und Batterieleistung über denselben AC-Ausgang liefern und PV-Energie direkt in die Batterie leiten. Die Wechselrichter-Wirkleistung bleibt daher für die getrennte AC-PV-Bilanz ungeeignet. Für einen separaten Messpunkt braucht es den Anschlussplan oder eine Auskunft von Installateur oder Sungrow. [SH10RT-Handbuch, Abschnitte 3.2 und 6.1](https://info-support.sungrowpower.com/product-materials/31d744b7-742d-4707-bd1d-d6ed665c733c.pdf)

### DATA-01 und DATA-02 · Gerätezähler und getrennte Live-Leistungen

Die autorisierte Gerätehistorie des SH10RT-V112 lieferte am 9. November 2026 von 11:00 bis 13:00 Uhr (Europe/Berlin) neun Viertelstundenwerte pro Zähler. Die Gerätekurve im Portal zeigt im Tagesmodus DC-, Wirk- und Batterieleistung. Die API-Zählerstände ließen sich dort nicht direkt ablesen. Die Tageskontrolle vergleicht deshalb die Differenz der API-Stände von 00:00 bis 23:55 Uhr mit den separat bestätigten Anlagen-Tagesaggregaten.

| Punkt | Kanal | 11:00 → 13:00 Uhr (Wh) | Differenz bis 23:55 Uhr (kWh) | Tagesaggregat (kWh) |
| --- | --- | ---: | ---: | ---: |
| 13134 | PV-Ertrag | 36.615.500 → 36.620.900 | 13,6 | 13,6 |
| 13130 | Hausverbrauch | 23.897.700 → 23.900.600 | 16,1 | 16,1 |
| 13148 | Netzbezug | 7.990.300 → 7.990.400 | 3,8 | 3,8 |
| 13125 | Einspeisung | 19.920.100 → 19.920.200 | 0,1 | 0,1 |
| 13034 | Batterieladung | 7.901.100 → 7.904.200 | 8,1 | 8,1 |
| 13035 | Batterieentladung | 7.113.100 → 7.113.700 | 6,9 | 6,9 |

Die letzten fünf Minuten des Tages fehlen in der Grenzwertprobe. Die Übereinstimmung belegt keine exakten Mitternachtswerte. Am 1. November führte die API Zählerstände vor 11:45 Uhr ohne passende Leistungstelemetrie fort. Die Last und der Bezug sprangen anschließend in einem Viertelstundenabstand um 17 beziehungsweise 13,2 kWh. Die Anwendung verwendet deshalb ausschließlich plausible, direkt benachbarte Stände mit passenden Leistungswerten an beiden Grenzen. Rücksetzungen, Lücken und überhöhte Sprünge ergeben keinen Intervallwert. Differenzen über Lücken bleiben Summen und werden nicht verteilt.

Der gezielte Nachimport schrieb 5 Intervalle am 1. November (11:45–13:00 Uhr), 8 am 6. November (11:00–13:00 Uhr) und 8 am 9. November (11:00–13:00 Uhr). Die Fenster lieferten jeweils Werte für alle sechs Energiekanäle. Die Summe PV/Last beträgt 1,5/3,2 kWh, 6,8/4,0 kWh und 5,4/2,9 kWh in dieser Reihenfolge. Eine schreibgeschützte PostgreSQL-Prüfung bestätigte alle 21 Zeilen und weiterhin genau 13 fehlende Lastintervalle im Nachmittagsfenster des 9. November. Der 4. November wurde nicht nachgeladen. Historischer SOC ist weiterhin offen, da dieser Nachimport ausschließlich Energiedifferenzen aus Zählern speichert.

Am 4. November um 11:00–13:00 Uhr enthielt die autorisierte 15-Minuten-Abfrage neun identische Stände je Zähler und die 5-Minuten-Abfrage 25 identische Stände je Zähler. Beide lieferten keine Watt- oder SOC-Stichproben. Es gab keine belastbare Nullmessung und keine benachbarten Leistungswerte für eine Trapezschätzung. Die private Tagesansicht lieferte am 9. November um 11:00 Uhr die erwarteten Intervallmittel von 3,2 kW PV-Ertrag, 0,4 kW Last und 2,8 kW Batterieladung. Die Netz-Heatmap lieferte um 11 Uhr je 0,1 kWh Bezug und Einspeisung.

Der Live-Vergleich vom 10. November für den Gerätezeitstempel 14:10 Uhr (Abruf 14:14:27 Uhr) ist unten in Watt beziehungsweise SOC-Anteilen dokumentiert. Die Portal-Geräteansicht zeigte keinen eigenen Quellzeitstempel. Die Übereinstimmung mehrerer Kanäle stützt die Zuordnung zur gleichen Stichprobe.

| Kanal | Geräte-API | iSolarCloud-Geräteansicht |
| --- | --- | --- |
| PV-DC-Leistung, 13003 | 1.039 W | gerundet 1 kW |
| Wechselrichter-Wirkleistung, 13011 | 558 W | 558 W |
| Hausverbrauch, 13119 | 570 W | in der Geräteansicht nicht angezeigt |
| Netzbezug / Einspeisung, 13149 / 13121 | 12 / 0 W | 12 / 0 W |
| Batterieladung / -entladung, 13126 / 13150 | 400 / 0 W | 400 / 0 W |
| SOC, 13141 | 0,852 | gerundet 85 % |

Die separate Anlagenübersicht liefert eine gerundete PV-Anzeige ohne belegten separaten AC-Messort oder vergleichbaren Gerätezeitstempel. Der veröffentlichte Anlagen-Live-Vertrag weist ebenfalls keinen vergleichbaren Quellzeitstempel aus. Die Übersicht führt deshalb PV-DC-Leistung und Wechselrichter-Wirkleistung mit Quellzeit getrennt von der weiterhin offenen AC-PV-Bilanz. [Anlagen-Live-Vertrag](https://developer-api.isolarcloud.eu/#/document/api?id=14061&project_id=1)

ESLint, TypeScript, Vite, 25 Unit-Tests, 40 Integrations- und Vertragstests, 12 Linux-Playwright-Tests und PostgreSQL-Restore bestanden. Die Demo wurde über Compose auf Port 8080 und die private Instanz über den bestehenden Release-Startweg auf Port 8081 neu bereitgestellt. Beide meldeten `ready`.

Der Browser-Smoke-Test gegen die private Instanz zeigte 0,684 kW PV-DC-Leistung und 0,665 kW Wechselrichter-Wirkleistung mit der gemeinsamen Quellzeit 14:45 Uhr (Europe/Berlin, Abruf 14:50:42 Uhr). Der AC-PV-Kanal blieb ohne Zuordnung. Die AC-Flussbilanz blieb unbestätigt.

### DATA-01 und DATA-02 · Erweiterter Nachimport und Anzeige

Der gezielte Gerätezähler-Nachimport für den 9. November 2026 prüfte den ganzen lokalen Tag in Fenstern von höchstens drei Stunden. Die letzte Abfrage nutzte die neu erlaubte Grenze um 00:00 Uhr des Folgetags. 75 weitere Viertelstunden wurden geschrieben. Die private Übersicht bestätigte danach für PV, Last, Bezug, Einspeisung, Ladung und Entladung jeweils 83 von 96 Viertelstunden, also 86,5 % gemeinsame Abdeckung für PV und Last. Tageswerte blieben bei 13,6 kWh PV, 16,1 kWh Last, 3,8 kWh Bezug, 0,1 kWh Einspeisung, 8,1 kWh Ladung und 6,9 kWh Entladung. Die 13 fehlenden Intervalle und historischer SOC bleiben offen.

Unter **Einstellungen → Diagnose und Messpunkte → Zählerdaten nachladen** bietet ein bestätigtes Betreiberprofil eine Vorschau und eine separate Übernahme. Zählerdifferenzen fließen bei belegten benachbarten Grenzen ins Intervall. Der Nachimport kann eine Differenz über mehrere fehlende Intervalle nicht zeitlich auflösen. Eine separate Summenanzeige für solche Lücken steht noch aus. Der normale Anlagenimport und der gezielte Gerätezähler-Nachimport sind getrennte Abläufe.

Die Übersicht hebt fehlende Zeitreihen hervor und zeigt beide Abdeckungszahlen mit ihrer Bedeutung unter **Hinweise zur Datengrundlage**. „Jetzt“ verwendet den vorhandenen PV-DC-Wert direkt als PV-Kachel, wenn kein AC-PV-Punkt zugeordnet ist. Wechselrichter-Wirkleistung bleibt separat. Die private API bestätigte für die Stichprobe vom 10. November um 15:30 Uhr (Europe/Berlin) 345 W DC und 629 W Wechselrichter-Wirkleistung.

ESLint, TypeScript/Vite, der Release-Publish, 25 Unit-Tests, 40 Integrations- und Vertragstests und acht gezielte Playwright-Tests bestanden. Playwright prüfte Warnhinweis, Abdeckungsdetails, Live-Kachel und getrennte Vorschau und Übernahme mit Testantworten. Die Demo auf Port 8080 wurde per Compose neu gebaut und gestartet. Die private Instanz auf Port 8081 wurde aus demselben Quellstand mit der bestehenden Datenbank neu bereitgestellt. Beide meldeten Readiness. Der private Import und die anschließende Prüfung verwendeten reale Sungrow-Daten.

### DATA-03 · Lesende Messpunktdiagnose

Die private Oberfläche bietet Live-Kandidaten und die dokumentierte Geräte-Minutenhistorie unter **Einstellungen → Diagnose und Messpunkte → Gerätemesspunkte lesend prüfen**. Das Backend prüft die Zugehörigkeit des Geräts zur aktiven autorisierten Anlage und gibt die Punktliste vor. Rohwert, Vorzeichen, Einheit, Quellzeit, Abrufzeit und Anzahl gültiger Werte werden pro Punkt getrennt ausgewiesen. Punktbezogene Daten ohne Providerbegründung bleiben als ungeklärt gekennzeichnet. Die historische Anlagenprobe erlaubt ein gezieltes Zeitfenster und zeigt Minutenmesswerte und Tagesenergien getrennt.

Die Proben teilen sich Aufrufabstand und Provider-Wartezeiten mit dem regulären Adapter. Die Live-Probe verbraucht höchstens einen regulären Cloud-Abrufplatz. Ein dadurch fälliger regulärer Abruf wird verschoben, ohne Messungen oder Abrufstatus im Live-Cache zu überschreiben. Bei der privaten Erstprobe meldete die Anwendung im laufenden Fünf-Minuten-Fenster die verbleibenden 221 Sekunden. Die anschließende autorisierte Probe lieferte nach Ablauf der Wartezeit Werte vom echten Gerät.

ESLint, TypeScript, Vite, 25 Unit-Tests, 38 Integrations- und Vertragstests, zwölf Linux-Playwright-Tests und ein PostgreSQL-Restore bestanden. Die Verträge prüfen Null, fehlenden Punkt, unbekannte Einheit, fehlende Freigabe, die gemeinsame Live-Abrufgrenze und die EU-Gerätehistorie. Der Browser prüft die Diagnose mit simulierten Antworten. Demo auf Port 8080 und private Instanz auf Port 8081 wurden neu gebaut und über die bestehenden Startwege bereitgestellt. Beide meldeten `ready`. Die Gerätehistorie wurde zusätzlich mit dem echten SH10RT-V112 geprüft.

### UX-06 und UX-07 · Übersicht und Energiebilanz

„Jetzt“ steht vor dem Zeitfilter und bleibt auch bei historischen Tagen aktuell. „Heute“ lädt den Verlauf erst beim Öffnen. Die Kennzahlen haben tastatur- und touchbedienbare Info-Symbole. Die Energiebilanz zeigt den gewählten Zeitraum, Tagesabdeckung, Ende des jüngsten Minutenintervalls und bilanzierbare Stunden separat. Sie benennt unbestätigte Energiepfade, fehlende Messwerte und unplausible Bilanzen. Vollständig fehlende Netz- und SOC-Reihen ergeben einen Leerzustand. Nullwerte bleiben darstellbar.

Die Browser-Fixture mit 96 Lastintervallen zeigt 100 % Tagesabdeckung, 0 von 24 bilanzierbaren Stunden und keine Netz-/SOC-Grafik. Der Test öffnet die Herkunftsangabe der Quote per Tastatur. Die Layouttests prüfen Desktop, 375 Pixel und 125 % Schriftgröße.

### LIVE-01 · Sichtbarkeit und Abrufzeiten

Eine schreibgeschützte Prüfung der privaten Datenbank bestätigte am 10. November 2026 60 erfolgreiche Live-Abrufe bis 18:43 Uhr und gespeicherte Stichproben bis 18:40 Uhr. Gleichzeitig lagen für den laufenden Tag keine historischen Intervalle vor. Die reguläre Tagesübersicht liest diese Live-Stichproben derzeit nicht. Der Befund und die geplante Abnahme stehen unter [LIVE-02](today-acceptance.md#laufender-tag).

Der Browser-Test übernimmt neue Cache-Daten nach 30 Sekunden bei sichtbarem Tab, meldet einen ausgeblendeten Tab und pausiert dessen Refresh. Ein Backend-Test prüft das Auslaufen eines fehlenden Sichtbarkeitssignals. Die Diagnose zeigt Abrufmodus, Quellmessung, letzten Abrufversuch und frühesten nächsten Versuch getrennt. Wiederholte Quellzeitstempel und Abrufwartezeit sind sichtbar.

Die private Instanz meldete im Hintergrund 600 Sekunden als frühesten nächsten Cloud-Abruf. Der gespeicherte Verlauf vom 10. November zeigt für den beobachteten Sprung zwei erfolgreiche Abrufe um 09:20:49 und 09:25:54 Uhr in Europe/Berlin. Die zugehörigen Quellmessungen stammen von 09:15 und 09:25 Uhr. Beide Abrufe hatten keine Wiederholungswartezeit. Der Zehn-Minuten-Sprung lag im Quellzeitstempel. Zwischen den Abrufen lagen rund fünf Minuten.

### LIVE-02 und UX-08 · Laufender Tagesverlauf und Leistungseinheiten

Die normale Ansicht des laufenden Tags zeigt gespeicherte Live-Stichproben ohne Handimport. Sie verwendet denselben Endpunkt und dieselbe Diagrammkomponente wie der aufklappbare Bereich „Heute“. Die Kanäle zeigen Solarleistung (DC), Hausverbrauch, Netz, Batterie und Ladezustand. Wechselrichter-Wirkleistung erscheint separat. Symbole und Legende unterscheiden Stichproben und historische Intervalle. Quellen, Messzeiten und erkannte Live-Lücken lassen sich aufklappen. Vorläufige Energiesummen stammen aus belegten historischen Intervallen. Der aktuelle Tag ohne solche Intervalle zeigt einen Hinweis.

„Jetzt“ zeigt die Kachel „Solarleistung“. Für das bestätigte DC-Profil sind Messort und fehlender separater AC-PV-Fluss im Info-Symbol erläutert. Die Kachel zeigt die Quellzeit. Leistungswerte, Achsen, Tooltips und Tabellen verwenden unter 10.000 W Watt und ab 10.000 W Kilowatt. Bei gerichteten Werten entscheidet der Betrag. Ladezustand bleibt in Prozent und Energie in kWh. Eingabefelder behalten ihre fest bezeichneten Einheiten.

ESLint, TypeScript, Vite, 25 Unit-Tests und 40 Integrations- und Vertragstests bestanden. Die Demo auf Port 8080 und die private Instanz auf Port 8081 wurden über ihre bestehenden Startwege neu gebaut und gestartet. Beide meldeten `ready`. Der private Browser-Smoke-Test bestätigte den automatisch sichtbaren Tagesverlauf, gespeicherte DC-Stichproben und den Quellhinweis. Die Abnahme mit zwei neuen Hintergrundzyklen bei geschlossenem Browser und der automatische Nachimport nach Tageswechsel stehen aus.

Der erste Browserlauf scheiterte am alten Ort der Minutenabdeckung. Der Test öffnet jetzt „Hinweise zur Datengrundlage“. Danach bestanden 13 Linux-Playwright-Tests einschließlich Demo-Kalender, Simulation, Export und neuer Tagesansicht sowie der PostgreSQL-Restore. Auf dem finalen Build bestanden der private Smoke-Test sowie die gezielten Browserprüfungen der Tagesansicht und Live-Aktualisierung erneut. Die Live-Fixture prüft 360 W, 9.999 W, 10 kW, Einspeiserichtung, 0 % SOC und den per Tastatur bedienbaren Quellhinweis.

### DATA-01 und LIVE-02 · Automatischer Zählerimport und Hintergrundabnahme

Die Migration `AutomaticDeviceCounters` ergänzt einen persistenten Geräte-Cursor im bestehenden historischen Hintergrunddienst. Bestätigte Profile starten bei den letzten 30 abgeschlossenen lokalen Kalendertagen und prüfen danach neu abgeschlossene Tage. Ein Gerätefenster umfasst höchstens drei Stunden. Der Dienst wartet mindestens eine Minute zwischen Zählerfenstern und nutzt den gemeinsamen Provider-Abrufabstand. Die private Instanz startete am 10. November die Rückschau ab dem 11. Oktober. Der Cursor erreichte 06:00 Uhr und enthielt für jeden der sechs Wh-Zähler 25 Quellwerte. Das belegt historische Abrufe für diesen Tag, keine garantierte Rückschau oder vollständige Tagesabdeckung.

Zählerdifferenzen über genau einen Rasterabstand werden als Intervalle gespeichert. Belastbare Summen über mehrere fehlende Intervalle erhalten einen eigenen Zeitraum und bleiben getrennt von Viertelstundenprofil, Tagesaggregaten und deren Summen. Die Kontrolle gegen Sungrow-Tagesaggregate erfolgt je Kanal. Rücksetzungen, unplausible Sprünge und fehlende Grenzen bleiben offen. Der bestätigte Geräte-SOC-Punkt 13141 wird historisch als Anteil mit Gerätezeit gelesen, sofern die Antwort die bekannte einheitenlose Skala liefert. Historischer SOC erscheint im Tagesdetail als einzelne Stichproben.

Der PostgreSQL-Integrationstest prüft den 30-Tage-Start, die persistente Wiederaufnahme mit neuem Kontext, das Ausbleiben doppelter Intervalle, eine getrennte Lückensumme und den nächsten abgeschlossenen Tag mit simulierter Uhr. Der echte Tageswechsel ist noch nicht beobachtet. Am jüngsten abgeschlossenen Tag bleibt die letzte Grenze offen, wenn der aktuelle Kalendertag für die Historienroute noch gesperrt ist.

Nach dem Schließen des privaten Prüf-Browsers wurden am 10. November um 19:22:25, 19:32:30 und 19:42:35 Uhr (Europe/Berlin) neue Quellmessungen für 19:20, 19:30 und 19:40 Uhr bestätigt. Beim Wiederöffnen zeigte der private Browser die gespeicherten Tageswerte und DC-Stichproben. Demo und private Instanz wurden mit der Migration neu gebaut und meldeten `ready`. ESLint, TypeScript, Vite, 25 Unit-Tests und 41 Integrations- und Vertragstests bestanden.

13 Linux-Playwright-Tests und der PostgreSQL-Restore bestanden. Die Browser-Fixture prüfte die Darstellung einer getrennten Zähler-Lückensumme und historischer SOC-Stichproben neben der weiterhin unvollständigen Intervallansicht.

Das tatsächlich gebuchte Sungrow-Kontingent ist aus den vorliegenden Antworten und dem Anlagenportal nicht ablesbar. Für A25 bleibt ein Nachweis aus der Entwickler-App erforderlich. Die AC-PV-Bilanz und die private Simulation bleiben wie vereinbart zurückgestellt. [Sungrow Developer Portal](https://developer-api.isolarcloud.com/)

### DATA-01, LIVE-02, UX-04, UX-05 und POT-01 · Befunde vom 11. November

Die private Datenbank enthält um 19:55 Uhr am 10. November 2.264 W Batterieentladung, 1.629 W Hausverbrauch und 635 W Einspeisung. Um 20:00 Uhr waren es 2.600 W, 1.961 W und 639 W. PV-DC war jeweils 0 W. Die Wechselrichter-Wirkleistung entsprach der Batterieentladung. Um 19:45 und 20:05 Uhr lag die Batterieentladung mit 572 und 806 W knapp unter dem Verbrauch von 580 und 809 W. Der Netzbezug deckte die Differenzen. Die „Jetzt“-Ansicht nennt bei gleichzeitiger Messung die Einspeisung neben der höheren Entladung. Sie setzt dafür keinen bestätigten AC-PV-Pfad voraus.

Am 10. November konnten Live-Stichproben erscheinen, obwohl der Historienabruf für den laufenden Tag noch keine Tagesaggregate liefert. Bei der Nachprüfung am 11. November lagen für den 10. November 30 Lastintervalle und weiterhin keine Tagesaggregate vor. Für die private Anlage sind außerdem keine Tarife gespeichert. Die Oberfläche benennt die für Energie, Quoten und Kosten fehlenden Daten. „Heutiges Datum öffnen“ wählt die Anlagenzeitzone. Ein Einzeltag erscheint einmal mit Jahr. Der ursprünglich angezeigte Oktoberzeitraum ist ohne die damalige URL oder einen Screenshot nicht reproduzierbar. Die Live-Kurven verbinden benachbarte Messungen und unterbrechen die Linie bei erkannten Lücken oder mehr als 15 Minuten Abstand.

Für KW 45 sind sieben Sungrow-Tagesaggregate mit zusammen 78,2 kWh PV gespeichert. Vor den gezielten Gegenproben enthielten 525 Intervalle Last. Gemeinsame PV- und Lastwerte lagen nur in acht Viertelstunden vom 6. November (11:00–13:00 Uhr, 6,8 kWh PV) vor. Der Datenstatus bezeichnet die Lücke jetzt als fehlende gemeinsame PV- und Verbrauchsreihe und zeigt Kanalzahlen. Das Mehrtagesdiagramm verwendet standardmäßig die Sungrow-Tageswerte und bietet die Intervallsummen als wählbare Quelle an. Die Energieflussanalyse benötigt weiterhin bestätigte AC-Pfade und gleichzeitig gemessene Werte. Tagesaggregate reichen dafür fachlich nicht aus.

Der manuelle Auftrag vom 10. November speicherte den Zeitraum 2. November 00:00 bis 8. November 00:00 Uhr mit exklusivem Ende. Er endete erfolgreich mit 53 Fenstern, acht leeren Fenstern und ohne geänderte Datensätze. Der reguläre Anlagenimport las im aktiven Profil Minutenwerte für Hausverbrauch. Die Oberfläche nimmt jetzt einen inklusiven letzten Importtag entgegen. Gerätezähler ergänzt die historische Abstimmung. Bei der Prüfung lief ihre einmalige Rückschau am 20. Oktober. Autorisierte Geräteproben für 2. und 8. November 11:00–13:00 Uhr lieferten neun Zählerstände pro Kanal sowie neun beziehungsweise sieben bestätigende Leistungspunkte. Damit ist in diesen Stichproben eine zusätzliche Quelle belegt. Am 9. November sind 83 gemeinsame Viertelstunden und sechs Tagesaggregate gespeichert. Die verbleibenden Lücken sind in der Anzeige weiterhin sichtbar.

Der gezielte Gerätezähler-Nachimport übernahm in diesen Fenstern acht Intervalle am 2. November und fünf am 8. November. Beide Fenster bestanden die Kontrolle gegen die Tagesaggregate. Die Intervallsumme der KW 45 stieg dadurch um 4,0 kWh PV auf 10,8 kWh. Die Quelle lässt sich in den Einstellungen unter „Diagnose und Messpunkte“ mit der Gerätehistorie für ein konkretes Fenster erneut prüfen.

**Abgleich mit der Spezifikation.** Speichererweiterung, Kapazitäts-Nutzen-Kurve, Verbrauchsverschiebung, Zusatzbedarf mit Ladefenster, beide Deckungsmodi, Energieherkunft, Kostenvergleich, kombinierte Szenarien und optionale Amortisation sind vorhanden. Die Demo prüft die kombinierte Simulation und Exporte. Die private Speichersimulation wartet auf bestätigte AC-Pfade und vollständige Modellparameter. Speicherereignisse benötigen je nach Typ Netzleistung, SOC und bestätigte Speichergrenzen. Die geräteunabhängige Überschussanalyse hatte Gesamtsumme und Aufnahmegrenze. Sie zeigt jetzt zusätzlich die Verteilung nach Tag, lokaler Stunde, Monat und Jahreszeit. Der Monatsvergleich weist die im Ladefenster theoretisch nutzbare Einspeisung und die erfassten Stunden aus. Die Heatmap erklärt Einheit, Achsen, Farben und den Unterschied zwischen gemessener Null und fehlendem Intervall. [Ursprüngliche Anforderungen](../SolarInsight_Spezifikation.md)

ESLint, TypeScript, Vite, 25 Unit-Tests, 41 Integrations- und Vertragstests und 13 Linux-Playwright-Tests bestanden. Der erste Playwright-Lauf griff durch die zusätzliche Monatstabelle auf die falsche Tabelle zu. Nach Eingrenzung auf den Abschnitt mit Tagesabweichungen bestand der vollständige Lauf. Der PostgreSQL-Backup-Restore bestand mit einer Testanlage und einem Tarif. Demo und private Instanz wurden über die bestehenden Startwege neu gebaut und gestartet. Beide meldeten `ready`. Im Browser der privaten Instanz wurden der 10. November als Einzeldatum, der Tagesverlauf mit realen Stichproben, die KW 45 mit sieben Tageswerten und 78,2 kWh PV, Quellenwahl, Heatmap und Potenzialansicht geprüft.

### DATA-04 · Lokale Sungrow-CSV-Exporte

Am 13. November 2026 wurden acht CSV-Dateien lokal untersucht. Sie enthalten drei Tagesberichte mit 30-Minuten-Zählerständen, zwei Monatsberichte und zwei Jahresberichte im 15-Minuten-Raster sowie einen Gesamtbericht mit Jahreswerten 2023 bis 2026. Anlagenname und Zeitzone wurden mit der aktiven privaten Anlage abgeglichen. Die überlappenden Gesamtzähler der Monats- und Jahresberichte stimmten an identischen Zeitstempeln überein.

Für die Vorschau wurden aus Gesamtzählerdifferenzen zwischen zwei lokalen Mitternachten die vier vorhandenen Tageskanäle PV-Ertrag, Last, Bezug und Einspeisung berechnet. Tage ohne ausreichende Zeitstempel sowie Kanäle mit unplausibler Schlussdifferenz oder mehr als 0,2 kWh Abweichung zur im Monatsbericht vorhandenen Tagessumme um 23:45 Uhr wurden ausgeschlossen. Der PostgreSQL-Abgleich in einer temporären Tabelle ergab 2.689 Kandidaten. 394 hatten bereits einen API-Tageswert. 385 davon lagen maximal 0,1 kWh auseinander. Neun hatten größere Abweichungen bis 7,6 kWh. 2.295 fehlende Tageswerte blieben übrig, davon 1.448 im Jahr 2024 und 847 von Januar bis Juli 2026. Im Halbstundenbericht vom 9. November stehen um 23:30 Uhr 15,8 kWh Last und in der API für den ganzen Tag 16,1 kWh. Die Vorschau endete mit Rollback. Die automatische Freigabeprüfung hat den anschließenden Schreibaufruf abgelehnt. Es wurde kein CSV-Wert dauerhaft gespeichert. Anwendungscode und lokale Bereitstellung waren nicht betroffen.

Die Exportzähler bieten zusätzlich Viertelstundenprofile für Zeiträume ohne API-Intervalle. Vor einer Nutzung sind die Zeitgrenzen, gerundeten Differenzen, fehlende oder mehrdeutige Zeitstempel bei der Zeitumstellung und rückläufige Zähler zu behandeln. Der Gesamtbericht enthält Jahreswerte einschließlich Batterieladung und -entladung und eignet sich zum Jahresvergleich und zur Gegenprüfung. Die drei Tagesberichte zeigen Batterieladung und -entladung im Halbstundenraster. Die Anlagenberichte enthalten außerdem installierte Leistung (12 kWp), äquivalente Volllaststunden und kumulierte Gesamtzähler. Kein Export enthält SOC oder einen separat belegten AC-PV-Messpunkt. Der Jahreswert 2026 ist ein Zwischenstand.

Nach der ausdrücklichen Freigabe wurde die private Datenbank mit `pg_dump` gesichert und das Archivverzeichnis mit `pg_restore -l` geprüft. Der transaktionale Import fügte 2.295 fehlende Tageswerte für 574 Kalendertage ein. PostgreSQL übersprang bestehende Werte über den eindeutigen Schlüssel. Die zweite Ausführung fügte null Werte ein. Die Datenbankprüfung bestätigte die Quellenrevision `sungrow-portal-csv-day-v1` für ausschließlich PV-Ertrag, Hausverbrauch, Netzbezug und Einspeisung. Die vorhandenen 104 API-Tageswerte je Kanal blieben erhalten. Ein angemeldeter Chrome-Test gegen die private Instanz auf Port 8081 prüfte Readiness, die Jahressummen und Monatsbalken 2024 einschließlich 9.191,5 kWh erfasster PV-Erzeugung, fehlende Batteriewerte für 2024 sowie den unveränderten API-Lastwert von 16,1 kWh am 9. November. Der erste Browserstart scheiterte am fehlenden gebündelten Chromium. Der Test bestand mit installiertem Chrome. Der Import änderte ausschließlich Daten der privaten Instanz. Es gab keinen Anwendungsbuild.

Für künftige Tageskennzahlen genügt die API, solange sie diese liefert. Der Hintergrundabgleich berücksichtigt die vorhandenen Tageswerte. Fehlende Batteriekanäle und Intervallwerte können weiterhin API-Abrufe erfordern. Viertelstundenexporte lohnen sich vor allem als gezielte Ergänzung bei belegten historischen Lücken für Heatmaps und Ladefenster. Ein monatlicher Ablauf hätte einen Mehrwert, falls diese Lücken regelmäßig auftreten und eine zeitlich aufgelöste Analyse benötigt wird. Für historische Jahresvergleiche genügt ein einmaliger Gesamtbericht, bei Bedarf gefolgt von einem Export nach Jahresabschluss. Installierte Leistung und daraus abgeleitete Volllaststunden begründen keinen monatlichen Import.

### DATA-04 · Abdeckung nach den Jahresexporten 2023 und 2025

Die zwei neu hinzugefügten Dateien sind die Anlagen-Jahresberichte 2023 und 2025. Der erste gültige Zählerstand ist am 17. Mai 2023 um 15:00 Uhr in Europe/Berlin erfasst. Das belegt den Beginn der verfügbaren Exportdaten. Ein genaues Installationsdatum ist aus den CSV-Dateien nicht nachweisbar. Nach erneuter lokaler PostgreSQL-Sicherung und Prüfung des Sicherungsverzeichnisses ergänzte dieselbe Importregel 2.270 Tageswerte an 570 Tagen, davon 900 für 2023, 4 für den 31. Dezember 2024 und 1.366 für 2025. Der Jahreswechsel 2024/25 liefert den zuvor fehlenden Schlussstand für den 31. Dezember. Die Wiederholung fügte null Zeilen ein. Die Quellenrevision und vorhandene Daten blieben erhalten. Chrome prüfte die private Readiness, Jahreswerte und Monatsbalken für 2023 bis 2025 sowie den unveränderten API-Referenzwert vom 9. November 2026. Kein Anwendungscode wurde geändert.

Die Prüfung umfasst 1.276 lokale Kalendertage vom 17. Mai 2023 bis zum 12. November 2026 einschließlich. An 1.242 Tagen sind alle vier Tageskanäle vorhanden. 28 Tage haben keinen der vier Werte und sechs sind teilweise belegt. Der laufende 13. November wurde für den Tagesabschluss ausgenommen.

| Jahr | Keine der vier Tageskennzahlen | Fehlende Kanäle an teilweise belegten Tagen |
| --- | --- | --- |
| 2023 | 17.05. (Quellbeginn erst 15:00), 14.11. | 19.05. und 09.07. (PV, Last, Einspeisung), 29.07. (Last), 13.08. (PV) |
| 2024 | 26.–28.08. | — |
| 2025 | 24.–26.04., 06.–08.05., 18.–19.05., 10.–14.07., 24.–26.08., 16.–17.09., 21.–23.09., 07.–08.10. | 11.10. (Last und Netzbezug) |
| 2026 | — | 29.07. (Last) |

Für diese Tage sind gezielte Tages- oder Monatsexporte mit vollständigen Tageswerten sinnvoll, sofern das Portal sie liefert. Der 17. Mai 2023 braucht für eine vollständige Tageskennzahl einen belegten Tageswert zur Inbetriebnahme. Ein erneuter Import derselben Dateien schließt die offenen Lücken nicht. Batterieladung und -entladung als Tageswerte fehlen vom 17. Mai 2023 bis 31. Juli 2026. Dafür ist eine Quelle mit abgeschlossenen Batterietageswerten nötig. Die Anlagen-Jahresberichte enthalten diese Kanäle nicht. Der Gesamtbericht enthält sie als Jahressummen.

Viertelstundenwerte fehlen in der Datenbank vollständig vom 17. Mai 2023 bis 31. Juli 2026. Vom 1. August bis 10. Oktober 2026 sowie am 11. November gibt es Intervalle, jedoch kein gemeinsames Intervall mit PV, Last, Bezug und Einspeisung. Alle vier Kanäle sind für jede Viertelstunde nur am 11.–13., 27. und 29. Oktober sowie am 6. November vorhanden. Teilprofile sind am 14.–26., 28. und 30. Oktober bis 5. November sowie am 7.–10. und 12. November vorhanden. Für einen gesonderten Viertelstundenimport liegen bereits Jahresdateien für 2023 bis 2026 vor. Ein neuer Export lohnt sich dort, wo auch diese Quelldateien eine Lücke haben und das Portal vollständigere Daten liefern kann. Dieser Durchgang speicherte Tageswerte für die vier vereinbarten Kanäle.

### KPI-01, FLOW-01, BAT-01, UX-09, UX-10, POT-02 und DATA-05 · Übernahme und Auswertungen

Am 13. November 2026 wurde das Projekt von Codex übernommen. Die nicht committete DATA-04-Dokumentation ist im ersten Commit auf dem Branch `fix/analytics-handover` gesichert. Die privaten Portal-Exporte in `data/` stehen jetzt in `.gitignore`.

**Ursachen.** Die Ursachen wurden mit lesenden Abfragen auf der privaten Datenbank geprüft.
- Quoten: Das Frontend zeigte Autarkie und Eigenverbrauch nur bei 100 % Tagesabdeckung des gesamten Zeitraums. Laufende Zeiträume und Quartale mit einem fehlenden Lasttag (etwa der 29.07.2026 in Q3) blieben deshalb leer. Q2 war vollständig und wurde angezeigt. Das Backend lieferte die Quoten zusätzlich nur bei `ConfirmedAcPaths = true`, für die private Anlage ist der Wert `false`.
- Energieflüsse: `EnergyInsights.Allocate` gab ohne bestätigte AC-Pfade immer `null` zurück und rechnete nur mit Intervallen. Die Gerätezählerintervalle (11.10. bis 12.11.2026) und alle Sungrow-Tageswerte mit Batteriekanälen schließen die Bilanz jedoch exakt (Residuum 0,00 kWh an jedem Tag).
- Ladezustand: Die 2.776 historischen SOC-Stichproben liegen in `LiveMeasurements` und wurden nur im Tagesdetail als Punktwolke geladen. In `Readings` gibt es keinen SOC. Monats- und Speicheransicht nahmen den SOC zudem aus den Tageswerten, die keinen enthalten.
- Ladegrenzen und Speicherereignisse: Es ist keine Speicherkonfiguration hinterlegt. Ohne Grenzen war nichts auswertbar.
- „Seit Datenbeginn“: Die feste Grenze von 732 Tagen war kleiner als der Datenbestand von 1.276 Tagen.

**Änderungen.**
- `Analysis.Summarize` berechnet die Quoten über gepaarte Werte ohne Pfadbedingung und gibt die gepaarte Abdeckung aus. `DataView.CreateReport` bezieht die Abdeckung auf die verstrichene Zeit.
- `EnergyBalance` (neu) wählt je Tag vollständige Intervalle oder Tageswerte. So gibt es keine Doppelzählung. Ohne Batteriewerte bleibt ein nicht trennbarer Anteil ausgewiesen. `Allocate` trennt Netzladung und Batterie-Einspeisung als Mindestwerte, statt das Intervall zu verwerfen.
- `Queries.WithSoc` hängt SOC-Stichproben an die Intervalle (Wert am Intervallende). `BatteryEstimation` schätzt Ladegrenzen (1.–99. Perzentil) und Kapazität (Kleinste-Quadrate aus Netto-Batterieenergie und SOC-Änderung), wenn keine Konfiguration existiert. `DataView.Daily()` ergänzt fehlende Tageswerte aus vollständigen Intervalltagen.
- Neu sind `StoragePotential` (Tagesbilanzmodell mit Übertrag, obere Abschätzung), `Observations` (Beobachtungen aus Tageswerten) und die Endpunkte `/storage-potential` und `/data-quality`. Die Zeitraumgrenze liegt jetzt bei 7.320 Tagen.
- Frontend: Die Jahresauswahl hat keinen Stichtag mehr. Neu sind `PeriodCharts` (gestapelte Bilanz mit Einspeisung unter null, Linien, Quoten, Speicher mit SOC-Achse, Tages-, Wochen- und Monatsauflösung, Monatsvergleich je Jahr, Jahreszeiten), Kreisdiagramme aus der Tagesbilanz mit Grundlage und Herkunftsnäherung, die neue Seite „Was bringt ein größerer Speicher?“ und die Datenbestandsübersicht im Datenstatus. Die Intervallsimulation ist als aufklappbarer Bereich erhalten.
- Bewusste Abweichung: Ein Energiefluss ohne bestätigte AC-Pfade wird nicht mehr unterdrückt, sondern als „aus der gemessenen Bilanz berechnet“ gekennzeichnet. Die PV-Basis ist der Sungrow-PV-Ertrag. Die Intervallsimulation verlangt weiterhin bestätigte Pfade.

**Prüfungen.**
- .NET-Build Release ohne Warnungen. 35 Unit-Tests bestanden, davon 10 neu für Quoten, Tagesbilanz, Netzladung, Speicherpotenzial, Speicherschätzung und Beobachtungen. 41 Integrations- und Vertragstests mit frischer Test-PostgreSQL (Port 55432) bestanden.
- TypeScript, ESLint und Vite-Build ohne Fehler. Vite meldet weiterhin den Größenhinweis für den ECharts-Chunk.
- Playwright gegen die Demo auf Port 8080: 12 bestanden, 1 übersprungen (A22 braucht einen separaten privaten Testserver). Drei Tests wurden an geänderte Texte angepasst: Diagrammname, KPI-Erklärung und die aufklappbare Intervallsimulation. Ein Zwischenlauf direkt nach einem abgebrochenen Lauf hatte drei Fehlschläge (Zeitüberschreitungen). Der anschließende vollständige Lauf war grün.
- Demo-API (synthetische Daten): Jahr, Quartal und „Seit Datenbeginn“ (730 Tage, 1,7 s) liefern Kreisdiagramme, Quoten und SOC. Die geschätzte Kapazität von 9,8 kWh liegt nahe den konfigurierten 9,6 kWh. Im Browser geprüft: Jahresauswahl ohne Stichtag, gestapelte Bilanz, Speicheransicht mit SOC-Achse, Kreisdiagramme und Potenzialseite.
- Private Instanz (Port 8081) mit echten Daten geprüft, nach Anmeldung durch den Betreiber (Browser und API, angemeldete Sitzung):
  - November 2026: Autarkie 50,3 %, Eigenverbrauch 99 %, jeweils Basis 93,8 %. Die Kreisdiagramme stammen aus einem Intervalltag, 11 Sungrow-Tagen und dem laufenden Tag. Ladegrenzen, Netzbezugszeiten, nächtlicher Bezug und Beobachtungen sind gefüllt.
  - Q3 2026: Autarkie 85,9 % (Basis 98,9 %). Ab August ist der Speicheranteil getrennt, davor als nicht trennbar ausgewiesen.
  - Q4 2026: 58,6 % (Basis 98,2 %).
  - Jahr 2025: 65,1 % (Basis 93,4 %). Mangels Batteriewerten ist das ganze Jahr nicht trennbar ausgewiesen.
  - „Seit Datenbeginn“: 66,2 %, Antwortzeit 130 ms.
  - Tagesansicht 10.11.2026: SOC an 96 von 96 Intervallen.
  - Geschätzte Speicherkapazität 8,3–8,6 kWh, Ladegrenzen 0 % bzw. 86–100 %.
  - Der niedrige mittlere SOC im November (13,6 %) entspricht den gespeicherten Gerätewerten: An vielen Tagen stand der Speicher bei 0 %, und auch die Momentanwerte vom 11. und 12.11. zeigen 0–5 %.
  - Speicherpotenzial über 1.242 Tage: +5 kWh ersetzen rund 1.279 kWh Netzbezug (etwa 79 € pro Jahr bei Standardpreisen). Der vorhandene Speicher hob die Autarkie an 104 Tagen von etwa 39 % auf 72 %.
  - `/data-quality` antwortet.
  - Danach korrigiert: Singular „1 Tag“ in den Grundlagentexten der Kreisdiagramme.

**Bereitstellung.** Demo (Port 8080) per Docker Compose neu gebaut. Docker Desktop war gestoppt und wurde dafür gestartet. Die private Instanz (Port 8081) läuft im neuen Release-Verzeichnis `app-claude-analytics`. `start.ps1` zeigt darauf, die vorherige Fassung liegt als `start.ps1.before-claude-analytics` daneben. Das portable PostgreSQL der privaten Instanz war gestoppt. Es wurde über den bestehenden `pg_ctl`-Weg gestartet und führte eine automatische Wiederherstellung nach unsauberem Herunterfahren durch. Beide Instanzen melden `ready` auf `/health/ready`. Datenbanken, Konfiguration und Zugangsdaten blieben unverändert, auf die private Datenbank wurde nicht geschrieben.

### UX-11, COST-01, BAT-02 und DATA-07 · Oberfläche, Kosten und Speicherzustand

Am 15. November 2026 umgesetzt, in der Reihenfolge Oberfläche, Kosten, Speicherzustand.

**Oberfläche (UX-11).**
- Farbwerte als Variablen mit hellem und dunklem Schema. Die Kopfzeile hat einen Umschalter System, Dunkel, Hell; die Wahl liegt nur im Browser. Diagramme nutzen im dunklen Schema das dunkle ECharts-Thema.
- Kennzahl-Kacheln (`Tile`) mit Symbol, Farbe je Größe, Veränderung zum Vorjahr oder zur Vorperiode und Erklärung auf Abruf. Der Vergleich ist voreingestellt auf das Vorjahr. Er wird ausgeblendet, wenn einer der Zeiträume noch läuft oder unvollständig ist, und bei „Seit Datenbeginn“.
- Der gelbe Hinweis „Zeitverlauf unvollständig“ und der doppelte Block „Sungrow-Tagesaggregate“ stehen jetzt unter „Hinweise zur Datengrundlage“.
- Die Zeitraumleiste bleibt oben sichtbar. Die Jahresauswahl zeigt nur Jahre ab dem ersten Datentag (`firstDay` in `/plant`).
- Energiefluss-Diagramm (Sankey) über den Kreisdiagrammen. Tage ohne Batteriewerte fließen über den Knoten „Nicht trennbar“.
- Kalender (`/daily`) mit Autarkie, PV-Ertrag, Netzbezug und Speicherentladung je Tag, Ausfall- und Nachholtage als Raute, Klick öffnet den Tag.
- Monatsvergleich: letztes Jahr als Balken neben dem helleren Vorjahr, frühere Jahre gestrichelt.
- ECharts 6 setzt Legenden standardmäßig nach unten. `Chart` setzt sie wieder nach oben, wo die Diagramme Platz dafür lassen.
- Die API-Begrenzung steigt von 240 auf 600 Anfragen pro Minute und Adresse. Eine Ansicht lädt jetzt etwa zehn Datensätze; beim Durchklicken von Zeiträumen wurde die alte Grenze erreicht.

**Kosten (COST-01).** `Economics.Evaluate` bepreist jeden Tag mit dem gültigen Tarif und vergleicht mit denselben Preisen ohne Anlage und ohne Speicher. Endpunkt `/economics`, Ansicht unter Perspektive „Kosten“ mit Kacheln, Monatsdiagramm (Bezug, Grundpreis, Erlös negativ, Ersparnis kumuliert) und Tabelle. Ohne Tarife führt ein Knopf zur Tariferfassung. Für die private Anlage sind noch keine Tarife hinterlegt.

**Speicherzustand (BAT-02).** `BatteryHealth.Evaluate` über alle Tage mit Batteriewerten, Endpunkt `/battery-health`, Abschnitt oben auf „Speicher & Potenzial“. Kennzahlen je Jahr und Monat, Feststellungen zu Wirkungsgrad und Winterbetrieb, Rechenweg. Die Kapazität kommt aus den Einstellungen oder der Schätzung (gekennzeichnet).

**Ausfall- und Nachholtage (DATA-07).** `DayFlags.Detect`: Ein Tag mit 0 kWh Hausverbrauch ist ein Ausfall, der Folgetag ein Nachholtag. Beispiel: 18.08.2025 (alle Werte 0) und 19.08.2025 (136,1 kWh PV bei 12 kWp). Beobachtungen nehmen diese Tage nicht als Höchstwert und nennen die Anzahl. Der Speicherzustand schließt sie aus.

**Prüfungen.**
- 47 Unit-Tests (neu: Ausfall- und Nachholtage, Höchstwerte ohne Nachholtag, Kosten mit beiden Vergleichsfällen, Tage ohne Preis, Speicherzustand) und 42 Integrationstests bestanden.
- TypeScript und ESLint ohne Fehler.
- Playwright gegen die Demo: 12 bestanden, 1 übersprungen. Angepasst wurden die Selektoren für die Kacheln, der verschobene Datenhinweis und der Text „bisher vergangenen Stunden“.
- In der Demo im Browser geprüft (dunkles Schema): Kacheln mit Vorjahresvergleich, Energiefluss, Kalender über drei Jahre, Kostenansicht und Speicherzustand.
- Demo und private Instanz neu bereitgestellt, beide `ready`. Die Browserprüfung der privaten Instanz steht aus, weil die Anmeldung nach dem Neustart abgelaufen ist.

**Offen.** Tagesprofile nach Uhrzeit für Sommer und Winter brauchen historische 15-Minuten-Werte (DATA-08). Die Speicherkapazität laut Datenblatt ist noch nicht hinterlegt.

### DATA-05 und DATA-06 · Fehlerprüfung und Herkunft historischer Batteriewerte

Am 14. November 2026 folgte eine Prüfung der neuen Auswertungen und der Datenquellen.

**Fehler behoben.**
- Die Basis der Sungrow-Tageswerte zählte den laufenden Tag als fehlend. Tageswerte gibt es nur für abgeschlossene Tage. Im laufenden November stand deshalb „Basis 93,8 %“, obwohl kein Tag fehlte. Die Basis endet jetzt am Beginn des heutigen Tages (`DataView.CreateReport`).
- „Gemeinsam bilanzierbar“ setzte die bilanzierbaren Stunden ins Verhältnis zu allen Stunden des Zeitraums, auch den künftigen. Bezug ist jetzt die bisher vergangene Zeit (`elapsedHours` in `/energy-insights`).

**Lesende Proben über die private Instanz.** Es wurde nichts gespeichert.
- Die Tagesroute (`/provider/probe`) lieferte für den 10.09.2023 und den 15.07.2024 PV, Last, Bezug und Einspeisung. Für die Batteriepunkte 83322 und 83323 kam kein Wert. Ein erneuter API-Tagesimport ergänzt deshalb keine Batteriewerte.
- Die Gerätehistorie (`/provider/probe/device-minutes`, Gerät `<Geräte-Key>`) lieferte für den 01.06.2023 und den 15.07.2024 alle Gesamtzähler, auch Batterieladung (13034) und -entladung (13035), dazu Momentanleistungen und SOC als Anteil 0–1. Der SOC-Punkt 13141 hat keine Einheit und erscheint in der Diagnose deshalb als `unknown-unit`. Die Werte (0,789 bzw. 1) sind plausibel.
- Der bestehende Gerätezähler-Nachimport schreibt historische Tage nicht, weil er Sungrow-Tageswerte für alle sechs Kanäle voraussetzt. Ein eigener Tageszähler-Auftrag ist als DATA-06 beschrieben.
- Die Anlagen-Monats- und -Jahresberichte (CSV) haben keine Batteriespalten. Der Gesamtbericht enthält Jahressummen, deren Bilanz exakt schließt.
- In der Datenbank stimmt die Netto-Batterieenergie aus der Bilanz (PV + Bezug − Last − Einspeisung) an allen Tagen mit Batteriewerten mit der gemessenen Differenz Ladung − Entladung überein. Für historische Tage ist sie damit bekannt, die Bruttowerte fehlen.

**Prüfungen.** 35 Unit-Tests, 41 Integrationstests, TypeScript, ESLint und Vite-Build ohne Fehler.

### DATA-06 · Batterie-Tageswerte aus Gerätezählern

**Umsetzung.**
- `BatteryCounterDays.Evaluate` (Application) bildet Tageswerte aus zwei Mitternachtsständen der Gesamtzähler 13134, 13130, 13148, 13125, 13034 und 13035.
- Ein Tag gilt nur, wenn
  - beide Stände vollständig vorliegen,
  - kein Zähler rückläufig ist,
  - die sechs Kanäle die Bilanz innerhalb der Tagestoleranz schließen,
  - jeder bereits gespeicherte Tageswert höchstens 0,2 kWh abweicht.
- `BatteryHistory` (Api) führt den Lauf im Hintergrund aus:
  - ein Abruf der Gerätehistorie je Mitternacht (Fenster 00:00 bis 01:00 Uhr, 15-Minuten-Raster), über den bestehenden Aufrufabstand. Ein 15-Minuten-Fenster lieferte für Juni 2023 an den meisten Tagen keinen Wert, das Stundenfenster an denselben Tagen schon;
  - wartet bei Rate-Limits, wiederholt vorübergehende Fehler zweimal und zählt Abrufprobleme;
  - lässt sich abbrechen.
- Vorschau: höchstens 31 Tage, schreibt nichts.
- Übernahme:
  - schreibt mit `fillOnly` nur fehlende Kanäle als Sungrow-Tageswert mit Revision `sungrow-device-counter-day-v1`;
  - setzt keinen Prüfpunkt, weil bereits vollständige Tage übersprungen werden; ein erneuter Lauf setzt dadurch fort;
  - startet nicht, solange ein regulärer Import läuft.
- Endpunkte: `GET`, `POST` und `DELETE /api/v1/provider/battery-history`, im Demomodus gesperrt.
- Oberfläche im Datenstatus mit Zeitraum, Abrufschätzung, Vorschau, Bestätigung, Fortschritt und Tagesliste.

**Prüfungen.**
- Neu: sechs Unit-Tests für die Tagesbewertung und ein Integrationstest (Vorschau ohne Schreiben, Übernahme nur fehlender Werte, unveränderte CSV-Werte, erneuter Lauf ohne Abrufe).
- Insgesamt 41 Unit- und 42 Integrationstests bestanden. TypeScript und ESLint ohne Fehler.
- Vorschau auf der privaten Instanz für den 20.08. bis 02.09.2024 (15 Abrufe, nichts gespeichert):
  - 12 Tage übernehmbar; PV, Last, Bezug und Einspeisung stimmen dort mit den gespeicherten CSV-Tageswerten auf 0,0 kWh überein.
  - Zusammen 103,5 kWh Ladung und 95,3 kWh Entladung.
  - Für den 27.08. lieferte die Gerätehistorie um 00:00 Uhr keinen Zählerstand. Dieser Tag und der 26.08. bleiben deshalb offen; der 26.–28.08. ist auch in den CSV-Daten eine Lücke.
  - Der 28.08. hatte keinen gespeicherten Tageswert und wäre mit allen sechs Kanälen ergänzt worden.
  - Der erste Vorschaulauf ohne Wiederholungen verlor acht Tage durch einzelne fehlgeschlagene Abrufe. Seitdem werden solche Fehler wiederholt und als Hinweis gezählt.

**Schreiblauf auf der privaten Datenbank (14. November 2026, nach Freigabe).**
- Vorher gesichert mit `pg_dump`: `before-battery-history-20261114-004735.dump`, 14 Tabellen laut `pg_restore -l`.
- Erster Anlauf mit 15-Minuten-Fenster: für Juni 2023 fehlten fast alle Mitternachtsstände. Nach sechs Tagen ohne Schreibvorgang abgebrochen und auf ein Stundenfenster umgestellt.
- Lauf 1 (01.06.2023 bis 31.07.2026, 1.157 offene Tage): 490 Tage übernommen, 667 ohne Zählerstand, keiner verworfen. Einzelabfragen zeigten, dass die Werte für einen Teil dieser Tage existieren und die Antworten nur zufällig leer waren.
- Leere Antworten werden seitdem nach 30 s einmal wiederholt.
- Lauf 2 (dieselben Grenzen, nur die 667 offenen Tage): 641 Tage übernommen, 3 verworfen, 23 ohne Zählerstand. Zusammen 1.331 Werte geschrieben. 333 Antworten waren beim ersten Versuch leer.
- Verworfen wurden der 03.07., 08.07. und 14.08.2023: Dort weichen PV bzw. Einspeisung der Zähler um mehr als 0,2 kWh von den gespeicherten CSV-Tageswerten ab.
- Ohne Zählerstand blieben unter anderem der 27./28.06. und 26./27.08.2024 sowie mehrere Tage im April, Mai, Juli, August und September 2025. Sie decken sich überwiegend mit den bekannten Lücken der CSV-Daten.

**Abgleich mit den Jahressummen des Portal-Gesamtberichts.**

| Jahr | Tage mit Batteriewert | Ladung DB / CSV (kWh) | Entladung DB / CSV (kWh) | Bewertung |
| --- | --- | --- | --- | --- |
| 2023 | 211 von 228 | 1.056,2 / 1.150,7 | 938,3 / 987,7 | Die CSV-Summe enthält den 17. bis 31.05. und 17 weitere Tage, die in der Datenbank fehlen. |
| 2024 | 362 von 364 | 2.158,5 / 2.187,8 | 1.938,4 / 1.946,7 | −1,3 % bzw. −0,4 %, erklärbar durch vier fehlende Tage. |
| 2025 | 346 von 352 | 2.117,3 / 2.228,1 | 1.924,1 / 1.963,5 | −5,0 % bzw. −2,0 %, 19 Kalendertage fehlen in der Datenbank. |
| 2026 | 316 von 316 bis 12.11. | 2.287,4 / 2.270,6 | 2.063,1 / 1.971,0 | Nicht vergleichbar. Die Datenbank liegt in allen Kanälen über der CSV (PV +120 kWh), der Gesamtbericht deckt also weniger Tage ab. |

Die Jahresbilanz schließt in der Datenbank in jedem Jahr exakt. Für 2023, 2024 und 2025 liegt die Datenbank in jedem Kanal höchstens auf Höhe der CSV-Summe, was zu den fehlenden Tagen passt.

**Wirkung.**
- Seit Datenbeginn sind 331 von 23.585 kWh Hausverbrauch (1,4 %) nicht trennbar, vorher waren es mehr als die Hälfte.
- 2024: 2.375,6 kWh direkt aus PV, 1.938,4 kWh aus dem Speicher, 2.428,6 kWh aus dem Netz, 47,9 kWh nicht trennbar.

**Danach behoben.** Die Energiebilanz zählte Tage vor dem ersten gespeicherten Wert als fehlend, etwa Januar bis Mai 2023 im Jahr 2023. Sie beginnt jetzt am ersten Datentag. Ein Unit-Test ist ergänzt.

**Offen.** Mai 2023 vor dem 01.06. ist nicht nachgeladen.

### Lokale Bereitstellung

Vor der Zusammenführung auf `main` wurden TypeScript, ESLint, der Vite-Build, 25 Unit-Tests und 41 Integrationstests mit einer frischen PostgreSQL-Testdatenbank erneut geprüft. Beide bestehenden lokalen Instanzen meldeten HTTP 200 auf `/health/ready`.

TypeScript, ESLint, Vite und der .NET-Release-Publish liefen durch. Ein Playwright-Lauf gegen die lokale Demo ergab zehn bestandene Tests und einen übersprungenen privaten Anmeldetest. Beim erneuten Lauf erhielt der allgemeine Demoablauf vor dem CSV-Export HTTP 429. Die übrigen neun Tests bestanden. Der gezielte Browser-Test für Tages- und Monatsabdeckung sowie Heatmap-Leerzustand und gemessene Null bestand auch separat. Der Live-Cache-Test deckte 0 % SOC ab.

Das Demo-Image wurde mit Docker Compose neu gebaut und der App-Container auf Port 8080 ersetzt. Die private Instanz auf Port 8081 wurde mit dem neuen Release-Publish über ihren bestehenden Startweg neu gestartet. Datenbank und Konfiguration wurden weiterverwendet. Beide Instanzen meldeten HTTP 200 auf `/health/ready`. Der private Server lieferte die Startseite aus dem neuen Build.

Mit UX-06, UX-07 und LIVE-01 bestanden TypeScript, ESLint, Vite, 25 Unit-Tests, 36 Integrations- und Vertragstests, 11 Linux-Playwright-Tests und der PostgreSQL-Restore. Beide lokalen Instanzen wurden aus diesem Quellstand erneut bereitgestellt.

## OPS-01 · Cloud-Betrieb

Stand 2026-11-15 auf `feat/cloud-oracle`:

- Neu: Produktions-Overlay `deploy/cloud/compose.cloud.yaml` mit internem DB-Netz, festen IPs, Tailscale `serve`, Healthcheck, `read_only`, `cap_drop`, Limits und Log-Rotation. Dazu Skripte für Abgleich (`verify.sql`), verschlüsseltes Backup (`backup.sh` mit age, systemd-Timer) und Wiederherstellungsprobe (`restore-check.sh`).
- Sicherheitskorrekturen:
  - `data` in `.dockerignore`;
  - OpenAPI nur im Demo- und Development-Modus;
  - ungültige `Solar:TrustedProxy` bricht den Start ab.
- Prüfungen:
  - Unit-Tests und Docker-Build;
  - lokaler Stack mit Dummy-Secrets: HTTPS-Zwang, Proxy-Vertrauen, Netztrennung, Read-only-Dateisystem, OpenAPI 404, Startabbruch bei ungültiger Proxy-IP, Restore-Check;
  - `verify.sql` lesend gegen die private DB.
  Einzelheiten stehen im Übergabeprotokoll von [cloud-deployment.md](cloud-deployment.md#übergabeprotokoll).
- ARM64-Build unter QEMU ist erfolgreich.
- Nicht geprüft: Tailscale `serve` (noch kein Account), `backup.sh` als Ganzes (braucht Linux mit age), Lauf auf echter ARM-Hardware.
- Lokale Bereitstellung auf 8080/8081 **nicht** aktualisiert: Die Änderungen liegen nur auf dem Feature-Branch, und laufende Dienste werden ohne Freigabe nicht gestoppt.

Stand 2026-11-16 (`main` 78351a2):

- Demo 8080 und lokale private Instanz 8081 neu gebaut. Vorher die private DB gesichert (`before-main-78351a2-*.dump`), es gab keine neue Migration. Beide meldeten `ready`.
- Danach Oracle-VM eingerichtet, Daten migriert und Cutover um 00:55. Die private Instanz läuft seitdem in der Cloud, 8081 ist gestoppt.
- Geprüft:
  - `verify.sql` zwischen Quelle und Ziel identisch;
  - Login mit dem bisherigen Passwort;
  - erster Sungrow-Live-Abruf in der Cloud `confirmed`;
  - Backup entschlüsselt und wiederhergestellt.
- Einzelheiten stehen im [Übergabeprotokoll](cloud-deployment.md#übergabeprotokoll).

## UX-11 · Logo in der Kopfzeile und Tab-Icon

Stand 2026-11-16:

- **Änderungen:**
  - Aus `src/Img/SolarInsight_Logo_Horizontal_Transparent.png` wurde der innere Logobereich auf `src/Web/public/solar-insight-logo.png` (568 × 96 px) zugeschnitten und in `src/Web/src/main.tsx` als Markenbild der Kopfzeile (`.brand img`, 44 px Höhe auf Desktop, 34 px auf Mobilgeräten) eingebunden. Die Hintergrundfarbe von `header` in `src/Web/src/style.css` entspricht exakt dem dunklen Grünton des Logos (`#012c23`), sodass kein Rahmen sichtbar ist.
  - Aus `src/Img/Mint Solar Field Badge.png` wurden die Tab- und App-Icons `favicon.ico` (32 × 32 px), `favicon-32.png` (32 × 32 px), `apple-touch-icon.png` (180 × 180 px) und `icon-192.png` (192 × 192 px) unter `src/Web/public/` erzeugt und in `src/Web/index.html` samt `<meta name="theme-color" content="#012c23">` verlinkt.
  - In `docs/backlog.md`, `docs/implementation-status.md`, `docs/import-todos.md` und `docs/cloud-deployment.md` wurden widersprüchliche Statusangaben und offene Checklistenpunkte mit den dokumentierten Prüfnachweisen abgeglichen.
- **Prüfungen:**
  - TypeScript (`tsc --noEmit`), ESLint und Vite-Produktionsbuild ohne Fehler; alle Icon- und Logodateien liegen im gebauten `dist/`-Verzeichnis.
  - .NET-Release-Build und Tests: 47/47 Unit-Tests und 42/42 PostgreSQL-Integrationstests bestanden.
  - Vollständige Browser- und Restore-Suite (`.local/backlog-browser.ps1` gegen isolierte Demo- und Privat-Instanzen auf Port 8180/8181 mit PostgreSQL): 13/13 Playwright-Tests (inkl. 375-px-Layoutprüfung auf allen Hauptansichten) sowie Demo-Neustart und `pg_dump`/`pg_restore`-Prüfung bestanden.
- **Lokale Bereitstellung:**
  - Die frühere private Instanz auf Port 8081 wurde gemäß Betriebsregel nach dem Cloud-Cutover (OPS-01) nicht gestartet.
  - Das Docker-Image `solarinsight-app:latest` für die lokale Demo auf Port 8080 wurde mit `.local/ci-deploy-demo.cmd` aus dem aktuellen Quellstand erfolgreich neu gebaut und die Container `solarinsight-db-1` und `solarinsight-app-1` wurden gestartet (`Healthy`). Anschließend beendete sich jedoch die Linux-Engine von Docker Desktop (`open //./pipe/dockerDesktopLinuxEngine: The system cannot find the file specified`), sodass `http://127.0.0.1:8080/health/ready` nach dem Engine-Stopp nicht mehr erreichbar war und im aktuellen Sitzungskontext ohne erhöhten Dienststart nicht erneut geprüft werden konnte.

## Statische GitHub-Pages-Demo und öffentliche Dokumentation

Stand 2026-11-16 (Branch `feat/github-pages-demo`):

- **Änderungen:**
  - `src/Web/src/staticDemo.ts`: Reiner Client-Demo-Adapter für statisches Hosting (GitHub Pages), der exakt denselben synthetischen Datengenerator (PRNG-Seed `17293`, 210.240 Fünf-Minuten-Intervalle von `2024-11-01` bis `2026-11-01`, 9,6-kWh-Batterie und Beispieltarife) wie `SolarInsight.Application.Demo` im Browser ausführt und alle Lese-, Simulations-, Probe- und Export-Endpunkte ohne Backend-Server bedient.
  - `src/Web/src/api.ts`, `Overview.tsx`, `Scenario.tsx`, `main.tsx`: Optionale Build-Weiche `import.meta.env.VITE_STATIC_DEMO === 'true'` und Base-URL-Unterstützung (`import.meta.env.BASE_URL`). Im regulären Build (`npm run build` / `Dockerfile`) ist `VITE_STATIC_DEMO` nicht gesetzt; Vite entfernt `staticDemo.ts` vollständig per Tree-Shaking (617 statt 618 Module), sodass der private Betrieb und das Docker-Image unverändert bleiben.
  - `.github/workflows/pages.yml` und `npm run build:pages`: GitHub-Actions-Workflow für den automatischen Build (`--base=/SolarInsight/`) und das Deployment auf GitHub Pages mit minimalen Berechtigungen (`contents: read`, `pages: write`, `id-token: write`).
  - `README.md`: Vollständige Einrichtungs- und Self-Hosting-Anleitung für Nutzer mit eigener Sungrow-/iSolarCloud-Anlage sowie Hinweise zu optionalem Heimnetz-/Cloud-Betrieb und GitHub Pages.
- **Prüfungen:**
  - TypeScript (`tsc --noEmit`), ESLint, regulärer Vite-Build (`npm run build`) und statischer Pages-Build (`VITE_STATIC_DEMO=true npm run build:pages`) fehlerfrei.
  - Playwright-Smoke-Test gegen den gebauten statischen Pages-Ordner unter `/SolarInsight/`: Übersicht, Energiefluss, Speicher & Potenzial, interaktive Szenario-Simulation, Datenstatus und Live-Probe bestanden mit `0` ausgehenden `/api/`-Netzwerkaufrufen.
  - Vollständige E2E- und Restore-Suite (`.local/backlog-browser.ps1`) gegen den regulären .NET-/PostgreSQL-Build: 13/13 Playwright-Tests bestanden.
