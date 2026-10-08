-- Automatic join cancellation can only release the exact request it owns.
-- Keep the existing one-argument manual cancellation contract unchanged.
create or replace function prime.social_party_reservation_cancel(p_actor uuid, p_request uuid)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_party uuid;
    v_leader uuid;
    v_changed integer;
begin
    if p_request is null then
        return jsonb_build_object('ok', false, 'status', 'invalid_request_id');
    end if;
    select party_id into v_party from prime.social_party_members where player_id = p_actor;
    if v_party is null then
        return jsonb_build_object('ok', false, 'status', 'not_in_party');
    end if;
    -- Serialize with leadership changes and reservation creation for this party.
    select leader_id into v_leader from prime.social_parties where party_id = v_party for update;
    if v_leader is distinct from p_actor then
        return jsonb_build_object('ok', false, 'status', 'leader_only');
    end if;
    update prime.social_party_reservations
    set status = 'cancelled', updated_at = now()
    where request_id = p_request and party_id = v_party and status in ('pending', 'reserved');
    get diagnostics v_changed = row_count;
    return jsonb_build_object('ok', true, 'status', case when v_changed > 0
        then 'reservation_cancelled' else 'reservation_already_clear' end);
end;
$$;
revoke execute on function prime.social_party_reservation_cancel(uuid,uuid) from public, anon, authenticated;
grant execute on function prime.social_party_reservation_cancel(uuid,uuid) to service_role;
