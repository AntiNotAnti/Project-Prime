-- Native Party keeps the existing authority and privacy rules. A server-owned
-- revision witnesses lifecycle state; presence heartbeats do not invalidate it.
alter table prime.social_profiles add column native_party_revision bigint not null default 0
    check (native_party_revision between 0 and 9007199254740991);

create function prime.social_native_party_revision_touch()
returns trigger language plpgsql security invoker set search_path = prime, pg_temp as $$
declare
    v_party uuid;
    v_players uuid[];
begin
    if tg_table_name = 'social_party_invites' then
        if tg_op = 'DELETE' then v_players := array[old.sender_id, old.recipient_id];
        else v_players := array[new.sender_id, new.recipient_id]; end if;
    elsif tg_table_name = 'social_party_members' then
        if tg_op = 'DELETE' then
            v_party := old.party_id; v_players := array[old.player_id];
        else
            v_party := new.party_id; v_players := array[new.player_id];
        end if;
        v_players := v_players || array(select player_id from prime.social_party_members where party_id = v_party);
    else
        v_party := new.party_id;
        v_players := array(select player_id from prime.social_party_members where party_id = v_party);
    end if;
    update prime.social_profiles set native_party_revision = native_party_revision + 1
    where player_id = any(v_players);
    return null;
end;
$$;
create trigger social_native_party_invite_revision after insert or update or delete
on prime.social_party_invites for each row execute function prime.social_native_party_revision_touch();
create trigger social_native_party_member_revision after insert or delete
on prime.social_party_members for each row execute function prime.social_native_party_revision_touch();
create trigger social_native_party_leader_revision after update of leader_id
on prime.social_parties for each row execute function prime.social_native_party_revision_touch();

-- Serialize authority entry points before they acquire profile/party locks.
-- Both restored and native clients use these wrappers. This also includes block
-- cleanup and expiry, which can remove members or close invitations indirectly.
alter function prime.social_party_action(uuid,text,text,uuid) rename to social_party_action_legacy;
create function prime.social_party_action(p_actor uuid, p_action text,
    p_target_prime_id text default null, p_invite_id uuid default null)
returns jsonb language plpgsql security invoker set search_path = prime, pg_temp as $$
begin
    perform pg_advisory_xact_lock(6384340261223951::bigint);
    return prime.social_party_action_legacy(p_actor,p_action,p_target_prime_id,p_invite_id);
end;
$$;
alter function prime.social_mutate(uuid,text,text) rename to social_mutate_legacy;
create function prime.social_mutate(p_actor uuid,p_target_prime_id text,p_action text)
returns jsonb language plpgsql security invoker set search_path = prime, pg_temp as $$
begin
    perform pg_advisory_xact_lock(6384340261223951::bigint);
    return prime.social_mutate_legacy(p_actor,p_target_prime_id,p_action);
end;
$$;
alter function prime.social_party_expire(uuid) rename to social_party_expire_legacy;
create function prime.social_party_expire(p_actor uuid)
returns void language plpgsql security invoker set search_path = prime, pg_temp as $$
begin
    perform pg_advisory_xact_lock(6384340261223951::bigint);
    -- Transaction start time can predate a wait for the lifecycle lock.
    update prime.social_party_invites set status='expired',
        responded_at=coalesce(responded_at,clock_timestamp())
    where status='pending' and expires_at <= clock_timestamp()
        and (sender_id=p_actor or recipient_id=p_actor);
end;
$$;

create function prime.social_native_party_snapshot(p_actor uuid)
returns jsonb language plpgsql security invoker set search_path = prime, pg_temp as $$
declare
    v_state jsonb;
    v_presence jsonb;
    v_self text;
    v_revision bigint;
    v_members jsonb;
    v_incoming jsonb;
    v_outgoing jsonb;
