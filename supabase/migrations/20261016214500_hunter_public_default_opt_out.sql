-- Hunter Licenses are public by default, with an owner-controlled opt-out.
-- An existing explicit false is a durable privacy choice and MUST survive.
-- No bulk UPDATE or per-player backfill: missing rows adopt the new default.
--
-- Only the limited public career projection may show a nickname and aggregate
-- stats. Account UUIDs, emails, refresh tokens and personal match history are
-- never exposed via this preference.

ALTER TABLE prime.hunter_public_visibility
    ALTER COLUMN is_public SET DEFAULT true;

CREATE OR REPLACE FUNCTION public.project_prime_hunter_visibility()
RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER
SET search_path = ''
AS $$
    SELECT CASE
        WHEN auth.uid() IS NULL THEN false
        ELSE coalesce(
            (SELECT v.is_public
             FROM prime.hunter_public_visibility AS v
             WHERE v.player_id = auth.uid()),
            true
        )
    END;
$$;

-- The private table remains inaccessible to clients. Only an authenticated
-- owner can read a preference and call the existing owner-only setter.
REVOKE ALL ON FUNCTION public.project_prime_hunter_visibility() FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.project_prime_hunter_visibility() TO authenticated;

COMMENT ON TABLE prime.hunter_public_visibility IS
    'Owner overrides of public-by-default Hunter career profile visibility. Explicit false opts out. Missing row means public.';
COMMENT ON COLUMN prime.hunter_public_visibility.is_public IS
    'Explicit preference; missing row is public. False permanently hides the nickname and public profile until owner opts back in.';
