-- Social Slice 5: parties, recent players and Do Not Disturb.

alter table prime.social_settings
    add column if not exists do_not_disturb boolean not null default false;

alter table prime.social_lobby_memberships
    add column if not exists lobby_eligible boolean not null default false;

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
as $
declare
    v_lobby uuid;
begin
    if not exists (
        select 1 from prime.social_profiles where player_id = p_actor
    ) then
        return jsonb_build_object('ok', false, 'status', 'profile_required');
    end if;
    if not exists (
        select 1 from prime.social_lobby_memberships m
        where m.player_id = p_actor
          and m.authority_epoch = p_authority_epoch
          and m.lobby_eligible
          and m.expires_at > now()
    ) then
        return jsonb_build_object('ok', false, 'status', 'membership_unverified');
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

    delete from prime.social_lobbies l
    where l.owner_id = p_actor
      and l.expires_at < now() - interval '1 day';

    return jsonb_build_object(
        'ok', true,
        'status', 'lobby_registered',
        'lobby', prime.social_lobby_locator(v_lobby)
    );
end;
$;

create table if not exists prime.social_recent_players (
    actor_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    other_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    last_seen timestamptz not null default now(),
    encounters integer not null default 1,
    primary key (actor_id, other_id),
    constraint social_recent_not_self check (actor_id <> other_id),
    constraint social_recent_encounters check (encounters between 1 and 1000000)
);

create index if not exists social_recent_actor_seen_idx
    on prime.social_recent_players (actor_id, last_seen desc);

