# SolarInsight – fachliche Spezifikation und Implementierungsauftrag

Version 1.0 · 7. November 2026 · Sprache der Anwendung: Deutsch

## 1. Auftrag an den implementierenden Agenten

Implementiere aus diesem Dokument ein vollständiges, selbst hostbares Repository für die Analyse und Simulation einer Sungrow-PV-Anlage. Liefere lauffähigen Code, Datenbankmigrationen, reproduzierbare Demodaten, Tests, Docker Compose, CI und Betriebsdokumentation. Ein UI-Mockup oder eine Sammlung von Platzhaltern erfüllt den Auftrag nicht.

Das Backend muss in C#/.NET umgesetzt werden. Arbeite die unten genannten Etappen selbstständig ab, prüfe jede Etappe und dokumentiere Entscheidungen. Frage nur nach Informationen, ohne die eine externe Integration tatsächlich nicht möglich ist. Erfinde keine API-Endpunkte, Authentifizierungsverfahren, Gerätewerte oder angeblich gemessenen Daten. Fehlende Zugangsdaten dürfen die Entwicklung von Domänenmodell, Demo, UI und Simulation nicht blockieren.

Verwende diese Spezifikation als fachliche Quelle. MUSS bedeutet Bestandteil der vollständigen Version 1. SOLL bezeichnet eine Vorgabe, von der nur mit dokumentierter Begründung abgewichen werden darf. Spätere Erweiterungen sind ausdrücklich separat aufgeführt. Angaben mit „Entwurfsentscheidung“ sind für die Umsetzung festgelegte Defaults, keine Behauptungen über Sungrow.

Die einzige externe Fertigstellungsabhängigkeit ist der echte Sungrow-Zugang samt zugänglicher Dokumentation. Ist dieser nicht verfügbar, liefere den gesamten übrigen Umfang und einen klar als nicht live verifiziert gekennzeichneten Integrationsstand. Melde dann nicht die gesamte Integration als abgeschlossen. Keine Mock-Antworten im produktiven Provider.

## 2. Produktziel und Nutzer

SolarInsight beantwortet für private Anlagenbetreiber:

1. Wann und wie werden Erzeugung, Netzstrom und Batterie genutzt?
2. Wie viel Netzbezug hätte ein größerer Speicher vermieden?
3. Welches Potenzial bietet die zeitliche Verschiebung bestehenden Verbrauchs?
4. Wie viel zusätzlicher Verbrauch könnte aus bisherigen PV-Überschüssen gedeckt werden?
5. Welche Energie-, Autarkie- und Kostenunterschiede ergeben sich daraus?

Priorität: Verbrauch verstehen, Speicher bewerten, Verbrauchsverschiebung und Zusatzbedarf untersuchen. Amortisation ist ein kleiner optional nutzbarer Zusatz, keine zentrale Kaufberatung.

Jeder Nutzer betreibt seine eigene Instanz. Eine Instanz verwaltet einen lokalen Benutzer und eine aktive Anlage. Mehrere im Sungrow-Account sichtbare Anlagen dürfen im Einrichtungsdialog zur Auswahl stehen; gemeinsame Auswertungen mehrerer Anlagen sind nicht Teil von Version 1. Der Wechsel der aktiven Anlage darf historische Daten der vorherigen Anlage nicht vermischen oder löschen.

Das Repository enthält keine persönlichen Zugangsdaten. Die Anlage des Auftraggebers ist ein Referenzfall, kein hart codierter Sonderfall:

| Bestandteil | Vom Auftraggeber angegeben |
| --- | --- |
| PV-Nennleistung | 12 kWp |
| Wechselrichter | SH10RT-V112 |
| Batterie | SBR096 |
| Kommunikation | WiNet-S |
| Portal | https://www.isolarcloud.eu/ |

Nutzbare Kapazität, Ladegrenzen, Reserven und Betriebsstrategie sind noch nicht bestätigt. Nicht allein aus dem Modellnamen als gemessene Werte übernehmen.

## 3. Umfang und Grenzen

### 3.1 Vollständiger Umfang der Version 1

- Einrichtung, lokale Anmeldung, Anlagenkonfiguration und Provider-Verbindung.
- Hintergrundimport, historischer Import soweit verfügbar, persistente Daten und Importstatus.
- Übersicht, Zeitraumvergleich, Tagesdetails, Speicherereignisse und Datenqualitätsanzeige.
- Zeitabhängige Tarife und transparente Kostenberechnung.
- Ein interaktives Vergleichsszenario zum Ist-Zustand: Speicher ändern, Verbrauch verschieben, Zusatzbedarf ergänzen; kombinierbar.
- Kapazitäts-Nutzen-Kurve und erklärbare Simulationsergebnisse.
- Regelbasierte Auswertungstexte mit Verweisen auf zugrunde liegende Daten.
- Öffentlicher Demo-Betriebsmodus ohne echte Zugangsdaten.
- CSV-Export angezeigter Auswertungen und JSON-Export eines Simulationsergebnisses mit Annahmen.
- Optional ausfüllbarer Investitionspreis mit einfacher statischer Amortisationsrechnung.

### 3.2 Kein Bestandteil von Version 1

- Gerätesteuerung, Schreibbefehle an Wechselrichter oder Batterie.
- Multi-Tenant-SaaS, mehrere Benutzerrollen, öffentliche Registrierung.
- Dynamische Stromtarife, Stromhandel, Netzladen als Simulationsstrategie.
- Wetterprognosen, Fehlerdiagnosen aus Wetterdaten oder Verschattungsanalyse.
- Physikalische Gebäude-, Wärmepumpen-, Warmwasser- oder Fahrzeugmodelle.
- KI-Sprachmodell für Texte; regelbasierte Erklärungen genügen.
- Verwaltung beliebig vieler gespeicherter Szenarien.
- Eine Prognose zukünftiger Wetterjahre oder garantierter Einsparungen.

Ein lesender Empfehlungsschnittstellen-Endpunkt ist eine spätere Erweiterung, nicht Voraussetzung für die Abnahme. Seine fachlichen Regeln stehen in Abschnitt 16.

## 4. Architekturentscheidungen

Entwurfsentscheidung: ASP.NET Core Web API, React mit TypeScript und Vite, Apache ECharts, PostgreSQL und EF Core. Verwende zum Implementierungszeitpunkt unterstützte stabile Versionen; bevorzuge für .NET eine unterstützte LTS-Version. Prüfe offizielle Kompatibilität, pinne Versionen und dokumentiere sie. Keine Preview-Abhängigkeiten ohne zwingenden Grund.

- Modularer Monolith, keine Microservices-Infrastruktur.
- Reiner C#-Domänenkern für Kennzahlen, Tarife und Simulation; keine HTTP-/Datenbankabhängigkeit im Rechenkern.
- Provider-Adapter isoliert Sungrow-Spezifika. Separate Implementierung für Demo/Fixtures.
- Ingest als BackgroundService, entkoppelt von Browseranfragen. Derselbe Prozess ist für Version 1 ausreichend.
- UI liest ausschließlich die eigene API; niemals direkt Sungrow oder dessen Secrets.
- Server berechnet verbindliche Ergebnisse; das Frontend formatiert und visualisiert.
- Docker Compose startet App inklusive gebautem Frontend und PostgreSQL. Kein separat zu bedienender Frontend-Entwicklungsserver im Produktionsbetrieb.
- Datenbank und Schlüsselmaterial liegen in persistenten Volumes.
- UTC-Zeitstempel in der Datenhaltung; IANA-Zeitzone der Anlage für Kalenderlogik.

