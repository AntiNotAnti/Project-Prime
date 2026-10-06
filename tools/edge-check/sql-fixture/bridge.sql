begin;
do $test$
declare
 player uuid:=gen_random_uuid();
 seeded jsonb;
 recovered jsonb;
begin
 seeded:=public.project_prime_hunter_license_for(player,'Seeded fixture',2);
 perform set_config('request.jwt.claim.sub',player::text,true);
 recovered:=public.project_prime_hunter_license('Recovery default',6);
 if seeded->'profile'->>'display_name' is distinct from 'Seeded fixture'
  or recovered->'profile'->>'display_name' is distinct from 'Seeded fixture'
  or (recovered->'profile'->>'favorite_hunter')::integer is distinct from 2
  or (recovered->'profile'->>'player_id')::uuid is distinct from player then
  raise exception 'bridge failed to preserve recovered account profile';
 end if;
end;
$test$;
rollback;
