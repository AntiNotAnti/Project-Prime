-- Social Slice 4: authenticated lobby identities, durable game invites,
-- Join Friend resolution and private Realtime invalidation.
--
-- UDP endpoints remain control-plane data. A resolved locator is never trusted
-- by the client until it is independently matched against the live public
-- directory and the server's authoritative epoch.

create table if not exists prime.social_lobbies (
    lobby_id uuid primary key default gen_random_uuid(),
    owner_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    host_address inet not null,
    port integer not null,
    authority_epoch numeric(20,0) not null,
    protocol integer not null,
    room_key text not null,
    server_name text not null default '',
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    expires_at timestamptz not null,
    constraint social_lobbies_port check (port between 1 and 65535),
    constraint social_lobbies_epoch check (authority_epoch > 0),
    constraint social_lobbies_protocol check (protocol between 1 and 255),
    constraint social_lobbies_room_length check (char_length(room_key) between 1 and 128),
    constraint social_lobbies_name_length check (char_length(server_name) <= 96),
    unique (owner_id, authority_epoch)
);

create index if not exists social_lobbies_expiry_idx
    on prime.social_lobbies (expires_at);
create index if not exists social_lobbies_owner_expiry_idx
    on prime.social_lobbies (owner_id, expires_at desc);

create table if not exists prime.game_invites (
    invite_id uuid primary key default gen_random_uuid(),
    sender_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    recipient_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    lobby_id uuid not null references prime.social_lobbies(lobby_id) on delete cascade,
    status text not null default 'pending',
    created_at timestamptz not null default now(),
    expires_at timestamptz not null,
    responded_at timestamptz,
    constraint game_invites_not_self check (sender_id <> recipient_id),
    constraint game_invites_status check (
        status in ('pending', 'accepted', 'declined', 'cancelled', 'expired')
    )
);

create index if not exists game_invites_recipient_created_idx
    on prime.game_invites (recipient_id, created_at desc);
create index if not exists game_invites_sender_created_idx
    on prime.game_invites (sender_id, created_at desc);
create unique index if not exists game_invites_one_pending_pair_lobby_idx
    on prime.game_invites (sender_id, recipient_id, lobby_id)
    where status = 'pending';

alter table prime.social_presence_sessions
    add column if not exists lobby_id uuid
        references prime.social_lobbies(lobby_id) on delete set null;

create index if not exists social_presence_lobby_idx
    on prime.social_presence_sessions (lobby_id)
    where lobby_id is not null;

create or replace function prime.social_lobby_locator(p_lobby_id uuid)
returns jsonb
language sql
stable
security invoker
set search_path = prime, pg_temp
as $$
    select jsonb_build_object(
        'lobby_id', l.lobby_id,
        'host', host(l.host_address),
        'port', l.port,
        'authority_epoch', l.authority_epoch::text,
        'protocol', l.protocol,
        'room_key', l.room_key,
        'server_name', l.server_name,
        'expires_at', l.expires_at
    )
    from prime.social_lobbies l
    where l.lobby_id = p_lobby_id
      and l.expires_at > now()
$$;

