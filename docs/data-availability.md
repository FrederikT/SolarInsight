# Datenverfügbarkeit, Lücken und Nachimport

Stand 13. November 2026, ermittelt mit lesenden Abfragen auf der privaten Datenbank. Die laufende Übersicht steht in der Anwendung unter **Datenstatus → Datenbestand und Lücken** (`GET /api/v1/data-quality`).

## Was gespeichert ist

| Messgröße | Tageswerte | 15-Minuten-Intervalle | Quelle |
| --- | --- | --- | --- |
| PV-Ertrag, Hausverbrauch, Netzbezug, Einspeisung | 18.05.2023 bis 12.11.2026, einzelne Lücken | 11.10. bis 12.11.2026 | Portal-CSV bis 31.07.2026, danach iSolarCloud-API; Intervalle aus Gerätezählern |
| Batterieladung, Batterieentladung | erst ab 01.08.2026 | 11.10. bis 12.11.2026 | iSolarCloud-API, Gerätezähler |
| Hausverbrauch allein | — | 01.08. bis 12.11.2026 | Minutenwerte aus Momentanleistung (trapezförmig) |
| Ladezustand (SOC) | — | 2.776 Stichproben, 11.10. bis 12.11.2026 | Gerätehistorie (`LiveMeasurements`), seit dieser Änderung an die Intervalle angehängt |
| Live-Momentanwerte | — | seit 07.11.2026 | Gerätemomentanwerte |

Die Sungrow-Tageswerte schließen die Energiebilanz (Verbrauch + Ladung + Einspeisung = PV + Bezug + Entladung) an allen Tagen mit Batteriewerten exakt. Bei den Gerätezählerintervallen gilt das mit Ausnahme des Mischtages 12.10.2026 ebenfalls.

## Was bisher gespeichert, aber nicht ausgewertet wurde

- **Ladezustand:** Die SOC-Stichproben lagen nur in `LiveMeasurements` und erschienen ausschließlich im Tagesdetail als Punktwolke. Monats-, Speicher- und Ladegrenzenauswertungen sahen keinen SOC. Jetzt werden sie den Intervallen zugeordnet.
- **Tageswerte für Energieflüsse:** Die Kreisdiagramme rechneten nur mit Intervallen und verlangten bestätigte AC-Pfade (`ConfirmedAcPaths`). Für diese Anlage ist das nicht gesetzt. Deshalb blieben sie selbst bei vollständigen Tageswerten leer.
- **Batterie-Tageswerte** wurden nur als Summe angezeigt und nicht in Langzeitdiagrammen verwendet.

## Fehlerhafte Aggregationen und Zuordnungen

- Die Quoten im Frontend verlangten 100 % Tagesabdeckung des **gesamten** Zeitraums. Laufende Monate, Quartale und Jahre hatten damit nie eine Quote, und ein einzelner fehlender Lasttag blendete die Quote eines ganzen Quartals aus. Behoben: Die Quote wird über die gepaarten Tage berechnet, die Basis wird angezeigt, und der Bezug ist der verstrichene Zeitraum.
- Die Speicheransicht der Monatsübersicht nahm den SOC aus den Sungrow-Tageswerten, die keinen SOC enthalten. Behoben.
- Keine doppelten oder falsch zugeordneten Werte gefunden. Der eindeutige Schlüssel (Anlage, Messgröße, Tag, Herkunft) verhindert Duplikate.

## Lücken

Tage ohne einen der vier Grundkanäle (aus der DATA-04-Prüfung): 17.05.2023 (Beginn um 15:00), 14.11.2023, 26.–28.08.2024 sowie in 2025 der 24.–26.04., 06.–08.05., 18.–19.05., 10.–14.07., 24.–26.08., 16.–17.09., 21.–23.09. und 07.–08.10. Teilweise fehlend: 19.05., 09.07., 29.07. und 13.08.2023, 11.10.2025 und 29.07.2026 (Last).

Batterie-Tageswerte fehlen vom 17.05.2023 bis 31.07.2026. Intervalle mit allen Kanälen und SOC fehlen vor dem 11.10.2026.

## Was iSolarCloud nachliefern kann

| Lücke | Möglicher Weg | Sicherheit für bestehende Daten |
| --- | --- | --- |
| Batterie-Tageswerte vor August 2026 | API-Nachimport (Datenstatus → Nachimport). Die Tagesroute fragt die Punkte 83322 und 83323 mit ab. Ob iSolarCloud sie rückwirkend liefert, ist **ungeprüft**. Zuerst einen Testmonat importieren. | Der manuelle Nachimport ersetzt Tageswerte desselben Tages durch API-Werte. Die CSV-Werte lagen bei 385 von 394 Vergleichstagen höchstens 0,1 kWh daneben. Vorher sichern (`pg_dump`). |
| Batterie-Halbstundenwerte | Portal-Tagesberichte (CSV) enthalten Ladung und Entladung im 30-Minuten-Raster. Ein Importweg dafür existiert noch nicht. | Ein Import müsste wie DATA-04 nur fehlende Werte ergänzen. |
| Batterie-Jahressummen | Im Portal-Gesamtbericht vorhanden. Geeignet zur Plausibilisierung, nicht für Tagesanalysen. | Keine Speicherung nötig. |
| 15-Minuten-Intervalle und SOC vor Oktober 2026 | Gerätezähler-Nachimport (`/provider/device-counters/backfill`) für einzelne Tage. Die Gerätehistorie liefert begrenzte Fenster und ist kontingentiert. | Vorschau ohne `apply`. Schreibt nur geprüfte Intervalle. |
| Fehlende einzelne Tage 2023–2025 | Gezielte Tages- oder Monatsexporte aus dem Portal, falls dort vorhanden. Ein erneuter Import derselben Jahresdateien schließt die Lücken nicht. | Import nur für fehlende Werte. |

Wiederholbarkeit: Der Hintergrundabgleich (`HistoryWorker`) und der Gerätezählerimport schreiben mit `fillOnly` und ändern vorhandene vollständige Werte nicht. Die Quellenrevision (`Revision`) bleibt je Wert nachvollziehbar.
