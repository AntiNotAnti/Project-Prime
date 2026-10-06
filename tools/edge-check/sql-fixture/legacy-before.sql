begin;
create temporary table local_legacy_report (report jsonb, reporter uuid, hash text, body text) on commit drop;
do $test$
declare
 reporter uuid := gen_random_uuid();
 match_id uuid := gen_random_uuid();
 player uuid;
 participants jsonb := '[]';
 report jsonb;
 accepted jsonb;
 i integer;
begin
 insert into public.project_prime_career_reporters(server_id,display_name,key_hash,trust_class)
 values(reporter,'Local legacy fixture',repeat('b',64),2);
 for i in 1..2 loop
  player:=gen_random_uuid();
  perform public.project_prime_hunter_license_for(player,'Legacy Fixture',i);
  participants:=participants || jsonb_build_array(jsonb_build_object(
    'participant_id',gen_random_uuid(),'client_id',i,'player_id',player,
    'display_name','Legacy Fixture','hunter',i,'single_hunter',true,'team',0,
    'started_match',true,'departed',false,'played_ticks',60,'standing',i,
    'team_standing',i,'won',i=1,'tied',false,
    'metrics',jsonb_build_object('kills',2,'deaths',1,'assists',0,'damage',20,
      'headshots',0,'octolith_scores',0,'nodes_captured',0,'kills_as_prime',0,
      'longest_kill_streak',1,'beam_kills',jsonb_build_array(2,0,0,0,0,0,0,0,0))));
 end loop;
 report:=jsonb_build_object('version',1,'match_id',match_id,'server_incarnation',gen_random_uuid(),
   'started_at_utc',now()-interval '1 minute','ended_at_utc',now(),'room_key','Local legacy fixture',
   'mode',0,'teams',false,'played_ticks',60,'end_reason','completed','contains_bots',false,
   'rating_eligible',true,'participants',participants);
 accepted:=public.ingest_project_prime_career_match(report,reporter,2,repeat('D',64),report::text);
 if accepted->>'status' is distinct from 'accepted' or accepted->>'rating_status' is distinct from 'applied' then
  raise exception 'legacy report not accepted before upgrade: %',accepted;
 end if;
 insert into local_legacy_report values(report,reporter,repeat('D',64),report::text);
end;
$test$;