create or replace function prime.social_lobby_register(
    p_actor uuid,
    p_host inet,
    p_port integer,
    p_authority_epoch numeric,
    p_protocol integer,
    p_room_key text,
    p_server_name text
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_lobby uuid;
begin
    if not exists (
        select 1 from prime.social_profiles where player_id = p_actor
    ) then
        return jsonb_build_object('ok', false, 'status', 'profile_required');
    end if;
    if family(p_host) <> 4
       or p_host <<= inet '0.0.0.0/8'
       or p_host <<= inet '10.0.0.0/8'
       or p_host <<= inet '100.64.0.0/10'
       or p_host <<= inet '127.0.0.0/8'
       or p_host <<= inet '169.254.0.0/16'
       or p_host <<= inet '172.16.0.0/12'
       or p_host <<= inet '192.168.0.0/16'
       or p_host <<= inet '224.0.0.0/4'
       or p_host <<= inet '240.0.0.0/4' then
        return jsonb_build_object('ok', false, 'status', 'public_endpoint_required');
    end if;
    if p_port < 1 or p_port > 65535
       or p_protocol < 1 or p_protocol > 255
       or p_authority_epoch <= 0
       or char_length(btrim(p_room_key)) not between 1 and 128
       or char_length(coalesce(p_server_name, '')) > 96 then
        return jsonb_build_object('ok', false, 'status', 'invalid_lobby');
    end if;

    insert into prime.social_lobbies (
        owner_id, host_address, port, authority_epoch, protocol,
        room_key, server_name, updated_at, expires_at
    )
    values (
        p_actor, p_host, p_port, p_authority_epoch, p_protocol,
        btrim(p_room_key), left(coalesce(p_server_name, ''), 96),
        now(), now() + interval '90 seconds'
    )
    on conflict (owner_id, authority_epoch) do update
    set host_address = excluded.host_address,
        port = excluded.port,
        protocol = excluded.protocol,
        room_key = excluded.room_key,
        server_name = excluded.server_name,
        updated_at = now(),
        expires_at = now() + interval '90 seconds'
    returning lobby_id into v_lobby;

    -- Keep the registry bounded without erasing fresh invite history.
    delete from prime.social_lobbies l
    where l.owner_id = p_actor
      and l.expires_at < now() - interval '1 day';

    return jsonb_build_object(
        'ok', true,
        'status', 'lobby_registered',
        'lobby', prime.social_lobby_locator(v_lobby)
    );
end;
$$;

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
            ps.lobby_id,
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
            l.joinable and sl.lobby_id is not null and sl.expires_at > now()
                as joinable,
            case when sl.expires_at > now() then sl.lobby_id else null end as lobby_id,
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
        left join prime.social_lobbies sl on sl.lobby_id = l.lobby_id
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
                    when r.activity_visibility = 'everyone'
                       or (r.activity_visibility = 'friends' and r.is_friend)
                    then nullif(r.room_key, '')
                    else null
                end,
            'lobby_id',
                case
                    when (r.activity_visibility = 'everyone'
                       or (r.activity_visibility = 'friends' and r.is_friend))
                      and r.joinable
                    then r.lobby_id
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
    p_room_key text,
    p_joinable boolean,
    p_lobby_id uuid
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_lobby_room text;
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

    if p_activity <> 'lobby' then
        p_lobby_id := null;
        p_joinable := false;
    elsif p_lobby_id is not null then
        select l.room_key into v_lobby_room
        from prime.social_lobbies l
        where l.lobby_id = p_lobby_id
          and l.owner_id = p_actor
          and l.expires_at > now();

        if v_lobby_room is null then
            p_lobby_id := null;
            p_joinable := false;
        else
            p_room_key := v_lobby_room;
            p_joinable := true;
        end if;
    else
        p_joinable := false;
    end if;

    if p_activity not in ('lobby', 'in_match', 'spectating') then
        p_room_key := null;
    end if;

    insert into prime.social_presence_sessions (
        player_id, session_id, activity, room_key, joinable, lobby_id, updated_at
    )
    values (
        p_actor, p_session_id, p_activity, nullif(btrim(p_room_key), ''),
        p_joinable, p_lobby_id, now()
    )
    on conflict (player_id, session_id) do update
    set activity = excluded.activity,
        room_key = excluded.room_key,
        joinable = excluded.joinable,
        lobby_id = excluded.lobby_id,
        updated_at = now();

    delete from prime.social_presence_sessions
    where player_id = p_actor
      and updated_at < now() - interval '5 minutes';

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

-- Compatibility wrapper for Slice 2 clients while rollout is staggered.
create or replace function prime.social_presence_heartbeat(
    p_actor uuid,
    p_session_id uuid,
    p_activity text,
    p_room_key text default null,
    p_joinable boolean default false
)
returns jsonb
language sql
security invoker
set search_path = prime, pg_temp
as $$
    select prime.social_presence_heartbeat(
        p_actor, p_session_id, p_activity, p_room_key, false, null
    )
$$;

create or replace function prime.social_invites_expire(p_actor uuid)
returns void
language sql
security invoker
set search_path = prime, pg_temp
as $$
    update prime.game_invites
    set status = 'expired',
        responded_at = coalesce(responded_at, now())
    where status in ('pending', 'accepted')
      and expires_at <= now()
      and (sender_id = p_actor or recipient_id = p_actor)
$$;

create or replace function prime.social_invites_snapshot(p_actor uuid)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_incoming jsonb;
    v_outgoing jsonb;
begin
    perform prime.social_invites_expire(p_actor);

    select coalesce(jsonb_agg(jsonb_build_object(
        'invite_id', i.invite_id,
        'prime_id', sp.prime_id,
        'display_name', pp."DisplayName",
        'lobby_id', i.lobby_id,
        'room_key', l.room_key,
        'server_name', l.server_name,
        'status', i.status,
        'created_at', i.created_at,
        'expires_at', i.expires_at
    ) order by i.created_at desc), '[]'::jsonb)
    into v_incoming
    from prime.game_invites i
    join prime.social_profiles sp on sp.player_id = i.sender_id
    join prime.player_profiles pp on pp."PlayerId" = i.sender_id
    join prime.social_lobbies l on l.lobby_id = i.lobby_id
    where i.recipient_id = p_actor
      and i.status in ('pending', 'accepted')
      and i.expires_at > now();

    select coalesce(jsonb_agg(jsonb_build_object(
        'invite_id', i.invite_id,
        'prime_id', sp.prime_id,
        'display_name', pp."DisplayName",
        'lobby_id', i.lobby_id,
        'room_key', l.room_key,
        'server_name', l.server_name,
        'status', i.status,
        'created_at', i.created_at,
        'expires_at', i.expires_at
    ) order by i.created_at desc), '[]'::jsonb)
    into v_outgoing
    from prime.game_invites i
    join prime.social_profiles sp on sp.player_id = i.recipient_id
    join prime.player_profiles pp on pp."PlayerId" = i.recipient_id
    join prime.social_lobbies l on l.lobby_id = i.lobby_id
    where i.sender_id = p_actor
      and i.status in ('pending', 'accepted')
      and i.expires_at > now();

    return jsonb_build_object(
        'incoming', v_incoming,
        'outgoing', v_outgoing
    );
end;
$$;

create or replace function prime.social_invite_send(
    p_actor uuid,
    p_target_prime_id text,
    p_lobby_id uuid
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_target uuid;
    v_policy text;
    v_invite uuid;
    v_pending integer;
begin
    perform prime.social_invites_expire(p_actor);

    select player_id into v_target
    from prime.social_profiles
    where prime_id = upper(btrim(p_target_prime_id));

    if v_target is null then
        return jsonb_build_object('ok', false, 'status', 'player_not_found');
    end if;
    if v_target = p_actor then
        return jsonb_build_object('ok', false, 'status', 'self_target');
    end if;
    if not exists (
        select 1 from prime.social_lobbies
        where lobby_id = p_lobby_id
          and owner_id = p_actor
          and expires_at > now()
    ) then
        return jsonb_build_object('ok', false, 'status', 'lobby_unavailable');
    end if;
    if exists (
        select 1 from prime.player_blocks
        where (blocker_id = p_actor and blocked_id = v_target)
           or (blocker_id = v_target and blocked_id = p_actor)
    ) then
        return jsonb_build_object('ok', false, 'status', 'blocked');
    end if;

    select invite_policy into v_policy
    from prime.social_settings
    where player_id = v_target;

    if v_policy = 'nobody' then
        return jsonb_build_object('ok', false, 'status', 'invites_disabled');
    end if;
    if v_policy = 'friends'
       and not exists (
           select 1 from prime.friendships f
           where (f.player_a = p_actor and f.player_b = v_target)
              or (f.player_b = p_actor and f.player_a = v_target)
       ) then
        return jsonb_build_object('ok', false, 'status', 'friends_only');
    end if;

    select invite_id into v_invite
    from prime.game_invites
    where sender_id = p_actor
      and recipient_id = v_target
      and lobby_id = p_lobby_id
      and status = 'pending'
      and expires_at > now()
    order by created_at desc
    limit 1;

    if v_invite is not null then
        return jsonb_build_object(
            'ok', true,
            'status', 'invite_pending',
            'invite_id', v_invite,
            'snapshot', prime.social_invites_snapshot(p_actor)
        );
    end if;

    if exists (
        select 1 from prime.game_invites
        where sender_id = p_actor
          and recipient_id = v_target
          and created_at > now() - interval '5 seconds'
    ) then
        return jsonb_build_object('ok', false, 'status', 'invite_cooldown');
    end if;

    select count(*) into v_pending
    from prime.game_invites
    where sender_id = p_actor
      and status = 'pending'
      and expires_at > now();

    if v_pending >= 20 then
        return jsonb_build_object('ok', false, 'status', 'invite_limit');
    end if;

    insert into prime.game_invites (
        sender_id, recipient_id, lobby_id, expires_at
    )
    values (
        p_actor, v_target, p_lobby_id, now() + interval '5 minutes'
    )
    returning invite_id into v_invite;

    return jsonb_build_object(
        'ok', true,
        'status', 'invite_sent',
        'invite_id', v_invite,
        'snapshot', prime.social_invites_snapshot(p_actor)
    );
end;
$$;

create or replace function prime.social_invite_action(
    p_actor uuid,
    p_invite_id uuid,
    p_action text
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_invite prime.game_invites%rowtype;
    v_locator jsonb;
begin
    perform prime.social_invites_expire(p_actor);

    select * into v_invite
    from prime.game_invites
    where invite_id = p_invite_id
    for update;

    if not found then
        return jsonb_build_object('ok', false, 'status', 'invite_not_found');
    end if;

    if exists (
        select 1 from prime.player_blocks
        where (blocker_id = v_invite.sender_id and blocked_id = v_invite.recipient_id)
           or (blocker_id = v_invite.recipient_id and blocked_id = v_invite.sender_id)
    ) then
        update prime.game_invites
        set status = 'cancelled', responded_at = now()
        where invite_id = p_invite_id
          and status in ('pending', 'accepted');
        return jsonb_build_object('ok', false, 'status', 'blocked');
    end if;

    if p_action = 'accept' then
        if v_invite.recipient_id <> p_actor then
            return jsonb_build_object('ok', false, 'status', 'not_recipient');
        end if;
        if v_invite.status not in ('pending', 'accepted') then
            return jsonb_build_object('ok', false, 'status', 'invite_closed');
        end if;
        if v_invite.expires_at <= now() then
            update prime.game_invites
            set status = 'expired', responded_at = coalesce(responded_at, now())
            where invite_id = p_invite_id;
            return jsonb_build_object('ok', false, 'status', 'invite_expired');
        end if;

        update prime.game_invites
        set status = 'accepted', responded_at = coalesce(responded_at, now())
        where invite_id = p_invite_id;
        v_locator := prime.social_lobby_locator(v_invite.lobby_id);
        if v_locator is null then
            return jsonb_build_object('ok', false, 'status', 'lobby_unavailable');
        end if;
        return jsonb_build_object(
            'ok', true,
            'status', 'invite_accepted',
            'locator', v_locator,
            'snapshot', prime.social_invites_snapshot(p_actor)
        );

    elsif p_action = 'decline' then
        if v_invite.recipient_id <> p_actor or v_invite.status <> 'pending' then
            return jsonb_build_object('ok', false, 'status', 'invite_closed');
        end if;
        update prime.game_invites
        set status = 'declined', responded_at = now()
        where invite_id = p_invite_id;
        return jsonb_build_object(
            'ok', true,
            'status', 'invite_declined',
            'snapshot', prime.social_invites_snapshot(p_actor)
        );

    elsif p_action = 'cancel' then
        if v_invite.sender_id <> p_actor or v_invite.status not in ('pending', 'accepted') then
            return jsonb_build_object('ok', false, 'status', 'invite_closed');
        end if;
        update prime.game_invites
        set status = 'cancelled', responded_at = now()
        where invite_id = p_invite_id;
        return jsonb_build_object(
            'ok', true,
            'status', 'invite_cancelled',
            'snapshot', prime.social_invites_snapshot(p_actor)
        );
    end if;

    return jsonb_build_object('ok', false, 'status', 'invalid_action');
end;
$$;

create or replace function prime.social_invite_resolve(
    p_actor uuid,
    p_invite_id uuid
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_invite prime.game_invites%rowtype;
    v_locator jsonb;
begin
    perform prime.social_invites_expire(p_actor);

    select * into v_invite
    from prime.game_invites
    where invite_id = p_invite_id;

    if not found or v_invite.recipient_id <> p_actor
       or v_invite.status not in ('pending', 'accepted')
       or v_invite.expires_at <= now() then
        return jsonb_build_object('ok', false, 'status', 'invite_unavailable');
    end if;
    if exists (
        select 1 from prime.player_blocks
        where (blocker_id = v_invite.sender_id and blocked_id = v_invite.recipient_id)
           or (blocker_id = v_invite.recipient_id and blocked_id = v_invite.sender_id)
    ) then
        return jsonb_build_object('ok', false, 'status', 'blocked');
    end if;

    v_locator := prime.social_lobby_locator(v_invite.lobby_id);
    if v_locator is null then
        return jsonb_build_object('ok', false, 'status', 'lobby_unavailable');
    end if;

    return jsonb_build_object(
        'ok', true,
        'status', 'invite_resolved',
        'locator', v_locator
    );
end;
$$;

create or replace function prime.social_join_friend(
    p_actor uuid,
    p_target_prime_id text
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_target uuid;
    v_lobby uuid;
    v_presence text;
    v_activity text;
    v_locator jsonb;
begin
    select sp.player_id, st.presence_visibility, st.activity_visibility
    into v_target, v_presence, v_activity
    from prime.social_profiles sp
    join prime.social_settings st on st.player_id = sp.player_id
    where sp.prime_id = upper(btrim(p_target_prime_id));

    if v_target is null then
        return jsonb_build_object('ok', false, 'status', 'player_not_found');
    end if;
    if not exists (
        select 1 from prime.friendships f
        where (f.player_a = p_actor and f.player_b = v_target)
           or (f.player_b = p_actor and f.player_a = v_target)
    ) then
        return jsonb_build_object('ok', false, 'status', 'friends_only');
    end if;
    if exists (
        select 1 from prime.player_blocks
        where (blocker_id = p_actor and blocked_id = v_target)
           or (blocker_id = v_target and blocked_id = p_actor)
    ) then
        return jsonb_build_object('ok', false, 'status', 'blocked');
    end if;
    if v_presence = 'hidden' or v_activity = 'private' then
        return jsonb_build_object('ok', false, 'status', 'not_joinable');
    end if;

    select ps.lobby_id into v_lobby
    from prime.social_presence_sessions ps
    join prime.social_lobbies l on l.lobby_id = ps.lobby_id
    where ps.player_id = v_target
      and ps.updated_at > now() - interval '45 seconds'
      and ps.activity = 'lobby'
      and ps.joinable
      and ps.lobby_id is not null
      and l.expires_at > now()
    order by ps.updated_at desc
    limit 1;

    if v_lobby is null then
        return jsonb_build_object('ok', false, 'status', 'not_joinable');
    end if;

    v_locator := prime.social_lobby_locator(v_lobby);
    return jsonb_build_object(
        'ok', true,
        'status', 'friend_resolved',
        'locator', v_locator
    );
end;
$$;

create or replace function prime.social_invite_notify()
returns trigger
language plpgsql
security definer
set search_path = prime, pg_temp
as $$
declare
    v_payload jsonb;
begin
    if to_regprocedure('realtime.send(jsonb,text,text,boolean)') is null then
        return new;
    end if;

    v_payload := jsonb_build_object(
        'kind', 'invite_changed',
        'invite_id', new.invite_id,
        'status', new.status
    );

    execute 'select realtime.send($1,$2,$3,$4)'
    using v_payload, 'social_changed',
        'social:user:' || new.recipient_id::text, true;

    if new.sender_id <> new.recipient_id then
        execute 'select realtime.send($1,$2,$3,$4)'
        using v_payload, 'social_changed',
            'social:user:' || new.sender_id::text, true;
    end if;
    return new;
end;
$$;

drop trigger if exists project_prime_social_invite_notify on prime.game_invites;
create trigger project_prime_social_invite_notify
after insert or update of status on prime.game_invites
for each row execute function prime.social_invite_notify();

create or replace function prime.social_block_invite_cleanup()
returns trigger
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
begin
    update prime.game_invites
    set status = 'cancelled',
        responded_at = now()
    where status in ('pending', 'accepted')
      and ((sender_id = new.blocker_id and recipient_id = new.blocked_id)
        or (sender_id = new.blocked_id and recipient_id = new.blocker_id));
    return new;
end;
$$;

drop trigger if exists project_prime_social_block_invite_cleanup on prime.player_blocks;
create trigger project_prime_social_block_invite_cleanup
after insert on prime.player_blocks
for each row execute function prime.social_block_invite_cleanup();

alter table prime.social_lobbies enable row level security;
alter table prime.game_invites enable row level security;

revoke all on table prime.social_lobbies from public, anon, authenticated;
revoke all on table prime.game_invites from public, anon, authenticated;

create policy social_lobbies_owner_read
on prime.social_lobbies for select
to authenticated
using ((select auth.uid()) = owner_id);

create policy game_invites_party_read
on prime.game_invites for select
to authenticated
using ((select auth.uid()) = sender_id or (select auth.uid()) = recipient_id);

revoke execute on function prime.social_lobby_locator(uuid) from public, anon, authenticated;
revoke execute on function prime.social_lobby_register(uuid, inet, integer, numeric, integer, text, text)
    from public, anon, authenticated;
revoke execute on function prime.social_presence_heartbeat(uuid, uuid, text, text, boolean, uuid)
    from public, anon, authenticated;
revoke execute on function prime.social_presence_heartbeat(uuid, uuid, text, text, boolean)
    from public, anon, authenticated;
revoke execute on function prime.social_invites_expire(uuid) from public, anon, authenticated;
revoke execute on function prime.social_invites_snapshot(uuid) from public, anon, authenticated;
revoke execute on function prime.social_invite_send(uuid, text, uuid) from public, anon, authenticated;
revoke execute on function prime.social_invite_action(uuid, uuid, text) from public, anon, authenticated;
revoke execute on function prime.social_invite_resolve(uuid, uuid) from public, anon, authenticated;
revoke execute on function prime.social_join_friend(uuid, text) from public, anon, authenticated;
revoke execute on function prime.social_invite_notify() from public, anon, authenticated;
revoke execute on function prime.social_block_invite_cleanup() from public, anon, authenticated;

-- Realtime Authorization is available only in a real Supabase database. The
-- disposable PGlite contract database has no realtime schema, so create the
-- private receive policy conditionally.
do $$
begin
    if to_regclass('realtime.messages') is not null then
        execute 'drop policy if exists project_prime_social_notifications on realtime.messages';
        execute $policy$
            create policy project_prime_social_notifications
            on realtime.messages
            for select
            to authenticated
            using (
                realtime.messages.extension = 'broadcast'
                and (select realtime.topic())
                    = 'social:user:' || (select auth.uid())::text
            )
        $policy$;
    end if;
end;
$$;