Vorgeschlagene Struktur: `src/Domain`, `src/Application`, `src/Infrastructure`, `src/Api`, `src/Web`, `tests/Unit`, `tests/Integration`, `tests/E2E`, `docs`. Gleichwertige Struktur ist erlaubt, wenn Abhängigkeiten und Verantwortlichkeiten erhalten bleiben.

## 5. Datenanbindung und Erstinstallation

### F-01: Einrichtungsablauf

1. Lokalen Zugang einrichten oder im Demo-Modus starten.
2. Region EU vorauswählen; Provider-Konfiguration anzeigen.
3. Den offiziell dokumentierten Authentifizierungsprozess ausführen. Eine zusätzliche Entwickleranwendung oder Freigabe kann erforderlich sein; dann konkrete Anleitung anzeigen.
4. Verfügbare Anlagen lesen und eine auswählen.
5. Gerätemetadaten und Datenfähigkeiten abrufen.
6. Erkannte Werte bestätigen; fehlende Parameter manuell ergänzen und als manuell markieren.
7. Anlagenzeitzone bestätigen, Tarife erfassen oder vorerst überspringen.
8. Import starten; Dashboard während des Imports bereits nutzbar halten.

Die lokale Anmeldung ist getrennt vom Sungrow-Login. Keine automatische Zusage „jeder iSolarCloud-Login funktioniert ohne weitere Schritte“.

### F-02: Verbindlicher API-Erkundungsschritt

Vor dem produktiven Adapter eine `docs/provider-capabilities.md` erstellen. Für jeden Punkt dokumentieren: Quelle/Datum, bestätigt oder unbekannt, Konsequenz und gegebenenfalls anonymisierte Beispielantwort.

| Zu prüfen | Ergebnis muss dokumentiert sein |
| --- | --- |
| Authentifizierung | Offizieller Flow, App-Freigabe, Scopes, Tokenlaufzeit und Erneuerung |
| Region | Tatsächliche EU-API-Basisadresse; Webportal-URL ist nicht automatisch API-Basisadresse |
| Daten | Anlagen, Geräte, Messpunkt-IDs, Einheiten, Vorzeichen und Messort |
| Historie | Kleinste Zeitauflösung, maximale Rückschau, Abfragefenster, Pagination |
| Zeitsemantik | Momentanwert oder Intervallmittel, Zeitstempel am Anfang/Ende, Zeitzone |
| Grenzen | Rate Limits, Paketbedingungen, erlaubte Abrufhäufigkeit |
| Qualität | Fehlwerte, Zählerresets, nachträgliche Korrekturen, Zeitversatz |

Keine private Web-API durch geratenes Reverse Engineering als offiziellen Adapter ausgeben. Ist die Dokumentation zugangsbeschränkt, ihren benötigten Inhalt vom Betreiber anfordern und unabhängig weiterarbeiten.

### F-03: Datenfähigkeiten statt Alles-oder-nichts

- Energieübersichten funktionieren mit verfügbaren Energieaggregaten.
- Tageskurven benötigen passende Zeitreihen.
- Speicherereignisse benötigen synchronisierte Netz- und SOC-Daten.
- Vollständige Simulation benötigt PV-Erzeugung und Hausverbrauch auf gemeinsamer Zeitachse sowie bestätigte Speicherparameter; SOC/Batterieflüsse werden zur Validierung benötigt.
- Fehlender Hausverbrauch darf nur aus einer dokumentierten, am Messort gültigen Energiebilanz abgeleitet werden. Kennzeichnung „abgeleitet“ und Bilanztest sind Pflicht.
- Fehlen Voraussetzungen, betreffende Funktion mit konkretem Grund deaktivieren; andere Funktionen bleiben nutzbar.

### F-04: Import und Synchronisation

- Historische Abfragen in zulässige Fenster aufteilen, Fortschritt und Checkpoint persistieren.
- Inkrementeller Import mit überlappendem Wiederholungsfenster für verspätete Werte.
- Idempotentes Upsert anhand Anlage, Messpunkt und Intervall; wiederholter Import erzeugt keine Mehrfachzählung.
- Korrekturen invalidieren betroffene Aggregate und Simulation-Caches.
- Neustart setzt Import fort; keine parallelen konkurrierenden Imports derselben Anlage.
- Begrenzte Retries mit exponentiellem Backoff/Jitter, Retry-After beachten, Authfehler nicht endlos wiederholen.
- Abrufintervalle aus dokumentierten Limits ableiten und konfigurierbar machen. Kein erfundenes festes API-Limit.
- Verbindungsfehler beeinträchtigen vorhandene historische Auswertungen nicht.
- Status: letzter Versuch, letzter Erfolg, jüngster Messzeitpunkt, Importbereich, Fehlerkategorie.

## 6. Datenmodell und Datenqualität

### D-01: Kernobjekte

| Objekt | Mindestinhalt |
| --- | --- |
| Plant | Interne/Provider-ID, Name, Region, Zeitzone, aktiv |
| PlantConfiguration | Gültigkeitszeitraum, PV-Leistung, Speicherparameter, Herkunft |
| Device | Provider-ID, Modell, Typ, Anlagenbezug |
| ProviderConnection | Status, geschützte Zugangskonfiguration, Fähigkeiten |
| Measurement | Anlage, Messpunkt, Start/Ende UTC, Wert, Einheit, Semantik, Qualitätsflags, Quellrevision |
| EnergyAggregate | Zeitraum, Messgröße, Energie, Abdeckung, Herkunft/Version |
| TariffPeriod | Tarifart, lokaler Beginn/Ende, EUR/kWh oder EUR/Monat |
| ImportJob | Fenster, Fortschritt, Checkpoint, Status, Fehler |
| AnalysisEvent | Typ, Start/Ende, Schwere/Hinweis, Belegwerte, Regelversion |
| SimulationRun | Parameter, Datenrevision, Rechenkernversion, Ergebnisse, Einschränkungen |

SimulationRun darf technisch kurzzeitig gespeichert werden; daraus entsteht keine UI zur Verwaltung vieler Szenarien. Cache mit begrenzter Aufbewahrung. Änderungen an Daten, Tarifen oder Parametern machen alte Ergebnisse kenntlich beziehungsweise invalidieren sie.

### D-02: Kanonische Größen

- Energie: kWh; Leistung: kW; Zeitdauer: Stunden bei Integration; SOC: 0 bis 1 intern, Prozent in UI.
- Import, Export, Batterieladung und Batterieentladung sind getrennte nichtnegative Größen.
- Rohdaten-Vorzeichen werden ausschließlich im Adapter normalisiert.
- Momentanleistung, Intervallmittel, Intervallenergie und kumulativer Zähler sind unterschiedliche Datentypen/Semantiken.
- Kumulative Zähler über Differenzen auswerten; Reset/Überlauf markieren, nicht als negative Energie buchen.
- Ein Intervall ist halboffen `[start, end)`. Keine Doppelzählung an Tages-/Tarifgrenzen.
- Rohantworten nur soweit erforderlich aufbewahren, ohne Tokens oder personenbezogene Felder; normalisierte Messwerte dauerhaft erhalten.

