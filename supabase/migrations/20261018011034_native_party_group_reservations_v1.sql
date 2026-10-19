-- Native V1 extends the existing reservation owner. No new public seat registry.
-- All entry points are closed SECURITY INVOKER functions used by Edge workers.
alter table prime.social_party_reservations
    add column native_witness jsonb,
    add column native_generation bigint check(native_generation between 1 and 9007199254740991),
    add column native_cancelled boolean not null default false,
    add column native_assignments jsonb;
alter table prime.social_party_reservation_members
    add column native_declined boolean not null default false,
    add column native_claim_nonce numeric(20,0),
    add column native_connection_id numeric(20,0),
    add column native_claim_until timestamptz;

-- Preserve fixed native deadlines even when an old caller reaches a legacy API.
create function prime.social_native_reservation_immutable()
returns trigger language plpgsql security invoker set search_path=prime,pg_temp as $$
begin
    if old.native_witness is not null then
        if new.native_witness is distinct from old.native_witness or new.native_generation is distinct from old.native_generation
           or new.expires_at is distinct from old.expires_at or new.party_id<>old.party_id or new.leader_id<>old.leader_id
           or new.authority_epoch<>old.authority_epoch or new.requested_count<>old.requested_count or new.include_leader<>old.include_leader
           or new.created_at is distinct from old.created_at
           or (old.reserved_until is not null and new.reserved_until is distinct from old.reserved_until)
           or (old.server_reservation_id is not null and new.server_reservation_id is distinct from old.server_reservation_id)
           or (old.native_assignments is not null and new.native_assignments is distinct from old.native_assignments)
           or (old.native_cancelled and not new.native_cancelled) then
            raise exception 'immutable native reservation';
        end if;
        if old.status not in ('pending','reserved') and new.status in ('pending','reserved') then
            raise exception 'closed native reservation'; end if;
        if new.status in ('cancelled','expired','rejected') then new.native_cancelled:=true; end if;
    end if;
    return new;
end;
$$;
create trigger social_native_reservation_immutable before update on prime.social_party_reservations
for each row execute function prime.social_native_reservation_immutable();

create function prime.social_native_reservation_projection(p_request uuid,p_actor uuid)
returns jsonb language sql security invoker set search_path=prime,pg_temp as $$
    select (r.native_witness || jsonb_build_object('native_v1',true,'request_id',r.request_id,
        'reservation_id',r.server_reservation_id,'operation_generation',r.native_generation,
        'status',case when r.status in ('pending','reserved') and least(r.expires_at,coalesce(r.reserved_until,r.expires_at))<=clock_timestamp()
            then 'expired' else r.status end,
        'expires_at',least(r.expires_at,coalesce(r.reserved_until,r.expires_at)),
        'requested_count',r.requested_count,'self_slot',m.slot,
        'self_status',case when m.status='admitted' then 'admitted'
            when m.native_claim_until>clock_timestamp() then 'admitting' else m.status end,
        'cancellation_pending',(r.native_cancelled or coalesce(m.native_declined,false)) and exists(select 1 from prime.social_party_reservation_members c
            where c.request_id=r.request_id and c.status<>'admitted' and c.native_claim_until>clock_timestamp()
                and (r.native_cancelled or c.player_id=p_actor))))
        - 'roster_revisions' - 'roster' - 'leader_self' - 'reporter_id'
    from prime.social_party_reservations r left join prime.social_party_reservation_members m
        on m.request_id=r.request_id and m.player_id=p_actor
    where r.request_id=p_request and r.native_witness is not null
$$;

