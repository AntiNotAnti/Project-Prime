-- Social Slice 7: authoritative party reservation requests and server-reported admission state.
--
-- PostgreSQL coordinates authenticated party intent. The dedicated server alone
-- owns the actual slot mask and may reject a request even when the database
-- request is valid.

create table if not exists prime.social_party_reservations (
    request_id uuid primary key default gen_random_uuid(),
    party_id uuid not null references prime.social_parties(party_id) on delete cascade,
    leader_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    authority_epoch numeric(20,0) not null,
    include_leader boolean not null,
    requested_count smallint not null,
    status text not null default 'pending',
    reporter_id uuid,
    server_reservation_id uuid,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    expires_at timestamptz not null,
    reserved_until timestamptz,
    constraint social_party_reservation_count check (requested_count between 1 and 8),
    constraint social_party_reservation_epoch check (authority_epoch > 0),
    constraint social_party_reservation_status check (
        status in ('pending','reserved','completed','cancelled','expired','rejected')
    )
);

create unique index if not exists social_party_reservation_one_live_party_idx
    on prime.social_party_reservations (party_id)
    where status in ('pending','reserved');

create index if not exists social_party_reservation_expiry_idx
    on prime.social_party_reservations (expires_at);

create table if not exists prime.social_party_reservation_members (
    request_id uuid not null
        references prime.social_party_reservations(request_id) on delete cascade,
    player_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    slot smallint,
    status text not null default 'pending',
    updated_at timestamptz not null default now(),
    primary key (request_id, player_id),
    constraint social_party_reservation_member_slot check (
        slot is null or slot between 0 and 7
    ),
    constraint social_party_reservation_member_status check (
        status in ('pending','reserved','admitted','declined')
    )
);

create index if not exists social_party_reservation_member_player_idx
    on prime.social_party_reservation_members (player_id, updated_at desc);

create or replace function prime.social_party_reservation_expire(p_party uuid)
returns void
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
begin
    update prime.social_party_reservations
    set status = 'expired', updated_at = now()
    where party_id = p_party
      and status = 'pending'
      and expires_at <= now();

    update prime.social_party_reservations
    set status = 'expired', updated_at = now()
    where party_id = p_party
      and status = 'reserved'
      and coalesce(reserved_until, expires_at) <= now();
end;
$$;

create or replace function prime.social_party_reservation_snapshot(p_actor uuid)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_party uuid;
begin
    select party_id into v_party
    from prime.social_party_members
    where player_id = p_actor;

    if v_party is null then
        return null;
    end if;

    perform prime.social_party_reservation_expire(v_party);

    return (
        select jsonb_build_object(
            'request_id', r.request_id,
            'authority_epoch', r.authority_epoch::text,
            'include_leader', r.include_leader,
            'requested_count', r.requested_count,
            'status', r.status,
            'server_reservation_id', r.server_reservation_id,
            'created_at', r.created_at,
            'expires_at', case
                when r.status = 'reserved'
                    then coalesce(r.reserved_until, r.expires_at)
                else r.expires_at
            end,
            'members', coalesce((
                select jsonb_agg(jsonb_build_object(
                    'prime_id', sp.prime_id,
                    'display_name', pp."DisplayName",
                    'is_self', rm.player_id = p_actor,
                    'slot', rm.slot,
                    'status', rm.status
                ) order by (rm.player_id = r.leader_id) desc, lower(pp."DisplayName"), sp.prime_id)
                from prime.social_party_reservation_members rm
                join prime.social_profiles sp on sp.player_id = rm.player_id
                join prime.player_profiles pp on pp."PlayerId" = rm.player_id
                where rm.request_id = r.request_id
            ), '[]'::jsonb)
        )
        from prime.social_party_reservations r
        where r.party_id = v_party
          and (
              r.status = 'pending' and r.expires_at > now()
              or r.status = 'reserved'
                 and coalesce(r.reserved_until, r.expires_at) > now()
          )
        order by r.created_at desc
        limit 1
    );
end;
$$;

