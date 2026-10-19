-- Native HTTP reports retain a tombstone independently of membership TTL/cleanup.
-- Each process uses a fresh existing authority epoch; sequence never resets in it.
create table prime.social_native_authority_epochs (
    authority_epoch numeric(20,0) primary key check(authority_epoch between 1 and 18446744073709551615),
    reporter_id uuid not null references public.project_prime_career_reporters(server_id),
    created_at timestamptz not null default clock_timestamp()
);
-- Preserve unique pre-migration reporter ownership during rollout. Conflicting
-- live legacy rows are refused below until they expire, never guessed.
insert into prime.social_native_authority_epochs(authority_epoch,reporter_id)
select m.authority_epoch,(array_agg(distinct m.reporter_id))[1]
from prime.social_lobby_memberships m join public.project_prime_career_reporters r on r.server_id=m.reporter_id
group by m.authority_epoch having count(distinct m.reporter_id)=1;
create table prime.social_native_membership_fences (
    reporter_id uuid not null references public.project_prime_career_reporters(server_id),
    authority_epoch numeric(20,0) not null references prime.social_native_authority_epochs(authority_epoch),
    player_id uuid not null,
    reporter_sequence numeric(19,0) not null check(reporter_sequence between 1 and 9223372036854775807),
    primary key(reporter_id,authority_epoch,player_id)
);
alter table prime.social_native_authority_epochs enable row level security;
alter table prime.social_native_membership_fences enable row level security;
revoke all on prime.social_native_authority_epochs,prime.social_native_membership_fences from public,anon,authenticated;

create function prime.social_native_lobby_membership_report(p_reporter uuid,p_player uuid,
    p_epoch numeric,p_client bigint,p_sequence numeric,p_eligible boolean,p_leave boolean,p_ticket_expiry bigint)
returns jsonb language plpgsql security invoker set search_path=prime,pg_temp as $$
declare v_now timestamptz; v_owner uuid; v_floor numeric;
begin
    if p_reporter is null or p_player is null or p_epoch is null
       or p_epoch not between 1 and 18446744073709551615 or trunc(p_epoch)<>p_epoch
       or p_client is null or p_client not between 1 and 4294967295
       or p_sequence is null or p_sequence not between 0 and 9223372036854775807 or trunc(p_sequence)<>p_sequence
       or p_eligible is null or p_leave is null or p_ticket_expiry is null then
        return jsonb_build_object('ok',false,'status','invalid_membership');
    end if;
    perform pg_advisory_xact_lock(6384340261223951::bigint);
    v_now:=clock_timestamp();
    if not exists(select 1 from public.project_prime_career_reporters where server_id=p_reporter and enabled)
       or to_timestamp(p_ticket_expiry)+interval '60 seconds' < v_now then
        return jsonb_build_object('ok',false,'status','report_expired');
    end if;
    if exists(select 1 from prime.social_lobby_memberships where authority_epoch=p_epoch
        and reporter_id<>p_reporter and expires_at>v_now) then
        return jsonb_build_object('ok',false,'status','authority_owner_changed'); end if;
    insert into prime.social_native_authority_epochs(authority_epoch,reporter_id) values(p_epoch,p_reporter)
        on conflict(authority_epoch) do nothing;
    select reporter_id into v_owner from prime.social_native_authority_epochs where authority_epoch=p_epoch;
    if v_owner is distinct from p_reporter then return jsonb_build_object('ok',false,'status','authority_owner_changed'); end if;
    select reporter_sequence into v_floor from prime.social_native_membership_fences
        where reporter_id=p_reporter and authority_epoch=p_epoch and player_id=p_player;
    if v_floor is not null and p_sequence<=v_floor then
        return jsonb_build_object('ok',false,'status','stale_report');
    end if;
    if p_sequence>0 then
        insert into prime.social_native_membership_fences values(p_reporter,p_epoch,p_player,p_sequence)
        on conflict(reporter_id,authority_epoch,player_id) do update set reporter_sequence=excluded.reporter_sequence;
    end if;
    if p_leave then
        delete from prime.social_lobby_memberships where player_id=p_player and authority_epoch=p_epoch and reporter_id=p_reporter;
    else
        insert into prime.social_lobby_memberships(player_id,authority_epoch,reporter_id,client_id,lobby_eligible,updated_at,expires_at)
        values(p_player,p_epoch,p_reporter,p_client,p_eligible,v_now,v_now+interval '60 seconds')
        on conflict(player_id,authority_epoch,reporter_id) do update set client_id=excluded.client_id,
            lobby_eligible=excluded.lobby_eligible,updated_at=excluded.updated_at,expires_at=excluded.expires_at;
        perform prime.social_recent_touch(p_player,p_epoch,p_reporter);
    end if;
    return jsonb_build_object('ok',true,'player_id',p_player,'status',case when p_leave then 'membership_removed' else 'membership_verified' end);
end;
$$;
revoke execute on function prime.social_native_lobby_membership_report(uuid,uuid,numeric,bigint,numeric,boolean,boolean,bigint) from public,anon,authenticated;