create table if not exists prime.social_parties (
    party_id uuid primary key default gen_random_uuid(),
    leader_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table if not exists prime.social_party_members (
    party_id uuid not null references prime.social_parties(party_id) on delete cascade,
    player_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    joined_at timestamptz not null default now(),
    primary key (party_id, player_id),
    unique (player_id)
);

create index if not exists social_party_members_party_joined_idx
    on prime.social_party_members (party_id, joined_at);

create table if not exists prime.social_party_invites (
    invite_id uuid primary key default gen_random_uuid(),
    party_id uuid not null references prime.social_parties(party_id) on delete cascade,
    sender_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    recipient_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    status text not null default 'pending',
    created_at timestamptz not null default now(),
    expires_at timestamptz not null,
    responded_at timestamptz,
    constraint social_party_invite_not_self check (sender_id <> recipient_id),
    constraint social_party_invite_status check (
        status in ('pending','accepted','declined','cancelled','expired')
    )
);

create unique index if not exists social_party_invites_one_pending_idx
    on prime.social_party_invites (party_id, recipient_id)
    where status = 'pending';
create index if not exists social_party_invites_recipient_idx
    on prime.social_party_invites (recipient_id, created_at desc);

create or replace function prime.social_recent_touch(
    p_player uuid,
    p_authority_epoch numeric,
    p_reporter uuid
)
returns void
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_now timestamptz := now();
begin
    insert into prime.social_recent_players(actor_id, other_id, last_seen, encounters)
    select p_player, m.player_id, v_now, 1
    from prime.social_lobby_memberships m
    where m.authority_epoch = p_authority_epoch
      and m.reporter_id = p_reporter
      and m.player_id <> p_player
      and m.expires_at > v_now
    on conflict (actor_id, other_id) do update
    set encounters = least(1000000,
        prime.social_recent_players.encounters
        + case when prime.social_recent_players.last_seen < v_now - interval '10 minutes'
               then 1 else 0 end),
        last_seen = v_now;

    insert into prime.social_recent_players(actor_id, other_id, last_seen, encounters)
    select m.player_id, p_player, v_now, 1
    from prime.social_lobby_memberships m
    where m.authority_epoch = p_authority_epoch
      and m.reporter_id = p_reporter
      and m.player_id <> p_player
      and m.expires_at > v_now
    on conflict (actor_id, other_id) do update
    set encounters = least(1000000,
        prime.social_recent_players.encounters
        + case when prime.social_recent_players.last_seen < v_now - interval '10 minutes'
               then 1 else 0 end),
        last_seen = v_now;
end;
$$;

create or replace function prime.social_party_expire(p_actor uuid)
returns void
language sql
security invoker
set search_path = prime, pg_temp
as $$
    update prime.social_party_invites
    set status = 'expired',
        responded_at = coalesce(responded_at, now())
    where status = 'pending'
      and expires_at <= now()
      and (sender_id = p_actor or recipient_id = p_actor)
$$;

create or replace function prime.social_party_snapshot(p_actor uuid)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_party_id uuid;
    v_party jsonb;
    v_incoming jsonb;
    v_outgoing jsonb;
    v_recent jsonb;
begin
    perform prime.social_party_expire(p_actor);

    select party_id into v_party_id
    from prime.social_party_members
    where player_id = p_actor;

    if v_party_id is null then
        v_party := null;
    else
        select jsonb_build_object(
            'party_id', p.party_id,
            'leader_prime_id', leader_sp.prime_id,
            'is_leader', p.leader_id = p_actor,
            'members', coalesce((
                select jsonb_agg(jsonb_build_object(
                    'prime_id', sp.prime_id,
                    'display_name', pp."DisplayName",
                    'is_leader', pm.player_id = p.leader_id,
                    'is_self', pm.player_id = p_actor,
                    'joined_at', pm.joined_at
                ) order by (pm.player_id = p.leader_id) desc, pm.joined_at)
                from prime.social_party_members pm
                join prime.social_profiles sp on sp.player_id = pm.player_id
                join prime.player_profiles pp on pp."PlayerId" = pm.player_id
                where pm.party_id = p.party_id
            ), '[]'::jsonb)
        )
        into v_party
        from prime.social_parties p
        join prime.social_profiles leader_sp on leader_sp.player_id = p.leader_id
        where p.party_id = v_party_id;
    end if;

    select coalesce(jsonb_agg(jsonb_build_object(
        'invite_id', i.invite_id,
        'party_id', i.party_id,
        'prime_id', sp.prime_id,
        'display_name', pp."DisplayName",
        'created_at', i.created_at,
        'expires_at', i.expires_at
    ) order by i.created_at desc), '[]'::jsonb)
    into v_incoming
    from prime.social_party_invites i
    join prime.social_profiles sp on sp.player_id = i.sender_id
    join prime.player_profiles pp on pp."PlayerId" = i.sender_id
    where i.recipient_id = p_actor
      and i.status = 'pending'
      and i.expires_at > now();

    select coalesce(jsonb_agg(jsonb_build_object(
        'invite_id', i.invite_id,
        'party_id', i.party_id,
        'prime_id', sp.prime_id,
        'display_name', pp."DisplayName",
        'created_at', i.created_at,
        'expires_at', i.expires_at
    ) order by i.created_at desc), '[]'::jsonb)
    into v_outgoing
    from prime.social_party_invites i
    join prime.social_profiles sp on sp.player_id = i.recipient_id
    join prime.player_profiles pp on pp."PlayerId" = i.recipient_id
    where i.sender_id = p_actor
      and i.status = 'pending'
      and i.expires_at > now();

    select coalesce(jsonb_agg(jsonb_build_object(
        'prime_id', sp.prime_id,
        'display_name', pp."DisplayName",
        'last_seen', r.last_seen,
        'encounters', r.encounters
    ) order by r.last_seen desc), '[]'::jsonb)
    into v_recent
    from (
        select *
        from prime.social_recent_players r
        where r.actor_id = p_actor
          and not exists (
              select 1 from prime.player_blocks b
              where (b.blocker_id = p_actor and b.blocked_id = r.other_id)
                 or (b.blocker_id = r.other_id and b.blocked_id = p_actor)
          )
        order by r.last_seen desc
        limit 30
    ) r
    join prime.social_profiles sp on sp.player_id = r.other_id
    join prime.player_profiles pp on pp."PlayerId" = r.other_id;

    return jsonb_build_object(
        'party', v_party,
        'incoming_party_invites', v_incoming,
        'outgoing_party_invites', v_outgoing,
        'recent_players', v_recent
    );
end;
$$;

create or replace function prime.social_party_action(
    p_actor uuid,
    p_action text,
    p_target_prime_id text default null,
    p_invite_id uuid default null
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_party uuid;
    v_leader uuid;
    v_target uuid;
    v_invite prime.social_party_invites%rowtype;
    v_members integer;
    v_new_leader uuid;
    v_created boolean := false;
begin
    perform prime.social_party_expire(p_actor);

    select pm.party_id, p.leader_id into v_party, v_leader
    from prime.social_party_members pm
    join prime.social_parties p on p.party_id = pm.party_id
    where pm.player_id = p_actor;

    if v_party is not null then
        perform 1 from prime.social_parties
        where party_id = v_party
        for update;
    end if;

    if p_target_prime_id is not null then
        select player_id into v_target
        from prime.social_profiles
        where prime_id = upper(btrim(p_target_prime_id));
        if v_target is null then
            return jsonb_build_object('ok', false, 'status', 'player_not_found');
        end if;
    end if;

    if p_action = 'invite' then
        if v_target is null or v_target = p_actor then
            return jsonb_build_object('ok', false, 'status', 'invalid_target');
        end if;
        if exists (
            select 1 from prime.player_blocks
            where (blocker_id = p_actor and blocked_id = v_target)
               or (blocker_id = v_target and blocked_id = p_actor)
        ) then
            return jsonb_build_object('ok', false, 'status', 'blocked');
        end if;
        if not exists (
            select 1 from prime.friendships f
            where (f.player_a = p_actor and f.player_b = v_target)
               or (f.player_b = p_actor and f.player_a = v_target)
        ) then
            return jsonb_build_object('ok', false, 'status', 'friends_only');
        end if;
        if exists (
            select 1 from prime.social_settings
            where player_id = v_target and do_not_disturb
        ) then
            return jsonb_build_object('ok', false, 'status', 'do_not_disturb');
        end if;
        if exists (
            select 1 from prime.social_party_members where player_id = v_target
        ) then
            return jsonb_build_object('ok', false, 'status', 'already_in_party');
        end if;

        if v_party is null then
            insert into prime.social_parties(leader_id)
            values (p_actor)
            returning party_id into v_party;
            insert into prime.social_party_members(party_id, player_id)
            values (v_party, p_actor);
            v_leader := p_actor;
            v_created := true;
        end if;

        if v_leader <> p_actor then
            return jsonb_build_object('ok', false, 'status', 'leader_only');
        end if;

        select count(*) into v_members
        from prime.social_party_members
        where party_id = v_party;
        if v_members >= 8 then
            return jsonb_build_object('ok', false, 'status', 'party_full');
        end if;

        select * into v_invite
        from prime.social_party_invites
        where party_id = v_party
          and recipient_id = v_target
          and status = 'pending'
          and expires_at > now()
        order by created_at desc
        limit 1;

        if found then
            return jsonb_build_object(
                'ok', true, 'status', 'party_invite_pending',
                'snapshot', prime.social_party_snapshot(p_actor)
            );
        end if;

        insert into prime.social_party_invites(
            party_id, sender_id, recipient_id, expires_at
        ) values (
            v_party, p_actor, v_target, now() + interval '10 minutes'
        );

        return jsonb_build_object(
            'ok', true,
            'status', case when v_created then 'party_created_and_invited'
                           else 'party_invite_sent' end,
            'snapshot', prime.social_party_snapshot(p_actor)
        );

    elsif p_action in ('accept','decline','cancel') then
        if p_invite_id is null then
            return jsonb_build_object('ok', false, 'status', 'invite_required');
        end if;

        select * into v_invite
        from prime.social_party_invites
        where invite_id = p_invite_id
        for update;
        if not found or v_invite.status <> 'pending'
           or v_invite.expires_at <= now() then
            return jsonb_build_object('ok', false, 'status', 'party_invite_closed');
        end if;

        if p_action = 'accept' then
            if v_invite.recipient_id <> p_actor then
                return jsonb_build_object('ok', false, 'status', 'not_recipient');
            end if;
            if v_party is not null then
                return jsonb_build_object('ok', false, 'status', 'already_in_party');
            end if;
            if exists (
                select 1 from prime.player_blocks
                where (blocker_id = v_invite.sender_id and blocked_id = p_actor)
                   or (blocker_id = p_actor and blocked_id = v_invite.sender_id)
            ) then
                return jsonb_build_object('ok', false, 'status', 'blocked');
            end if;
            perform 1 from prime.social_parties
            where party_id = v_invite.party_id
            for update;
            select count(*) into v_members
            from prime.social_party_members
            where party_id = v_invite.party_id;
            if v_members >= 8 then
                return jsonb_build_object('ok', false, 'status', 'party_full');
            end if;

            insert into prime.social_party_members(party_id, player_id)
            values (v_invite.party_id, p_actor);
            update prime.social_party_invites
            set status = 'accepted', responded_at = now()
            where invite_id = p_invite_id;
            update prime.social_parties
            set updated_at = now()
            where party_id = v_invite.party_id;
            return jsonb_build_object(
                'ok', true, 'status', 'party_joined',
                'snapshot', prime.social_party_snapshot(p_actor)
            );
        end if;

        if p_action = 'decline' then
            if v_invite.recipient_id <> p_actor then
                return jsonb_build_object('ok', false, 'status', 'not_recipient');
            end if;
            update prime.social_party_invites
            set status = 'declined', responded_at = now()
            where invite_id = p_invite_id;
            return jsonb_build_object(
                'ok', true, 'status', 'party_invite_declined',
                'snapshot', prime.social_party_snapshot(p_actor)
            );
        end if;

        if v_invite.sender_id <> p_actor then
            return jsonb_build_object('ok', false, 'status', 'not_sender');
        end if;
        update prime.social_party_invites
        set status = 'cancelled', responded_at = now()
        where invite_id = p_invite_id;
        return jsonb_build_object(
            'ok', true, 'status', 'party_invite_cancelled',
            'snapshot', prime.social_party_snapshot(p_actor)
        );

    elsif p_action = 'leave' then
        if v_party is null then
            return jsonb_build_object('ok', false, 'status', 'not_in_party');
        end if;

        if v_leader = p_actor then
            select player_id into v_new_leader
            from prime.social_party_members
            where party_id = v_party and player_id <> p_actor
            order by joined_at
            limit 1;
            if v_new_leader is null then
                delete from prime.social_parties where party_id = v_party;
                return jsonb_build_object(
                    'ok', true, 'status', 'party_disbanded',
                    'snapshot', prime.social_party_snapshot(p_actor)
                );
            end if;
            update prime.social_parties
            set leader_id = v_new_leader, updated_at = now()
            where party_id = v_party;
        end if;

        delete from prime.social_party_members
        where party_id = v_party and player_id = p_actor;
        return jsonb_build_object(
            'ok', true, 'status', 'party_left',
            'snapshot', prime.social_party_snapshot(p_actor)
        );

    elsif p_action in ('kick','promote') then
        if v_party is null or v_leader <> p_actor then
            return jsonb_build_object('ok', false, 'status', 'leader_only');
        end if;
        if v_target is null or v_target = p_actor
           or not exists (
               select 1 from prime.social_party_members
               where party_id = v_party and player_id = v_target
           ) then
            return jsonb_build_object('ok', false, 'status', 'not_party_member');
        end if;

        if p_action = 'kick' then
            delete from prime.social_party_members
            where party_id = v_party and player_id = v_target;
            update prime.social_parties set updated_at = now()
            where party_id = v_party;
            return jsonb_build_object(
                'ok', true, 'status', 'party_member_removed',
                'snapshot', prime.social_party_snapshot(p_actor)
            );
        end if;

        update prime.social_parties
        set leader_id = v_target, updated_at = now()
        where party_id = v_party;
        return jsonb_build_object(
            'ok', true, 'status', 'party_leader_promoted',
            'snapshot', prime.social_party_snapshot(p_actor)
        );

    elsif p_action = 'disband' then
        if v_party is null or v_leader <> p_actor then
            return jsonb_build_object('ok', false, 'status', 'leader_only');
        end if;
        delete from prime.social_parties where party_id = v_party;
        return jsonb_build_object(
            'ok', true, 'status', 'party_disbanded',
            'snapshot', prime.social_party_snapshot(p_actor)
        );
    end if;

    return jsonb_build_object('ok', false, 'status', 'invalid_action');
end;
$$;

-- Extend account-level privacy while preserving Slice 2/4 callers.
create or replace function prime.social_privacy_update(
    p_actor uuid,
    p_presence_visibility text,
    p_activity_visibility text,
    p_invite_policy text,
    p_do_not_disturb boolean
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
        player_id, presence_visibility, activity_visibility, invite_policy,
        do_not_disturb, updated_at
    )
    values (
        p_actor, p_presence_visibility, p_activity_visibility, p_invite_policy,
        p_do_not_disturb, now()
    )
    on conflict (player_id) do update
    set presence_visibility = excluded.presence_visibility,
        activity_visibility = excluded.activity_visibility,
        invite_policy = excluded.invite_policy,
        do_not_disturb = excluded.do_not_disturb,
        updated_at = now();

    return prime.social_presence_snapshot(p_actor);
end;
$$;

create or replace function prime.social_privacy_update(
    p_actor uuid,
    p_presence_visibility text,
    p_activity_visibility text,
    p_invite_policy text
)
returns jsonb
language sql
security invoker
set search_path = prime, pg_temp
as $$
    select prime.social_privacy_update(
        p_actor, p_presence_visibility, p_activity_visibility, p_invite_policy,
        coalesce((select do_not_disturb from prime.social_settings where player_id = p_actor), false)
    )
$$;

-- Replace settings part of presence snapshot to include DND.
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
        'do_not_disturb', s.do_not_disturb,
        'updated_at', s.updated_at
    )
    into v_settings
    from prime.social_settings s
    where s.player_id = p_actor;

    if v_settings is null then return null; end if;

    with latest as (
        select distinct on (ps.player_id)
            ps.player_id, ps.activity, ps.room_key, ps.joinable, ps.lobby_id, ps.updated_at
        from prime.social_presence_sessions ps
        where ps.updated_at > now() - interval '45 seconds'
        order by ps.player_id, ps.updated_at desc, ps.session_id
    ),
    related as (
        select
            l.player_id, p."DisplayName" as display_name, sp.prime_id,
            l.activity, l.room_key,
            l.joinable and sl.lobby_id is not null and sl.expires_at > now() as joinable,
            case when sl.expires_at > now() then sl.lobby_id else null end as lobby_id,
            l.updated_at, st.presence_visibility, st.activity_visibility,
            exists (
                select 1 from prime.friendships f
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
              select 1 from prime.player_blocks b
              where (b.blocker_id = p_actor and b.blocked_id = l.player_id)
                 or (b.blocker_id = l.player_id and b.blocked_id = p_actor)
          )
    )
    select coalesce(jsonb_agg(jsonb_build_object(
        'prime_id', r.prime_id,
        'display_name', r.display_name,
        'activity', case
            when r.activity_visibility = 'everyone'
              or (r.activity_visibility = 'friends' and r.is_friend)
            then r.activity else 'online' end,
        'room_key', case
            when r.activity_visibility = 'everyone'
              or (r.activity_visibility = 'friends' and r.is_friend)
            then nullif(r.room_key, '') else null end,
        'lobby_id', case
            when (r.activity_visibility = 'everyone'
               or (r.activity_visibility = 'friends' and r.is_friend))
              and r.joinable then r.lobby_id else null end,
        'joinable',
            (r.activity_visibility = 'everyone'
             or (r.activity_visibility = 'friends' and r.is_friend))
            and r.joinable,
        'is_friend', r.is_friend,
        'last_seen', r.updated_at
    ) order by r.is_friend desc, lower(r.display_name), r.prime_id), '[]'::jsonb)
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

-- DND suppresses game invites without changing the configured base policy.
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
    v_dnd boolean;
    v_invite uuid;
    v_pending integer;
begin
    perform prime.social_invites_expire(p_actor);

    select sp.player_id, st.invite_policy, st.do_not_disturb
    into v_target, v_policy, v_dnd
    from prime.social_profiles sp
    join prime.social_settings st on st.player_id = sp.player_id
    where sp.prime_id = upper(btrim(p_target_prime_id));

    if v_target is null then return jsonb_build_object('ok', false, 'status', 'player_not_found'); end if;
    if v_target = p_actor then return jsonb_build_object('ok', false, 'status', 'self_target'); end if;
    if v_dnd then return jsonb_build_object('ok', false, 'status', 'do_not_disturb'); end if;
    if not exists (
        select 1 from prime.social_lobbies
        where lobby_id = p_lobby_id and owner_id = p_actor and expires_at > now()
    ) then return jsonb_build_object('ok', false, 'status', 'lobby_unavailable'); end if;
    if exists (
        select 1 from prime.player_blocks
        where (blocker_id = p_actor and blocked_id = v_target)
           or (blocker_id = v_target and blocked_id = p_actor)
    ) then return jsonb_build_object('ok', false, 'status', 'blocked'); end if;

    if v_policy = 'nobody' then return jsonb_build_object('ok', false, 'status', 'invites_disabled'); end if;
    if v_policy = 'friends'
       and not exists (
           select 1 from prime.friendships f
           where (f.player_a = p_actor and f.player_b = v_target)
              or (f.player_b = p_actor and f.player_a = v_target)
       ) then return jsonb_build_object('ok', false, 'status', 'friends_only'); end if;

    select invite_id into v_invite
    from prime.game_invites
    where sender_id = p_actor and recipient_id = v_target and lobby_id = p_lobby_id
      and status in ('pending','accepted') and expires_at > now()
    order by created_at desc limit 1;

    if v_invite is not null then
        return jsonb_build_object(
            'ok', true, 'status', 'invite_active', 'invite_id', v_invite,
            'snapshot', prime.social_invites_snapshot(p_actor)
        );
    end if;

    if exists (
        select 1 from prime.game_invites
        where sender_id = p_actor and recipient_id = v_target
          and created_at > now() - interval '5 seconds'
    ) then return jsonb_build_object('ok', false, 'status', 'invite_cooldown'); end if;

    select count(*) into v_pending
    from prime.game_invites
    where sender_id = p_actor and status = 'pending' and expires_at > now();
    if v_pending >= 20 then return jsonb_build_object('ok', false, 'status', 'invite_limit'); end if;

    insert into prime.game_invites(sender_id, recipient_id, lobby_id, expires_at)
    values (p_actor, v_target, p_lobby_id, now() + interval '5 minutes')
    returning invite_id into v_invite;

    return jsonb_build_object(
        'ok', true, 'status', 'invite_sent', 'invite_id', v_invite,
        'snapshot', prime.social_invites_snapshot(p_actor)
    );
end;
$$;

create or replace function prime.social_party_notify()
returns trigger
language plpgsql
security definer
set search_path = prime, pg_temp
as $$
declare
    v_payload jsonb;
    v_player uuid;
begin
    if to_regprocedure('realtime.send(jsonb,text,text,boolean)') is null then
        if tg_op = 'DELETE' then return old; else return new; end if;
    end if;

    v_payload := jsonb_build_object('kind', 'party_changed');

    if tg_table_name = 'social_party_invites' then
        execute 'select realtime.send($1,$2,$3,$4)'
        using v_payload, 'social_changed',
            'social:user:' || coalesce(new.recipient_id, old.recipient_id)::text, true;
        execute 'select realtime.send($1,$2,$3,$4)'
        using v_payload, 'social_changed',
            'social:user:' || coalesce(new.sender_id, old.sender_id)::text, true;
    elsif tg_table_name = 'social_party_members' then
        v_player := coalesce(new.player_id, old.player_id);
        execute 'select realtime.send($1,$2,$3,$4)'
        using v_payload, 'social_changed', 'social:user:' || v_player::text, true;
    elsif tg_table_name = 'social_parties' then
        execute 'select realtime.send($1,$2,$3,$4)'
        using v_payload, 'social_changed', 'social:user:' || new.leader_id::text, true;
        if old.leader_id is distinct from new.leader_id then
            execute 'select realtime.send($1,$2,$3,$4)'
            using v_payload, 'social_changed', 'social:user:' || old.leader_id::text, true;
        end if;
    end if;
    if tg_op = 'DELETE' then return old; else return new; end if;
end;
$;

drop trigger if exists project_prime_social_party_invite_notify on prime.social_party_invites;
create trigger project_prime_social_party_invite_notify
after insert or update of status on prime.social_party_invites
for each row execute function prime.social_party_notify();

drop trigger if exists project_prime_social_party_member_notify on prime.social_party_members;
create trigger project_prime_social_party_member_notify
after insert or delete on prime.social_party_members
for each row execute function prime.social_party_notify();

drop trigger if exists project_prime_social_party_leader_notify on prime.social_parties;
create trigger project_prime_social_party_leader_notify
after update of leader_id on prime.social_parties
for each row execute function prime.social_party_notify();

-- A block is stronger than party membership. Preserve the Slice 4 game-invite
-- cleanup and also cancel party invites / split an existing party immediately.
create or replace function prime.social_block_invite_cleanup()
returns trigger
language plpgsql
security invoker
set search_path = prime, pg_temp
as $
declare
    v_blocker_party uuid;
    v_blocked_party uuid;
    v_leader uuid;
begin
    update prime.game_invites
    set status = 'cancelled', responded_at = now()
    where status in ('pending', 'accepted')
      and ((sender_id = new.blocker_id and recipient_id = new.blocked_id)
        or (sender_id = new.blocked_id and recipient_id = new.blocker_id));

    update prime.social_party_invites
    set status = 'cancelled', responded_at = now()
    where status = 'pending'
      and ((sender_id = new.blocker_id and recipient_id = new.blocked_id)
        or (sender_id = new.blocked_id and recipient_id = new.blocker_id));

    select party_id into v_blocker_party
    from prime.social_party_members where player_id = new.blocker_id;
    select party_id into v_blocked_party
    from prime.social_party_members where player_id = new.blocked_id;

    if v_blocker_party is not null and v_blocker_party = v_blocked_party then
        select leader_id into v_leader
        from prime.social_parties
        where party_id = v_blocker_party
        for update;

        if v_leader = new.blocker_id then
            delete from prime.social_party_members
            where party_id = v_blocker_party and player_id = new.blocked_id;
        else
            delete from prime.social_party_members
            where party_id = v_blocker_party and player_id = new.blocker_id;
        end if;
        update prime.social_parties
        set updated_at = now()
        where party_id = v_blocker_party;
    end if;

    return new;
end;
$;

alter table prime.social_recent_players enable row level security;
alter table prime.social_parties enable row level security;
alter table prime.social_party_members enable row level security;
alter table prime.social_party_invites enable row level security;

revoke all on table prime.social_recent_players from public, anon, authenticated;
revoke all on table prime.social_parties from public, anon, authenticated;
revoke all on table prime.social_party_members from public, anon, authenticated;
revoke all on table prime.social_party_invites from public, anon, authenticated;

create policy social_recent_self_read on prime.social_recent_players
for select to authenticated
using ((select auth.uid()) = actor_id);

create policy social_party_member_read on prime.social_party_members
for select to authenticated
using ((select auth.uid()) = player_id);

create policy social_party_read on prime.social_parties
for select to authenticated
using (
    exists (
        select 1 from prime.social_party_members self
        where self.party_id = social_parties.party_id
          and self.player_id = (select auth.uid())
    )
);

create policy social_party_invite_party_read on prime.social_party_invites
for select to authenticated
using (
    sender_id = (select auth.uid()) or recipient_id = (select auth.uid())
);

revoke execute on function prime.social_recent_touch(uuid,numeric,uuid) from public, anon, authenticated;
revoke execute on function prime.social_party_expire(uuid) from public, anon, authenticated;
revoke execute on function prime.social_party_snapshot(uuid) from public, anon, authenticated;
revoke execute on function prime.social_party_action(uuid,text,text,uuid) from public, anon, authenticated;
revoke execute on function prime.social_privacy_update(uuid,text,text,text,boolean) from public, anon, authenticated;
revoke execute on function prime.social_party_notify() from public, anon, authenticated;
