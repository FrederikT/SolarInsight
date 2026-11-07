# Sungrow iSolarCloud Open API

Abruf am 7. November 2026. Quellen sind öffentlich lesbare Herstellerseiten. Für dieses Projekt liegen keine freigegebene App und keine autorisierte Anlage vor. Die Live-Abnahme A25 bleibt offen.

| Bereich | Bestätigt | Konsequenz / offen |
| --- | --- | --- |
| EU | JSON-Anfragen per POST an das Gateway `https://gateway.isolarcloud.eu/`. [Getting Started](https://developer-api.isolarcloud.com/#/document/md?id=14083) | Portaladresse dient nur als UI. |
| Freigabe | Entwickleranwendung, Plattform-Appkey und x-access-key sowie Autorisierung durch den Anlageneigentümer. [Sungrow EMEA](https://academy.sungrow-emea.com/de/articles/331036-sungrow-s-api-developer-portal) · [Getting Started](https://developer-api.isolarcloud.com/#/document/md?id=14083) | Freigabe und Eigentümerzustimmung vor Live-Test einholen. |
| OAuth | Autorisierungscode wird mit Redirect-URI eingetauscht. Antwort enthält Zugriffstoken, Refresh-Token, autorisierte Anlagen und Ablauf in Sekunden. Dokumentierte Modi global und authorization_bound. [Tokenvertrag](https://developer-api.isolarcloud.com/#/document/api?id=14113&project_id=1) | Ablauf aus Antwort lesen. Konkrete App-Berechtigungen und Redirect-Konfiguration prüfen. |
| Historie | Minutenroute maximal 3 Stunden, 50 Anlagen und 50 Messpunkte je Anfrage laut Beschreibung. Standardabstand 5 Minuten, 15 Minuten dokumentiert. Historische Route schließt aktuellen Tag aus. [Minutenhistorie](https://developer-api.isolarcloud.com/#/document/api?id=14062&project_id=1) | Maximale Rückschau und Reihenverfügbarkeit am konkreten Vertrag prüfen. Anfragebeispiel verwendet ein längeres Fenster als Beschreibung. |
| Zeit | Anfrage und Antwort verwenden lokale Anlagenzeit. Das Anfrageformat lautet yyyyMMddHHmmss. Fehlende Messpunkte werden ausgelassen. [Minutenhistorie](https://developer-api.isolarcloud.com/#/document/api?id=14062&project_id=1) | Intervallgrenzen, Stichprobe/Mittel, DST-Mehrdeutigkeit und Korrekturen sind ungeklärt. |
| Messpunkte | Beispiele im Anlagenverzeichnis: 83067 PV-Wirkleistung W, 83052 und 83106 Lastleistung W, 83549 Netz-Wirkleistung W, 83129 Batterie-SOC ohne dort ausgewiesene Einheit, 83118 Tagesverbrauch Wh. [Messpunktverzeichnis](https://developer-api.isolarcloud.com/#/document/md?id=14097&project_id=1) | Vorzeichen, Messort, Semantik, SOC-Skalierung und Modellverfügbarkeit per autorisierter Probe bestätigen. |
| Kontingent | Öffentlich angezeigtes Paket mit 2.000 Aufrufen/Stunde und 100.000 Aufrufen/Monat. [Pakete](https://developer-api.isolarcloud.com/#/package-manage-plan) | Effektiv gebuchtes Kontingent und Umgang mit 429 beim Live-Test prüfen. |


## Ergänzend bestätigte Verträge

- Der [offizielle Quick Start](https://developer-api.isolarcloud.com/#/quick-start) beschreibt die Erstellung und Prüfung der Anwendung, die registrierte Redirect-URL und den Einwilligungslink aus der App-Detailseite. Er nennt aktuell zwei Tage für den Zugriffstoken und 30 Tage für den Refresh-Token. Das Feld expires_in ist für den tatsächlichen Ablauf maßgeblich. Die Anwendung verwendet den in der Dokumentation beschriebenen autorisierungsgebundenen Tokenmodus. Frei wählbare Scopes, State-Rückgabe und PKCE-Unterstützung sind für die konkrete EU-Anwendung ungeklärt.
- Die [Anlagenliste](https://developer-api.isolarcloud.com/#/document/api?id=14055&project_id=1) und die [Geräteliste](https://developer-api.isolarcloud.com/#/document/api?id=14057&project_id=1) dokumentieren page, size, row_count und pageList. Für die Auswahl verwendet Solar Insight Zehnerseiten. Die Geräteausgabe enthält eine Anlagenbeziehung, eine technische Gerätekennung und, sofern geliefert, Typ und Modell aus type_name und device_model_code. Diese Metadaten bestätigen keine Speicherparameter.
- Die [Historie der Anlagenaggregate](https://developer-api.isolarcloud.com/#/document/api?id=14063&project_id=1) nennt maximal 100 Tage, 24 Monate oder fünf Jahre je Abfrage. Das sind Fenstergrößen. Sie belegen keine maximale Rückschau. Die Route verwendet Basiseinheiten wie Wh und W. Für bestätigte aggregierbare Tagesenergiepunkte nennt sie data_type 2. Die Anwendung speichert solche Tageswerte getrennt von Minutenreihen.
- Korrekturfrequenz, Resets, Flussrichtungen und Vorzeichen sind ohne autorisierte Anlagenstichprobe offen. Nicht eindeutige lokale Herbstzeitstempel bleiben Lücken. Synthetische Transport- und Mappingbeispiele stehen in tests/Integration/ProviderContractTests.cs. Es gibt keine aufgezeichnete Beispielantwort einer echten Anlage.

## Live-Abnahme

Erforderlich sind eine freigegebene App für die EU-Region, die Anlageneinwilligung des Eigentümers und mindestens ein autorisierter historischer Tag mit bestätigtem Messort, Einheit, Vorzeichen und Zeitsemantik. Verglichen werden die Energiekanäle und die Tagesaggregate mit demselben Zeitraum im Anlagenportal. Danach tatsächliche App-Kontingente und verfügbare Rückschau dokumentieren. A25 bleibt bis dahin offen.
