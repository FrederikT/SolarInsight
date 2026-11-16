# Arbeitsregeln für Solar Insight

## Backlog und Umsetzungsstand

- Lies zu Beginn einer Projektaufgabe [das Backlog](docs/backlog.md) und [den Umsetzungsstand](docs/implementation-status.md). Prüfe, ob die Aufgabe bereits eine ID hat, und verwende sie weiter.
- Wenn der Nutzer neue To-dos sammeln, aufnehmen oder öffnen möchte, ergänze `docs/backlog.md`. Fasse Überschneidungen mit bestehenden Einträgen zusammen. Gib jedem neuen Punkt eine eindeutige ID, einen Status und überprüfbare Abnahmekriterien. Eine Bitte um Aufnahme ins Backlog allein ist ein Dokumentationsauftrag.
- Wenn der Nutzer eine Umsetzung oder Korrektur beauftragt, aktualisiere `docs/implementation-status.md` im selben Arbeitsschritt. Halte unter der passenden ID fest, was sich geändert hat, welche Prüfungen tatsächlich ausgeführt wurden und wie sie ausgingen. Kennzeichne ausstehende Prüfungen, Prüfungen mit Testdaten und wesentliche Abweichungen von der Anforderung.
- Markiere einen Backlog-Punkt erst als erledigt, wenn seine Abnahmekriterien erfüllt und geprüft sind. Bei Teilergebnissen nenne die verbleibende Arbeit. Bei blockierten Punkten nenne die fehlende Voraussetzung und den nächsten Schritt. Verlinke erledigte Punkte mit ihrem Nachweis im Umsetzungsstand und gleiche widersprüchliche Statusangaben in betroffenen Checklisten ab.
- Das Backlog beantwortet, was noch zu tun ist. Der Umsetzungsstand dokumentiert, was geliefert und geprüft wurde. Verlinke im Abschlussbericht die aktualisierten Einträge.

## Lokale Bereitstellung nach Implementierungen

- Baue Solar Insight nach jeder Implementierung oder Fehlerkorrektur aus dem aktuellen Quellstand neu. Aktualisiere die eingerichtete lokale Demo auf Port 8080 und starte sie mit dem neuen Build, auch wenn sie zuvor gestoppt war.
- Die private Instanz läuft seit dem 16. November 2026 in der Cloud (Oracle-VM, siehe [Cloud-Betrieb](docs/cloud-deployment.md)). **Starte die frühere private Instanz auf Port 8081 nicht mehr.** Sie würde Sungrow mit denselben Tokens abfragen und die Cloud-Instanz aussperren. Ihre Datenbank bleibt als Rückfallebene unverändert. Rolle Änderungen an der privaten Instanz erst nach Merge nach `main` und nur mit Freigabe der Betreiberin bzw. des Betreibers nach dem Update-Ablauf in `docs/cloud-deployment.md` aus. Verwende die bestehenden lokalen Startwege und erhalte Datenbanken, Konfiguration und Zugangsdaten. Bei reiner Backlog- oder Dokumentationspflege entfällt dieser Schritt.
- Prüfe nach dem Neustart den Readiness-Endpunkt und, soweit lokal möglich, den geänderten Ablauf. Halte Build, Neustart und Prüfergebnis in `docs/implementation-status.md` fest. Nenne im Abschlussbericht die lokale URL. Falls ein Schritt scheitert, dokumentiere den konkreten Blocker und den tatsächlichen Stand der Instanz.