-- Caller already owns the global Party lock and the reservation row lock.
-- clock_timestamp is deliberately sampled after those locks.
create function prime.social_native_reservation_gate(p_request uuid,p_reporter uuid,p_player uuid,p_ticket_deadline timestamptz)
returns text language plpgsql security invoker set search_path=prime,pg_temp as $$
declare r prime.social_party_reservations%rowtype; w jsonb; n timestamptz:=clock_timestamp(); v_roster jsonb;
begin
    select * into r from prime.social_party_reservations where request_id=p_request;
    w:=r.native_witness;
    if w is null then return 'reservation_not_found'; end if;
    if p_ticket_deadline is null or p_ticket_deadline<=n then return 'ticket_expired'; end if;
    if r.native_cancelled or r.status not in ('pending','reserved') then return 'reservation_closed'; end if;
    if least(r.expires_at,coalesce(r.reserved_until,r.expires_at))<=n then return 'reservation_expired'; end if;
    if not exists(select 1 from public.project_prime_career_reporters where server_id=p_reporter and enabled)
       or not exists(select 1 from prime.social_native_authority_epochs where authority_epoch=r.authority_epoch and reporter_id=p_reporter)
       or p_reporter is distinct from (w->>'reporter_id')::uuid then return 'authority_owner_changed'; end if;
    if not exists(select 1 from prime.social_parties where party_id=r.party_id and leader_id=r.leader_id)
       or not exists(select 1 from prime.social_party_members where party_id=r.party_id and player_id=p_player)
       or not exists(select 1 from prime.social_profiles where player_id=r.leader_id and prime_id=w->>'leader_self') then return 'party_changed'; end if;
    select jsonb_agg(player_id::text order by player_id) into v_roster from prime.social_party_members where party_id=r.party_id;
    if v_roster is distinct from w->'roster' or exists(select 1 from jsonb_each_text(w->'roster_revisions') e
        left join prime.social_profiles s on s.player_id=e.key::uuid where s.native_party_revision is distinct from e.value::bigint) then return 'party_changed'; end if;
    if exists(select 1 from prime.player_blocks b where w->'roster' ? b.blocker_id::text and w->'roster' ? b.blocked_id::text)
       or exists(select 1 from prime.social_party_members m left join prime.social_settings s on s.player_id=m.player_id
           where m.party_id=r.party_id and m.player_id<>r.leader_id and
             (s.player_id is null or s.invite_policy='nobody' or s.do_not_disturb or
             (s.invite_policy='friends' and not exists(select 1 from prime.friendships f
               where (f.player_a=r.leader_id and f.player_b=m.player_id) or (f.player_b=r.leader_id and f.player_a=m.player_id)))))
       then return 'privacy_changed'; end if;
    if not exists(select 1 from prime.social_party_travel t join prime.social_lobbies l on l.lobby_id=t.lobby_id
        where t.party_id=r.party_id and t.travel_id=(w->>'travel_id')::uuid and t.revision=(w->>'travel_revision')::integer
          and t.leader_id=r.leader_id and t.expires_at>n and l.expires_at>n and l.owner_id=r.leader_id
          and l.authority_epoch=r.authority_epoch and host(l.host_address)=w->>'host' and l.port=(w->>'port')::integer
          and l.protocol=(w->>'protocol')::integer and l.room_key=w->>'room_key') then return 'party_travel_stale'; end if;
    if not exists(select 1 from prime.social_lobby_memberships where player_id=r.leader_id and authority_epoch=r.authority_epoch
        and reporter_id=p_reporter and lobby_eligible and expires_at>n) then return 'membership_unverified'; end if;
    return null;
end;
$$;

create function prime.social_native_party_reservation_action(p_actor uuid,p_action text,p_input jsonb)
returns jsonb language plpgsql security invoker set search_path=prime,pg_temp as $$
declare n timestamptz; r prime.social_party_reservations%rowtype; w jsonb; s prime.social_profiles%rowtype;
    t prime.social_party_travel%rowtype; l prime.social_lobbies%rowtype; v_reporter uuid; v_request uuid;
    v_roster jsonb; v_revisions jsonb; v_missing uuid[]; v_reason text; v_deadline timestamptz;