### D-03: Aggregation

Intervallenergie hat Vorrang vor Integration von Leistung. Bei Intervallmittel gilt E = P × Dauer. Bei Momentanwerten ist eine dokumentierte Näherung nötig (standardmäßig trapezförmig zwischen benachbarten gültigen Punkten); niemals über Datenlücken integrieren. Maximal erlaubter Abstand: 1,5 × erwartete Quellschrittweite, sonst Lücke. Herkunft und Näherung sichtbar halten.

Keine künstliche Verfeinerung von Tageswerten auf 5 Minuten. Unterschiedliche Quellintervalle auf die gröbste gemeinsame tatsächlich tragfähige Auflösung aggregieren. Originalauflösung erhalten. Provider-Tagesaggregate und selbst integrierte Zeitreihen getrennt speichern; nicht summieren oder stillschweigend gleichsetzen. Abweichungen anzeigen.

### D-04: Fehlende Daten

- Fehlende Werte sind null/unknown, niemals automatisch null kWh.
- Abdeckung pro Kennzahl aus der Dauer ihrer benötigten gültigen Intervalle berechnen.
- Für gekoppelte Analysen gilt die Schnittmenge der erforderlichen Messkanäle.
- Unvollständige Summen als „erfasste Energiemenge“ kennzeichnen, nicht auf den gesamten Zeitraum hochrechnen.
- Kennzahlen mit fehlender Voraussetzung: „nicht verfügbar“, nicht 0 %.
- Simulation nur auf zusammenhängenden gültigen Segmenten; kein kostenloses Übertragen eines unbekannten SOC über Lücken. Jedes Segment startet konservativ mit leerem nutzbarem Speicher, sofern kein gesicherter passender Anfangszustand vorliegt. Resets und betroffene Zeitanteile ausweisen.
- Entwurfsentscheidung: unter 95 % gemeinsamer Abdeckung oder gröber als 15 Minuten keine reguläre Potenzialbewertung; nur ausdrücklich aktivierbarer explorativer Lauf mit deutlichem Einschränkungshinweis. Keine Amortisationsanzeige daraus.

### D-05: Zeit und Saison

- Tag/Woche/Monat/Jahr anhand Anlagenzeitzone, Standard Europe/Berlin nach Bestätigung.
- ISO-Woche beginnt Montag. Tage dürfen bei Zeitumstellung 23 oder 25 Stunden haben.
- Quartale Jan–März, Apr–Juni, Juli–September, Oktober–Dezember.
- Frühjahr März–Mai; Sommer Juni–August; Herbst September–November; Winter Dezember–Februar.
- Winterbezeichnung „2025/26“ bedeutet Dezember 2025 bis Februar 2026.
- Laufende Perioden als unvollständig ausweisen. Vorjahres-/Vorperiodenvergleich standardmäßig gleicher verstrichener Kalenderabschnitt; Schaltjahrende auf gültigen Monatstag begrenzen. Genaue Vergleichsgrenzen anzeigen.
- Keine Vergleiche zwischen vollständiger und laufender Periode ohne sichtbare Kennzeichnung.

## 7. Kennzahlen und Tarife

### K-01: Energiekennzahlen

Erzeugung, Hausverbrauch, Netzbezug, Einspeisung, Batterieladung und Batterieentladung getrennt als Energiesummen ausweisen. SOC nicht summieren; zeitgewichtetes Mittel, Minimum, Maximum und Verlauf verwenden.

Autarkiegrad = (Hausverbrauch − Netzenergie für den Hausverbrauch) / Hausverbrauch. Bei Hausverbrauch 0: nicht definiert. Netzbezug für Batterieladung darf nicht blind als direkter Hausverbrauch interpretiert werden. Sind die Pfade nicht trennbar, vereinfachte Quote nur bei bestätigtem Betrieb ohne Netzladen; sonst nicht verfügbar oder ausdrücklich Näherung.

Eigenverbrauchsquote: In Version 1 standardmäßig „PV-Rückhaltequote“ = (PV-Erzeugung − PV-Einspeisung) / PV-Erzeugung; bei Erzeugung 0 nicht definiert. In der UI als „Eigenverbrauchsquote (inkl. Speicherverluste und Bestandsänderung)“ erklären. Das ist nicht gleich tatsächlich aus PV versorgter Hausverbrauch. Bei Batterieexport/Netzladen und fehlender Herkunftstrennung diese Quote nicht als exakt ausweisen. Über Periodengrenzen kann gespeicherte Energie zu scheinbar unplausiblen Quoten führen; keine stillschweigende Begrenzung auf 0–100 %, sondern Daten-/Herkunftshinweis.

Exakter PV-Anteil am Hausverbrauch wird nur angeboten, wenn die Energieherkunft zuverlässig verfolgbar ist. Ein Sankey ist optional; keine erfundenen Flüsse zur optischen Schließung einer Bilanz.

### K-02: Tarifverwaltung

- Bezugspreis und Einspeisevergütung besitzen voneinander unabhängige Gültigkeitsintervalle.
- Eingabe in ct/kWh, intern EUR/kWh mit Decimal; Währung zunächst EUR.
- Preise werden einheitlich als vom Nutzer tatsächlich gezahlte/erhaltene Preise eingegeben. Keine automatische Steuerlogik.
- Beginn inklusive, Ende exklusive, lokale Mitternacht; letzter Zeitraum darf offen enden.
- Überschneidungen derselben Tarifart verhindern. Lücken erlauben, aber sichtbar warnen.
- Optional monatlicher Grundpreis, tageweise nach tatsächlichen Kalendertagen des jeweiligen Monats anteilig.
- Änderungen in der Vergangenheit sind erlaubt und lösen Neuberechnung aus; Vorher/Nachher und betroffener Zeitraum im Speicherdialog anzeigen.

### K-03: Kosten

Netzbezugskosten = Summe Importenergie × gültiger Bezugspreis.
Einspeiseerlös = Summe Exportenergie × gültige Vergütung.
Netto-Stromkosten = Netzbezugskosten + Grundpreis − Einspeiseerlös.

Intervalle an Tarifgrenzen aufteilen. Nur wenn keine feineren Werte vorhanden sind, zeitproportional aufteilen und als Näherung markieren. Bei Tariflücken nur gedeckten Teilbetrag und dessen Abdeckung anzeigen; keinen vollständigen Gesamtbetrag vortäuschen.

Szenarioersparnis = Netto-Kosten Referenzsimulation − Netto-Kosten Szenariosimulation auf identischem Datenbereich. Grundpreis ist bei unverändertem Vertrag gleich und erzeugt keine Einsparung. Ersparnis kann negativ sein.

Bei Zusatzverbrauch zwei Perspektiven zeigen:

1. Veränderung der gesamten Stromkosten gegenüber dem Haushalt ohne Zusatzbedarf.
2. Kostenvorteil gegenüber demselben Zusatzbedarf vollständig zum Bezugspreis aus dem Netz, einschließlich entgangener Einspeiseerlöse.

## 8. Oberfläche und Nutzerabläufe

### U-01: Hauptnavigation

Vier Bereiche: Übersicht, Speicher & Potenzial, Datenstatus, Einstellungen. Tagesdetails sind aus Übersicht, Ereignis und Simulation erreichbar. Gemeinsame Zeitraumwahl bleibt beim Seitenwechsel erhalten und ist in URL-Parametern abbildbar.

