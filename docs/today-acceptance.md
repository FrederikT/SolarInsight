# Abnahme und nächste Schritte für Solar Insight

Stand 10. November 2026, Europe/Berlin. Die private Instanz wurde schreibgeschützt geprüft.

## Laufender Tag

Der Serverdienst liest Sungrow bei aktiver, autorisierter Anlage und bestätigtem Profil automatisch. Im Hintergrund liegt der Standardtakt bei zehn Minuten. Bei sichtbarer Übersicht gilt ein Cloud-Mindestabstand von fünf Minuten. Die Oberfläche liest alle 30 Sekunden den lokalen Cache. Anbieterfehler können einen Abruf verschieben. Ein ausgeschalteter Server erfasst während des Ausfalls keine Live-Stichproben.

| Messwert | Gespeicherte Stichproben am 10. November | Quellzeit |
| --- | ---: | --- |
| Hausverbrauch, Netz und Batterie | je 60 | 08:35–18:40 Uhr |
| Solarleistung (DC) und Wechselrichter-Wirkleistung | je 37 | 14:45–18:40 Uhr |
| Ladezustand | 39 | 14:25–18:40 Uhr |

PostgreSQL enthielt 60 bestätigte Abrufe und einen Abruf ohne Messwert. Der letzte bestätigte Abruf war um 18:43 Uhr. Für den laufenden Tag enthielt die historische Intervalltabelle zum Prüfzeitpunkt keine Zeile. Das belegt die Speicherung der Live-Werte, aber keine lückenlose Laufzeit oder einen vollständigen Tagesverlauf.

Der Befund erklärte die zuvor leere Tagesübersicht. Die normale Ansicht des laufenden Tags und der aufklappbare Bereich **Heute → Heutigen Leistungsverlauf anzeigen** verwenden jetzt beide `/api/v1/live/today`. Das Diagramm unterscheidet gespeicherte Live-Stichproben, historische Intervalle und Demowerte. PV-DC und Wechselrichter-Wirkleistung sind eigene Reihen. Vorläufige kWh im Tagesdetail stammen aus bereits verfügbaren Energieintervallen. Die historische Abstimmung arbeitet auf abgeschlossenen Tagen. [Live-Endpunkt](../src/Api/LiveEndpoints.cs) · [Live-Abrufdienst](../src/Api/LiveMonitor.cs) · [Live-Diagramm](../src/Web/src/LiveView.tsx)

### Umsetzung und ausstehende Nachweise

1. **LIVE-02.** Gespeicherte Live-Stichproben sind ohne Handimport im ausgewählten heutigen Tag sichtbar. Die private Datenbank bestätigte nach dem Schließen des Prüf-Browsers bestätigte neue Quellwerte um 19:20, 19:30 und 19:40 Uhr. Beim Wiederöffnen blieb der Verlauf verfügbar.
2. **DATA-01.** Der persistente Hintergrundimport für Gerätezähler ist aktiv. Er speichert benachbarte Zählerdifferenzen als Intervalle, belastbare Lückensummen separat und den bestätigten historischen SOC als Zeitpunktwerte. Der Tageswechsel wurde in PostgreSQL mit Provider-Testdaten und simulierter Uhr geprüft. Der Tageswechsel der privaten Anlage bleibt separat zu beobachten.
3. **DATA-02 und A25.** Die gemessene Solarleistung am DC-Eingang im Betrieb nutzen. Für die AC-PV-Bilanz und die Simulation mit privaten Messdaten bleibt ein bestätigter Messpunkt erforderlich. Gebuchtes Sungrow-Kontingent und verfügbare Rückschau dokumentieren.

## Jetzt

Die Kachel soll **Solarleistung** heißen. Das Info-Symbol erklärt die Quelle **PV-DC-Leistung am Wechselrichter** und den Hinweis **AC-PV-Fluss nicht separat gemessen**. Die Messzeit bleibt beim Wert erkennbar. Für Leistung gilt beispielsweise **360 W**, **9.999 W** und ab der Schwelle **10 kW**. Die Schwelle richtet sich bei Netz und Batterie nach dem Betrag. Richtungen werden weiterhin benannt. Ladezustand bleibt in Prozent, Energie in kWh.

## Abnahme

| Prüfung | Erwartetes Ergebnis |
| --- | --- |
| Aktuelle private Übersicht | Solarleistung und Zeitstempel entsprechen einer vergleichbaren iSolarCloud-Stichprobe. Netz- und Batterierichtung sind nachvollziehbar. |
| 9. November, bereits nachgeladen | Sechs Energiekanäle liefern jeweils 83 von 96 Viertelstunden. Die 13 fehlenden Intervalle und der fehlende historische SOC sind sichtbar. PV und Verbrauch zeigen 13,6 und 16,1 kWh Tagesenergie. |
| Laufender Tag, nach LIVE-02 | Heutiges Datum öffnen und gespeicherte Stichproben ohne Import sehen. Browser schließen, bei laufendem Server mindestens zwei Hintergrundzyklen abwarten und nach dem Wiederöffnen neuere Werte sehen, sofern Sungrow neue Zeitstempel geliefert hat. |
| Automatischer Zählerimport, nach DATA-01 | Einen Tageswechsel abwarten und prüfen, dass belegte historische Intervalle automatisch erscheinen. Wiederholte Abrufe zählen keine Energie doppelt. |
| Leistungseinheiten, nach UX-08 | 360 W, 9.999 W und 10 kW sowie eine gerichtete Leistung und einen SOC-Wert prüfen. Quellhinweise sind per Tastatur und Touch erreichbar. |
| Abschluss | Demo-Ablauf mit Kalender, Szenario und Export prüfen. Die AC-PV-Bilanz und Speichersimulation mit privaten Messdaten sind wegen fehlender Messdaten aus der Abnahme dieser Version ausgenommen. Tatsächliche Sungrow-Kontingente und Rückschau bleiben zu prüfen. Der dokumentierte Restore ist mit Testdaten belegt. |

## Bestätigte Entscheidungen

- Der Verlauf erscheint in der normalen Tagesansicht und bleibt im kompakten Bereich „Heute“ verfügbar. Beide verwenden dieselbe Datenquelle.
- Der laufende Tag zeigt die bisher bekannten Leistungswerte und vorläufige Energiesummen für belegte Zeitabschnitte. Bei einem Ausfall bleiben vorhandene Werte und Teilsummen sichtbar. Fehlende Zeitabschnitte werden gekennzeichnet und bleiben bei der Summe unberücksichtigt. Dieselbe Regel gilt für abgeschlossene Tage mit Lücken.
- Die Einheitenregel gilt überall, wo Leistung angezeigt wird, einschließlich Diagrammen und Tooltips. Unter 10.000 W erscheint der Wert in Watt, ab 10.000 W in Kilowatt. Energiesummen bleiben in kWh.

- Beim Start der Funktion prüft der automatische Nachimport einmalig die letzten 30 abgeschlossenen Tage. Danach prüft er jeden neu abgeschlossenen Tag. Verfügbare Sungrow-Daten und Abrufgrenzen bestimmen, welche Lücken nachgeladen werden können.
- Für diese Version ist die gemessene DC-Solarleistung vorgesehen. Die AC-PV-Bilanz und Speichersimulation mit privaten Messdaten entfallen vorerst, weil ein bestätigter separater AC-PV-Messwert für die Anlage fehlt. Eine Schätzung ist für diese Version ebenfalls ausgenommen. Die Funktionen können erneut bewertet werden, sobald eine geeignete Messquelle belegt ist.