begin
    perform pg_advisory_xact_lock(6384340261223951::bigint);
    perform 1 from prime.social_parties where party_id=(p_input->>'party_id')::uuid for update;
    n:=clock_timestamp();
    select * into s from prime.social_profiles where player_id=p_actor;
    if s.prime_id is distinct from p_input->>'expected_self_prime_id' then return jsonb_build_object('ok',false,'status','account_changed'); end if;
    if s.native_party_revision is distinct from (p_input->>'expected_revision')::bigint or not exists(select 1 from prime.social_party_members
        where player_id=p_actor and party_id=(p_input->>'party_id')::uuid) then return jsonb_build_object('ok',false,'status','party_changed'); end if;
    if p_action not in ('reserve_intent','reservation_snapshot','cancel_reservation')
        or p_input->>'match_id' is null or (p_input->>'match_id')::integer not between 1 and 65535
        or (p_action<>'reservation_snapshot' and (p_input->>'operation_generation' is null
            or (p_input->>'operation_generation')::bigint not between 1 and 9007199254740991)) then
        return jsonb_build_object('ok',false,'status','invalid_reservation'); end if;
    v_request:=nullif(p_input->>'request_id','')::uuid;
    if p_action='reservation_snapshot' and v_request is null then
        select request_id into v_request from prime.social_party_reservations where party_id=(p_input->>'party_id')::uuid
            and native_witness is not null and native_witness->>'travel_id'=p_input->>'travel_id'
            and native_witness->>'travel_revision'=p_input->>'travel_revision'
            and native_witness->>'match_id'=p_input->>'match_id' and authority_epoch=(p_input->>'authority_epoch')::numeric
            and status in ('pending','reserved') order by created_at desc limit 1;
        if v_request is null then return jsonb_build_object('ok',true,'status','no_reservation','reservation',null); end if;
    end if;
    select * into r from prime.social_party_reservations where request_id=v_request for update;
    perform 1 from prime.social_party_reservation_members where request_id=v_request order by player_id for update;
    n:=clock_timestamp();
    if r.request_id is not null then
        if r.native_witness is null or r.party_id is distinct from (p_input->>'party_id')::uuid
            or r.native_witness->>'travel_id' is distinct from p_input->>'travel_id'
            or r.native_witness->>'travel_revision' is distinct from p_input->>'travel_revision'
            or r.native_witness->>'match_id' is distinct from p_input->>'match_id'
            or r.authority_epoch is distinct from (p_input->>'authority_epoch')::numeric
            or (p_action<>'reservation_snapshot' and r.native_generation is distinct from (p_input->>'operation_generation')::bigint)
            or (p_action='reserve_intent' and (r.native_witness->>'host' is distinct from p_input->>'host'
                or r.native_witness->>'port' is distinct from p_input->>'port' or r.native_witness->>'protocol' is distinct from p_input->>'protocol'
                or r.native_witness->>'room_key' is distinct from p_input->>'room_key')) then
            return jsonb_build_object('ok',false,'status','reservation_stale'); end if;
        if p_action='cancel_reservation' then
            if p_actor=r.leader_id then
                update prime.social_party_reservations set status='cancelled',native_cancelled=true,updated_at=n where request_id=v_request and status in ('pending','reserved');
                update prime.social_party_reservation_members set status='declined',updated_at=n where request_id=v_request
                    and status<>'admitted' and (native_claim_until is null or native_claim_until<=n);
            else
                if r.status<>'reserved' then return jsonb_build_object('ok',false,'status','member_not_reserved'); end if;
                update prime.social_party_reservation_members set native_declined=true,
                    status=case when native_claim_until>n then status else 'declined' end,updated_at=n where request_id=v_request
                    and player_id=p_actor and status<>'admitted';
            end if;
            return jsonb_build_object('ok',true,'status','reservation_cancelled','reservation',prime.social_native_reservation_projection(v_request,p_actor));
        end if;
        if p_action='reserve_intent' and p_actor<>r.leader_id then return jsonb_build_object('ok',false,'status','leader_only'); end if;
        v_reason:=prime.social_native_reservation_gate(v_request,(r.native_witness->>'reporter_id')::uuid,p_actor,n+interval '1 second');
        if v_reason is not null then return jsonb_build_object('ok',false,'status',v_reason,'reservation',prime.social_native_reservation_projection(v_request,p_actor)); end if;
        return jsonb_build_object('ok',true,'status',r.status,'reservation',prime.social_native_reservation_projection(v_request,p_actor));
    end if;
    if p_action<>'reserve_intent' then return jsonb_build_object('ok',false,'status','reservation_not_found'); end if;
    if not exists(select 1 from prime.social_parties where party_id=(p_input->>'party_id')::uuid and leader_id=p_actor) then
        return jsonb_build_object('ok',false,'status','leader_only'); end if;
    select * into t from prime.social_party_travel where party_id=(p_input->>'party_id')::uuid
        and travel_id=(p_input->>'travel_id')::uuid and revision=(p_input->>'travel_revision')::integer and leader_id=p_actor and expires_at>n;
    select * into l from prime.social_lobbies where lobby_id=t.lobby_id and owner_id=p_actor and expires_at>n
        and authority_epoch=(p_input->>'authority_epoch')::numeric and host(host_address)=p_input->>'host'
        and port=(p_input->>'port')::integer and protocol=(p_input->>'protocol')::integer and room_key=p_input->>'room_key';
    if l.lobby_id is null then return jsonb_build_object('ok',false,'status','party_travel_stale'); end if;
    select m.reporter_id into v_reporter from prime.social_lobby_memberships m join prime.social_native_authority_epochs e
        on e.authority_epoch=m.authority_epoch and e.reporter_id=m.reporter_id
        join public.project_prime_career_reporters c on c.server_id=m.reporter_id and c.enabled
        where m.player_id=p_actor and m.authority_epoch=l.authority_epoch and m.lobby_eligible and m.expires_at>n;
    if v_reporter is null then return jsonb_build_object('ok',false,'status','membership_unverified'); end if;
    select jsonb_agg(m.player_id::text order by m.player_id),jsonb_object_agg(m.player_id::text,member_profile.native_party_revision)
        into v_roster,v_revisions from prime.social_party_members m join prime.social_profiles member_profile on member_profile.player_id=m.player_id where m.party_id=t.party_id;
    if jsonb_array_length(v_roster) not between 2 and 8 then return jsonb_build_object('ok',false,'status','party_size_invalid'); end if;
    select array_agg(m.player_id order by m.player_id) into v_missing from prime.social_party_members m where m.party_id=t.party_id and m.player_id<>p_actor
        and not exists(select 1 from prime.social_lobby_memberships lm where lm.player_id=m.player_id and lm.authority_epoch=l.authority_epoch
            and lm.reporter_id=v_reporter and lm.lobby_eligible and lm.expires_at>n);
    if coalesce(cardinality(v_missing),0)=0 then return jsonb_build_object('ok',false,'status','reservation_not_needed'); end if;
    -- Close expired existing rows under the same lifecycle lock before the unique-live check.
    update prime.social_party_reservations set status='expired',updated_at=n where party_id=t.party_id
        and status in ('pending','reserved') and least(expires_at,coalesce(reserved_until,expires_at))<=n;
    -- A changed travel/revision/authority retires the old immutable operation.
    -- Its row remains as a tombstone; an already-linearized claim can settle.
    update prime.social_party_reservations old_request set status='cancelled',native_cancelled=true,updated_at=clock_timestamp()
        where old_request.party_id=t.party_id and old_request.status in ('pending','reserved') and old_request.native_witness is not null
          and prime.social_native_reservation_gate(old_request.request_id,(old_request.native_witness->>'reporter_id')::uuid,
              p_actor,clock_timestamp()+interval '1 second') is not null;
    if exists(select 1 from prime.social_party_reservations where party_id=t.party_id and status in ('pending','reserved')) then
        return jsonb_build_object('ok',false,'status','reservation_active'); end if;
    v_deadline:=least(n+interval '60 seconds',t.expires_at,l.expires_at);
    w:=jsonb_build_object('party_id',t.party_id,'party_revision',s.native_party_revision,'travel_id',t.travel_id,'travel_revision',t.revision,
        'match_id',(p_input->>'match_id')::integer,'authority_epoch',l.authority_epoch::text,'host',host(l.host_address),'port',l.port,
        'protocol',l.protocol,'room_key',l.room_key,'reporter_id',v_reporter,'roster',v_roster,'roster_revisions',v_revisions,'leader_self',s.prime_id);
    insert into prime.social_party_reservations(request_id,party_id,leader_id,authority_epoch,include_leader,requested_count,expires_at,native_witness,native_generation,created_at,updated_at)
        values(v_request,t.party_id,p_actor,l.authority_epoch,false,cardinality(v_missing),v_deadline,w,(p_input->>'operation_generation')::bigint,n,n);
    insert into prime.social_party_reservation_members(request_id,player_id) select v_request,unnest(v_missing);
    v_reason:=prime.social_native_reservation_gate(v_request,v_reporter,p_actor,n+interval '1 second');
    if v_reason is not null then
        update prime.social_party_reservations set status='rejected',updated_at=clock_timestamp() where request_id=v_request;
        return jsonb_build_object('ok',false,'status',v_reason); end if;
    return jsonb_build_object('ok',true,'status','pending','reservation',prime.social_native_reservation_projection(v_request,p_actor));