### U-02: Übersicht

- Standard: aktueller Monat; Demo: letzter vollständiger Demomonat.
- Auswahl Tag, Woche, Monat, Quartal, Jahreszeit, Jahr, seit Datenbeginn, frei; Vor/Zurück.
- KPI-Karten: PV, Hausverbrauch, Netzbezug, Einspeisung, Autarkie, Eigenverbrauchsquote, Netto-Kosten, Datenabdeckung.
- Balkendiagramme in passender Auflösung (Tag im Monat; Monat im Jahr), mit Drill-down.
- Umschaltbare Perspektiven Energie, Kosten und Speicher; gleiche Zeitgrenzen.
- Vergleich mit Vorperiode/Vorjahr optional zuschaltbar, keine zusätzliche Szenarioverwaltung.
- Heatmap Tag × lokale Uhrzeit für Bezug/Export; DST-Stunden korrekt behandeln und im Tooltip differenzieren.
- Regelbasierte Erkenntnisse mit belegbaren Zahlen und Link zum relevanten Zeitraum.
- Lade-, Leer-, Fehler-, Import- und Teilabdeckungszustände müssen gestaltet sein.

### U-03: Tagesdetail

Synchronisierte Diagramme für PV, Hausverbrauch, Import/Export, Batterieladung/-entladung und SOC. Leistung und SOC auf getrennten sinnvoll beschrifteten Achsen/Diagrammen. Zoom, Tooltip, Reihen ein-/ausblenden und Ereignismarkierungen. Datenlücken als Lücken darstellen. Simulation kann als zweite Darstellung zugeschaltet werden; Messung und Modell sind eindeutig beschriftet.

### U-04: Szenarioeditor

Genau ein Vergleichsszenario, mit unabhängig aktivierbaren Modulen Speicher, Verschiebung, Zusatzverbrauch. Änderungen kombinierbar. Initial alle Module deaktiviert. „Zurücksetzen“ stellt unveränderten Zustand her. Keine Speicherung von fünf Varianten nötig; letzte Eingaben lokal merken. JSON-Export mit Parametern möglich.

Einfacher Modus zeigt Kapazität beziehungsweise tägliche Energiemengen und Zeitfenster. „Erweiterte Parameter“ zeigt Leistung, Verluste, Reserve und Initialisierung. Eingaben validieren, Einheiten erklären. Simulation per Button; laufenden Job abbrechen können. Keine schwere Jahressimulation bei jedem Pixel Sliderbewegung.

Resultate: gemessener Ist-Zustand, modellierter Referenzfall und Vergleichsszenario klar unterscheiden. Hauptvergleich Szenario minus modellierte Referenz; Messabweichung separat. Anzeigen: absolute Werte, Differenzen, prozentuale Änderung (bei Referenz 0 nicht definiert), Autarkieänderung in Prozentpunkten, täglicher Verlauf und Einschränkungen.

### U-05: Einstellungen

Verbindung; Anlage/Speicher mit Parameterherkunft und Gültigkeit; Tarife; Analyse-Schwellen; lokaler Zugang. Sensible Werte niemals nach dem Speichern im Klartext zurückliefern. Tarifbearbeitung und Verbindungsänderung fehlen im öffentlichen Demo-Modus.

### U-06: Bedienbarkeit

Desktop zuerst, responsive bis 375 px. Tastaturbedienbare Formulare, sichtbarer Fokus, verständliche Fehlermeldungen, Farbe nie alleiniger Bedeutungsträger. Diagrammdaten zusätzlich tabellarisch/als CSV. Deutsche Zahlen-/Datumsdarstellung, englische Codebezeichner. Keine Implementierungsdetails in normalen Nutzertexten.

## 9. Speicherereignisse und Erklärungstexte

### E-01: Regeln

Entwurfsdefaults: Leistungsschwelle 0,1 kW; SOC-Toleranz 2 Prozentpunkte zur konfigurierten Ober-/Untergrenze; Mindestdauer 15 Minuten. Alle anpassbar. Ereignisse nur auf gültigen zusammenhängenden Intervallen; Lücken beenden das Ereignis. Keine Aussage „15 Minuten lang“ allein aus zwei weit auseinanderliegenden Momentanwerten.

Ereignistypen:

- Einspeisung bei oberem Speicherladegrenzwert.
- Netzbezug bei unterem Speicherladegrenzwert.
- Einspeisung bei freier Speicherkapazität.
- Hoher Verbrauch bei geringer PV-Erzeugung: Schwelle konfigurierbar, standardmäßig als optionaler Hinweis deaktiviert, bis Betreiber sie setzt.

Jedes Ereignis besitzt Dauer, relevanten Energieumfang, Extremwerte und Datenabdeckung. „Leer“ bedeutet untere nutzbare Ladegrenze, nicht zwingend 0 % Hardware-SOC.

### E-02: Erklärung

Beobachtung, Modellannahme und Simulationsergebnis sprachlich unterscheiden. Beispiel: „In diesem Zeitraum wurden während … Stunden bei vollem Speicher … kWh eingespeist.“ Daraus nicht automatisch einen Speicherkauf empfehlen.

Einspeisung bei freier Kapazität beweist keine Ursache. Mögliche Ursachen nur als allgemeine Erläuterung, nicht als Diagnose. Keine Behauptung über Ladeleistung, Temperatur oder Betriebsmodus ohne entsprechende Daten.

Texte aus getesteten Templates mit Regelversion erzeugen. Jeder Zahlenwert muss auf denselben Filter und dieselbe Datenbasis wie die Diagramme zurückführbar sein.

## 10. Physikalischer Simulationskern

### S-01: Modellgrenze

Version 1 simuliert eine AC-seitige Energiebilanz mit PV und Hausverbrauch auf gemeinsamer Zeitachse. Ein realer Hybridwechselrichter kann DC-seitig arbeiten; Mapping und Verlustannahmen müssen diese Vereinfachung dokumentieren. DC-Erzeugung nicht ohne nachvollziehbare Umrechnung als AC-Energie behandeln.

Keine Netzladung, Batterieeinspeisung oder Tarifoptimierung im Referenzmodell. Erkannte reale Betriebsarten außerhalb dieser Annahmen führen zu eingeschränkter Validität, nicht zu unbemerkter Anpassung der Messdaten.

### S-02: Kapazität und Reserve

C bezeichnet betriebsseitige Kapazität vor der eingestellten Reserve in kWh. Mit SOC-Grenzen s_min und s_max gilt nutzbares Fenster C_use = C × (s_max − s_min). x ist Energie über der unteren Grenze, 0 ≤ x ≤ C_use. UI unterscheidet Gesamtkapazität und nutzbares Fenster, damit Reserve nicht doppelt abgezogen wird.

Falls nur eine nutzbare Kapazität bekannt ist, direkt als C_use eingeben und keine zusätzliche unbekannte Reserve ableiten. SOC-Mapping nur bei bestätigter Semantik. η_charge und η_discharge liegen in (0,1]; Produkt ist Rundlaufwirkungsgrad. Verlustannahmen müssen bestätigt werden; Demowerte dürfen nicht stillschweigend für eine reale Anlage gelten.

### S-03: Bilanz pro Intervall

Für Intervalllänge Δt in Stunden, PV-Energie G und Last L:

