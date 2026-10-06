do $test$
declare
 f local_legacy_report%rowtype;
 result jsonb;
 before jsonb;
 after jsonb;
begin
 select * into strict f from local_legacy_report;
 select jsonb_build_object('matches',(select jsonb_agg(to_jsonb(m) order by "MatchId") from prime.accepted_matches m),
  'participants',(select jsonb_agg(to_jsonb(p) order by "MatchId","PlayerId") from prime.career_participations p),
  'ratings',(select jsonb_agg(to_jsonb(r) order by "MatchId","PlayerId") from prime.rating_transactions r),
  'pairs',(select jsonb_agg(to_jsonb(p) order by "MatchId","PlayerId","OpponentPlayerId") from prime.rating_pair_contributions p),
  'aggregates',(select jsonb_agg(to_jsonb(a) order by "PlayerId","TrustClass","Dimension","Key") from prime.career_aggregates a),
  'balances',(select jsonb_agg(to_jsonb(l) order by "PlayerId") from prime.hunter_licenses l)) into before;
 result:=public.ingest_project_prime_career_match(f.report,f.reporter,2,f.hash,f.body);
 if result->>'status' is distinct from 'duplicate' then raise exception 'legacy retry changed after migration: %',result; end if;
 begin
  perform public.ingest_project_prime_career_match(f.report,f.reporter,2,repeat('E',64),f.body);
  raise exception 'different payload hash accepted for immutable match';
 exception when others then
  if sqlerrm <> 'match id conflict' then raise; end if;
 end;
 begin
  perform public.ingest_project_prime_career_match(f.report,gen_random_uuid(),2,f.hash,f.body);
  raise exception 'different reporter accepted for immutable match';
 exception when others then
  if sqlerrm <> 'match id conflict' then raise; end if;
 end;
 result:=public.ingest_project_prime_career_match(
   f.report || jsonb_build_object('match_id',gen_random_uuid(),'contains_bots',true),
   f.reporter,2,repeat('F',64),f.body);
 if result is distinct from '{"accepted":false,"reason":"BotAssistedMatch"}'::jsonb then
  raise exception 'bot result invalid: %',result;
 end if;
 select jsonb_build_object('matches',(select jsonb_agg(to_jsonb(m) order by "MatchId") from prime.accepted_matches m),
  'participants',(select jsonb_agg(to_jsonb(p) order by "MatchId","PlayerId") from prime.career_participations p),
  'ratings',(select jsonb_agg(to_jsonb(r) order by "MatchId","PlayerId") from prime.rating_transactions r),
  'pairs',(select jsonb_agg(to_jsonb(p) order by "MatchId","PlayerId","OpponentPlayerId") from prime.rating_pair_contributions p),
  'aggregates',(select jsonb_agg(to_jsonb(a) order by "PlayerId","TrustClass","Dimension","Key") from prime.career_aggregates a),
  'balances',(select jsonb_agg(to_jsonb(l) order by "PlayerId") from prime.hunter_licenses l)) into after;
 if before is distinct from after then raise exception 'immutable retry/conflict changed persistent facts'; end if;
end;
$test$;
rollback;
