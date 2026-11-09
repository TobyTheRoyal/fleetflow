-- Run with PostgreSQL 18 psql, VerifyFull TLS and -X -v ON_ERROR_STOP=1.
-- Catalog metadata only. Never selects business data, users or credentials.
BEGIN READ ONLY;
SET LOCAL statement_timeout = '10s';

SELECT current_database() AS database, current_user AS login,
       current_setting('server_version') AS version,
       current_setting('transaction_read_only') AS read_only,
       current_setting('max_connections') AS connection_limit;
SELECT ssl, version, bits FROM pg_stat_ssl WHERE pid = pg_backend_pid();
SELECT pg_get_userbyid(datdba) AS database_owner,
       has_database_privilege(current_user, oid, 'CREATE') AS can_create_schema,
       has_schema_privilege(current_user, 'public', 'CREATE') AS can_create_in_public
FROM pg_database WHERE datname = current_database();

SELECT rolname, rolcanlogin, rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls
FROM pg_roles WHERE rolname = current_user OR rolname LIKE 'fleetflow%'
ORDER BY rolname;
SELECT member.rolname AS member, granted.rolname AS granted,
       m.admin_option, m.inherit_option, m.set_option
FROM pg_auth_members m
JOIN pg_roles member ON member.oid = m.member
JOIN pg_roles granted ON granted.oid = m.roleid
WHERE member.rolname = current_user OR member.rolname LIKE 'fleetflow%'
ORDER BY 1, 2;

SELECT nspname AS schema, pg_get_userbyid(nspowner) AS owner, nspacl AS grants
FROM pg_namespace WHERE nspname <> 'information_schema' AND nspname NOT LIKE 'pg_%'
ORDER BY 1;
SELECT n.nspname AS schema, c.relname AS relation, c.relkind AS kind,
       pg_get_userbyid(c.relowner) AS owner, c.relacl AS grants
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg_%'
  AND c.relkind IN ('r', 'p', 'v', 'm', 'S', 'f')
ORDER BY 1, 2;
SELECT c.relname AS relation, a.attname AS column, a.attacl AS grants
FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
WHERE c.oid = to_regclass('public."PortfolioSlots"') AND a.attacl IS NOT NULL
ORDER BY a.attnum;
SELECT n.nspname AS schema, p.proname AS routine, p.prosecdef AS security_definer,
       pg_get_userbyid(p.proowner) AS owner, p.proacl AS grants
FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg_%'
ORDER BY 1, 2;
SELECT n.nspname AS schema, c.relname AS relation, t.tgname AS trigger, t.tgenabled AS enabled
FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE NOT t.tgisinternal AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg_%'
ORDER BY 1, 2, 3;
SELECT extname, extversion FROM pg_extension ORDER BY 1;
SELECT to_regclass('public."__EFMigrationsHistory"') IS NOT NULL AS history_exists \gset
\if :history_exists
SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";
\else
\echo No EF migration history table.
\endif
ROLLBACK;
