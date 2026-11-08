# Geplante Verbesserungen für Solar Insight

Hier stehen offene Anforderungen für kommende Änderungen. Der [Umsetzungsstand](implementation-status.md) verlinkt die bisherigen Anforderungen und dokumentiert ihre Abnahme.

## UX-01 · Oberfläche nach den Überarbeitungen prüfen

**Status:** Offen.

Die gesamte Oberfläche auf eine konsistente Gestaltung prüfen und insbesondere Einstellungen, Einrichtung und Datenstatus vereinfachen. Wichtige Informationen, Statusmeldungen und häufige Aktionen klar anordnen. Technische Details bei Bedarf aufklappbar machen. Vorhandene Informationen müssen auffindbar bleiben.

**Abnahme.** Die Ansichten wirken auf Desktop und Mobilgeräten zusammengehörig. Verbindungsstatus, letzter erfolgreicher Datenabruf und anstehende Nutzeraktionen sind schnell erkennbar. Lade-, Leer- und Fehlerzustände sind verständlich.

## UX-02 · Setup mit Standardwerten und geschütztem Sungrow-Mapping

**Status:** Offen.

Standardwerte automatisch anwenden und im Setup vorausfüllen, wo eine Nutzereingabe vorgesehen ist. Das Setup fragt Angaben ab, die nur der Betreiber liefern kann, etwa die Sungrow-Autorisierung, die Anlagenwahl oder individuelle Tarife. Technische Vorgaben aus der Anwendung und der Sungrow-API, etwa Messpunkt-IDs, Einheiten, Vorzeichen und Abrufparameter, zentral im Backend pflegen. Bestätigte Zuordnungen automatisch anwenden. Ist eine Zuordnung für die Anlage unbekannt, den betroffenen Kanal und den Grund verständlich anzeigen.

Ports und andere Betriebsparameter können als schreibgeschützter Diagnosehinweis erscheinen, sofern sie bei der Fehlersuche helfen. Anpassungen für Sonderinstallationen gehören in die dokumentierte Betreiberkonfiguration.

**Abnahme.** Eine Standardinstallation lässt sich mit den notwendigen Betreiberangaben einrichten. Die Oberfläche bietet keine Eingabefelder für technische Vorgaben. Auch die API schützt festgelegte Zuordnungen vor Änderungen aus dem normalen Setup. Fehlende oder unbestätigte Messpunkte bleiben als solche sichtbar.

## UX-03 · Datenimport im Normalbetrieb automatisieren

**Status:** Offen. Der automatische Abgleich nach dem ersten historischen Import ist bereits umgesetzt.

Nach erfolgreicher Autorisierung und bestätigter Anlagenzuordnung den initialen Import mit sinnvollen Vorgaben einplanen. Anschließend den vorhandenen automatischen Abgleich weiterverwenden. Der Datenstatus zeigt Fortschritt, letzten erfolgreichen Abruf und verständliche Hinweise, falls der Nutzer eingreifen muss. Kurze Testimporte und technische Details bleiben in der Diagnose verfügbar. Ein gezielter Nachimport oder Abbruch bleibt bei Bedarf erreichbar.

**Abnahme.** Der erste Import startet nach erfolgreichem Setup ohne manuelle Zeitfenster oder technische Eingaben. Neustarts und wiederholte Setup-Bestätigungen erzeugen keinen zweiten Auftrag für denselben Rückimport. Fehlende Freigaben und Fehler erscheinen mit einem konkreten nächsten Schritt.
