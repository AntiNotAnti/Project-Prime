-- Hunter visibility is private by default; only a verified owner may opt in.
-- No user account ID, email, raw match history or report is made public.
CREATE TABLE IF NOT EXISTS prime.hunter_public_visibility (
  player_id uuid PRIMARY KEY REFERENCES prime.hunter_licenses("PlayerId") ON DELETE CASCADE,
  is_public boolean NOT NULL DEFAULT false,
  updated_at timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE prime.hunter_public_visibility ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE prime.hunter_public_visibility FROM PUBLIC, anon, authenticated;

CREATE OR REPLACE FUNCTION public.project_prime_hunter_visibility()
RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER
SET search_path = pg_catalog, prime, pg_temp
AS $$
  SELECT coalesce((SELECT v.is_public
    FROM prime.hunter_public_visibility v WHERE v.player_id = auth.uid()), false);
$$;

CREATE OR REPLACE FUNCTION public.project_prime_set_hunter_visibility(p_public boolean)
RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
SET search_path = pg_catalog, prime, pg_temp
AS $$
DECLARE
  v_player uuid := auth.uid();
BEGIN
  IF v_player IS NULL THEN
    RAISE EXCEPTION 'Hunter License authentication required' USING ERRCODE = '42501';
  END IF;
  IF p_public IS NULL THEN
    RAISE EXCEPTION 'Visibility must be true or false' USING ERRCODE = '22023';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM prime.hunter_licenses l WHERE l."PlayerId" = v_player
  ) THEN
    RAISE EXCEPTION 'Hunter License must exist before changing visibility'
      USING ERRCODE = '22023';
  END IF;
  INSERT INTO prime.hunter_public_visibility (player_id, is_public, updated_at)
  VALUES (v_player, p_public, now())
  ON CONFLICT (player_id) DO UPDATE
    SET is_public = EXCLUDED.is_public,
        updated_at = EXCLUDED.updated_at;
  RETURN p_public;
END;
$$;

REVOKE ALL ON FUNCTION public.project_prime_hunter_visibility() FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.project_prime_set_hunter_visibility(boolean) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.project_prime_hunter_visibility() TO authenticated;
GRANT EXECUTE ON FUNCTION public.project_prime_set_hunter_visibility(boolean) TO authenticated;

COMMENT ON TABLE prime.hunter_public_visibility IS
  'Owner opt-in for public career display; starts false, never changes authoritative career data.';
