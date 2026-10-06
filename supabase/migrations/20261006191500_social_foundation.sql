-- Social foundation: stable public Prime IDs, friend relationships, requests, and blocks.
-- Gameplay networking remains unchanged. Social tables are private-by-default and
-- mutations are performed through the authenticated social Edge Function.

create table if not exists prime.social_profiles (
    player_id uuid primary key references prime.players("Id") on delete cascade,
    prime_id text not null unique,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint social_profiles_prime_id_format check (
        prime_id ~ '^PP-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$'
    )
);

create table if not exists prime.friend_requests (
    sender_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    recipient_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    created_at timestamptz not null default now(),
    primary key (sender_id, recipient_id),
    constraint friend_requests_not_self check (sender_id <> recipient_id)
);

create index if not exists friend_requests_recipient_created_idx
    on prime.friend_requests (recipient_id, created_at desc);

create table if not exists prime.friendships (
    player_a uuid not null references prime.social_profiles(player_id) on delete cascade,
    player_b uuid not null references prime.social_profiles(player_id) on delete cascade,
    created_at timestamptz not null default now(),
    primary key (player_a, player_b),
    constraint friendships_canonical_order check (player_a < player_b)
);

create index if not exists friendships_player_b_idx
    on prime.friendships (player_b, created_at desc);

create table if not exists prime.player_blocks (
    blocker_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    blocked_id uuid not null references prime.social_profiles(player_id) on delete cascade,
    created_at timestamptz not null default now(),
    primary key (blocker_id, blocked_id),
    constraint player_blocks_not_self check (blocker_id <> blocked_id)
);

create index if not exists player_blocks_blocked_idx
    on prime.player_blocks (blocked_id);

create or replace function prime.social_public_id(p_user uuid)
returns text
language sql
immutable
strict
as $$
    select 'PP-'
        || upper(substr(v, 1, 4)) || '-'
        || upper(substr(v, 5, 4)) || '-'
        || upper(substr(v, 9, 4)) || '-'
        || upper(substr(v, 13, 4)) || '-'
        || upper(substr(v, 17, 4))
    from (select replace(p_user::text, '-', '') as v) s
$$;

insert into prime.social_profiles (player_id, prime_id)
select p."PlayerId", prime.social_public_id(p."PlayerId")
from prime.player_profiles p
on conflict (player_id) do nothing;

create or replace function prime.social_profile_seed()
returns trigger
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
begin
    insert into prime.social_profiles (player_id, prime_id)
    values (new."PlayerId", prime.social_public_id(new."PlayerId"))
    on conflict (player_id) do nothing;
    return new;
end;
$$;

drop trigger if exists project_prime_social_profile_seed on prime.player_profiles;
create trigger project_prime_social_profile_seed
after insert on prime.player_profiles
for each row execute function prime.social_profile_seed();

create or replace function prime.social_snapshot(p_actor uuid)
returns jsonb
language plpgsql
stable
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_self jsonb;
    v_friends jsonb;
    v_incoming jsonb;
    v_outgoing jsonb;
    v_blocked jsonb;
begin
    select jsonb_build_object(
        'prime_id', s.prime_id,
        'display_name', p."DisplayName",
        'created_at', s.created_at
    )
    into v_self
    from prime.social_profiles s
    join prime.player_profiles p on p."PlayerId" = s.player_id
    where s.player_id = p_actor;

    if v_self is null then
        return null;
    end if;

    select coalesce(jsonb_agg(jsonb_build_object(
        'prime_id', x.prime_id,
        'display_name', x.display_name,
        'created_at', x.created_at
    ) order by lower(x.display_name), x.prime_id), '[]'::jsonb)
    into v_friends
    from (
        select s.prime_id, p."DisplayName" as display_name, f.created_at
        from prime.friendships f
        join prime.social_profiles s
          on s.player_id = case when f.player_a = p_actor then f.player_b else f.player_a end
        join prime.player_profiles p on p."PlayerId" = s.player_id
        where f.player_a = p_actor or f.player_b = p_actor
    ) x;

    select coalesce(jsonb_agg(jsonb_build_object(
        'prime_id', s.prime_id,
        'display_name', p."DisplayName",
        'created_at', r.created_at
    ) order by r.created_at desc), '[]'::jsonb)
    into v_incoming
    from prime.friend_requests r
    join prime.social_profiles s on s.player_id = r.sender_id
    join prime.player_profiles p on p."PlayerId" = s.player_id
    where r.recipient_id = p_actor;

    select coalesce(jsonb_agg(jsonb_build_object(
        'prime_id', s.prime_id,
        'display_name', p."DisplayName",
        'created_at', r.created_at
    ) order by r.created_at desc), '[]'::jsonb)
    into v_outgoing
    from prime.friend_requests r
    join prime.social_profiles s on s.player_id = r.recipient_id
    join prime.player_profiles p on p."PlayerId" = s.player_id
    where r.sender_id = p_actor;

    select coalesce(jsonb_agg(jsonb_build_object(
        'prime_id', s.prime_id,
        'display_name', p."DisplayName",
        'created_at', b.created_at
    ) order by b.created_at desc), '[]'::jsonb)
    into v_blocked
    from prime.player_blocks b
    join prime.social_profiles s on s.player_id = b.blocked_id
    join prime.player_profiles p on p."PlayerId" = s.player_id
    where b.blocker_id = p_actor;

    return jsonb_build_object(
        'self', v_self,
        'friends', v_friends,
        'incoming_requests', v_incoming,
        'outgoing_requests', v_outgoing,
        'blocked', v_blocked
    );
