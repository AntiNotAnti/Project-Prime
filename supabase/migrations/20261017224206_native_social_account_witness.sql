-- Native Friends actions retain the verified public identity captured at
-- admission. The existing Social mutation remains the relationship authority.
create function prime.social_native_mutate(p_actor uuid,p_expected_self text,
    p_target_prime_id text,p_action text)
returns jsonb language plpgsql security invoker set search_path = prime,pg_temp as $$
declare
    v_self text;
begin
    perform pg_advisory_xact_lock(6384340261223951::bigint);
    select prime_id into v_self from prime.social_profiles where player_id=p_actor;
    if v_self is null or v_self is distinct from p_expected_self then
        return jsonb_build_object('ok',false,'status','account_changed');
    end if;
    return prime.social_mutate(p_actor,p_target_prime_id,p_action);
end;
$$;
revoke execute on function prime.social_native_mutate(uuid,text,text,text) from public,anon,authenticated;