```text
direct = min(G, L)
surplus = G - direct
deficit = L - direct

charge_ac = min(surplus, P_charge_max * Δt, (C_use - x) / η_charge)
x_after_charge = x + charge_ac * η_charge

discharge_ac = min(deficit, P_discharge_max * Δt, x_after_charge * η_discharge)
x_next = x_after_charge - discharge_ac / η_discharge

export = surplus - charge_ac
import = deficit - discharge_ac
charge_loss = charge_ac * (1 - η_charge)
discharge_loss = discharge_ac * (1 / η_discharge - 1)
```

Auflösung bildet nur Intervallmittel ab; subintervallige Spitzen können unentdeckt bleiben. Das wird insbesondere bei gröberen Daten angezeigt. Keine gleichzeitige Ladung/Entladung innerhalb desselben modellierten Intervalls. Clamping nur für numerische Rundungsfehler im dokumentierten Epsilon, nicht zur Reparatur fachlicher Fehler.

Invariante: G + import = L + export + (x_next − x) + charge_loss + discharge_loss. Für konstantes System über den Lauf prüfen; Kapazitätswechsel separat behandeln.

### S-04: Anfang, Ende und Referenz

- Modellreferenz verwendet bestätigte reale Anlagenparameter, Szenario nur ausdrücklich veränderte Parameter.
- Bei bekannten Anfangsdaten denselben absoluten gespeicherten Energiebestand in Referenz und Szenario verwenden, begrenzt durch jeweilige Kapazität; zusätzliche Kapazität startet leer. Begrenzungen offen ausweisen.
- Bei unbekanntem Anfang beide mit x = 0 starten. Optional verfügbare sieben Vortage als identischen Warm-up rechnen und aus Ergebniszeitraum ausschließen; verwendete Methode anzeigen.
- Endbestand und Differenz des Endbestands ausgeben. Restenergie nicht als bereits realisierte Stromersparnis gutschreiben.
- Änderungen realer Konfiguration innerhalb einer Periode berücksichtigen oder Zeitraum an Konfigurationsgrenze aufteilen; nie die aktuelle Batteriegröße stillschweigend auf die gesamte Anlagenhistorie anwenden.
- Unverändertes Szenario muss exakt der modellierten Referenz entsprechen.

### S-05: Validierung gegen Messdaten

Referenz gegen gemessenen Netzbezug, Export und SOC prüfen. Mindestens absolute Energieabweichungen, normierter Energiefehler und SOC-MAE in Prozentpunkten ausgeben. Relative Fehler bei nahezu null Referenz mit sinnvoller Mindestbasis versehen und zusätzlich absolute Werte zeigen.

Entwurfsgrenzen für „ausreichend angenähert“: gemeinsame Abdeckung ≥95 %, mindestens 7 vollständige Tage, Fehler (|Import_sim−Import_real| + |Export_sim−Export_real|) / max(Verbrauch_real + PV_real, 1 kWh) ≤5 %, SOC-MAE ≤10 Prozentpunkte, soweit SOC vorhanden. Grenzen sind Produktdefaults, keine wissenschaftliche Zertifizierung. Einzelne Tagesfehler zusätzlich zeigen, damit Jahressummen gegenläufige Fehler nicht verdecken.

Bei nicht erfüllten Kriterien Ergebnisse ausdrücklich explorativ kennzeichnen. Ist SOC nicht verfügbar, Speicherzustand als nicht validiert ausweisen; kein Status „vollständig validiert“. Nicht automatisch Parameter so fitten, dass Messfehler verschwinden.

### S-06: Kapazitäts-Nutzen-Kurve

Rechne 0 bis zur vom Nutzer ausgewählten Maximal-Kapazität in maximal 21 Stützpunkten, inklusive Ist- und Szenariowert. Ladeleistung, Verluste und Verbrauch bleiben konstant. Zeige vermiedenen Netzbezug und Kostenänderung relativ zur Ist-Kapazität sowie Zusatznutzen pro weiterem kWh. Keine unterstellte proportionale Zunahme der Ladeleistung mit Kapazität.

## 11. Bestehenden Verbrauch verschieben

### V-01: Bedeutung und Grenzen

Ein allgemeines theoretisches Potenzial, kein erkanntes Geräteprofil. Jahres-/Tagesverbrauch bleibt unverändert. Es wird keine Waschmaschine im Lastgang behauptet. UI muss erläutern: „Obergrenze unter den gewählten Annahmen; tatsächliche Geräteverfügbarkeit ist nicht bekannt.“

Eingaben: maximal verschiebbare kWh je lokalem Tag; maximaler verschiebbarer Anteil je Ursprungsintervall (Default 20 %, explizite Annahme); zulässiges Ursprungszeitfenster (Default ganzer Tag); Zielzeitfenster (Default 09–17 Uhr); maximale zusätzliche Leistung im Ziel (Default 1 kW, Annahme). Nicht über Mitternacht verschieben in Version 1. Parameter dürfen auf 0 gesetzt werden, dann keine Änderung.

### V-02: Deterministischer Algorithmus

Verwende einen diskretisierten, nachvollziehbaren Ansatz statt eines undokumentierten Optimierers:

1. Ursprungsbudget pro Intervall = gemessene Last × eingestellter Anteil, begrenzt durch das Tagesbudget; niemals negative Last erzeugen.
2. Kandidaten für Herkunft nach Netzbezug der aktuellen Simulation absteigend, für Ziel nach Export absteigend sortieren; Gleichstände chronologisch lösen.
3. Energie in Schritten von höchstens 0,05 kWh verschieben, begrenzt durch verbleibendes Quellbudget, Ziel-Leistung × Intervalllänge und Tagesbudget. Einmal verschobene Energie nicht erneut als Ursprungsbudget verwenden.
4. Nach einer Änderung den betroffenen Simulationsverlauf einschließlich Speicherfolge neu berechnen. Nur Verschiebungen akzeptieren, die den Netzbezug über den ausgewählten Lauf reduzieren; bei Gleichstand die kleinere Netto-Kostensumme, dann früheren Zielzeitpunkt bevorzugen. Keine als Vorteil ausgewiesene Verschiebung allein aufgrund künstlich gefüllten Endspeichers.
5. Abbrechen, wenn kein verbessernder Kandidat verbleibt oder Budget ausgeschöpft ist. Ein Rechenlimit mit sichtbarer Kennzeichnung „Suche begrenzt“ ist erlaubt; keine Behauptung eines globalen Optimums.

Der Agent darf einen mathematisch gleichwertigen oder besseren deterministischen Optimierer verwenden, wenn Ziel, Nebenbedingungen, Konvergenz und Tests dokumentiert sind. Ergebnisse als heuristisches Potenzial, nicht garantierte maximale Einsparung bezeichnen. Vollständige historische Kenntnis ausdrücklich als Rückblick kennzeichnen.

Budget ist eine Obergrenze, keine Pflicht zur Verschiebung. Quellen/Ziele und tatsächlich verschobene Energie je Tag exportierbar machen. Kombination mit Speicher gemeinsam rechnen; Einzelvorteile nicht addieren.

## 12. Überschuss und zusätzlicher Verbrauch

### Z-01: Geräteunabhängige Überschussanalyse

