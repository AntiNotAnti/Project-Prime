-- Run only against a disposable LOCAL Postgres with the EF prime schema and
-- repository Supabase migrations already applied. All fixtures roll back.
-- psql <local connection> -v ON_ERROR_STOP=1 -f tools/edge-check/career-cumulative.sql
begin;
do $test$
declare
    reporter uuid := gen_random_uuid();
    match_id uuid := gen_random_uuid();
    incarnation uuid := gen_random_uuid();
    player uuid;
    participants jsonb := '[]';
    p jsonb;
    report jsonb;
    accepted jsonb;
    repeated jsonb;
    body text;
    hash text := repeat('A',64);
    i integer;
begin
    insert into public.project_prime_career_reporters(server_id,display_name,key_hash,trust_class)
    values(reporter,'Local cumulative fixture',repeat('a',64),2);
    for i in 1..128 loop
        player := gen_random_uuid();
        insert into prime.players ("Id","UserName","NormalizedUserName","EmailConfirmed",
            "PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount")
        values(player,'fixture_'||player::text,upper('fixture_'||player::text),false,false,false,false,0);
        insert into prime.player_profiles("PlayerId","DisplayName","FavoriteHunter") values(player,'Fixture',0);
        insert into prime.hunter_licenses("PlayerId","CreatedAt","RatingPoints") values(player,now(),0);
        p := jsonb_build_object('participant_id',gen_random_uuid(),'client_id',i,'player_id',player,
            'display_name','Fixture','hunter',0,'single_hunter',true,'team',0,'started_match',i<=8,
            'departed',false,'played_ticks',60,'standing',i%8,'team_standing',i%8,'won',false,'tied',false,
            'metrics',jsonb_build_object('kills',1,'deaths',0,'assists',0,'damage',20,'headshots',0,
                'octolith_scores',0,'nodes_captured',0,'kills_as_prime',0,'longest_kill_streak',1,
                'beam_kills',jsonb_build_array(1,0,0,0,0,0,0,0,0)));
        participants := participants || jsonb_build_array(p);
    end loop;
    report := jsonb_build_object('version',1,'match_id',match_id,'server_incarnation',incarnation,
        'started_at_utc',now()-interval '1 minute','ended_at_utc',now(),'room_key','Local fixture',
        'mode',0,'teams',false,'played_ticks',60,'end_reason','completed','contains_bots',false,
        'rating_eligible',true,'participants',participants);
    body := report::text;
    accepted := public.ingest_project_prime_career_match(report,reporter,2,hash,body);
    if accepted->>'status' <> 'accepted' or accepted->>'rating_status' <> 'applied' then
        raise exception '128 cumulative participants with eight starters failed: %',accepted;
    end if;
    if (select count(*) from prime.career_participations where "MatchId"=match_id) <> 128 then
        raise exception 'cumulative participant rows missing';
    end if;
    if (select count(*) from prime.rating_transactions where "MatchId"=match_id) <> 8 then
        raise exception 'rating starting roster is not independently eight';
    end if;
    repeated := public.ingest_project_prime_career_match(report,reporter,2,hash,body);
    if repeated->>'status' <> 'duplicate' then raise exception 'idempotent retry failed'; end if;
    report := report || jsonb_build_object('match_id',gen_random_uuid(),'participants',participants || jsonb_build_array(p));
    begin
        perform public.ingest_project_prime_career_match(report,reporter,2,repeat('B',64),report::text);
        raise exception '129 participant report accepted';
    exception when others then
        if sqlerrm not like '%invalid authoritative report facts%' then raise; end if;
    end;
    report := report || jsonb_build_object('match_id',gen_random_uuid(),'participants',
        (select jsonb_agg(value || jsonb_build_object('started_match',true)) from jsonb_array_elements(participants)));
    accepted := public.ingest_project_prime_career_match(report,reporter,2,repeat('C',64),report::text);
    if accepted->>'rating_status' <> 'ineligible'
       or (accepted->>'rating_ineligibility_reason')::integer <> 12 then
        raise exception 'oversized starting roster received rating: %', accepted;
    end if;
    raise notice 'PASS 128 cumulative admissions, eight rating starters, immutable retry, 129 bound, oversized rating roster';
end;
$test$;
rollback;