end;
$$;

create function prime.social_native_reservation_capability(p_reporter uuid)
returns jsonb language sql security invoker set search_path=prime,pg_temp as $$
    select jsonb_build_object('ok',v.enabled,'native_v1',v.enabled,'version',1,'claim_max_ms',2000)
    from (select exists(select 1 from public.project_prime_career_reporters where server_id=p_reporter and enabled) as enabled) v
$$;

create function prime.social_native_reservation_server(p_reporter uuid,p_player uuid,p_ticket_deadline timestamptz,p_input jsonb)
returns jsonb language plpgsql security invoker set search_path=prime,pg_temp as $$
declare r prime.social_party_reservations%rowtype; m prime.social_party_reservation_members%rowtype;
    w jsonb; n timestamptz; a text:=p_input->>'action'; reason text; v_request uuid:=(p_input->>'request_id')::uuid;
    v_until timestamptz; v_assignments jsonb; v_reservation uuid:=nullif(p_input->>'reservation_id','')::uuid;
    v_connection numeric:=nullif(p_input->>'connection_id','')::numeric; v_nonce numeric:=nullif(p_input->>'claim_nonce','')::numeric;
begin
    perform pg_advisory_xact_lock(6384340261223951::bigint);
    perform 1 from prime.social_parties where party_id=(select party_id from prime.social_party_reservations where request_id=v_request) for update;
    select * into r from prime.social_party_reservations where request_id=v_request for update;
    perform 1 from prime.social_party_reservation_members where request_id=v_request order by player_id for update;
    n:=clock_timestamp(); w:=r.native_witness;
    if w is null then return jsonb_build_object('ok',false,'status','reservation_not_found'); end if;
    if p_reporter is distinct from (w->>'reporter_id')::uuid or not exists(select 1 from public.project_prime_career_reporters
        where server_id=p_reporter and enabled) or not exists(select 1 from prime.social_native_authority_epochs
        where authority_epoch=r.authority_epoch and reporter_id=p_reporter) then return jsonb_build_object('ok',false,'status','authority_owner_changed'); end if;
    if r.native_generation is distinct from (p_input->>'operation_generation')::bigint
        or w->>'party_id' is distinct from p_input->>'party_id' or w->>'party_revision' is distinct from p_input->>'party_revision'
        or w->>'travel_id' is distinct from p_input->>'travel_id' or w->>'travel_revision' is distinct from p_input->>'travel_revision'
        or w->>'match_id' is distinct from p_input->>'match_id' or r.authority_epoch is distinct from (p_input->>'authority_epoch')::numeric then
        return jsonb_build_object('ok',false,'status','reservation_stale'); end if;
    if p_input->>'operation_deadline' is null or (p_input->>'operation_deadline')::timestamptz<=n or (p_input->>'operation_deadline')::timestamptz>n+interval '10 seconds' then
        return jsonb_build_object('ok',false,'status','operation_expired'); end if;
    if a not in ('validate','activate','claim_admission','admitted','cancel')
       or (a='validate' and (p_input->>'purpose' is null or p_input->>'purpose' not in ('leader','member')))
       or (a='activate' and (v_reservation is null or p_input->>'expires_at' is null or jsonb_typeof(p_input->'assignments') is distinct from 'array'))
       or (a in ('claim_admission','admitted') and (v_reservation is null or v_connection is null or v_nonce is null
           or v_connection not between 1 and 18446744073709551615 or v_nonce not between 1 and 18446744073709551615
           or trunc(v_connection)<>v_connection or trunc(v_nonce)<>v_nonce or p_input->>'slot' is null))
       or (a='cancel' and (p_input->>'status' is null or p_input->>'status' not in ('cancelled','rejected','expired')
           or p_input->>'cancel_scope' is null or p_input->>'cancel_scope' not in ('group','self'))) then
        return jsonb_build_object('ok',false,'status','invalid_reservation'); end if;
    if a='cancel' then
        if r.server_reservation_id is not null and v_reservation is distinct from r.server_reservation_id
            then return jsonb_build_object('ok',false,'status','reservation_mismatch'); end if;
        if p_input->>'cancel_scope'='self' then
            if r.status<>'reserved' then return jsonb_build_object('ok',false,'status','member_not_reserved'); end if;
            if p_ticket_deadline is null or p_ticket_deadline<=n then return jsonb_build_object('ok',false,'status','ticket_expired'); end if;
            update prime.social_party_reservation_members set native_declined=true,
                status=case when native_claim_until>n then status else 'declined' end,updated_at=n where request_id=v_request and player_id=p_player
                and status<>'admitted';
        else
            update prime.social_party_reservations set status=p_input->>'status',native_cancelled=true,updated_at=n where request_id=v_request and status in ('pending','reserved');
            update prime.social_party_reservation_members set status='declined',updated_at=n where request_id=v_request and status<>'admitted'
                and (native_claim_until is null or native_claim_until<=n);
        end if;
        return jsonb_build_object('ok',true,'status','reservation_cancelled','reservation',prime.social_native_reservation_projection(v_request,p_player));
    end if;
    if p_ticket_deadline is null or p_ticket_deadline<=n then return jsonb_build_object('ok',false,'status','ticket_expired'); end if;
    -- admitted settles an already-linearized short claim, even if cancellation,
    -- roster or travel changed after claim. It never creates an admission itself.
    if a='admitted' then
        select * into m from prime.social_party_reservation_members where request_id=v_request and player_id=p_player for update;
        if r.server_reservation_id is distinct from v_reservation or m.native_claim_nonce is distinct from v_nonce
            or m.native_connection_id is distinct from v_connection or m.slot is distinct from (p_input->>'slot')::smallint
            or p_input->>'peer_created' is distinct from 'true' then return jsonb_build_object('ok',false,'status','claim_mismatch'); end if;
        if m.status='admitted' then return jsonb_build_object('ok',true,'status','member_admitted'); end if;
        if m.native_claim_until is null or m.native_claim_until<=n then return jsonb_build_object('ok',false,'status','claim_expired'); end if;
        update prime.social_party_reservation_members set status='admitted',updated_at=n where request_id=v_request and player_id=p_player;
        if not exists(select 1 from prime.social_party_reservation_members where request_id=v_request and status not in ('admitted','declined')) then
            update prime.social_party_reservations set status='completed',updated_at=n where request_id=v_request and status='reserved'; end if;
        return jsonb_build_object('ok',true,'status','member_admitted');
    end if;
    -- An exact retry observes the original claim even if cancellation followed
    -- its linearization. It cannot replace, extend or create a claim.
    if a='claim_admission' then
        select * into m from prime.social_party_reservation_members where request_id=v_request and player_id=p_player;
        if m.native_claim_nonce is not null then
            if r.server_reservation_id is distinct from v_reservation or m.native_claim_nonce is distinct from v_nonce
                or m.native_connection_id is distinct from v_connection or m.slot is distinct from (p_input->>'slot')::smallint then
                return jsonb_build_object('ok',false,'status','claim_mismatch'); end if;
            if m.native_claim_until<=n then return jsonb_build_object('ok',false,'status','claim_expired'); end if;
            return jsonb_build_object('ok',true,'status','admitting','claim_expires_at',m.native_claim_until);
        end if;
    end if;
    reason:=prime.social_native_reservation_gate(v_request,p_reporter,p_player,p_ticket_deadline);
    if reason is not null then return jsonb_build_object('ok',false,'status',reason); end if;
    if a='validate' then
        if (p_input->>'purpose'='leader' and p_player<>r.leader_id) or (p_input->>'purpose'='member' and not exists(
            select 1 from prime.social_party_reservation_members where request_id=v_request and player_id=p_player and status<>'declined' and not native_declined)) then
            return jsonb_build_object('ok',false,'status','not_reservation_member'); end if;
        return prime.social_native_reservation_projection(v_request,p_player) || jsonb_build_object('ok',true,'player_id',p_player,'leader_id',r.leader_id,
            'members',(select jsonb_agg(player_id::text order by player_id) from prime.social_party_reservation_members where request_id=v_request),
            'roster',w->'roster');
    elsif a='activate' then
        if p_player<>r.leader_id then return jsonb_build_object('ok',false,'status','leader_only'); end if;
        select jsonb_agg(jsonb_build_object('player_id',x->>'player_id','slot',(x->>'slot')::integer) order by x->>'player_id')
            into v_assignments from jsonb_array_elements(p_input->'assignments') x;
        if v_assignments is null or jsonb_array_length(v_assignments)<>r.requested_count
            or (select count(distinct x->>'player_id') from jsonb_array_elements(v_assignments) x)<>r.requested_count
            or (select count(distinct x->>'slot') from jsonb_array_elements(v_assignments) x)<>r.requested_count
            or exists(select 1 from jsonb_array_elements(v_assignments) x where (x->>'slot')::integer not between 0 and 7 or not exists(
                select 1 from prime.social_party_reservation_members where request_id=v_request and player_id=(x->>'player_id')::uuid and status<>'declined')) then
            return jsonb_build_object('ok',false,'status','invalid_assignments'); end if;
        if r.status='reserved' then
            if r.server_reservation_id is distinct from v_reservation or r.native_assignments is distinct from v_assignments then
                return jsonb_build_object('ok',false,'status','reservation_mismatch'); end if;
            return jsonb_build_object('ok',true,'status','reserved','expires_at',r.reserved_until); end if;
        v_until:=least(r.expires_at,(p_input->>'expires_at')::timestamptz,n+interval '15 seconds',p_ticket_deadline);
        if v_until<=n then return jsonb_build_object('ok',false,'status','reservation_expired'); end if;
        update prime.social_party_reservations set status='reserved',reporter_id=p_reporter,server_reservation_id=v_reservation,
            reserved_until=v_until,native_assignments=v_assignments,updated_at=n where request_id=v_request;
        update prime.social_party_reservation_members assigned_member set slot=(x->>'slot')::smallint,status='reserved',updated_at=n
            from jsonb_array_elements(v_assignments) x where assigned_member.request_id=v_request and assigned_member.player_id=(x->>'player_id')::uuid;
        return jsonb_build_object('ok',true,'status','reserved','expires_at',v_until);
    elsif a='claim_admission' then
        select * into m from prime.social_party_reservation_members where request_id=v_request and player_id=p_player for update;
        if r.status<>'reserved' or r.server_reservation_id is distinct from v_reservation or m.status is distinct from 'reserved' or m.native_declined
            or m.slot is distinct from (p_input->>'slot')::smallint then return jsonb_build_object('ok',false,'status','member_not_reserved'); end if;
        v_until:=least(n+interval '2 seconds',r.expires_at,r.reserved_until,p_ticket_deadline,(p_input->>'operation_deadline')::timestamptz);
        if v_until<=n then return jsonb_build_object('ok',false,'status','claim_expired'); end if;
        update prime.social_party_reservation_members set native_claim_nonce=v_nonce,native_connection_id=v_connection,native_claim_until=v_until,updated_at=n
            where request_id=v_request and player_id=p_player;
        return jsonb_build_object('ok',true,'status','admitting','claim_expires_at',v_until);
    end if;
    return jsonb_build_object('ok',false,'status','invalid_action');