Zeige gemessene Exportenergie nach Tag, Uhrzeit, Monat und Jahreszeit sowie zusammenhängende Exportepisoden oberhalb einstellbarer Leistungsschwelle. Für Aufnahmegrenze P gilt theoretisch direkt auffangbare Energie Σ min(Exportleistung, P) × Δt innerhalb des erlaubten Zeitfensters. Das unterstellt einen flexibel regelbaren Verbraucher ohne Mindestleistung; diese Annahme nennen.

Export ist Überschuss nach dem realen bisherigen Speicherverhalten. Nicht zusätzlich die gesamte PV-Erzeugung als freien Überschuss anbieten.

### Z-02: Zusatzbedarfsszenario

Allgemeines Profil mit kWh pro ausgewähltem Wochentag, Verfügbarkeitsfenster innerhalb eines Tages, maximaler Aufnahmeleistung; optionale Monatsfaktoren für saisonalen Bedarf. Keine Gerätemodelle nötig. Zwei explizite Betriebsweisen:

- **Nur Überschuss nutzen:** flexibel modulierbare Last bis zum Tagesziel; kein zusätzlicher Netzbezug und keine zusätzliche Entladung des bestehenden Speichers für diese Last. Nicht erreichter Bedarf wird als ungedeckt angezeigt.
- **Bedarf vollständig decken:** Tagesziel innerhalb des Fensters erfüllen; PV-Anteil bevorzugen, Rest gegebenenfalls aus Speicher/Netz. Ist Ziel > Leistung × verfügbare Dauer, Eingabe zurückweisen statt Bedarf verschwinden lassen.

Historische Allokation bei vollständiger Deckung: nach Berechnung des Haushalts inklusive gewählter Speicher-/Verschiebungsänderungen erst verbleibende Exportintervalle, danach verbleibende Leistungsplätze chronologisch verwenden; anschließend gesamten Lastgang erneut simulieren. Tagesziel, Leistung und Zeitfenster bleiben harte Grenzen. Als transparente Heuristik mit historischem Wissen kennzeichnen.

In Version 1 hat der bisherige Haushalt einschließlich gewählter Speichernutzung Vorrang vor dem zusätzlichen Verbraucher. Diese Priorität in der UI nennen. Für „nur Überschuss“ gilt Zusatzlast ≤ Export des identischen Haushalts-Szenarios ohne Zusatzlast; somit werden weder dessen Batterie noch seine Versorgung beeinträchtigt.

### Z-03: Energieherkunft und Vergleich

Für vollständige Deckung im Simulationskern Lastanteile getrennt führen: bisheriger Haushalt und Zusatzlast. Direkte PV-Zuteilung zuerst an Haushalt; restliche PV an Zusatzlast; Batterieentladung zuerst an verbleibenden Haushaltsbedarf, danach Zusatzbedarf. Danach erst verbleibenden PV-Überschuss laden. Die Energiebilanz bleibt identisch zum Gesamtlastmodell. Herkunft der Anfangsbatterie separat führen; sie darf nicht als nachweislich neue PV-Energie ausgegeben werden.

Zeige: angeforderter und gedeckter Zusatzbedarf, direkte PV, Batterieanteil mit Herkunftshinweis, Netzanteil, ungedeckter Bedarf, zusätzlicher Gesamt-Netzbezug und verbleibende Einspeisung. Direkter Netzanteil der Zusatzlast und Veränderung des gesamten Netzbezugs sind verschieden und beide entsprechend benennen: zusätzliche Last kann indirekt spätere Haushaltsversorgung aus der Batterie verdrängen.

Kostenreferenz „alles aus dem Netz“: Kosten desselben Haushalts ohne Zusatzlast plus Summe der tatsächlich bedienten Zusatzlast × jeweils gültigem Bezugspreis. Nur identisch gedeckte Bedarfe vergleichen; ungedeckten Bedarf nie als Ersparnis behandeln.

## 13. Amortisation als Zusatz

Optionaler Preis ausschließlich für die betrachtete Speichererweiterung. Keine voreingestellten Marktpreise. Statische Amortisation = Investition / positive jährliche modellierte Netto-Ersparnis.

Nur für einen vollständigen zusammenhängenden Zwölfmonatszeitraum mit vollständigen Tarifen und ausreichender Validierung anbieten. Keine automatische Hochrechnung eines Sommermonats. Bei Ersparnis ≤0: „Unter diesen Annahmen keine statische Amortisation“. Hinweis: ohne Finanzierung, Alterung, Ersatzinvestition und zukünftige Tarifänderungen. Keine Renditebehauptung und keine Garantie.

## 14. Demo, Sicherheit und Betrieb

### B-01: Demo

- Reproduzierbare synthetische Zeitreihen mit festem Seed, mindestens 24 vollständige Monate in 5-Minuten-Auflösung, einschließlich eines Winters über den Jahreswechsel und Zeitumstellungen.
- Referenz-Demodatensatz physikalisch bilanziert aus demselben dokumentierten Grundmodell, aber mit separat testbaren Generatorregeln. SOC, Verlust, Import und Export konsistent.
- Enthält Sommerüberschuss, Winterbezug, bewölkte Tage und Abendlast. Separate Qualitätsfixture enthält Lücken, Duplikate und Zählerreset; sauberen Demo-Hauptdatensatz nicht durch Testfehler verfälschen.
- Sichtbarer Hinweis „Synthetische Demodaten“. Keine echte Adresse, Anlagenkennung oder Zugangsdaten.
- Besucher dürfen Szenarien rechnen, aber keine globale Konfiguration ändern. Einstellungen je Browser/Request isoliert; kein Besucher überschreibt andere.
- Demo nutzt dieselben Analyse-/Simulationsdienste und UI wie produktiver Betrieb.
- Begrenzte parallele Rechenjobs, Requestlimits und Laufzeitgrenzen schützen öffentlichen Betrieb.

### B-02: Sicherheit

- Produktiv standardmäßig lokale Anmeldung; initiales Passwort über sicheren Setup-Prozess/Secret, keine Standardcredentials.
- Passwort sicher hashen; HTTP-only/SameSite-Sessioncookie, CSRF-Schutz bei Cookie-Authentifizierung, restriktive CORS-Policy, Login-Rate-Limit.
- Secrets nur serverseitig, verschlüsselt persistent; Schlüssel getrennt konfiguriert und für Restore dokumentiert. Keine Secrets in Logs, Exceptions, URL-Parametern, Browserstorage oder Exporten.
- Falls OAuth verwendet wird: offizieller Flow, State-Prüfung und PKCE soweit unterstützt; Redirect-URI validieren.
- Öffentlicher Demo-Modus deaktiviert Provider-Konfigurations- und echten Importzugriff auch serverseitig.
- TLS via dokumentiertem Reverse Proxy. Standard-Portbindung lokal; Veröffentlichung ist bewusste Betreiberentscheidung.
- Strikte Parametergrenzen für Zeiträume, Punktanzahl und Simulationsgrößen.

### B-03: Betrieb

- README mit Start über Docker Compose, Einrichtung, Demo, Updates, Backup/Restore und Fehlerdiagnose.
- Health/Readiness, strukturierte Logs ohne Geheimnisse, sichtbarer Importzustand.
- Versionierte Migrationen, dokumentiertes Backup vor Updates.
- Compose-Neustart erhält Daten, Einstellungen und Schlüssel; Restore auf leerer Instanz testen.
- Keine automatische Löschung von Rohmesswerten; Cache und nicht benötigte technische Logs begrenzt halten.

