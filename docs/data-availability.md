# Datenverfügbarkeit, Lücken und Nachimport

Stand 13. November 2026, ermittelt mit lesenden Abfragen auf der privaten Datenbank. Die laufende Übersicht steht in der Anwendung unter **Datenstatus → Datenbestand und Lücken** (`GET /api/v1/data-quality`).

## Was gespeichert ist

| Messgröße | Tageswerte | 15-Minuten-Intervalle | Quelle |
| --- | --- | --- | --- |
| PV-Ertrag, Hausverbrauch, Netzbezug, Einspeisung | 18.05.2023 bis 12.11.2026, einzelne Lücken | 11.10. bis 12.11.2026 | Portal-CSV bis 31.07.2026, danach iSolarCloud-API; Intervalle aus Gerätezählern |
| Batterieladung, Batterieentladung | ab 01.06.2023, bis 31.07.2026 aus Gerätezählern (DATA-06), 26 Tage ohne Wert | 11.10. bis 12.11.2026 | iSolarCloud-API, Gerätezähler |
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

Batterie-Tageswerte fehlen vom 17. bis 31.05.2023 sowie an 26 Einzeltagen bis Juli 2026 (siehe Umsetzungsstand DATA-06). Intervalle mit allen Kanälen und SOC fehlen vor dem 11.10.2026.

## Was iSolarCloud nachliefern kann

| Lücke | Möglicher Weg | Sicherheit für bestehende Daten |
| --- | --- | --- |
| Batterie-Tageswerte vor August 2026 | **Nicht** über die Tagesroute: Lesende Proben für den 10.09.2023 und den 15.07.2024 lieferten PV, Last, Bezug und Einspeisung, aber keine Werte für 83322 und 83323. Ein erneuter API-Tagesimport bringt also keine Batteriewerte und würde nur CSV-Tageswerte überschreiben. **Möglich über die Gerätehistorie:** Proben für den 01.06.2023 und den 15.07.2024 lieferten die Gesamtzähler für Batterieladung (13034) und -entladung (13035). Die Differenz zweier Mitternachtsstände ergibt die Tageswerte. Importweg: **Datenstatus → Batterie-Tageswerte aus Gerätezählern nachladen** (DATA-06), zuerst als Vorschau. | Ergänzt nur fehlende Werte (Revision `sungrow-device-counter-day-v1`) und prüft jeden Tag gegen die gespeicherten Tageswerte. Vorher sichern (`pg_dump`). |
| Batterie-Halbstundenwerte und Jahressummen aus CSV | Der eigene Portalbericht „export 1“ enthält Tagesladung und -entladung, liegt aber nur für einzelne Tage vor. Die Anlagen-Monats- und -Jahresberichte haben **keine** Batteriespalten. Der Gesamtbericht enthält Jahressummen 2023–2026 für Ladung und Entladung. Sie schließen die Jahresbilanz exakt (2024: 6.749,6 + 2.187,8 + 4.753,0 = 9.361,0 + 2.382,7 + 1.946,7 kWh) und taugen zur Gegenprüfung eines Gerätezählerimports. | Kein Import nötig, wenn DATA-06 umgesetzt ist. |
| Batterie-Jahressummen | Im Portal-Gesamtbericht vorhanden. Geeignet zur Plausibilisierung, nicht für Tagesanalysen. | Keine Speicherung nötig. |
| 15-Minuten-Intervalle und SOC vor Oktober 2026 | Die Gerätehistorie liefert sie ab Juni 2023, einschließlich SOC als Anteil 0–1 (Probe 15.07.2024, 12:00 Uhr: 100 %). Der bestehende Nachimport (`/provider/device-counters/backfill`) schreibt dort aber nicht, weil er Sungrow-Tageswerte für alle sechs Kanäle voraussetzt. Je Tag sind etwa neun Aufrufe nötig (Fenster bis drei Stunden), bei 15 s Aufrufabstand rund 2 Minuten pro Tag. | Vorschau ohne `apply`. Schreibt nur geprüfte Intervalle. |
| Fehlende einzelne Tage 2023–2025 | Gezielte Tages- oder Monatsexporte aus dem Portal, falls dort vorhanden. Ein erneuter Import derselben Jahresdateien schließt die Lücken nicht. | Import nur für fehlende Werte. |

Wiederholbarkeit: Der Hintergrundabgleich (`HistoryWorker`) und der Gerätezählerimport schreiben mit `fillOnly` und ändern vorhandene vollständige Werte nicht. Die Quellenrevision (`Revision`) bleibt je Wert nachvollziehbar.