end;
$$;

create or replace function prime.social_lookup(p_actor uuid, p_prime_id text)
returns jsonb
language sql
stable
security invoker
set search_path = prime, pg_temp
as $$
    select jsonb_build_object(
        'prime_id', s.prime_id,
        'display_name', p."DisplayName",
        'created_at', s.created_at
    )
    from prime.social_profiles s
    join prime.player_profiles p on p."PlayerId" = s.player_id
    where s.prime_id = upper(btrim(p_prime_id))
      and s.player_id <> p_actor
      and not exists (
          select 1 from prime.player_blocks b
          where (b.blocker_id = p_actor and b.blocked_id = s.player_id)
             or (b.blocker_id = s.player_id and b.blocked_id = p_actor)
      )
$$;

create or replace function prime.social_mutate(
    p_actor uuid,
    p_target_prime_id text,
    p_action text
)
returns jsonb
language plpgsql
security invoker
set search_path = prime, pg_temp
as $$
declare
    v_target uuid;
    v_a uuid;
    v_b uuid;
    v_actor_count integer;
    v_target_count integer;
    v_status text;
begin
    select player_id into v_target
    from prime.social_profiles
    where prime_id = upper(btrim(p_target_prime_id));

    if v_target is null then
        return jsonb_build_object('ok', false, 'status', 'player_not_found');
    end if;
    if v_target = p_actor then
        return jsonb_build_object('ok', false, 'status', 'self_target');
    end if;
    if not exists (select 1 from prime.social_profiles where player_id = p_actor) then
        return jsonb_build_object('ok', false, 'status', 'profile_required');
    end if;

    -- Serialize social mutations touching this pair so crossed requests,
    -- accept/block races, and duplicate clicks converge on one relationship.
    perform 1
    from prime.social_profiles
    where player_id in (p_actor, v_target)
    order by player_id
    for update;

    v_a := least(p_actor, v_target);
    v_b := greatest(p_actor, v_target);

    if p_action = 'send_request' then
        if exists (
            select 1 from prime.player_blocks
            where (blocker_id = p_actor and blocked_id = v_target)
               or (blocker_id = v_target and blocked_id = p_actor)
        ) then
            return jsonb_build_object('ok', false, 'status', 'blocked');
        end if;
        if exists (select 1 from prime.friendships where player_a = v_a and player_b = v_b) then
            return jsonb_build_object('ok', true, 'status', 'already_friends', 'snapshot', prime.social_snapshot(p_actor));
        end if;

        if exists (
            select 1 from prime.friend_requests
            where sender_id = v_target and recipient_id = p_actor
        ) then
            select count(*) into v_actor_count from prime.friendships
            where player_a = p_actor or player_b = p_actor;
            select count(*) into v_target_count from prime.friendships
            where player_a = v_target or player_b = v_target;
            if v_actor_count >= 200 or v_target_count >= 200 then
                return jsonb_build_object('ok', false, 'status', 'friend_limit');
            end if;
            delete from prime.friend_requests
            where sender_id = v_target and recipient_id = p_actor;
            insert into prime.friendships (player_a, player_b)
            values (v_a, v_b)
            on conflict do nothing;
            v_status := 'friends';
        else
            select count(*) into v_actor_count
            from prime.friend_requests where sender_id = p_actor;
            if v_actor_count >= 50 then
                return jsonb_build_object('ok', false, 'status', 'request_limit');
            end if;
            insert into prime.friend_requests (sender_id, recipient_id)
            values (p_actor, v_target)
            on conflict do nothing;
            v_status := 'request_sent';
        end if;

    elsif p_action = 'accept_request' then
        if exists (
            select 1 from prime.player_blocks
            where (blocker_id = p_actor and blocked_id = v_target)
               or (blocker_id = v_target and blocked_id = p_actor)
        ) then
            return jsonb_build_object('ok', false, 'status', 'blocked');
        end if;
        if not exists (
            select 1 from prime.friend_requests
            where sender_id = v_target and recipient_id = p_actor
        ) then
            return jsonb_build_object('ok', false, 'status', 'request_missing');
        end if;
        select count(*) into v_actor_count from prime.friendships
        where player_a = p_actor or player_b = p_actor;
        select count(*) into v_target_count from prime.friendships
        where player_a = v_target or player_b = v_target;
        if v_actor_count >= 200 or v_target_count >= 200 then
            return jsonb_build_object('ok', false, 'status', 'friend_limit');
        end if;
        delete from prime.friend_requests
        where sender_id = v_target and recipient_id = p_actor;
        insert into prime.friendships (player_a, player_b)
        values (v_a, v_b)
        on conflict do nothing;
        v_status := 'friends';

    elsif p_action = 'decline_request' then
        delete from prime.friend_requests
        where sender_id = v_target and recipient_id = p_actor;
        v_status := 'request_declined';

    elsif p_action = 'cancel_request' then
        delete from prime.friend_requests
        where sender_id = p_actor and recipient_id = v_target;
        v_status := 'request_cancelled';

    elsif p_action = 'remove_friend' then
        delete from prime.friendships
        where player_a = v_a and player_b = v_b;
        v_status := 'friend_removed';

    elsif p_action = 'block_player' then
        select count(*) into v_actor_count
        from prime.player_blocks where blocker_id = p_actor;
        if v_actor_count >= 500
           and not exists (
               select 1 from prime.player_blocks
               where blocker_id = p_actor and blocked_id = v_target
           ) then
            return jsonb_build_object('ok', false, 'status', 'block_limit');
        end if;
        delete from prime.friend_requests
        where (sender_id = p_actor and recipient_id = v_target)
           or (sender_id = v_target and recipient_id = p_actor);
        delete from prime.friendships
        where player_a = v_a and player_b = v_b;
        insert into prime.player_blocks (blocker_id, blocked_id)
        values (p_actor, v_target)
        on conflict do nothing;
        v_status := 'blocked';

    elsif p_action = 'unblock_player' then
        delete from prime.player_blocks
        where blocker_id = p_actor and blocked_id = v_target;
        v_status := 'unblocked';

    else
        return jsonb_build_object('ok', false, 'status', 'invalid_action');
    end if;

    return jsonb_build_object(
        'ok', true,
        'status', v_status,
        'snapshot', prime.social_snapshot(p_actor)
    );