## 15. Eigene API und technische Qualitätsziele

Die folgenden Routen sind Designvorgaben für die eigene Anwendung, ausdrücklich keine Sungrow-Endpunkte:

| Route | Zweck |
| --- | --- |
| GET /api/v1/plant | Aktive Anlage und Fähigkeiten |
| GET /api/v1/overview | Zeitraum-KPIs, Qualität, Vergleich |
| GET /api/v1/timeseries | Zeitreihen mit angemessener Aggregation |
| GET /api/v1/events | Ereignisse mit Filter/Pagination |
| GET /api/v1/data-status | Importstatus und Lücken |
| GET/PUT /api/v1/settings/plant | Anlagenparameter |
| GET/POST/PUT/DELETE /api/v1/tariffs | Tarifverwaltung, einzelne Ressourcen mit ID |
| POST /api/v1/import-jobs | Historischen/incrementellen Import starten |
| POST /api/v1/simulations | Job starten, Job-ID zurückgeben |
| GET /api/v1/simulations/{id} | Fortschritt/Ergebnis |
| DELETE /api/v1/simulations/{id} | Lauf abbrechen bzw. eigenes Ergebnis entfernen |
| GET /api/v1/exports/... | Validierte CSV-/JSON-Exporte |

Auth-/Provider-Verbindungsrouten gemäß tatsächlich verifiziertem Flow ergänzen. OpenAPI-Dokumentation liefern. Einheitliche ProblemDetails-Fehler, Cancellation, Requestvalidierung und begrenzte Pagination. Responses enthalten Zeitraum, Zeitzone, Auflösung, Datenabdeckung, Revision und Warnungen. Kein Erfolg mit leeren Zufallswerten bei Providerfehlern.

Performanceziele auf dokumentierter Referenzhardware (mindestens 2 CPU-Kerne, 4 GB RAM): Monatsübersicht mit warmem Cache p95 unter 2 Sekunden, Tagesdetail unter 2 Sekunden; einjährige reine Speichersimulation unter 5 Sekunden. Kombinierte Optimierung als Hintergrundjob mit Fortschritt, Ziel unter 60 Sekunden; Überschreitung dokumentieren und optimieren oder explizit begrenzen, keine blockierte UI. Langzeitansichten aggregieren serverseitig, keine Millionen Punkte an den Browser senden.

## 16. Spätere lesende Empfehlungsschnittstelle

Nicht zur Abnahme von Version 1 erforderlich. Architektur soll sie ermöglichen, ohne die Analyse umzubauen.

Eine Empfehlung benötigt Status `increase`, `decrease`, `neutral` oder `unknown`, gemessenen Überschuss, zulässige Datenalterung, Messzeitpunkt, Erstellzeitpunkt, Ablaufzeit, Grundcodes und Qualität. `unknown` bei veralteten/fehlenden Daten; `neutral` bedeutet gültige Daten ohne Handlungsbedarf. Mindestdauer/Hysterese verhindern Flattern. Keine Steuersignale oder zugesagte Echtzeit bei Cloud-Verzögerung. Eigener lesender API-Schlüssel wäre erforderlich; keine ungeschützten privaten Echtzeitdaten.

## 17. Verbindliche Tests und Abnahmekriterien

Die fachlichen Tests müssen unabhängig erwartete Ergebnisse besitzen, nicht nur den Produktionsalgorithmus duplizieren. xUnit für Domäne/Backend, PostgreSQL-Integrationstests und Browser-E2E für Kernabläufe. Kein echter Sungrow-Zugang in CI erforderlich.

| ID | Test / Abnahme |
| --- | --- |
| A01 | Frischer Clone startet nach dokumentierter Konfiguration mit Docker Compose; Demo ohne externe Credentials nutzbar. |
| A02 | Wiederholter Import derselben Fixture verändert Energiesummen nicht; korrigierte Messwerte ersetzen alte Werte und invalidieren Ergebnisse. |
| A03 | Stromausfall/Neustart im Backfill setzt am Checkpoint fort; simulierte 429/401/Timeouts erzeugen begrenzte korrekte Fehlerbehandlung. |
| A04 | 4 kW Intervallmittel über 15 Minuten ergeben 1 kWh; 4 kW Momentanwert wird nicht pauschal als Stundenenergie behandelt. |
| A05 | 10 kWh Bezug zu 0,30 EUR plus 5 kWh zu 0,40 EUR kosten 5 EUR; 4 kWh Export zu 0,08 EUR ergeben 0,32 EUR; netto ohne Grundpreis 4,68 EUR. |
| A06 | Tarifüberlappung wird abgelehnt; Tariflücke führt zu unvollständigen Kosten; Monatswechsel und Grundpreisanteil getestet. |
| A07 | Europe/Berlin-Tage am 29.03.2026 und 25.10.2026 besitzen 23 bzw. 25 Stunden; keine doppelte oder verlorene UTC-Energie. Winter 2025/26 umfasst Dez–Feb. |
| A08 | Fehlende Nachtintervalle erzeugen keine scheinbare Null-Last; Abdeckung und deaktivierte Funktionen stimmen. |
| A09 | Ohne Batterie, G=5 und L=2 kWh: Import 0, Export 3, Direktverbrauch 2. |
| A10 | Eine Stunde, G=6, L=1, C_use=4, x=0, P_charge=2, η_charge=0,9: charge_ac=2, x_next=1,8, Export=3, Verlust=0,2 kWh. |
| A11 | Anschließend G=0, L=3, P_discharge≥3, η_discharge=0,9: Entladung AC=1,62, Import=1,38, x_next=0, Verlust=0,18 kWh. |
| A12 | Invariante der Energiebilanz, Kapazitäts- und Leistungsgrenzen für deterministische Zufallstests; Nullkapazität und η=1 abgedeckt. |
| A13 | Gleiche Szenarioparameter ergeben Delta 0; zusätzliche Kapazität startet nicht kostenlos voll; Endbestand wird nicht als realisierte Ersparnis gezählt. |
| A14 | Tagesenergie bleibt bei Verschiebung exakt erhalten; Last nie negativ; Quelle/Leistung/Fenster/Tagesbudget eingehalten; nicht verschiebbare Energie bleibt liegen. |
| A15 | Beispiel ohne Batterie: mittags G=3,L=1; abends G=0,L=2 kWh. Bei erlaubter Verschiebung von 1 kWh sinken Export und Import jeweils von 2 auf 1 kWh. |
| A16 | Überschuss-only-Zusatzbedarf überschreitet weder Bedarf noch gemessenen/modellierten verfügbaren Überschuss; Haushaltsnetzbezug und Batterie werden nicht verschlechtert. |
| A17 | Vollständige Zusatzbedarfsdeckung erreicht Tagesziel oder lehnt unmögliche Eingabe ab; nicht gedeckter Bedarf wird niemals als Ersparnis gebucht. |
| A18 | Kombination Speicher/Verschiebung/Zusatzlast erfüllt Bilanz; Vorteile werden nicht aus Einzelläufen addiert. |
| A19 | Weniger als 12 vollständige Monate erzeugen keine statische Amortisationszahl; Null-/negative Ersparnis wird korrekt behandelt. |
| A20 | Jeder Ereignis-/Erklärungstext stimmt mit gefilterten Daten überein und öffnet den belegenden Zeitraum. |
| A21 | Öffentliche Demo erlaubt keine Tarif-/Secretänderung und keine echten Imports; zwei Besucher beeinflussen sich nicht. |
| A22 | Nicht angemeldete Produktivnutzer erhalten keine Anlagendaten; Secrets erscheinen weder in HTTP-Antworten noch Logs/Exporten. |
| A23 | E2E: Zeitraum wechseln → Tag öffnen → zurück → Szenario größerer Speicher → Kombination → CSV/JSON exportieren. |
| A24 | Backup und Restore erhalten Anlagen-/Tarifdaten; Providerzugang ist mit dokumentiertem Schlüssel-Restore wieder nutzbar. |
| A25 | Live-Integration separat: echte Anlage abrufen, bestätigte Messfelder normalisieren, mindestens einen historischen Tag und ein Aggregat plausibilisieren; tatsächliche Limits dokumentieren. Ohne Zugang explizit offen. |

