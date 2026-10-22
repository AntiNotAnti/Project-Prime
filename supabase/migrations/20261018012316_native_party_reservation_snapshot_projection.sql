-- Capture an existing group intent alongside the native Party/travel snapshot.
-- Capability hints can disappear after this read; callers retain this evidence
-- and fail closed instead of silently switching a group journey to solo join.
alter function prime.social_native_party_travel_state(uuid) rename to social_native_party_travel_state_pre_reservation_v1;
create function prime.social_native_party_travel_state(p_actor uuid)
returns jsonb language plpgsql security invoker set search_path=prime,pg_temp as $$
declare v_state jsonb; v_request uuid; v_reservation jsonb;
begin
    perform pg_advisory_xact_lock(6384340261223951::bigint);
    v_state:=prime.social_native_party_travel_state_pre_reservation_v1(p_actor);
    if v_state is null then return null; end if;
    -- Preserve intent evidence independently of the reporter/capability probe.
    -- Admission performs its stricter current authority and privacy gates.
    select reservation.request_id into v_request
    from prime.social_party_reservations reservation
    join prime.social_party_members member on member.party_id=reservation.party_id and member.player_id=p_actor
    where reservation.native_witness is not null and reservation.status in ('pending','reserved')
      and not reservation.native_cancelled
      and reservation.party_id::text=v_state->>'party_id'
      and reservation.native_witness->>'travel_id'=v_state->'travel'->>'travel_id'
      and reservation.native_witness->>'travel_revision'=v_state->'travel'->>'revision'
      and least(reservation.expires_at,coalesce(reservation.reserved_until,reservation.expires_at))>clock_timestamp()
    order by reservation.created_at desc limit 1;
    if v_request is not null then
        v_reservation:=prime.social_native_reservation_projection(v_request,p_actor);
    end if;
    return v_state || jsonb_build_object('reservation',v_reservation);
end;
$$;
revoke execute on function prime.social_native_party_travel_state(uuid) from public,anon,authenticated;
-- The renamed predecessor retains its original closed ACL and invoker security.
revoke execute on function prime.social_native_party_travel_state_pre_reservation_v1(uuid) from public,anon,authenticated;
