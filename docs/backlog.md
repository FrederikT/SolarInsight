# Geplante Verbesserungen für Solar Insight

Hier stehen offene Anforderungen für kommende Änderungen. Der [Umsetzungsstand](implementation-status.md) verlinkt die bisherigen Anforderungen und dokumentiert ihre Abnahme.

## UX-01 · Oberfläche nach den Überarbeitungen prüfen

**Status:** Erledigt und lokal geprüft. [Nachweis UX-01](implementation-status.md#ux-01--oberfläche-und-datenstatus).

Die gesamte Oberfläche auf eine konsistente Gestaltung prüfen und insbesondere Einstellungen, Einrichtung und Datenstatus vereinfachen. Wichtige Informationen, Statusmeldungen und häufige Aktionen klar anordnen. Technische Details bei Bedarf aufklappbar machen. Vorhandene Informationen müssen auffindbar bleiben.

**Abnahme.** Die Ansichten wirken auf Desktop und Mobilgeräten zusammengehörig. Verbindungsstatus, letzter erfolgreicher Datenabruf und anstehende Nutzeraktionen sind schnell erkennbar. Lade-, Leer- und Fehlerzustände sind verständlich.

## UX-02 · Setup mit Standardwerten und geschütztem Sungrow-Mapping

**Status:** Erledigt und mit Provider-Fixtures geprüft. [Nachweis UX-02](implementation-status.md#ux-02--setup-und-betreiberprofil).

Standardwerte automatisch anwenden und im Setup vorausfüllen, wo eine Nutzereingabe vorgesehen ist. Das Setup fragt Angaben ab, die nur der Betreiber liefern kann, etwa die Sungrow-Autorisierung, die Anlagenwahl oder individuelle Tarife. Technische Vorgaben aus der Anwendung und der Sungrow-API, etwa Messpunkt-IDs, Einheiten, Vorzeichen und Abrufparameter, zentral im Backend pflegen. Bestätigte Zuordnungen automatisch anwenden. Ist eine Zuordnung für die Anlage unbekannt, den betroffenen Kanal und den Grund verständlich anzeigen.

Ports und andere Betriebsparameter können als schreibgeschützter Diagnosehinweis erscheinen, sofern sie bei der Fehlersuche helfen. Anpassungen für Sonderinstallationen gehören in die dokumentierte Betreiberkonfiguration.

**Abnahme.** Eine Standardinstallation lässt sich mit den notwendigen Betreiberangaben einrichten. Die Oberfläche bietet keine Eingabefelder für technische Vorgaben. Auch die API schützt festgelegte Zuordnungen vor Änderungen aus dem normalen Setup. Fehlende oder unbestätigte Messpunkte bleiben als solche sichtbar.

## UX-03 · Datenimport im Normalbetrieb automatisieren

**Status:** Erledigt und mit PostgreSQL und Provider-Fixtures geprüft. [Nachweis UX-03](implementation-status.md#ux-03--automatischer-erstimport).

Nach erfolgreicher Autorisierung und bestätigter Anlagenzuordnung den initialen Import mit sinnvollen Vorgaben einplanen. Anschließend den vorhandenen automatischen Abgleich weiterverwenden. Der Datenstatus zeigt Fortschritt, letzten erfolgreichen Abruf und verständliche Hinweise, falls der Nutzer eingreifen muss. Kurze Testimporte und technische Details bleiben in der Diagnose verfügbar. Ein gezielter Nachimport oder Abbruch bleibt bei Bedarf erreichbar.

**Abnahme.** Der erste Import startet nach erfolgreichem Setup ohne manuelle Zeitfenster oder technische Eingaben. Neustarts und wiederholte Setup-Bestätigungen erzeugen keinen zweiten Auftrag für denselben Rückimport. Fehlende Freigaben und Fehler erscheinen mit einem konkreten nächsten Schritt.

## CI-01 · Fehlgeschlagene GitHub-Actions-Pipeline beheben

**Status:** Erledigt. GitHub Actions hat den korrigierten Anwendungsstand einschließlich Browser- und Restore-Prüfung bestätigt. [Nachweis CI-01](implementation-status.md#ci-01--pipeline).

Die fehlgeschlagenen GitHub-Actions-Läufe und Jobprotokolle prüfen, bestätigte Ursachen beheben und den vollständigen Lauf mit der Korrektur verifizieren.

**Abnahme.** Der Job `verify` des Workflows **Build and verification** läuft einschließlich Browser- und Restore-Prüfung erfolgreich durch. Fehlerursache, Korrektur und Link zum erfolgreichen Lauf stehen im [Umsetzungsstand](implementation-status.md).

## UX-04 · Tageswerte und Abdeckung getrennt ausweisen

**Status:** Umgesetzt und mit einer Sungrow-Browserfixture geprüft. [Nachweis UX-04](implementation-status.md#ux-04-und-ux-05--kennzahlen-und-zeitreihen).

Am 6. November 2026 zeigt das Tagesdetail Energiewerte und beide Quoten, zugleich aber 0 % Datenabdeckung. Die Kennzahlen können aus Sungrow-Tagesaggregaten stammen, während die gemeinsame Abdeckung aus PV- und Verbrauchsintervallen berechnet wird, sobald Minutenwerte vorliegen. Kennzahlen, Quellen und Abdeckung auf derselben Ansicht eindeutig benennen. Tagesenergien in kWh, Leistungswerte im Tagesverlauf in kW und Ladezustand in Prozent ausweisen. Bei einer aus Tageswerten abgeleiteten Quote die Näherung und ihre Voraussetzungen am Wert kenntlich machen.

**Abnahme.** Eine Testfixture mit vollständigen Tageswerten und einer reinen Verbrauchszeitreihe zeigt die Tagesenergien und berechenbaren Quoten mit Quelle sowie 0 % **gemeinsame Minutenabdeckung** und den eigenen Abdeckungsstatus der Tageswerte. Fehlende Werte und gemessene Nullwerte sind unterscheidbar. Die Monatsansicht macht bei Teildaten erkennbar, weshalb eine Quote leer bleibt.

## UX-05 · Fehlende Zeitreihen und leere Heatmaps erklären

**Status:** Umgesetzt und mit einer Sungrow-Browserfixture geprüft. [Nachweis UX-05](implementation-status.md#ux-04-und-ux-05--kennzahlen-und-zeitreihen).

Im Tagesverlauf und in den Speicher- und Netzansichten je Kanal erklären, welche Intervallwerte fehlen. Tagesaggregate bleiben getrennt sichtbar. Die Heatmap bewertet den ausgewählten Kanal, auch wenn für dieselben Stunden andere Messreihen vorliegen.

**Abnahme.** Mit ausschließlich gemessenem Hausverbrauch bleibt dessen Kurve sichtbar. PV-Erzeugung, Netzbezug, Einspeisung, Batterieleistung und Ladezustand werden als fehlend gekennzeichnet. Beide Heatmap-Optionen zeigen bei fehlenden Netzdaten einen verständlichen Leerzustand. Eine vorhandene stündliche Null wird als Messwert behandelt.

## DATA-01 · Historische Sungrow-Kanäle verifizieren und gezielt nachladen

**Status:** Teilgeprüft. Verfügbare Zählerintervalle für den 9. November wurden über das bisherige Mittagsfenster hinaus nachgeladen. Ein gezielter Nachimport mit Vorschau ist in der Oberfläche erreichbar. Quelllücken und historischer Ladezustand bleiben offen. [Befund](implementation-status.md#data-01-und-data-02--erweiterter-nachimport-und-anzeige).

Für den 1., 4. und 6. November 2026 zuerst lokal je Kanal die gespeicherten Tagesaggregate und Intervalle prüfen. Für den 6. November ist eine autorisierte Probe mit allen sechs Tageskanälen dokumentiert. Im geprüften 15-Minuten-Fenster waren nur Last und Anlagenwirkleistung verfügbar. Fehlende historische PV-Leistung, Netzleistung mit Richtung, Batterieleistung und SOC anhand autorisierter Proben mit iSolarCloud abgleichen. Pro Kanal zwischen fehlender Zuordnung, fehlender Providerfreigabe, ausbleibenden Providerwerten und einer lokalen Importlücke unterscheiden.

Mit der bestehenden Anlagen-Minutenroute ein kleines abgeschlossenes Tageslichtfenster prüfen. Die vorhandene Diagnose fragt PV `83067`, Netz `83549`, Batterie `83238` und SOC `83129` neben Last und Anlagenwirkleistung bereits ab. Vor einem weiteren Wochenimport je Punkt Rückgabe, Einheit, Zeitstempel und Auflösung prüfen. Liefert die Anlagenroute weiterhin nur Last und Anlagenwirkleistung, die dokumentierte Gerätehistorie für das konkrete Gerät und dessen API-Freigabe separat prüfen. Nur bei bestätigter Verfügbarkeit und passender Intervallsemantik eine zusätzliche historische Quelle anbinden.

Der erneute Import des 9. November änderte keine Datensätze. Gespeichert sind sechs Tageskanäle und 83 Lastintervalle. Die Anlagenprobe lieferte um Mittag nur Last und Anlagenwirkleistung. Die Geräteprobe bestätigte zusätzliche Rohreihen. Die 13 fehlenden Lastintervalle beginnen zwischen 16:15 und 19:15 Uhr. Im betroffenen Fenster lieferten beide Quellen nur eine Laststichprobe. Einen gezielten Nachimport erst nach Abgleich der Quellwerte und der Intervallsemantik planen. [Prüfergebnis](implementation-status.md#data-01-und-data-02--gerätequelle-und-lastlücke).

**Abnahme.** Der Prüfbericht nennt Quelle und Ergebnis je Kanal und Tag. Für bestätigte historische Reihen wird das Betreiberprofil angewendet und nur der betroffene Zeitraum nachgeladen. Anschließend stimmen Zeitstempel, Einheiten, Richtungen und Stichproben in Tagesverlauf, Speicheransicht und Heatmap mit der Quelle überein. Kanäle ohne bestätigte historische Quelle bleiben mit dokumentiertem Grund offen. Tagesaggregate und Live-Stichproben dienen dabei nicht als Ersatz für historische Intervalle.

Der EU-Endpunkt und die Freigabe für das Gerät sind bestätigt. Die gespeicherten Tageswerte entsprechen den Portalreferenzen von 13,6 kWh für PV und 16,1 kWh für Last. Für den offenen Gegenvergleich PV, Last, Netz, Batterie und SOC mit den Portalwerten derselben Zeitstempel am 9. November gegen Mittag vergleichen.

## DATA-02 · Aktuelle Sungrow-Messpunkte mit iSolarCloud abgleichen

**Status:** PV-DC-Leistung, Wechselrichter-Wirkleistung und SOC sind umgesetzt. Die AC-PV-Bilanz und Speichersimulation mit privaten Messdaten sind zurückgestellt, bis ein separater AC-PV-Messwert für die Anlage bestätigt ist. [Nachweis](implementation-status.md#data-01-und-data-02--erweiterter-nachimport-und-anzeige) · [Abnahmeumfang](today-acceptance.md#abnahme).

Die vorhandenen und fehlenden Live-Kanäle mit der Quelle vergleichen. Beim Vergleich von Netzbezug und Hausverbrauch Messzeitpunkt, Messort, Geräte- gegenüber Anlagenwert, Vorzeichen und Aktualisierungsintervall festhalten. Für PV einen bestätigten Messpunkt am passenden Messort bestimmen. SOC als Prozentwert von der Richtung der Batterieleistung trennen und die Skalierung auch bei einem von null verschiedenen Wert prüfen.

Zuerst Gerätetyp und Modell über die vorhandene Geräteliste bestätigen. Am selben Gerät die aktuell verwendeten Last-, Netz- und Batteriepunkte zusammen mit dem SOC-Kandidaten `13141` und geeigneten PV-Kandidaten aus dem passenden Herstellerkatalog abfragen. `13003` bezeichnet DC-Gesamtleistung. Eine separate DC-Anzeige kommt nur mit korrekter Quellenangabe infrage. Für die AC-Flussbilanz den Messort separat belegen. Die dokumentierte Anlagen-Live-Route als mögliche Ergänzung prüfen, falls die Geräteroute keinen passenden PV-Wert liefert. Ohne nachweisbar vergleichbaren Messzeitpunkt Werte aus beiden Quellen separat ausweisen.

Am 10. November bestätigte der lokale Verlauf je 16 Stichproben für Last, Netz und Batterie. PV und SOC waren ohne Zuordnung. Für den offenen Portalvergleich die lesende Probe aus DATA-03 nutzen und die fehlenden Kandidaten erst nach Bestätigung übernehmen.

**Abnahme.** Vergleichswerte aus Solar Insight und iSolarCloud sind je Kanal mit Zeitstempel und Einheit dokumentiert. Bestätigte PV- und SOC-Kanäle werden über das Betreiberprofil übernommen. Eine bestätigte SOC-Messung von 0 % erscheint als leer. Fehlende Zuordnung, fehlender Messwert, veralteter Messwert und Abruffehler bleiben unterscheidbar. Zeitversatz oder verbleibende Abweichungen beim Netzbezug sind belegt.

## UX-06 · Jetzt oben und Kennzahlen kompakt halten

**Status:** Erledigt und lokal im Browser geprüft. [Nachweis](implementation-status.md#ux-06-und-ux-07--übersicht-und-energiebilanz).

Den kompakten Block „Jetzt“ oben in der Übersicht sichtbar halten. Er zeigt unabhängig vom ausgewählten Zeitraum aktuelle Werte. „Heute“ bleibt kompakt und der Live-Tagesgraph eingeklappt. Wiederholte Angaben zu Quelle, Abdeckung und den Voraussetzungen der Quoten in Info-Symbole an den Kachelüberschriften verschieben.

Ein klar gezeichnetes SVG-Infozeichen verwenden. Desktop-Hover und Tastaturfokus zeigen einen Tooltip, Touch zeigt dieselbe Erklärung beim Antippen. Die Kacheln behalten dabei ihre Höhe.

**Abnahme.** Auf Desktop und Mobilgeräten bleibt „Jetzt“ auch bei Auswahl eines historischen Tags oben erkennbar. Die Kacheln zeigen Wert und nötigen Hinweis zum Datenzustand. Detailangaben zu Quelle, Abdeckung und Berechnung sind per Maus, Tastatur und Touch zugänglich.

## UX-07 · Energiebilanzen und Datenstand eindeutig beschriften

**Status:** Erledigt und mit einer Sungrow-Fixture geprüft. [Nachweis](implementation-status.md#ux-06-und-ux-07--übersicht-und-energiebilanz).

Bei den historischen Energiebilanzen den ausgewählten Zeitraum nennen und drei verschiedene Aussagen klar trennen: vorhandene Tagesaggregate, letzter Zeitpunkt mit irgendeinem Minutenmesswert und gemeinsam bilanzierbare Intervalle. „Letzter abgedeckter Tag“ bezeichnet derzeit lediglich das Datum des letzten Intervallendes im gewählten Zeitraum. Die Diagramme für Speicherzustand und Netzleistung brauchen einen Leerzustand je Kanal. Ein ganztägiger Netzbezug in kWh liefert keinen Leistungsverlauf in kW.

**Abnahme.** Die Fixture vom 6. November mit sechs Tagesaggregaten und 96 Lastintervallen erklärt gleichzeitig 100 % Tagesabdeckung und 0 von 24 bilanzierbaren Stunden. Fehlende Kanäle und unbestätigte oder unplausible Energiepfade haben jeweils einen nachvollziehbaren Grund. Gemessene 0 kW oder 0 % werden als Werte dargestellt, während vollständig fehlende Netz- und SOC-Reihen einen verständlichen Hinweis statt einer leeren Grafik zeigen.

## LIVE-01 · Messalter und Aktualisierung bei sichtbarer Übersicht prüfen

**Status:** Erledigt und lokal geprüft. Zwischen den Quellmessungen 09:15 und 09:25 lagen zwei erfolgreiche Abrufe im Abstand von rund fünf Minuten. [Nachweis](implementation-status.md#live-01--sichtbarkeit-und-abrufzeiten).

Der sichtbare Browser liest den lokalen Cache alle 30 Sekunden. Der direkte Sungrow-Abruf hat im aktuellen Adapter eine Untergrenze von fünf Minuten. Im Hintergrund beträgt der Standard zehn Minuten. Die beobachtete Folge 09:15 → 09:25 reicht allein nicht aus, um einen Fehler im Sichtbarkeitssignal festzustellen. Sichtbarkeit, letzter Abruf, Zeitpunkt der Quellmessung und etwaige Wartezeiten getrennt prüfen und verständlich anzeigen. Einen minutenaktuellen Quellwert nur zusagen, wenn der Providervertrag oder eine dafür geeignete Quelle ihn tatsächlich ermöglicht.

**Abnahme.** Ein Test unterscheidet sichtbaren Tab, Hintergrund und fehlendes Sichtbarkeitssignal. Die sichtbare Übersicht übernimmt verfügbare Cache-Updates innerhalb einer Minute. Ein Vergleich auf der privaten Instanz erklärt den beobachteten Zehn-Minuten-Sprung anhand von Mess- und Abrufzeit, Sichtbarkeitsstatus oder Wartezeit. Die Anzeige macht erkennbar, wenn Sungrow seit dem letzten Abruf keinen neueren Messwert geliefert hat.

## LIVE-02 · Laufenden Tag mit gespeicherten Messwerten anzeigen

**Status:** Offen. Hintergrundabrufe und gespeicherte Stichproben sind an der privaten Anlage belegt. Der normale Tagesverlauf liest diese Stichproben derzeit nicht. [Befund und Abnahme](today-acceptance.md#laufender-tag).

**Abnahme.** Die Tagesübersicht zeigt beim Öffnen des heutigen Datums vorhandene Stichproben für Solarleistung (DC), Last, Netz, Batterie und Ladezustand. Zeit, Quelle und Lücken sind erkennbar. Der Dienst sammelt auch ohne offenen Browser weiter. Belastbare historische Intervalle ergänzen den Verlauf nach Tagesabschluss. Ein täglicher Handimport ist für die Anzeige der bereits erfassten Werte nicht erforderlich.

## UX-08 · Solarleistung und Einheiten unter „Jetzt“

**Status:** Offen. [Abnahmekriterien](today-acceptance.md#jetzt).

**Abnahme.** Die Kachel heißt „Solarleistung“. Die Quelle „PV-DC-Leistung am Wechselrichter“ und der Hinweis „AC-PV-Fluss nicht separat gemessen“ sind über das Info-Symbol erreichbar. Die Kacheln zeigen Leistung unter 10.000 W in Watt und ab 10.000 W in Kilowatt. Die Schwelle bezieht sich bei gerichteten Kanälen auf den Betrag. Ladezustand bleibt in Prozent.

## DATA-03 · Lesende Messpunktdiagnose für Kandidaten ergänzen

**Status:** Erledigt. Lesende Diagnose für Live und Gerätehistorie lokal bereitgestellt, Vertrag und Oberfläche mit Testdaten geprüft. Beide Datenquellen lieferten autorisierte Antworten vom echten Gerät. Die fachliche Kanalzuordnung bleibt bei DATA-01 und DATA-02. [Nachweis](implementation-status.md#data-03--lesende-messpunktdiagnose).

Für den ersten Live-Abgleich die Last `13119`, Netzrichtungen `13149` und `13121`, Batterierichtungen `13126` und `13150`, SOC `13141` und die für das Modell dokumentierten PV-Kandidaten zusammen abfragen. Quellzeitstempel, Einheit, Vorzeichen und Messort gegen iSolarCloud prüfen. Beim SOC einen von null verschiedenen Portalwert als Referenz verwenden. DC-Leistung `13003` separat ausweisen.

Die bestehende Live-Diagnose liest nur die aktive Zuordnung. Die historische Probe fragt eine feste Kandidatenliste ab und zeigt für Minutenwerte vor allem Anzahlen. Eine getrennte Betreiberdiagnose soll serverseitig festgelegte Kandidaten für eine autorisierte Anlage und deren Geräte abfragen können. Sie zeigt pro Kanal angefragte und gelieferte Messpunkte, Einheit, Messzeit, Abrufzeit, Anzahl gültiger Werte und den Grund bei fehlenden Daten, soweit der Provider diesen liefert. Bei fehlendem Anbietergrund die Ursache als ungeklärt ausweisen. Eine bestätigte Null als gültigen Wert behandeln.

**Abnahme.** PV- und SOC-Kandidaten sind lesend prüfbar, bevor sie aktiv zugeordnet werden. Die Diagnose ändert weder Profil noch Historie oder regulären Live-Cache. Ein Test deckt fehlenden Punkt, gültige Null, unbekannte Einheit und fehlende Freigabe ab. Der Probeablauf respektiert dieselben Anbietergrenzen wie der reguläre Abruf.
