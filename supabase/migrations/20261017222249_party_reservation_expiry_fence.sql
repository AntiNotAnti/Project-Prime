-- Reject delayed server activation/admission after the database lease expired.
-- Existing signatures, SECURITY INVOKER behavior and execution ACLs are retained.
-- Pending requests use expires_at; reserved requests use reserved_until with the
-- same expires_at fallback as the existing snapshot/validation expiry rules.
-- Neither handler can renew or complete a lease after its effective deadline.

create or replace function prime.social_party_reservation_server_activate(
    p_reporter uuid,
    p_authority_epoch numeric,
    p_request uuid,
    p_reservation uuid,
    p_assignments jsonb,
    p_seconds integer
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_request prime.social_party_reservations%rowtype;
    v_now timestamptz;
    v_count integer;
    v_distinct_players integer;
    v_distinct_slots integer;
begin
    if p_seconds < 10 or p_seconds > 60
       or jsonb_typeof(p_assignments) <> 'array' then
        return jsonb_build_object('ok', false, 'status', 'invalid_activation');
    end if;

    select * into v_request
    from prime.social_party_reservations
    where request_id = p_request
    for update;

    if not found or v_request.status not in ('pending','reserved') then
        return jsonb_build_object('ok', false, 'status', 'reservation_closed');
    end if;
    -- Evaluate wall-clock time after acquiring the row lock: transaction-start
    -- now() could predate a lock wait that consumed the remaining lease.
    v_now := clock_timestamp();
    if (v_request.status = 'pending' and v_request.expires_at <= v_now)
       or (v_request.status = 'reserved'
           and coalesce(v_request.reserved_until, v_request.expires_at) <= v_now) then
        update prime.social_party_reservations
        set status = 'expired', updated_at = v_now
        where request_id = p_request;
        return jsonb_build_object('ok', false, 'status', 'reservation_closed');
    end if;
    if v_request.authority_epoch <> p_authority_epoch then
        return jsonb_build_object('ok', false, 'status', 'authority_mismatch');
    end if;
    if v_request.reporter_id is not null and v_request.reporter_id <> p_reporter then
        return jsonb_build_object('ok', false, 'status', 'reporter_mismatch');
    end if;
    if v_request.server_reservation_id is not null
       and v_request.server_reservation_id <> p_reservation then
        return jsonb_build_object('ok', false, 'status', 'reservation_mismatch');
    end if;

    select count(*),
           count(distinct item->>'player_id'),
           count(distinct (item->>'slot')::integer)
    into v_count, v_distinct_players, v_distinct_slots
    from jsonb_array_elements(p_assignments) item
    where jsonb_typeof(item) = 'object'
      and (item->>'player_id') ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$'
      and (item->>'slot') ~ '^[0-7]$';

    if v_count <> v_request.requested_count
       or v_distinct_players <> v_count
       or v_distinct_slots <> v_count
       or exists (
           select 1
           from jsonb_array_elements(p_assignments) item
           where not exists (
               select 1
               from prime.social_party_reservation_members rm
               where rm.request_id = p_request
                 and rm.player_id = (item->>'player_id')::uuid
           )
       ) then
        return jsonb_build_object('ok', false, 'status', 'assignment_mismatch');
    end if;

    update prime.social_party_reservations
    set status = 'reserved',
        reporter_id = p_reporter,
        server_reservation_id = p_reservation,
        reserved_until = v_now + make_interval(secs => p_seconds),
        expires_at = greatest(expires_at, v_now + make_interval(secs => p_seconds)),
        updated_at = v_now
    where request_id = p_request;

    update prime.social_party_reservation_members rm
    set slot = (item->>'slot')::smallint,
        status = case when rm.status = 'admitted' then 'admitted' else 'reserved' end,
        updated_at = now()
    from jsonb_array_elements(p_assignments) item
    where rm.request_id = p_request
      and rm.player_id = (item->>'player_id')::uuid;

    return jsonb_build_object('ok', true, 'status', 'reservation_reserved');
end;
$$;

create or replace function prime.social_party_reservation_server_admitted(
    p_reporter uuid,
    p_request uuid,
    p_reservation uuid,
    p_player uuid
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_request prime.social_party_reservations%rowtype;
    v_now timestamptz;
    v_remaining integer;
begin
    select * into v_request
    from prime.social_party_reservations
    where request_id = p_request
    for update;

    if not found or v_request.status <> 'reserved' then
        return jsonb_build_object('ok', false, 'status', 'reservation_mismatch');
    end if;
    v_now := clock_timestamp();
    if coalesce(v_request.reserved_until, v_request.expires_at) <= v_now then
        update prime.social_party_reservations
        set status = 'expired', updated_at = v_now
        where request_id = p_request;
        return jsonb_build_object('ok', false, 'status', 'reservation_closed');
    end if;
    if v_request.reporter_id <> p_reporter
       or v_request.server_reservation_id <> p_reservation then
        return jsonb_build_object('ok', false, 'status', 'reservation_mismatch');
    end if;

    update prime.social_party_reservation_members
    set status = 'admitted', updated_at = now()
    where request_id = p_request
      and player_id = p_player
      and status in ('reserved','pending');

    if not found then
        return jsonb_build_object('ok', false, 'status', 'member_not_reserved');
    end if;

    select count(*) into v_remaining
    from prime.social_party_reservation_members
    where request_id = p_request
      and status <> 'admitted';

    if v_remaining = 0 then
        update prime.social_party_reservations
        set status = 'completed', updated_at = now()
        where request_id = p_request;
    end if;

    return jsonb_build_object(
        'ok', true,
        'status', case when v_remaining = 0
            then 'reservation_completed' else 'member_admitted' end
    );
end;
$$;

revoke execute on function prime.social_party_reservation_server_activate(uuid,numeric,uuid,uuid,jsonb,integer)
    from public, anon, authenticated;
revoke execute on function prime.social_party_reservation_server_admitted(uuid,uuid,uuid,uuid)
    from public, anon, authenticated;
