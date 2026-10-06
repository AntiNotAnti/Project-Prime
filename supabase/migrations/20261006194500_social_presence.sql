-- Social Slice 2: bounded online presence, activity privacy, and account-level
-- social privacy preferences. Gameplay/session protocol remains untouched.

create table if not exists prime.social_settings (
    player_id uuid primary key references prime.social_profiles(player_id) on delete cascade,
    presence_visibility text not null default 'everyone',
    activity_visibility text not null default 'friends',
    invite_policy text not null default 'friends',
    updated_at timestamptz not null default now(),
    constraint social_settings_presence_visibility check (
        presence_visibility in ('everyone', 'friends', 'hidden')
    ),
    constraint social_settings_activity_visibility check (
        activity_visibility in ('everyone', 'friends', 'private')
    ),
    constraint social_settings_invite_policy check (
        invite_policy in ('everyone', 'friends', 'nobody')
    )
);

create table if not exists prime.social_presence_sessions (
    player_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    session_id uuid not null,
    activity text not null,
    room_key text,
    joinable boolean not null default false,
    updated_at timestamptz not null default now(),
    primary key (player_id, session_id),
    constraint social_presence_activity check (
        activity in ('online', 'menu', 'lobby', 'in_match', 'spectating')
    ),
    constraint social_presence_room_key_length check (
        room_key is null or char_length(room_key) <= 128
    )
);

create index if not exists social_presence_updated_idx
    on prime.social_presence_sessions (updated_at desc);
create index if not exists social_presence_player_updated_idx
    on prime.social_presence_sessions (player_id, updated_at desc);

insert into prime.social_settings (player_id)
select player_id
from prime.social_profiles
on conflict (player_id) do nothing;

create or replace function prime.social_settings_seed()
returns trigger
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
begin
    insert into prime.social_settings (player_id)
    values (new.player_id)
    on conflict (player_id) do nothing;
    return new;
end;
$$;

drop trigger if exists project_prime_social_settings_seed on prime.social_profiles;
create trigger project_prime_social_settings_seed
after insert on prime.social_profiles
for each row execute function prime.social_settings_seed();

create or replace function prime.social_presence_snapshot(p_actor uuid)
returns jsonb
language plpgsql
stable
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_settings jsonb;
    v_players jsonb;
begin
    select jsonb_build_object(
        'presence_visibility', s.presence_visibility,
        'activity_visibility', s.activity_visibility,
        'invite_policy', s.invite_policy,
        'updated_at', s.updated_at
    )
    into v_settings
    from prime.social_settings s
    where s.player_id = p_actor;

    if v_settings is null then
        return null;
    end if;

    with latest as (
        select distinct on (ps.player_id)
            ps.player_id,
            ps.activity,
            ps.room_key,
            ps.joinable,
            ps.updated_at
        from prime.social_presence_sessions ps
        where ps.updated_at > now() - interval '45 seconds'
        order by ps.player_id, ps.updated_at desc, ps.session_id
    ),
    related as (
        select
            l.player_id,
            p."DisplayName" as display_name,
            sp.prime_id,
            l.activity,
            l.room_key,
            l.joinable,
            l.updated_at,
            st.presence_visibility,
            st.activity_visibility,
            exists (
                select 1
                from prime.friendships f
                where (f.player_a = p_actor and f.player_b = l.player_id)
                   or (f.player_b = p_actor and f.player_a = l.player_id)
            ) as is_friend
        from latest l
        join prime.social_profiles sp on sp.player_id = l.player_id
        join prime.player_profiles p on p."PlayerId" = l.player_id
        join prime.social_settings st on st.player_id = l.player_id
        where l.player_id <> p_actor
          and not exists (
              select 1
              from prime.player_blocks b
              where (b.blocker_id = p_actor and b.blocked_id = l.player_id)
                 or (b.blocker_id = l.player_id and b.blocked_id = p_actor)
          )
    )
    select coalesce(jsonb_agg(
        jsonb_build_object(
            'prime_id', r.prime_id,
            'display_name', r.display_name,
            'activity',
                case
                    when r.activity_visibility = 'everyone'
                      or (r.activity_visibility = 'friends' and r.is_friend)
                    then r.activity
                    else 'online'
                end,
            'room_key',
                case
                    when (r.activity_visibility = 'everyone'
                       or (r.activity_visibility = 'friends' and r.is_friend))
                      and r.joinable
                    then nullif(r.room_key, '')
                    else null
                end,
            'joinable',
                (r.activity_visibility = 'everyone'
                  or (r.activity_visibility = 'friends' and r.is_friend))
                and r.joinable,
            'is_friend', r.is_friend,
            'last_seen', r.updated_at
        )
        order by r.is_friend desc, lower(r.display_name), r.prime_id
    ), '[]'::jsonb)
    into v_players
    from related r
    where r.presence_visibility = 'everyone'
       or (r.presence_visibility = 'friends' and r.is_friend);

    return jsonb_build_object(
        'settings', v_settings,
        'players', v_players,
        'expires_after_seconds', 45
    );
