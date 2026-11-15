-- Read-only comparison of source and target after pg_restore (docs/cloud-deployment.md, 4.3).
-- Run on both sides and diff the output; every line must match. Contains no token contents.
BEGIN TRANSACTION READ ONLY;
-- Server defaults differ (Windows local time vs. container UTC); pin the session for a byte-exact diff.
SET LOCAL TIME ZONE 'UTC';
\pset footer off
\pset tuples_only on
\pset format unaligned

SELECT 'rows', table_name,
       (xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM public.%I', table_name), false, true, '')))[1]::text
FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
ORDER BY table_name;

SELECT 'migration', max("MigrationId") FROM "__EFMigrationsHistory";

SELECT 'range-readings', min("Start"), max("End") FROM "Readings";
SELECT 'range-aggregates', min("Start"), max("End") FROM "Aggregates";
SELECT 'range-live', min("MeasuredAt"), max("MeasuredAt") FROM "LiveMeasurements";

SELECT 'aggregates', "Origin", "Revision", "Point", to_char("Start" AT TIME ZONE 'Europe/Berlin', 'YYYY-MM'),
       count(*), sum("Kwh")::numeric(18,4), sum("Coverage")::numeric(18,4)
FROM "Aggregates"
GROUP BY 2, 3, 4, 5 ORDER BY 2, 3, 4, 5;

SELECT 'readings-month', to_char("Start" AT TIME ZONE 'Europe/Berlin', 'YYYY-MM'), count(*),
       sum("Pv")::numeric(18,4), sum("Load")::numeric(18,4)
FROM "Readings" GROUP BY 2 ORDER BY 2;

SELECT 'live-channel', "Channel", "Source", count(*), sum("Value")::numeric(20,6)
FROM "LiveMeasurements" GROUP BY 2, 3 ORDER BY 2, 3;

SELECT 'plant', "Id", "Zone" FROM "Plants" ORDER BY 2;
SELECT 'configuration', "PlantId", "Since" FROM "Configurations" ORDER BY 2, 3;
SELECT 'connection', "PlantId", "Status", length("ProtectedSettings") > 0 FROM "Connections" ORDER BY 2;
ROLLBACK;