begin
    perform pg_advisory_xact_lock(6384340261223951::bigint);
    v_state := prime.social_party_snapshot(p_actor);
    select prime_id, native_party_revision into v_self, v_revision
    from prime.social_profiles where player_id = p_actor;
    if v_self is null then return null; end if;
    v_presence := prime.social_presence_snapshot(p_actor);
    select coalesce(jsonb_agg(jsonb_build_object(
        'prime_id', m.value->>'prime_id', 'display_name', m.value->>'display_name',
        'presence', coalesce((select case p.value->>'activity'
            when 'lobby' then 'lobby' when 'in_match' then 'match'
            when 'spectating' then 'match' else 'online' end
            from jsonb_array_elements(v_presence->'players') p
            where p.value->>'prime_id' = m.value->>'prime_id'), 'unknown')
    ) order by m.ordinality), '[]'::jsonb) into v_members
    from jsonb_array_elements(coalesce(v_state->'party'->'members','[]'::jsonb)) with ordinality m;
    select coalesce(jsonb_agg(jsonb_build_object(
        'invite_id', i->>'invite_id', 'party_id', i->>'party_id',
        'from_prime_id', i->>'prime_id', 'to_prime_id', v_self,
        'expires_at', i->>'expires_at') order by i->>'invite_id'), '[]'::jsonb)
    into v_incoming from jsonb_array_elements(v_state->'incoming_party_invites') i;
    select coalesce(jsonb_agg(jsonb_build_object(
        'invite_id', i->>'invite_id', 'party_id', i->>'party_id',
        'from_prime_id', v_self, 'to_prime_id', i->>'prime_id',
        'expires_at', i->>'expires_at') order by i->>'invite_id'), '[]'::jsonb)
    into v_outgoing from jsonb_array_elements(v_state->'outgoing_party_invites') i;
    return jsonb_build_object('self_prime_id',v_self,
        'party_id',coalesce(v_state->'party'->>'party_id',''),
        'leader_prime_id',coalesce(v_state->'party'->>'leader_prime_id',''),
        'server_revision',v_revision, 'members',v_members,
        'incoming',v_incoming,'outgoing',v_outgoing);
end;
$$;

create function prime.social_native_party_action(p_actor uuid,p_action text,
    p_expected_revision bigint,p_party_id uuid,p_target_prime_id text,
    p_invite_id uuid,p_expected_self_prime_id text)
returns jsonb language plpgsql security invoker set search_path = prime, pg_temp as $$
declare
    v_state jsonb;
    v_action text;
    v_result jsonb;
begin
    perform pg_advisory_xact_lock(6384340261223951::bigint);
    v_state := prime.social_native_party_snapshot(p_actor);
    if v_state is null or p_expected_self_prime_id is distinct from v_state->>'self_prime_id' then
        return jsonb_build_object('ok',false,'status','account_changed');
    end if;
    if p_expected_revision is null or p_expected_revision < 0
       or p_expected_revision is distinct from (v_state->>'server_revision')::bigint then
        return jsonb_build_object('ok',false,'status','party_changed');
    end if;
    v_action := case p_action when 'accept_invite' then 'accept'
        when 'decline_invite' then 'decline' when 'cancel_invite' then 'cancel' else p_action end;
    if v_action in ('accept','decline','cancel') then
        if not exists (select 1 from jsonb_array_elements(
            case when v_action = 'cancel' then v_state->'outgoing' else v_state->'incoming' end) i
            where i->>'invite_id' = p_invite_id::text and i->>'party_id' = p_party_id::text) then
            return jsonb_build_object('ok',false,'status','invite_changed');
        end if;
    elsif v_action in ('invite','leave','kick','promote','disband') then
        if coalesce(p_party_id::text,'') is distinct from v_state->>'party_id' then
            return jsonb_build_object('ok',false,'status','party_changed');
        end if;
    else
        return jsonb_build_object('ok',false,'status','invalid_action');
    end if;
    v_result := prime.social_party_action(p_actor,v_action,p_target_prime_id,p_invite_id);
    if v_result->>'ok' = 'true' then
        return jsonb_build_object('ok',true,'snapshot',prime.social_native_party_snapshot(p_actor));
    end if;
    return jsonb_build_object('ok',false,'status',v_result->>'status');
end;
$$;

revoke execute on function prime.social_native_party_revision_touch() from public,anon,authenticated;
revoke execute on function prime.social_party_action(uuid,text,text,uuid) from public,anon,authenticated;
revoke execute on function prime.social_mutate(uuid,text,text) from public,anon,authenticated;
revoke execute on function prime.social_party_expire(uuid) from public,anon,authenticated;
revoke execute on function prime.social_native_party_snapshot(uuid) from public,anon,authenticated;
revoke execute on function prime.social_native_party_action(uuid,text,bigint,uuid,text,uuid,text) from public,anon,authenticated;
