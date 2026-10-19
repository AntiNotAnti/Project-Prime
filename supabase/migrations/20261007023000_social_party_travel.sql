-- Social Slice 6: party travel intents, follow/regroup state and consent.

create table if not exists prime.social_party_travel (
    party_id uuid primary key references prime.social_parties(party_id) on delete cascade,
    travel_id uuid not null unique default gen_random_uuid(),
    leader_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    lobby_id uuid not null references prime.social_lobbies(lobby_id) on delete cascade,
    revision integer not null default 1,
    reason text not null default 'leader_lobby',
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    expires_at timestamptz not null,
    constraint social_party_travel_revision check (revision between 1 and 1000000),
    constraint social_party_travel_reason check (
        reason in ('leader_lobby','quick_play','regroup')
    )
);

create index if not exists social_party_travel_expiry_idx
    on prime.social_party_travel (expires_at);

create table if not exists prime.social_party_travel_responses (
    party_id uuid not null references prime.social_party_travel(party_id) on delete cascade,
    player_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    travel_id uuid not null,
    revision integer not null,
    status text not null default 'pending',
    updated_at timestamptz not null default now(),
    primary key (party_id, player_id),
    constraint social_party_travel_response_revision check (
        revision between 1 and 1000000
    ),
    constraint social_party_travel_response_status check (
        status in ('pending','following','joined','declined')
    )
);

create index if not exists social_party_travel_response_player_idx
    on prime.social_party_travel_responses (player_id, updated_at desc);

create or replace function prime.social_party_travel_snapshot(p_actor uuid)
returns jsonb
language plpgsql
stable
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_party uuid;
    v_leader uuid;
    v_travel prime.social_party_travel%rowtype;
begin
    select pm.party_id, p.leader_id
    into v_party, v_leader
    from prime.social_party_members pm
    join prime.social_parties p on p.party_id = pm.party_id
    where pm.player_id = p_actor;

    if v_party is null then
        return null;
    end if;

    select t.* into v_travel
    from prime.social_party_travel t
    join prime.social_lobbies l on l.lobby_id = t.lobby_id
    where t.party_id = v_party
      and t.leader_id = v_leader
      and t.expires_at > now()
      and l.expires_at > now()
      and exists (
          select 1
          from prime.social_lobby_memberships m
          where m.player_id = v_leader
            and m.authority_epoch = l.authority_epoch
            and m.lobby_eligible
            and m.expires_at > now()
      );

    if not found then
        return null;
    end if;

    return (
        select jsonb_build_object(
            'travel_id', v_travel.travel_id,
            'revision', v_travel.revision,
            'reason', v_travel.reason,
            'leader_prime_id', leader_sp.prime_id,
            'leader_display_name', leader_pp."DisplayName",
            'lobby_id', l.lobby_id,
            'room_key', l.room_key,
            'server_name', l.server_name,
            'authority_epoch', l.authority_epoch::text,
            'expires_at', least(v_travel.expires_at, l.expires_at),
            'is_leader', v_leader = p_actor,
            'self_status',
                case
                    when v_leader = p_actor then 'joined'
                    else coalesce(self_response.status, 'pending')
                end,
            'members', coalesce((
                select jsonb_agg(jsonb_build_object(
                    'prime_id', member_sp.prime_id,
                    'display_name', member_pp."DisplayName",
                    'is_leader', pm.player_id = v_leader,
                    'is_self', pm.player_id = p_actor,
                    'status',
                        case
                            when pm.player_id = v_leader then 'joined'
                            else coalesce(response.status, 'pending')
                        end
                ) order by (pm.player_id = v_leader) desc, pm.joined_at)
                from prime.social_party_members pm
                join prime.social_profiles member_sp
                  on member_sp.player_id = pm.player_id
                join prime.player_profiles member_pp
                  on member_pp."PlayerId" = pm.player_id
                left join prime.social_party_travel_responses response
                  on response.party_id = v_party
                 and response.player_id = pm.player_id
                 and response.travel_id = v_travel.travel_id
                 and response.revision = v_travel.revision
                where pm.party_id = v_party
            ), '[]'::jsonb)
        )
        from prime.social_lobbies l
        join prime.social_profiles leader_sp on leader_sp.player_id = v_leader
        join prime.player_profiles leader_pp on leader_pp."PlayerId" = v_leader
        left join prime.social_party_travel_responses self_response
          on self_response.party_id = v_party
         and self_response.player_id = p_actor
         and self_response.travel_id = v_travel.travel_id
         and self_response.revision = v_travel.revision
        where l.lobby_id = v_travel.lobby_id
    );