-- One native projection and the existing Party authority/lock; no seat claims.
create function prime.social_native_party_travel_state(p_actor uuid)
returns jsonb language plpgsql security invoker set search_path=prime,pg_temp as $$
declare v_state jsonb; v_travel jsonb;
begin
    perform pg_advisory_xact_lock(6384340261223951::bigint);
    v_state:=prime.social_native_party_snapshot(p_actor);
    v_travel:=prime.social_party_travel_snapshot(p_actor);
    if v_travel is not null and not exists(
        select 1 from prime.social_party_travel t join prime.social_parties p on p.party_id=t.party_id and p.leader_id=t.leader_id
        join prime.social_lobbies l on l.lobby_id=t.lobby_id
        join prime.social_lobby_memberships m on m.player_id=t.leader_id and m.authority_epoch=l.authority_epoch
        where t.travel_id=(v_travel->>'travel_id')::uuid and t.revision=(v_travel->>'revision')::integer
            and t.expires_at>clock_timestamp() and l.expires_at>clock_timestamp()
            and m.lobby_eligible and m.expires_at>clock_timestamp()
    ) then v_travel:=null; end if;
    return v_state || jsonb_build_object('travel',v_travel);
end;
$$;
create function prime.social_native_party_travel_action(p_actor uuid,p_action text,p_expected_revision bigint,
    p_party uuid,p_expected_self text,p_travel uuid,p_travel_revision integer,
    p_host inet,p_port integer,p_epoch numeric,p_protocol integer,p_room text,p_server text)
returns jsonb language plpgsql security invoker set search_path=prime,pg_temp as $$
declare v_state jsonb; v_travel prime.social_party_travel%rowtype; v_lobby prime.social_lobbies%rowtype;
    v_result jsonb; v_now timestamptz; v_locator jsonb; v_current jsonb;
begin
    perform pg_advisory_xact_lock(6384340261223951::bigint);
    v_now:=clock_timestamp();
    v_state:=prime.social_native_party_snapshot(p_actor);
    if v_state is null or p_expected_self is distinct from v_state->>'self_prime_id' then
        return jsonb_build_object('ok',false,'status','account_changed'); end if;
    if p_expected_revision is null or p_expected_revision is distinct from (v_state->>'server_revision')::bigint
       or p_party is null or p_party::text is distinct from v_state->>'party_id' then
        return jsonb_build_object('ok',false,'status','party_changed'); end if;
    if p_action='publish_travel' then
        v_current:=prime.social_native_party_travel_state(p_actor)->'travel';
        if (v_current is null or v_current='null'::jsonb) then
            if p_travel is not null or coalesce(p_travel_revision,0)<>0 then
                return jsonb_build_object('ok',false,'status','party_travel_stale'); end if;
        elsif p_travel::text is distinct from v_current->>'travel_id'
            or p_travel_revision is distinct from (v_current->>'revision')::integer then
            return jsonb_build_object('ok',false,'status','party_travel_stale');
        end if;
        if v_state->>'leader_prime_id' is distinct from p_expected_self or not exists(
            select 1 from prime.social_lobby_memberships where player_id=p_actor and authority_epoch=p_epoch
                and lobby_eligible and expires_at>v_now) then
            return jsonb_build_object('ok',false,'status','membership_unverified'); end if;
        v_result:=prime.social_lobby_register(p_actor,p_host,p_port,p_epoch,p_protocol,p_room,p_server);
        if v_result->>'ok' is distinct from 'true' then return v_result; end if;
        v_result:=prime.social_party_travel_publish(p_actor,(v_result->'lobby'->>'lobby_id')::uuid,'leader_lobby');
        if v_result->>'ok' is distinct from 'true' then return v_result; end if;
    elsif p_action in ('follow_travel','join_leader','decline_travel','joined_travel') then
        select * into v_travel from prime.social_party_travel where party_id=p_party
            and travel_id=p_travel and revision=p_travel_revision and expires_at>v_now for update;
        if not found or not exists(select 1 from prime.social_parties where party_id=p_party and leader_id=v_travel.leader_id) then
            return jsonb_build_object('ok',false,'status','party_travel_stale'); end if;
        select * into v_lobby from prime.social_lobbies where lobby_id=v_travel.lobby_id and expires_at>v_now;
        if not found or not exists(select 1 from prime.social_lobby_memberships
            where player_id=v_travel.leader_id and authority_epoch=v_lobby.authority_epoch and lobby_eligible and expires_at>v_now) then
            return jsonb_build_object('ok',false,'status','lobby_unavailable'); end if;
        if p_action='joined_travel' and not exists(select 1 from prime.social_lobby_memberships
            where player_id=p_actor and authority_epoch=v_lobby.authority_epoch and lobby_eligible and expires_at>v_now) then
            return jsonb_build_object('ok',false,'status','admission_not_verified'); end if;
        v_result:=prime.social_party_travel_respond(p_actor,p_travel,p_travel_revision,
            case p_action when 'decline_travel' then 'decline' when 'joined_travel' then 'joined' else 'follow' end);
        if v_result->>'ok' is distinct from 'true' then return v_result; end if;
        if p_action in ('follow_travel','join_leader') then v_locator:=v_result->'locator'; end if;
    elsif p_action='clear_travel' then
        if not exists(select 1 from prime.social_party_travel where party_id=p_party
            and travel_id=p_travel and revision=p_travel_revision and expires_at>v_now) then
            return jsonb_build_object('ok',false,'status','party_travel_stale'); end if;
        v_result:=prime.social_party_travel_clear(p_actor);
        if v_result->>'ok' is distinct from 'true' then return v_result; end if;
    else return jsonb_build_object('ok',false,'status','invalid_action'); end if;
    return jsonb_build_object('ok',true,'snapshot',prime.social_native_party_travel_state(p_actor),'locator',v_locator);
end;
$$;
revoke execute on function prime.social_native_party_travel_state(uuid) from public,anon,authenticated;
revoke execute on function prime.social_native_party_travel_action(uuid,text,bigint,uuid,text,uuid,integer,inet,integer,numeric,integer,text,text) from public,anon,authenticated;
