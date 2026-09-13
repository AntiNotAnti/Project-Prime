-- Run with the migration/administrator connection after applying migrations.
-- Supply the password without putting it in this file:
--   psql "$MIGRATION_DATABASE_URL" -v prime_app_password="$PRIME_APP_PASSWORD" \
--     -f src/Backend/Data/Scripts/provision-prime-app.sql
-- Re-run this script after migrations which add tables or sequences.

\if :{?prime_app_password}
\else
\echo 'prime_app_password is required'
\quit 2
\endif

SELECT format(
    'CREATE ROLE prime_app LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
    :'prime_app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'prime_app')
\gexec

SELECT format(
    'ALTER ROLE prime_app LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
    :'prime_app_password')
\gexec

ALTER ROLE prime_app SET search_path = prime;

SELECT format('GRANT CONNECT ON DATABASE %I TO prime_app', current_database())
\gexec

GRANT USAGE ON SCHEMA prime TO prime_app;
REVOKE CREATE ON SCHEMA prime FROM prime_app;

REVOKE ALL PRIVILEGES ON TABLE
    prime."AspNetRoles",
    prime."AspNetRoleClaims",
    prime."AspNetUserClaims",
    prime."AspNetUserLogins",
    prime."AspNetUserRoles",
    prime."AspNetUserTokens",
    prime.players,
    prime.player_profiles,
    prime.player_cosmetic_loadouts,
    prime.hunter_licenses,
    prime.accepted_matches,
    prime.career_participations,
    prime.career_aggregates,
    prime.rating_transactions,
    prime.rating_pair_contributions,
    prime.career_projection_state
FROM prime_app;

GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
    prime."AspNetRoles",
    prime."AspNetRoleClaims",
    prime."AspNetUserClaims",
    prime."AspNetUserLogins",
    prime."AspNetUserRoles",
    prime."AspNetUserTokens",
    prime.players,
    prime.player_profiles,
    prime.player_cosmetic_loadouts,
    prime.hunter_licenses,
    prime.accepted_matches,
    prime.career_participations,
    prime.career_aggregates,
    prime.rating_transactions,
    prime.rating_pair_contributions,
    prime.career_projection_state
TO prime_app;

REVOKE ALL PRIVILEGES ON TABLE prime."__EFMigrationsHistory" FROM prime_app;
GRANT SELECT ON TABLE prime."__EFMigrationsHistory" TO prime_app;

REVOKE ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA prime FROM prime_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA prime TO prime_app;

DO $prime_role_checks$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_namespace
        WHERE nspname <> 'information_schema'
          AND left(nspname, 3) <> 'pg_'
          AND has_schema_privilege('prime_app', oid, 'CREATE')
    ) THEN
        RAISE EXCEPTION 'prime_app unexpectedly has CREATE on a database schema';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM pg_auth_members membership
        JOIN pg_roles member ON member.oid = membership.member
        WHERE member.rolname = 'prime_app'
    ) THEN
        RAISE EXCEPTION 'prime_app must not be a member of another role';
    END IF;
    IF EXISTS (
        SELECT 1 FROM pg_class object
        JOIN pg_roles owner ON owner.oid = object.relowner
        WHERE owner.rolname = 'prime_app'
    ) OR EXISTS (
        SELECT 1 FROM pg_namespace schema_object
        JOIN pg_roles owner ON owner.oid = schema_object.nspowner
        WHERE owner.rolname = 'prime_app'
    ) OR EXISTS (
        SELECT 1 FROM pg_database database_object
        JOIN pg_roles owner ON owner.oid = database_object.datdba
        WHERE owner.rolname = 'prime_app'
    ) THEN
        RAISE EXCEPTION 'prime_app must not own database objects';
    END IF;
END;
$prime_role_checks$;