Zusätzliche Randfälle: Nenner 0, Schaltjahr, negativer/ungültiger Preis als explizite Validierungsentscheidung, ungültige SOC-Grenzen, Einheitenfehler, unsortierte Daten, doppelte Zeitstempel, fehlender Anfangs-SOC, Konfigurationsänderung, 0-kWh-Zusatzbedarf, leeres Zeitfenster, Abbruch eines Rechenjobs.

## 18. Umsetzungsetappen und Definition of Done

### Etappe 1 – Fundament und Datenvertrag

Repository, Versionen, Architekturentscheidungen, CI, Compose, Domänenobjekte, Provider-Capability-Bericht, normalisierte Fixtures und sichere Konfiguration. API-Discovery starten; fehlende Zugänge als externe Abhängigkeit dokumentieren.

### Etappe 2 – Vertikaler Demo-Pfad

Demogenerator → Persistenz → eigene API → Monatsübersicht → Tagesdetail. Früher End-to-End-Test. Die Demo verwendet produktive Rechenpfade.

### Etappe 3 – Import, Qualität, Tarife

Produktiver Adapter soweit dokumentiert, Backfill, Wiederaufnahme, Datenqualität, Einstellungen und Tarifberechnung. Kein UI-Fake für nicht implementierte Funktionen.

### Etappe 4 – Speicheranalyse und Simulation

Ereignisse, Templates, Referenzmodell, Validierung, Kapazitätskurve, Szenarioeditor und Tagesvergleich. Numerische Referenztests müssen vor Freigabe bestehen.

### Etappe 5 – Verbrauchspotenziale

Verschiebung, Überschussanalyse, Zusatzbedarf, kombinierte Simulation und transparente Kostengegenüberstellung. Parameter-/Ergebnisexport und Amortisationszusatz.

### Etappe 6 – Härtung und Übergabe

Authentifizierung, Demo-Isolation, E2E, Performance, Backup/Restore, Dokumentation und Live-Check soweit Zugang vorhanden. README mit Screenshots aus synthetischen Daten, Architekturübersicht und Grenzen.

Definition of Done:

- Alle MUSS-Anforderungen sind implementiert oder ein konkreter externer API-Blocker ist getrennt benannt.
- Build, Lint/Typecheck, Unit-, Integrations- und E2E-Tests bestehen; tatsächlich ausgeführte Befehle dokumentiert.
- Keine Kernfunktion hinter TODO, statischer Erfolgsantwort oder unmarkierten Mockdaten.
- Startanleitung wurde auf frischem Zustand geprüft.
- Fachliche Formeln, Qualitätsgrenzen, Simulationsannahmen und Provider-Mapping sind dokumentiert.
- Projekt enthält `.env.example` ohne echte Geheimnisse, Migrationen, Compose, Lockfiles, CI und Betriebsanleitung.
- Gewählte externe Bibliotheken und ihre Lizenzen dokumentieren. Eine Repository-Lizenz darf der Agent nicht als Entscheidung des Eigentümers erfinden; Entscheidung als separat offen markieren.
- Abschlussbericht nennt implementierte Funktionen, Testergebnisse, bekannte Grenzen und exakt noch erforderliche Betreiberaktionen.

## 19. Evidenz, offene Punkte und Quellen

### Bestätigt durch den Auftraggeber

Single-User-Self-Hosting; öffentlich zugängliches Repository als Ziel; .NET-Backend; öffentlicher synthetischer Demo-Modus; keine Gerätesteuerung; Monatsstart mit Saison-/Jahresansichten; ein kombinierbares Vergleichsszenario; regelbasierte Texte; feste Tarife mit Gültigkeit; oben aufgeführte Anlage.

### Entwurfsentscheidungen dieses Dokuments

React/ECharts/PostgreSQL, Docker Compose, lokale Authentifizierung, API-Routen, numerische Qualitätsgrenzen, Ereignisschwellen, Heuristik zur Verbrauchsverschiebung, Zusatzlast-Priorität und Rechenleistungsziele. Diese sind ausführbare Vorgaben, keine aus Messungen abgeleiteten Tatsachen.

### Vor Live-Abnahme offen

Tatsächlicher API-Flow und Freigabe, EU-Basisadresse, Messpunktdefinitionen, Historientiefe, Auflösung und Limits; reales SOC-Mapping, nutzbare Kapazität/Reserve, Lade-/Entladegrenzen und bestehende Anlagenstrategie; vom Betreiber einzutragende Tarife. Fehlende Fakten in der UI nicht mit Demoannahmen überschreiben.

### Primärquellen zur Integration

- Sungrow Developer Portal: https://developer-api.isolarcloud.com/
- Sungrow Europa, „Sungrow´s API Developer Portal“, 3. Juni 2025: https://academy.sungrow-emea.com/de/articles/331036-sungrow-s-api-developer-portal
- EU-Anlagenportal des Auftraggebers: https://www.isolarcloud.eu/

Die öffentlich zugängliche Sungrow-Anleitung beschreibt den Zugang zum Entwicklerportal und die Erstellung/Freigabe einer Anwendung. Sie ersetzt nicht die aktuelle technische API-Dokumentation des konkreten Accounts. Die genaue API wurde für diese Spezifikation nicht authentifiziert geprüft. Authentifizierungsdetails und Messpunktverträge sind daher bewusst nicht erfunden. Berechnungen und Produktregeln dieses Dokuments sind eigene explizite Modellvorgaben.

## 20. Kurzer Startauftrag zum Kopieren

> Implementiere das Projekt vollständig gemäß `SolarInsight_Spezifikation.md`. Beginne mit Repositorystruktur, Architekturentscheidungen und einer Anforderungs-/Testcheckliste. Arbeite die sechs Etappen selbstständig ab. Verwende C#/.NET im Backend und die festgelegten Frontend-/Datenbankdefaults. Erfinde keine Sungrow-API-Verträge: prüfe die offiziellen Dokumente und kennzeichne externe Zugangsblocker. Entwickle währenddessen sämtliche Funktionen mit reproduzierbaren synthetischen Daten und demselben Domänenkern. Liefere eine mit Docker Compose startbare Anwendung, die beschriebenen Simulationen, Tests, sichere Konfiguration und Betriebsdokumentation. Melde eine Funktion erst als fertig, wenn ihr Abnahmekriterium tatsächlich geprüft ist. Unterscheide am Ende fertigen Demo-/Anwendungsumfang und live verifizierte Provider-Integration.