end;
$$;
revoke execute on function prime.social_native_reservation_immutable() from public,anon,authenticated;
revoke execute on function prime.social_native_reservation_projection(uuid,uuid) from public,anon,authenticated;
revoke execute on function prime.social_native_reservation_gate(uuid,uuid,uuid,timestamptz) from public,anon,authenticated;
revoke execute on function prime.social_native_party_reservation_action(uuid,text,jsonb) from public,anon,authenticated;
revoke execute on function prime.social_native_reservation_capability(uuid) from public,anon,authenticated;
revoke execute on function prime.social_native_reservation_server(uuid,uuid,timestamptz,jsonb) from public,anon,authenticated;

-- The legacy reporter endpoints cannot bypass native claim/witness gates.
alter function prime.social_party_reservation_server_validate(uuid,numeric,uuid,uuid) rename to social_party_reservation_server_validate_legacy_v1;
create function prime.social_party_reservation_server_validate(p_reporter uuid,p_authority_epoch numeric,p_request uuid,p_player uuid)
returns jsonb language plpgsql security invoker set search_path=prime,pg_temp as $$
begin
    if exists(select 1 from prime.social_party_reservations where request_id=p_request and native_witness is not null) then
        return jsonb_build_object('ok',false,'status','native_v1_required'); end if;
    return prime.social_party_reservation_server_validate_legacy_v1(p_reporter,p_authority_epoch,p_request,p_player);
