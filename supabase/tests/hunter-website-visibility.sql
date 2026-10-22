-- Post-migration privacy and authorization smoke checks.
-- This script is read-only and never changes Hunter identity or stats.
BEGIN;
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='prime' AND c.relname='hunter_public_visibility' AND c.relrowsecurity
  ) THEN
    RAISE EXCEPTION 'Hunter public visibility table must have RLS enabled';
  END IF;

  IF has_table_privilege('anon', 'prime.hunter_public_visibility', 'SELECT') OR
     has_table_privilege('authenticated', 'prime.hunter_public_visibility', 'SELECT') OR
     has_table_privilege('anon', 'prime.hunter_public_visibility', 'UPDATE') OR
     has_table_privilege('authenticated', 'prime.hunter_public_visibility', 'UPDATE') THEN
    RAISE EXCEPTION 'Raw Hunter visibility table leaked via direct grants';
  END IF;

  IF has_function_privilege('anon', 'public.project_prime_hunter_visibility()', 'EXECUTE') OR
     has_function_privilege('anon', 'public.project_prime_set_hunter_visibility(boolean)', 'EXECUTE') THEN
    RAISE EXCEPTION 'Anonymous users must not invoke Hunter visibility RPCs';
  END IF;

  IF NOT has_function_privilege('authenticated', 'public.project_prime_hunter_visibility()', 'EXECUTE') OR
     NOT has_function_privilege('authenticated', 'public.project_prime_set_hunter_visibility(boolean)', 'EXECUTE') THEN
    RAISE EXCEPTION 'Authenticated Hunter owner RPC grants are missing';
  END IF;
END;
$$;
ROLLBACK;