create or replace function prime.social_party_reservation_request(
    p_actor uuid,
    p_authority_epoch numeric,
    p_include_leader boolean
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_party uuid;
    v_leader uuid;
    v_party_size integer;
    v_requested integer;
    v_existing prime.social_party_reservations%rowtype;
    v_request uuid;
begin
    select pm.party_id, p.leader_id
    into v_party, v_leader
    from prime.social_party_members pm
    join prime.social_parties p on p.party_id = pm.party_id
    where pm.player_id = p_actor;

    if v_party is null then
        return jsonb_build_object('ok', false, 'status', 'not_in_party');
    end if;

    perform 1 from prime.social_parties
    where party_id = v_party
    for update;

    if v_leader <> p_actor then
        return jsonb_build_object('ok', false, 'status', 'leader_only');
    end if;

    if p_authority_epoch <= 0 or p_authority_epoch > 18446744073709551615::numeric then
        return jsonb_build_object('ok', false, 'status', 'invalid_authority');
    end if;

    select count(*) into v_party_size
    from prime.social_party_members
    where party_id = v_party;

    if v_party_size < 2 or v_party_size > 8 then
        return jsonb_build_object('ok', false, 'status', 'party_size_invalid');
    end if;

    select count(*) into v_requested
    from prime.social_party_members pm
    where pm.party_id = v_party
      and (p_include_leader or pm.player_id <> p_actor)
      and not exists (
          select 1
          from prime.social_lobby_memberships membership
          where membership.player_id = pm.player_id
            and membership.authority_epoch = p_authority_epoch
            and membership.lobby_eligible
            and membership.expires_at > now()
      );

    if v_requested < 1 then
        return jsonb_build_object('ok', false, 'status', 'reservation_not_needed');
    end if;

    perform prime.social_party_reservation_expire(v_party);

    select * into v_existing
    from prime.social_party_reservations
    where party_id = v_party
      and status in ('pending','reserved')
      and (
          status = 'pending' and expires_at > now()
          or status = 'reserved' and coalesce(reserved_until, expires_at) > now()
      )
    order by created_at desc
    limit 1
    for update;

    if found then
        if v_existing.authority_epoch = p_authority_epoch
           and v_existing.include_leader = p_include_leader
           and v_existing.requested_count = v_requested then
            if v_existing.status = 'pending' then
                update prime.social_party_reservations
                set expires_at = greatest(expires_at, now() + interval '20 seconds'),
                    updated_at = now()
                where request_id = v_existing.request_id;
            end if;
            return jsonb_build_object(
                'ok', true,
                'status', 'reservation_active',
                'reservation', prime.social_party_reservation_snapshot(p_actor)
            );
        end if;

        return jsonb_build_object(
            'ok', false,
            'status', 'reservation_active',
            'reservation', prime.social_party_reservation_snapshot(p_actor)
        );
    end if;

    insert into prime.social_party_reservations(
        party_id, leader_id, authority_epoch, include_leader,
        requested_count, expires_at
    ) values (
        v_party, p_actor, p_authority_epoch, p_include_leader,
        v_requested, now() + interval '20 seconds'
    )
    returning request_id into v_request;

    insert into prime.social_party_reservation_members(request_id, player_id)
    select v_request, pm.player_id
    from prime.social_party_members pm
    where pm.party_id = v_party
      and (p_include_leader or pm.player_id <> p_actor)
      and not exists (
          select 1
          from prime.social_lobby_memberships membership
          where membership.player_id = pm.player_id
            and membership.authority_epoch = p_authority_epoch
            and membership.lobby_eligible
            and membership.expires_at > now()
      );

    return jsonb_build_object(
        'ok', true,
        'status', 'reservation_requested',
        'reservation', prime.social_party_reservation_snapshot(p_actor)
    );
end;
$$;

create or replace function prime.social_party_reservation_cancel(p_actor uuid)
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
    select pm.party_id, p.leader_id
    into v_party, v_leader
    from prime.social_party_members pm
    join prime.social_parties p on p.party_id = pm.party_id
    where pm.player_id = p_actor;

    if v_party is null then
        return jsonb_build_object('ok', false, 'status', 'not_in_party');
    end if;
    if v_leader <> p_actor then
        return jsonb_build_object('ok', false, 'status', 'leader_only');
    end if;

    update prime.social_party_reservations
    set status = 'cancelled', updated_at = now()
    where party_id = v_party
      and status in ('pending','reserved');
    get diagnostics v_changed = row_count;

    return jsonb_build_object(
        'ok', true,
        'status', case when v_changed > 0
            then 'reservation_cancelled' else 'reservation_already_clear' end
    );
end;
$$;

create or replace function prime.social_party_reservation_server_validate(
    p_reporter uuid,
    p_authority_epoch numeric,
    p_request uuid,
    p_player uuid
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_request prime.social_party_reservations%rowtype;
begin
    select * into v_request
    from prime.social_party_reservations
    where request_id = p_request
    for update;

    if not found then
        return jsonb_build_object('ok', false, 'status', 'reservation_not_found');
    end if;

    perform prime.social_party_reservation_expire(v_request.party_id);

    select * into v_request
    from prime.social_party_reservations
    where request_id = p_request;

    if v_request.status not in ('pending','reserved') then
        return jsonb_build_object('ok', false, 'status', 'reservation_closed');
    end if;
    if v_request.authority_epoch <> p_authority_epoch then
        return jsonb_build_object('ok', false, 'status', 'authority_mismatch');
    end if;
    if v_request.reporter_id is not null and v_request.reporter_id <> p_reporter then
        return jsonb_build_object('ok', false, 'status', 'reporter_mismatch');
    end if;
    if not exists (
        select 1 from prime.social_party_reservation_members
        where request_id = p_request
          and player_id = p_player
    ) then
        return jsonb_build_object('ok', false, 'status', 'not_reservation_member');
    end if;

    return jsonb_build_object(
        'ok', true,
        'status', v_request.status,
        'request_id', v_request.request_id,
        'party_id', v_request.party_id,
        'leader_id', v_request.leader_id,
        'authority_epoch', v_request.authority_epoch::text,
        'requested_count', v_request.requested_count,
        'server_reservation_id', v_request.server_reservation_id,
        'expires_at', case
            when v_request.status = 'reserved'
                then coalesce(v_request.reserved_until, v_request.expires_at)
            else v_request.expires_at
        end,
        'members', coalesce((
            select jsonb_agg(rm.player_id order by rm.player_id)
            from prime.social_party_reservation_members rm
            where rm.request_id = p_request
        ), '[]'::jsonb)
    );
end;
$$;

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
        reserved_until = now() + make_interval(secs => p_seconds),
        expires_at = greatest(expires_at, now() + make_interval(secs => p_seconds)),
        updated_at = now()
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
    v_remaining integer;
begin
    select * into v_request
    from prime.social_party_reservations
    where request_id = p_request
    for update;

    if not found
       or v_request.status <> 'reserved'
       or v_request.reporter_id <> p_reporter
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

create or replace function prime.social_party_reservation_server_cancel(
    p_reporter uuid,
    p_request uuid,
    p_reservation uuid,
    p_status text default 'rejected'
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_request prime.social_party_reservations%rowtype;
begin
    if p_status not in ('rejected','cancelled','expired') then
        return jsonb_build_object('ok', false, 'status', 'invalid_status');
    end if;

    select * into v_request
    from prime.social_party_reservations
    where request_id = p_request
    for update;

    if not found then
        return jsonb_build_object('ok', false, 'status', 'reservation_not_found');
    end if;
    if v_request.reporter_id is not null and v_request.reporter_id <> p_reporter then
        return jsonb_build_object('ok', false, 'status', 'reporter_mismatch');
    end if;
    if v_request.server_reservation_id is not null
       and p_reservation <> '00000000-0000-0000-0000-000000000000'::uuid
       and v_request.server_reservation_id <> p_reservation then
        return jsonb_build_object('ok', false, 'status', 'reservation_mismatch');
    end if;

    update prime.social_party_reservations
    set status = p_status, updated_at = now()
    where request_id = p_request
      and status in ('pending','reserved');

    return jsonb_build_object('ok', true, 'status', 'reservation_' || p_status);
end;
$$;

create or replace function prime.social_party_reservation_notify()
returns trigger
language plpgsql
security definer
set search_path = prime, pg_temp
as $$
declare
    v_request uuid;
    v_party uuid;
    v_player uuid;
    v_payload jsonb := jsonb_build_object('kind', 'party_reservation_changed');
begin
    if tg_table_name = 'social_party_reservation_members' then
        v_request := coalesce(new.request_id, old.request_id);
        select party_id into v_party
        from prime.social_party_reservations
        where request_id = v_request;
    else
        v_party := coalesce(new.party_id, old.party_id);
    end if;

    if v_party is null then
        if tg_op = 'DELETE' then return old; else return new; end if;
    end if;

    if to_regprocedure('realtime.send(jsonb,text,text,boolean)') is not null then
        for v_player in
            select player_id from prime.social_party_members
            where party_id = v_party
        loop
            execute 'select realtime.send($1,$2,$3,$4)'
            using v_payload, 'social_changed',
                'social:user:' || v_player::text, true;
        end loop;
    end if;

    if tg_op = 'DELETE' then return old; else return new; end if;
end;
$$;

drop trigger if exists project_prime_social_party_reservation_notify
on prime.social_party_reservations;
create trigger project_prime_social_party_reservation_notify
after insert or update or delete on prime.social_party_reservations
for each row execute function prime.social_party_reservation_notify();

drop trigger if exists project_prime_social_party_reservation_member_notify
on prime.social_party_reservation_members;
create trigger project_prime_social_party_reservation_member_notify
after insert or update or delete on prime.social_party_reservation_members
for each row execute function prime.social_party_reservation_notify();

create or replace function prime.social_party_reservation_roster_changed()
returns trigger
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_party uuid;
begin
    if tg_table_name = 'social_parties' then
        v_party := new.party_id;
    elsif tg_op = 'DELETE' then
        v_party := old.party_id;
    else
        v_party := new.party_id;
    end if;

    update prime.social_party_reservations
    set status = 'cancelled', updated_at = now()
    where party_id = v_party
      and status in ('pending','reserved');

    if tg_op = 'DELETE' then
        return old;
    end if;
    return new;
end;
$$;

drop trigger if exists project_prime_social_party_reservation_roster_change
on prime.social_party_members;
create trigger project_prime_social_party_reservation_roster_change
after insert or delete on prime.social_party_members
for each row execute function prime.social_party_reservation_roster_changed();

drop trigger if exists project_prime_social_party_reservation_leader_change
on prime.social_parties;
create trigger project_prime_social_party_reservation_leader_change
after update of leader_id on prime.social_parties
for each row
when (old.leader_id is distinct from new.leader_id)
execute function prime.social_party_reservation_roster_changed();

alter table prime.social_party_reservations enable row level security;
alter table prime.social_party_reservation_members enable row level security;

revoke all on table prime.social_party_reservations from public, anon, authenticated;
revoke all on table prime.social_party_reservation_members from public, anon, authenticated;

create policy social_party_reservation_member_read
on prime.social_party_reservations for select
to authenticated
using (
    exists (
        select 1 from prime.social_party_members pm
        where pm.party_id = social_party_reservations.party_id
          and pm.player_id = (select auth.uid())
    )
);

create policy social_party_reservation_response_self_read
on prime.social_party_reservation_members for select
to authenticated
using ((select auth.uid()) = player_id);

revoke execute on function prime.social_party_reservation_expire(uuid)
    from public, anon, authenticated;
revoke execute on function prime.social_party_reservation_snapshot(uuid)
    from public, anon, authenticated;
revoke execute on function prime.social_party_reservation_request(uuid,numeric,boolean)
    from public, anon, authenticated;
revoke execute on function prime.social_party_reservation_cancel(uuid)
    from public, anon, authenticated;
revoke execute on function prime.social_party_reservation_server_validate(uuid,numeric,uuid,uuid)
    from public, anon, authenticated;
revoke execute on function prime.social_party_reservation_server_activate(uuid,numeric,uuid,uuid,jsonb,integer)
    from public, anon, authenticated;
revoke execute on function prime.social_party_reservation_server_admitted(uuid,uuid,uuid,uuid)
    from public, anon, authenticated;
revoke execute on function prime.social_party_reservation_server_cancel(uuid,uuid,uuid,text)
    from public, anon, authenticated;
revoke execute on function prime.social_party_reservation_notify()
    from public, anon, authenticated;
revoke execute on function prime.social_party_reservation_roster_changed()
    from public, anon, authenticated;
