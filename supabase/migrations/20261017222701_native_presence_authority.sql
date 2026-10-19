-- Native presence adds an account witness and an exact settings comparison.
-- Existing presence visibility and TTL rules remain the sole read authority.
create function prime.social_native_presence_snapshot(p_actor uuid)
returns jsonb language sql stable security invoker set search_path = prime,pg_temp as $$
    select jsonb_build_object('self_prime_id',p.prime_id,
        'settings',s.value->'settings','players',s.value->'players',
        'expires_after_seconds',s.value->'expires_after_seconds')
    from prime.social_profiles p
    cross join lateral (select prime.social_presence_snapshot(p_actor) as value) s
    where p.player_id=p_actor and s.value is not null
$$;

create function prime.social_native_presence_action(p_actor uuid,p_action text,p_expected_self text,
    p_session uuid,p_activity text,p_room text,p_expected_settings jsonb,p_settings jsonb)
returns jsonb language plpgsql security invoker set search_path = prime,pg_temp as $$
declare
    v_self text;
    v_settings jsonb;
begin
    select prime_id into v_self from prime.social_profiles where player_id=p_actor;
    if v_self is null or v_self is distinct from p_expected_self then
        return jsonb_build_object('ok',false,'status','account_changed');
    end if;
    if p_action='heartbeat' then
        -- Native callers have no authority to manufacture a lobby/seat token.
        perform prime.social_presence_heartbeat(p_actor,p_session,p_activity,p_room,false,null);
    elsif p_action='leave' then
        perform prime.social_presence_leave(p_actor,p_session);
    elsif p_action='set_privacy' then
        perform 1 from prime.social_settings where player_id=p_actor for update;
        v_settings := prime.social_presence_snapshot(p_actor)->'settings';
        if p_expected_settings is null or p_settings is null
            or (v_settings-'updated_at') is distinct from p_expected_settings then
            return jsonb_build_object('ok',false,'status','privacy_changed');
        end if;
        if jsonb_typeof(p_settings->'do_not_disturb') is distinct from 'boolean' then
            return jsonb_build_object('ok',false,'status','invalid_privacy');
        end if;
        perform prime.social_privacy_update(p_actor,p_settings->>'presence_visibility',
            p_settings->>'activity_visibility',p_settings->>'invite_policy',
            (p_settings->>'do_not_disturb')::boolean);
    else
        return jsonb_build_object('ok',false,'status','invalid_action');
    end if;
    return jsonb_build_object('ok',true,'snapshot',prime.social_native_presence_snapshot(p_actor));
end;
$$;
revoke execute on function prime.social_native_presence_snapshot(uuid) from public,anon,authenticated;
revoke execute on function prime.social_native_presence_action(uuid,text,text,uuid,text,text,jsonb,jsonb) from public,anon,authenticated;
