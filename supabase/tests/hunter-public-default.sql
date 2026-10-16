-- Run after 20261016214500_hunter_public_default_opt_out.sql.
-- Read-only verification of default, preference permissions and policy body.
BEGIN;
DO $$
DECLARE
    configured_default text;
    reader_body text;
BEGIN
    SELECT pg_get_expr(d.adbin, d.adrelid)
      INTO configured_default
      FROM pg_attribute a
      JOIN pg_class c ON c.oid = a.attrelid
      JOIN pg_namespace n ON n.oid = c.relnamespace
      JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
     WHERE n.nspname = 'prime'
       AND c.relname = 'hunter_public_visibility'
       AND a.attname = 'is_public';

    IF configured_default IS DISTINCT FROM 'true' THEN
        RAISE EXCEPTION 'Hunter profiles should default to public: %', configured_default;
    END IF;

    SELECT pg_get_functiondef('public.project_prime_hunter_visibility()'::regprocedure)
      INTO reader_body;
    IF position('v.is_public' in reader_body) = 0
       OR position('true' in reader_body) = 0
       OR position('auth.uid()' in reader_body) = 0 THEN
        RAISE EXCEPTION 'Owner visibility RPC has lost the public fallback or auth guard';
    END IF;

    IF has_table_privilege('anon', 'prime.hunter_public_visibility', 'SELECT')
       OR has_table_privilege('authenticated', 'prime.hunter_public_visibility', 'UPDATE')
       OR has_function_privilege('anon',
          'public.project_prime_hunter_visibility()', 'EXECUTE') THEN
        RAISE EXCEPTION 'Hunter visibility permissions allow a direct or anonymous bypass';
    END IF;

    IF NOT has_function_privilege('authenticated',
           'public.project_prime_set_hunter_visibility(boolean)', 'EXECUTE') THEN
        RAISE EXCEPTION 'Opt-out RPC is unavailable to authenticated guest/secured Hunters';
    END IF;
END;
$$;
ROLLBACK;