end;
$$;

alter table prime.social_profiles enable row level security;
alter table prime.friend_requests enable row level security;
alter table prime.friendships enable row level security;
alter table prime.player_blocks enable row level security;

-- These tables are not a direct client API. Revoke table access even if the
-- project's custom schema is exposed; the Edge Function uses the DB service
-- connection after separately validating the caller's Supabase session.
revoke all on table prime.social_profiles from public, anon, authenticated;
revoke all on table prime.friend_requests from public, anon, authenticated;
revoke all on table prime.friendships from public, anon, authenticated;
revoke all on table prime.player_blocks from public, anon, authenticated;

create policy social_profiles_self_read
on prime.social_profiles for select
to authenticated
using ((select auth.uid()) = player_id);

create policy friend_requests_party_read
on prime.friend_requests for select
to authenticated
using ((select auth.uid()) = sender_id or (select auth.uid()) = recipient_id);

create policy friendships_party_read
on prime.friendships for select
to authenticated
using ((select auth.uid()) = player_a or (select auth.uid()) = player_b);

create policy player_blocks_owner_read
on prime.player_blocks for select
to authenticated
using ((select auth.uid()) = blocker_id);

revoke execute on function prime.social_public_id(uuid) from public, anon, authenticated;
revoke execute on function prime.social_profile_seed() from public, anon, authenticated;
revoke execute on function prime.social_snapshot(uuid) from public, anon, authenticated;
revoke execute on function prime.social_lookup(uuid, text) from public, anon, authenticated;
revoke execute on function prime.social_mutate(uuid, text, text) from public, anon, authenticated;
