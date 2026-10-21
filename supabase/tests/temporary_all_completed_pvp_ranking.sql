-- Run only after the temporary-all-completed-PvP migration.
-- Every fixture write is rolled back; no historical match, RP, or toggle is changed.
BEGIN;
DO $check$
DECLARE
    sample record;
    report jsonb;
    result jsonb;
    test_match uuid;
    tx_count integer;
BEGIN
    if not (select enabled from prime.rating_rollout_controls where key = 'all_completed_pvp') then
        raise exception 'Temporary RP rollout is not enabled';
    end if;
    if has_table_privilege('anon', 'prime.rating_rollout_controls', 'SELECT')
       or has_table_privilege('authenticated', 'prime.rating_rollout_controls', 'SELECT') then
        raise exception 'Rating rollout toggle is exposed to clients';
    end if;
    if position('v_temporary_all_pvp' in
       pg_get_functiondef('public.ingest_project_prime_career_match(jsonb,uuid,integer,text,text)'::regprocedure)) = 0 then
        raise exception 'Career ingest is missing temporary policy';
    end if;

    -- Use an existing previously unrated verified FFA report as an immutable
    -- read-only fixture. Clone only its match_id, never update the original.
    select m."ServerId" as server_id, m."TrustClass" as trust_class,
           convert_from(m."OriginalReport", 'UTF8')::jsonb as payload
    into sample
    from prime.accepted_matches m
    where m."RatingIneligibilityReason" = 8
      and m."TrustClass" = 2
      and (convert_from(m."OriginalReport", 'UTF8')::jsonb ->> 'end_reason') = 'completed'
      and (convert_from(m."OriginalReport", 'UTF8')::jsonb ->> 'teams') = 'false'
      and jsonb_array_length(convert_from(m."OriginalReport", 'UTF8')::jsonb -> 'participants') between 2 and 8
      and not exists (
          select 1 from jsonb_array_elements(convert_from(m."OriginalReport", 'UTF8')::jsonb -> 'participants') p
          where coalesce((p->>'started_match')::boolean, false)
            and nullif(p->>'player_id', '') is null
      )
    order by m."EndedAt" desc limit 1;

    if not found then
        raise notice 'No eligible historical PvP fixture: policy and ACL checks passed, transaction path skipped';
        return;
    end if;

    test_match := gen_random_uuid();
    report := jsonb_set(sample.payload, '{match_id}', to_jsonb(test_match::text));
    result := public.ingest_project_prime_career_match(
        report, sample.server_id, sample.trust_class, repeat('E',64), report::text);
    select count(*) into tx_count from prime.rating_transactions where "MatchId" = test_match;
    if result->>'rating_status' <> 'applied' or tx_count < 2 then
        raise exception 'Temporary RP path did not award verified PvP: %, % transactions', result, tx_count;
    end if;

    update prime.rating_rollout_controls set enabled = false
    where key = 'all_completed_pvp';
    test_match := gen_random_uuid();
    report := jsonb_set(sample.payload, '{match_id}', to_jsonb(test_match::text));
    result := public.ingest_project_prime_career_match(
        report, sample.server_id, sample.trust_class, repeat('F',64), report::text);
    select count(*) into tx_count from prime.rating_transactions where "MatchId" = test_match;
    if result->>'rating_status' <> 'ineligible' or tx_count <> 0 then
        raise exception 'Disabling toggle did not restore original gating: %, % transactions', result, tx_count;
    end if;
END;
$check$;
ROLLBACK;
