# Umsetzungsstand

## Anforderungen und offene Arbeit

| Bereich | Anforderungen | Stand |
| --- | --- | --- |
| Kernumfang | [Solar Insight Spezifikation](../SolarInsight_Spezifikation.md) | A01–A24 umgesetzt und geprüft. A24 mit Testdaten. Die Sungrow-Abnahme A25 ist offen. Die Einzelprüfungen stehen unten. |
| Kalender, Energieflüsse und Speicher | [Bugs und Feature-Anforderungen](../SolarInsight_Bugs_und_Features.md) | BUG-01–04 und FEATURE-01–05 umgesetzt. Der Abgleich der Messpunkte an einer echten Anlage bleibt Teil von A25. |
| Import, Live-Daten und Heimnetz | [Sungrow-Import](import-todos.md) | Erledigte Anforderungen und die offene Anlagenprüfung stehen in der Checkliste. |
| Bedienung, Setup und automatische Importe | [Geplante Verbesserungen](backlog.md) | Offen. Die Anforderungen und Abnahmekriterien stehen im Backlog. |
| Repository-Lizenz | [Solar Insight Spezifikation](../SolarInsight_Spezifikation.md) | Entscheidung des Eigentümers offen. |

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
| A25 | Offen | Freigegebene EU-Entwickleranwendung, Eigentümerautorisierung, autorisierte Tagesprobe mit Messpunktvertrag und tatsächlich geltende Kontingente fehlen. Öffentliche Verträge und synthetische Vertragstests liegen vor. |

## Fortsetzung

1. Nach Sungrow-App-Freigabe mit einer autorisierten Anlage die in der Provider-Dokumentation beschriebene A25-Abnahme durchführen. Messorte, Vorzeichen, SOC-Semantik, Paketgrenzen und die reale Datenrückschau mit Belegen ergänzen.
2. Repository-Lizenz durch den Eigentümer festlegen.

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

Docker Compose hat das aktuelle Image gebaut und die bestehende Demo auf Port 8080 aktualisiert. Die private Instanz auf Port 8081 erhielt denselben Quellstand als .NET Release-Publish. Vor ihrer ersten Migration wurde ein lokaler Datenbankdump erstellt. Beide Instanzen meldeten HTTP 200 auf `/health/ready`.

Zusätzlich starteten getrennte Compose-Projekte mit frischen Volumes für Demo und privaten Testbetrieb. Die Demo schloss den synthetischen Import ab. Bestanden sind 25 Unit-Tests, sechs Playwright-Tests gegen diese Compose-Projekte, ESLint, TypeScript und der Release-Build. Der neue Speichertest prüft, dass dieselbe SOC-Messung vor und nach einem Wechsel der Ladegrenze unterschiedlich eingeordnet wird.

Die erneute Sungrow-Live-Abnahme bleibt offen. Die automatische Freigabeprüfung hat die lesende Abfrage privater Anlagendaten bis zu einer ausdrücklichen Betreiberfreigabe abgelehnt.