end;
$$;
alter function prime.social_party_reservation_server_activate(uuid,numeric,uuid,uuid,jsonb,integer) rename to social_party_reservation_server_activate_legacy_v1;
create function prime.social_party_reservation_server_activate(p_reporter uuid,p_authority_epoch numeric,p_request uuid,p_reservation uuid,p_assignments jsonb,p_seconds integer)
returns jsonb language plpgsql security invoker set search_path=prime,pg_temp as $$
begin
    if exists(select 1 from prime.social_party_reservations where request_id=p_request and native_witness is not null) then
        return jsonb_build_object('ok',false,'status','native_v1_required'); end if;
    return prime.social_party_reservation_server_activate_legacy_v1(p_reporter,p_authority_epoch,p_request,p_reservation,p_assignments,p_seconds);
end;
$$;
alter function prime.social_party_reservation_server_admitted(uuid,uuid,uuid,uuid) rename to social_party_reservation_server_admitted_legacy_v1;
create function prime.social_party_reservation_server_admitted(p_reporter uuid,p_request uuid,p_reservation uuid,p_player uuid)
returns jsonb language plpgsql security invoker set search_path=prime,pg_temp as $$
begin
    if exists(select 1 from prime.social_party_reservations where request_id=p_request and native_witness is not null) then
        return jsonb_build_object('ok',false,'status','native_v1_required'); end if;
    return prime.social_party_reservation_server_admitted_legacy_v1(p_reporter,p_request,p_reservation,p_player);
end;
$$;
alter function prime.social_party_reservation_server_cancel(uuid,uuid,uuid,text) rename to social_party_reservation_server_cancel_legacy_v1;
create function prime.social_party_reservation_server_cancel(p_reporter uuid,p_request uuid,p_reservation uuid,p_status text default 'rejected')
returns jsonb language plpgsql security invoker set search_path=prime,pg_temp as $$
begin
    if exists(select 1 from prime.social_party_reservations where request_id=p_request and native_witness is not null) then
        return jsonb_build_object('ok',false,'status','native_v1_required'); end if;
    return prime.social_party_reservation_server_cancel_legacy_v1(p_reporter,p_request,p_reservation,p_status);
end;
$$;
revoke execute on function prime.social_party_reservation_server_validate(uuid,numeric,uuid,uuid) from public,anon,authenticated;
revoke execute on function prime.social_party_reservation_server_activate(uuid,numeric,uuid,uuid,jsonb,integer) from public,anon,authenticated;
revoke execute on function prime.social_party_reservation_server_admitted(uuid,uuid,uuid,uuid) from public,anon,authenticated;
revoke execute on function prime.social_party_reservation_server_cancel(uuid,uuid,uuid,text) from public,anon,authenticated;
