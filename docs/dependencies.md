# Versionen und Lizenzen

Geprüft am 7. November 2026. Die Projekte verwenden feste direkte Versionen sowie NuGet- und npm-Lockfiles. Docker-Basisimages sind versionsgebunden. Deren Tag kann weiterhin herstellerseitig aktualisiert werden.

| Komponente | Version | Lizenz |
| --- | --- | --- |
| .NET SDK | 10.0.403 | MIT |
| ASP.NET Core Runtime | 10.0.14 | MIT |
| EF Core / Relational / Design | 10.0.14 | MIT |
| Npgsql EF Provider | 10.0.0 | PostgreSQL |
| PostgreSQL | 17.11 | PostgreSQL |
| Node.js | 24.15.0 | MIT und Drittanbieterhinweise |
| React / React DOM | 19.3.1 | MIT |
| TypeScript | 5.9.3 | Apache-2.0 |
| Vite | 8.3.6 | MIT |
| Apache ECharts | 6.1.0 | Apache-2.0 |
| ESLint | 10.14.0 | MIT |
| typescript-eslint | 8.72.1 | MIT |
| Playwright | 1.58.2 | Apache-2.0 |
| xUnit | 2.9.2 | Apache-2.0 |
| xUnit VS runner | 2.8.2 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.12.0 | MIT |

Die .NET-10-LTS-Linie wird laut Microsoft bis November 2028 unterstützt. PostgreSQL 17 wird laut Hersteller bis November 2029 unterstützt. Npgsql 10 ist der Provider für EF Core 10. Vite 8 setzt Node ab 20.19 oder 22.12 voraus. Der verwendete Node-24-Zweig erfüllt diese Grenze. Die npm-Versionen wurden über die offizielle Registry verifiziert. Die Testwerkzeuge sind bewusst explizit gepinnt.

Quellen sind die [Microsoft-Supporttabelle](https://dotnet.microsoft.com/en-us/platform/support/policy), [PostgreSQL-Versionspolitik](https://www.postgresql.org/support/versioning/), [Npgsql-10-Versionshinweise](https://www.npgsql.org/efcore/release-notes/10.0.html) und [Vite-8-Ankündigung](https://v8.vite.dev/blog/announcing-vite8).

Die transitiven Bibliotheken und ihre Integritätswerte sind in den Lockfiles erfasst. Lizenztexte liegen in den jeweiligen NuGet-/npm-Paketen und Basisimages. Der Quellcode von Solar Insight steht unter der [MIT-Lizenz](../LICENSE).