end;
$$;

create or replace function prime.social_presence_heartbeat(
    p_actor uuid,
    p_session_id uuid,
    p_activity text,
    p_room_key text default null,
    p_joinable boolean default false
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
begin
    if not exists (
        select 1 from prime.social_profiles where player_id = p_actor
    ) then
        raise exception 'social profile required';
    end if;
    if p_activity not in ('online', 'menu', 'lobby', 'in_match', 'spectating') then
        raise exception 'invalid social activity';
    end if;
    if p_room_key is not null and char_length(p_room_key) > 128 then
        raise exception 'room key too long';
    end if;

    if p_activity not in ('lobby', 'in_match', 'spectating') then
        p_room_key := null;
        p_joinable := false;
    end if;
    if p_activity <> 'lobby' then
        p_joinable := false;
    end if;

    insert into prime.social_presence_sessions (
        player_id, session_id, activity, room_key, joinable, updated_at
    )
    values (
        p_actor, p_session_id, p_activity, nullif(btrim(p_room_key), ''),
        p_joinable, now()
    )
    on conflict (player_id, session_id) do update
    set activity = excluded.activity,
        room_key = excluded.room_key,
        joinable = excluded.joinable,
        updated_at = now();

    -- A process crash leaves an ephemeral row behind. The online read model
    -- ignores it after 45 seconds; this wider cleanup bounds storage without
    -- letting an unrelated user's heartbeat mutate another account's rows.
    delete from prime.social_presence_sessions
    where player_id = p_actor
      and updated_at < now() - interval '5 minutes';

    -- Account recovery can legitimately create multiple concurrent sessions.
    -- Keep a small bounded set and let the most recent session represent the
    -- player in the online directory.
    delete from prime.social_presence_sessions stale
    where stale.player_id = p_actor
      and stale.session_id in (
          select ps.session_id
          from prime.social_presence_sessions ps
          where ps.player_id = p_actor
          order by ps.updated_at desc, ps.session_id
          offset 4
      );

    return prime.social_presence_snapshot(p_actor);
end;
$$;

create or replace function prime.social_presence_leave(
    p_actor uuid,
    p_session_id uuid
)
returns boolean
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_deleted integer;
begin
    delete from prime.social_presence_sessions
    where player_id = p_actor
      and session_id = p_session_id;
    get diagnostics v_deleted = row_count;
    return v_deleted > 0;
end;
$$;

create or replace function prime.social_privacy_update(
    p_actor uuid,
    p_presence_visibility text,
    p_activity_visibility text,
    p_invite_policy text
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
begin
    if p_presence_visibility not in ('everyone', 'friends', 'hidden')
       or p_activity_visibility not in ('everyone', 'friends', 'private')
       or p_invite_policy not in ('everyone', 'friends', 'nobody') then
        raise exception 'invalid social privacy setting';
    end if;

    insert into prime.social_settings (
        player_id, presence_visibility, activity_visibility, invite_policy, updated_at
    )
    values (
        p_actor, p_presence_visibility, p_activity_visibility, p_invite_policy, now()
    )
    on conflict (player_id) do update
    set presence_visibility = excluded.presence_visibility,
        activity_visibility = excluded.activity_visibility,
        invite_policy = excluded.invite_policy,
        updated_at = now();

    return prime.social_presence_snapshot(p_actor);
end;
$$;

alter table prime.social_settings enable row level security;
alter table prime.social_presence_sessions enable row level security;

revoke all on table prime.social_settings from public, anon, authenticated;
revoke all on table prime.social_presence_sessions from public, anon, authenticated;

create policy social_settings_self_read
on prime.social_settings for select
to authenticated
using ((select auth.uid()) = player_id);

create policy social_presence_self_read
on prime.social_presence_sessions for select
to authenticated
using ((select auth.uid()) = player_id);

revoke execute on function prime.social_settings_seed() from public, anon, authenticated;
revoke execute on function prime.social_presence_snapshot(uuid) from public, anon, authenticated;
revoke execute on function prime.social_presence_heartbeat(uuid, uuid, text, text, boolean)
    from public, anon, authenticated;
revoke execute on function prime.social_presence_leave(uuid, uuid)
    from public, anon, authenticated;
revoke execute on function prime.social_privacy_update(uuid, text, text, text)
    from public, anon, authenticated;