end;
$$;

create or replace function prime.social_party_travel_publish(
    p_actor uuid,
    p_lobby_id uuid,
    p_reason text default 'leader_lobby'
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_party uuid;
    v_leader uuid;
    v_members integer;
    v_existing prime.social_party_travel%rowtype;
    v_travel_id uuid;
    v_revision integer;
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

    select count(*) into v_members
    from prime.social_party_members
    where party_id = v_party;
    if v_members < 2 then
        delete from prime.social_party_travel where party_id = v_party;
        return jsonb_build_object('ok', false, 'status', 'party_solo');
    end if;

    if p_reason not in ('leader_lobby','quick_play','regroup') then
        return jsonb_build_object('ok', false, 'status', 'invalid_reason');
    end if;

    if not exists (
        select 1
        from prime.social_lobbies l
        where l.lobby_id = p_lobby_id
          and l.owner_id = p_actor
          and l.expires_at > now()
          and exists (
              select 1
              from prime.social_lobby_memberships m
              where m.player_id = p_actor
                and m.authority_epoch = l.authority_epoch
                and m.lobby_eligible
                and m.expires_at > now()
          )
    ) then
        return jsonb_build_object('ok', false, 'status', 'lobby_unavailable');
    end if;

    select * into v_existing
    from prime.social_party_travel
    where party_id = v_party
    for update;

    if found then
        v_travel_id := v_existing.travel_id;
        if v_existing.lobby_id = p_lobby_id
           and v_existing.expires_at > now() then
            v_revision := v_existing.revision;
            update prime.social_party_travel
            set leader_id = p_actor,
                reason = p_reason,
                updated_at = now(),
                expires_at = now() + interval '90 seconds'
            where party_id = v_party;
        else
            if v_existing.revision >= 1000000 then
                v_travel_id := gen_random_uuid();
                v_revision := 1;
            else
                v_revision := v_existing.revision + 1;
            end if;
            update prime.social_party_travel
            set travel_id = v_travel_id,
                leader_id = p_actor,
                lobby_id = p_lobby_id,
                revision = v_revision,
                reason = p_reason,
                created_at = now(),
                updated_at = now(),
                expires_at = now() + interval '90 seconds'
            where party_id = v_party;
        end if;
    else
        insert into prime.social_party_travel(
            party_id, leader_id, lobby_id, reason, expires_at
        ) values (
            v_party, p_actor, p_lobby_id, p_reason,
            now() + interval '90 seconds'
        )
        returning travel_id, revision into v_travel_id, v_revision;
    end if;

    delete from prime.social_party_travel_responses r
    where r.party_id = v_party
      and not exists (
          select 1
          from prime.social_party_members pm
          where pm.party_id = v_party
            and pm.player_id = r.player_id
      );

    insert into prime.social_party_travel_responses(
        party_id, player_id, travel_id, revision, status, updated_at
    )
    select
        v_party, pm.player_id, v_travel_id, v_revision, 'pending', now()
    from prime.social_party_members pm
    where pm.party_id = v_party
      and pm.player_id <> p_actor
    on conflict (party_id, player_id) do update
    set travel_id = excluded.travel_id,
        revision = excluded.revision,
        status = case
            when prime.social_party_travel_responses.travel_id = excluded.travel_id
             and prime.social_party_travel_responses.revision = excluded.revision
            then prime.social_party_travel_responses.status
            else 'pending'
        end,
        updated_at = case
            when prime.social_party_travel_responses.travel_id = excluded.travel_id
             and prime.social_party_travel_responses.revision = excluded.revision
            then prime.social_party_travel_responses.updated_at
            else now()
        end;

    return jsonb_build_object(
        'ok', true,
        'status', 'party_travel_published',
        'travel', prime.social_party_travel_snapshot(p_actor)
    );
end;
$$;

create or replace function prime.social_party_travel_clear(p_actor uuid)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_party uuid;
    v_leader uuid;
    v_deleted integer;
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

    delete from prime.social_party_travel where party_id = v_party;
    get diagnostics v_deleted = row_count;
    return jsonb_build_object(
        'ok', true,
        'status', case when v_deleted > 0
            then 'party_travel_cleared' else 'party_travel_already_clear' end
    );
end;
$$;

create or replace function prime.social_party_travel_respond(
    p_actor uuid,
    p_travel_id uuid,
    p_revision integer,
    p_action text
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_party uuid;
    v_leader uuid;
    v_travel prime.social_party_travel%rowtype;
    v_locator jsonb;
begin
    select pm.party_id, p.leader_id
    into v_party, v_leader
    from prime.social_party_members pm
    join prime.social_parties p on p.party_id = pm.party_id
    where pm.player_id = p_actor;

    if v_party is null then
        return jsonb_build_object('ok', false, 'status', 'not_in_party');
    end if;
    if v_leader = p_actor then
        return jsonb_build_object('ok', false, 'status', 'leader_already_there');
    end if;

    select * into v_travel
    from prime.social_party_travel
    where party_id = v_party
      and travel_id = p_travel_id
      and revision = p_revision
      and expires_at > now()
    for update;

    if not found then
        return jsonb_build_object('ok', false, 'status', 'party_travel_stale');
    end if;
    if v_travel.leader_id <> v_leader then
        return jsonb_build_object('ok', false, 'status', 'party_travel_stale');
    end if;

    if p_action = 'decline' then
        insert into prime.social_party_travel_responses(
            party_id, player_id, travel_id, revision, status, updated_at
        ) values (
            v_party, p_actor, p_travel_id, p_revision, 'declined', now()
        )
        on conflict (party_id, player_id) do update
        set travel_id = excluded.travel_id,
            revision = excluded.revision,
            status = excluded.status,
            updated_at = now();

        return jsonb_build_object(
            'ok', true,
            'status', 'party_travel_declined',
            'travel', prime.social_party_travel_snapshot(p_actor)
        );
    end if;

    if p_action = 'joined' then
        insert into prime.social_party_travel_responses(
            party_id, player_id, travel_id, revision, status, updated_at
        ) values (
            v_party, p_actor, p_travel_id, p_revision, 'joined', now()
        )
        on conflict (party_id, player_id) do update
        set travel_id = excluded.travel_id,
            revision = excluded.revision,
            status = excluded.status,
            updated_at = now();

        return jsonb_build_object(
            'ok', true,
            'status', 'party_travel_joined',
            'travel', prime.social_party_travel_snapshot(p_actor)
        );
    end if;

    if p_action <> 'follow' then
        return jsonb_build_object('ok', false, 'status', 'invalid_action');
    end if;

    if not exists (
        select 1
        from prime.social_lobbies l
        join prime.social_lobby_memberships m
          on m.player_id = v_travel.leader_id
         and m.authority_epoch = l.authority_epoch
         and m.lobby_eligible
         and m.expires_at > now()
        where l.lobby_id = v_travel.lobby_id
          and l.expires_at > now()
    ) then
        return jsonb_build_object('ok', false, 'status', 'lobby_unavailable');
    end if;

    v_locator := prime.social_lobby_locator(v_travel.lobby_id);
    if v_locator is null then
        return jsonb_build_object('ok', false, 'status', 'lobby_unavailable');
    end if;

    insert into prime.social_party_travel_responses(
        party_id, player_id, travel_id, revision, status, updated_at
    ) values (
        v_party, p_actor, p_travel_id, p_revision, 'following', now()
    )
    on conflict (party_id, player_id) do update
    set travel_id = excluded.travel_id,
        revision = excluded.revision,
        status = excluded.status,
        updated_at = now();

    return jsonb_build_object(
        'ok', true,
        'status', 'party_travel_following',
        'locator', v_locator,
        'travel', prime.social_party_travel_snapshot(p_actor)
    );
end;
$$;

create or replace function prime.social_party_travel_notify()
returns trigger
language plpgsql
security definer
set search_path = prime, pg_temp
as $$
declare
    v_party uuid;
    v_player uuid;
    v_payload jsonb := jsonb_build_object('kind', 'party_travel_changed');
begin
    if tg_op = 'DELETE' then
        v_party := old.party_id;
    else
        v_party := new.party_id;
    end if;

    if to_regprocedure('realtime.send(jsonb,text,text,boolean)') is null then
        if tg_op = 'DELETE' then return old; else return new; end if;
    end if;

    for v_player in
        select player_id
        from prime.social_party_members
        where party_id = v_party
    loop
        execute 'select realtime.send($1,$2,$3,$4)'
        using v_payload, 'social_changed',
            'social:user:' || v_player::text, true;
    end loop;

    if tg_op = 'DELETE' then return old; else return new; end if;
end;
$$;

drop trigger if exists project_prime_social_party_travel_notify
on prime.social_party_travel;
create trigger project_prime_social_party_travel_notify
after insert or update or delete on prime.social_party_travel
for each row execute function prime.social_party_travel_notify();

drop trigger if exists project_prime_social_party_travel_response_notify
on prime.social_party_travel_responses;
create trigger project_prime_social_party_travel_response_notify
after insert or update or delete on prime.social_party_travel_responses
for each row execute function prime.social_party_travel_notify();

create or replace function prime.social_party_travel_clear_on_leader_change()
returns trigger
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
begin
    if old.leader_id is distinct from new.leader_id then
        delete from prime.social_party_travel
        where party_id = new.party_id;
    end if;
    return new;
end;
$$;

drop trigger if exists project_prime_social_party_travel_leader_change
on prime.social_parties;
create trigger project_prime_social_party_travel_leader_change
after update of leader_id on prime.social_parties
for each row execute function prime.social_party_travel_clear_on_leader_change();

create or replace function prime.social_party_travel_remove_member()
returns trigger
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
begin
    delete from prime.social_party_travel_responses
    where party_id = old.party_id
      and player_id = old.player_id;

    if (
        select count(*)
        from prime.social_party_members
        where party_id = old.party_id
    ) < 2 then
        delete from prime.social_party_travel
        where party_id = old.party_id;
    end if;

    return old;
end;
$$;

drop trigger if exists project_prime_social_party_travel_member_remove
on prime.social_party_members;
create trigger project_prime_social_party_travel_member_remove
after delete on prime.social_party_members
for each row execute function prime.social_party_travel_remove_member();

alter table prime.social_party_travel enable row level security;
alter table prime.social_party_travel_responses enable row level security;

revoke all on table prime.social_party_travel from public, anon, authenticated;
revoke all on table prime.social_party_travel_responses from public, anon, authenticated;

create policy social_party_travel_member_read
on prime.social_party_travel for select
to authenticated
using (
    exists (
        select 1
        from prime.social_party_members pm
        where pm.party_id = social_party_travel.party_id
          and pm.player_id = (select auth.uid())
    )
);

create policy social_party_travel_response_self_read
on prime.social_party_travel_responses for select
to authenticated
using ((select auth.uid()) = player_id);

revoke execute on function prime.social_party_travel_snapshot(uuid)
    from public, anon, authenticated;
revoke execute on function prime.social_party_travel_publish(uuid,uuid,text)
    from public, anon, authenticated;
revoke execute on function prime.social_party_travel_clear(uuid)
    from public, anon, authenticated;
revoke execute on function prime.social_party_travel_respond(uuid,uuid,integer,text)
    from public, anon, authenticated;
revoke execute on function prime.social_party_travel_notify()
    from public, anon, authenticated;
revoke execute on function prime.social_party_travel_clear_on_leader_change()
    from public, anon, authenticated;
revoke execute on function prime.social_party_travel_remove_member()
    from public, anon, authenticated;
