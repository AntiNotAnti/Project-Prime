import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import { PGlite } from '@electric-sql/pglite';
import { checkNativeParty } from './native-party.mjs';
import { checkNativePresence } from './native-presence.mjs';
import { checkNativeSocial } from './native-social.mjs';
import { checkNativePartyTravel, prepareNativePartyTravel } from './native-party-travel.mjs';
import { checkNativePartyReservationsV1 } from './native-party-reservations-v1.mjs';

const migrations = new URL('../../supabase/migrations/', import.meta.url);
const candidates = (await readdir(migrations))
  .filter(name => /(?:social|native_party_authority|party_reservation_expiry_fence|native_presence_authority|native_party_travel_and_membership_fence|native_party_group_reservations_v1|native_party_reservation_snapshot_projection).*\.sql$/.test(name)).sort();
const sources = new Map();
for (const name of candidates) {
  const sql = await readFile(new URL(name, migrations), 'utf8');
  if (sql.trim()) sources.set(name, sql);
  else console.log('SKIPPED EMPTY IN-PROGRESS MIGRATION', name);
}
const names = [...sources.keys()];
assert(names.filter(name => /social.*\.sql$/.test(name)).length >= 7, 'the seven restored Social migrations must be present');
const db = new PGlite(); // No dataDir, socket, database URL, credentials, or external service.
let checks = 0;
function check(value, label) { assert(value, label); checks++; }
const uuid = number => `${number.toString(16).padStart(8, '0')}-1111-4111-8111-111111111111`;
const players = Array.from({ length: 12 }, (_, n) => uuid(n + 1));
const [a, b, c, outsider] = players;
async function one(sql, args = []) { return (await db.query(sql, args)).rows[0]?.value; }
async function call(name, signature, args = []) {
  assert(/^[a-z_]+$/.test(name));
  return one(`select prime.${name}(${signature}) as value`, args);
}
const party = (actor, action, target = null, invite = null) =>
  call('social_party_action', '$1::uuid,$2::text,$3::text,$4::uuid', [actor, action, target, invite]);
const friends = (actor, target, action) =>
  call('social_mutate', '$1::uuid,$2::text,$3::text', [actor, target, action]);
const snapshot = actor => call('social_party_snapshot', '$1::uuid', [actor]);
const privacy = (actor, presence, activity, invites = 'friends', dnd = false) =>
  call('social_privacy_update', '$1::uuid,$2::text,$3::text,$4::text,$5::boolean', [actor, presence, activity, invites, dnd]);
async function rejected(work, label, code = null) {
  let error;
  try { await work(); } catch (caught) { error = caught; }
  check(error && (!code || error.code === code), label + (error ? ` [${error.code}]` : ' (unexpectedly accepted)'));
}

try {
  await db.exec(await readFile(new URL('bootstrap.sql', import.meta.url), 'utf8'));
  if (names.some(name => name.endsWith('_native_party_travel_and_membership_fence.sql'))) {
    // Exact predecessor registry DDL, not a replacement auth/reporting schema.
    // The remaining historical career ingestion functions are outside this
    // fixture and their saved delimiter syntax is not repaired here.
    const predecessor = await readFile(new URL('20260921195500_authoritative_career_ingestion.sql', migrations), 'utf8');
    const boundary = predecessor.indexOf('\nCREATE OR REPLACE FUNCTION public.project_prime_rating_tier');
    assert(boundary > 0, 'actual reporter registry predecessor boundary must exist');
    await db.exec(predecessor.slice(0, boundary));
    console.log('APPLIED REPORTER REGISTRY DDL PREFIX', '20260921195500_authoritative_career_ingestion.sql');
  }
  for (const name of names) {
    if (name.endsWith('_native_party_travel_and_membership_fence.sql')) {
      await prepareNativePartyTravel({ db, uuid });
    }
    const expiryMigration = name.endsWith('_party_reservation_expiry_fence.sql');
    const functionAcls = async () => (await db.query("select p.oid::regprocedure::text as signature,p.proacl::text as acl,p.prosecdef from pg_proc p join pg_namespace n on n.oid=p.pronamespace where n.nspname='prime' and p.proname in ('social_party_reservation_server_activate','social_party_reservation_server_admitted') order by p.proname")).rows;
    const before = expiryMigration ? await functionAcls() : null;
    try { await db.exec(sources.get(name)); }
    catch (error) { throw new Error(`Unmodified migration ${name} failed: ${error.message}`, { cause: error }); }
    if (expiryMigration) {
      const after = await functionAcls();
      check(before.length === 2 && JSON.stringify(before) === JSON.stringify(after), 'expiry hardening preserves function signatures, execution ACLs and invoker security');
    }
    console.log('APPLIED', name);
  }
  for (let n = 0; n < players.length; n++) {
    await db.query('insert into prime.players ("Id") values ($1::uuid)', [players[n]]);
    await db.query('insert into prime.player_profiles ("PlayerId","DisplayName") values ($1::uuid,$2)', [players[n], `Synthetic Hunter ${n + 1}`]);
  }
  const ids = new Map((await db.query('select player_id, prime_id from prime.social_profiles where player_id=any($1::uuid[])', [players])).rows.map(row => [row.player_id, row.prime_id]));
  check(ids.size === players.length, 'real profile/settings triggers seed stable Prime IDs');
  check((await one('select count(*)::int as value from prime.social_settings where player_id=any($1::uuid[])', [players])) === players.length, 'all seeded identities have privacy defaults');
  const pa = ids.get(a), pb = ids.get(b), pc = ids.get(c);

  check((await friends(a, pa, 'send_request')).status === 'self_target', 'self friend request rejected');
  check((await friends(a, pb, 'send_request')).ok, 'friend request created');
  check((await call('social_snapshot', '$1::uuid', [b])).incoming_requests.length === 1, 'recipient sees incoming request');
  check((await friends(b, pa, 'accept_request')).ok, 'recipient accepts request');
  check((await friends(a, pb, 'send_request')).status === 'already_friends', 'duplicate friend request converges');
  check((await call('social_snapshot', '$1::uuid', [a])).friends.length === 1, 'real friendship snapshot returned');

  await call('social_presence_heartbeat', '$1::uuid,$2::uuid,$3::text,$4::text,$5::boolean,$6::uuid', [b, uuid(900), 'in_match', 'SYNTHETIC ROOM', true, null]);
  let visible = await call('social_presence_snapshot', '$1::uuid', [a]);
  check(visible.players[0].activity === 'in_match' && !visible.players[0].joinable, 'friends receive permitted activity; heartbeat cannot forge joinability');
  await privacy(b, 'everyone', 'private');
  visible = await call('social_presence_snapshot', '$1::uuid', [a]);
  check(visible.players[0].activity === 'online' && visible.players[0].room_key === null && !visible.players[0].joinable, 'private activity redacts room and joining');
  await privacy(b, 'hidden', 'private');
  check((await call('social_presence_snapshot', '$1::uuid', [a])).players.length === 0, 'hidden presence excluded');
  await privacy(b, 'friends', 'friends');
  check((await call('social_presence_snapshot', '$1::uuid', [c])).players.length === 0, 'friends-only presence excludes strangers');
  await db.query("update prime.social_presence_sessions set updated_at=now()-interval '46 seconds' where player_id=$1::uuid", [b]);
  check((await call('social_presence_snapshot', '$1::uuid', [a])).players.length === 0, '45-second presence TTL excludes stale sessions without sleeping');
  await rejected(() => privacy(b, 'invalid', 'friends'), 'invalid privacy fails without mutation');
  check((await call('social_presence_snapshot', '$1::uuid', [b])).settings.presence_visibility === 'friends', 'invalid privacy retains previous settings');

  check((await friends(a, pb, 'block_player')).ok, 'blocking accepted');
  let directory = await call('social_snapshot', '$1::uuid', [a]);
  check(directory.friends.length === 0 && directory.blocked.length === 1, 'block removes friendship');
  check(await call('social_lookup', '$1::uuid,$2::text', [b, pa]) === null, 'blocked identity hidden in reverse lookup');
  check((await friends(b, pa, 'send_request')).status === 'blocked', 'reverse friend request rejected while blocked');
  await friends(a, pb, 'unblock_player');
  for (const target of players.slice(1)) {
    await friends(a, ids.get(target), 'send_request');
    await friends(target, pa, 'accept_request');
  }

  check((await party(a, 'invite', ids.get(outsider))).ok, 'leader creates party and invite');
  let invite = (await snapshot(outsider)).incoming_party_invites[0];
  check((await party(c, 'accept', null, invite.invite_id)).status === 'not_recipient', 'foreign account cannot consume party invite');
  check((await party(outsider, 'decline', null, invite.invite_id)).ok, 'recipient declines invite');
  await party(a, 'invite', pb);
  invite = (await snapshot(b)).incoming_party_invites[0];
  check((await party(b, 'accept', null, invite.invite_id)).ok, 'recipient accepts party invite');
  check((await party(b, 'accept', null, invite.invite_id)).status === 'party_invite_closed', 'accepted invite cannot be reused');
  check((await party(b, 'promote', pa)).status === 'leader_only', 'follower cannot promote');
  check((await party(a, 'promote', pb)).ok, 'current leader promotes member');
  check((await snapshot(b)).party.leader_prime_id === pb, 'party snapshot reflects promoted leader');
  check((await party(a, 'disband')).status === 'leader_only', 'former leader cannot disband');
  check((await party(b, 'leave')).ok, 'leader departure transfers remaining membership');
  check((await snapshot(a)).party.leader_prime_id === pa, 'remaining member becomes leader');
  await party(a, 'invite', pc);
  invite = (await snapshot(c)).incoming_party_invites[0];
  await db.query("update prime.social_party_invites set expires_at=now()-interval '1 second' where invite_id=$1::uuid", [invite.invite_id]);
  check((await party(c, 'accept', null, invite.invite_id)).status === 'party_invite_closed', 'expired invite cannot be accepted');
  check((await snapshot(c)).incoming_party_invites.length === 0, 'expired invite removed from read model');
  await privacy(c, 'everyone', 'friends', 'friends', true);
  check((await party(a, 'invite', pc)).status === 'do_not_disturb', 'DND rejects new party invites');
  await privacy(c, 'everyone', 'friends');
  for (const target of players.slice(1, 8)) {
    await party(a, 'invite', ids.get(target));
    const incoming = (await snapshot(target)).incoming_party_invites.find(value => value.prime_id === pa);
    check(incoming && (await party(target, 'accept', null, incoming.invite_id)).ok, 'member admitted under eight-seat capacity');
  }
  check((await snapshot(a)).party.members.length === 8, 'party has exactly eight members');
  check((await party(a, 'invite', ids.get(players[8]))).status === 'party_full', 'ninth seat rejected');
  check((await party(a, 'kick', pb)).ok, 'leader removes selected member');
  check((await snapshot(b)).party === null, 'removed member no longer owns party');
  check((await party(a, 'disband')).ok, 'leader disbands party');
  check((await snapshot(a)).party === null && (await snapshot(c)).party === null, 'disband cascades membership');

  // Two-member travel/reservation exercise uses actual database authorities.
  await party(a, 'invite', pb);
  invite = (await snapshot(b)).incoming_party_invites[0];
  await party(b, 'accept', null, invite.invite_id);
  const reporter = uuid(700), epoch = '123456789';
  await db.query("insert into prime.social_lobby_memberships(player_id,authority_epoch,reporter_id,client_id,expires_at,lobby_eligible) values($1::uuid,$2::numeric,$3::uuid,1,now()+interval '90 seconds',true)", [a, epoch, reporter]);
  const registration = await call('social_lobby_register', '$1::uuid,$2::inet,$3::int,$4::numeric,$5::int,$6::text,$7::text', [a, '8.8.8.8', 27015, epoch, 57, 'SYNTHETIC ROOM', 'SYNTHETIC SERVER']);
  check(registration.ok, 'verified membership can register lobby');
  const lobbyId = registration.lobby.lobby_id;
  let travel = await call('social_party_travel_publish', '$1::uuid,$2::uuid,$3::text', [a, lobbyId, 'regroup']);
  check(travel.ok, 'leader publishes verified travel');
  const travelState = travel.travel;
  check((await call('social_party_travel_respond', '$1::uuid,$2::uuid,$3::int,$4::text', [b, travelState.travel_id, travelState.revision + 1, 'follow'])).ok === false, 'stale travel revision rejected');
  check((await call('social_party_travel_respond', '$1::uuid,$2::uuid,$3::int,$4::text', [b, travelState.travel_id, travelState.revision, 'follow'])).ok, 'follower explicitly consents to current travel');
  let reserved = await call('social_party_reservation_request', '$1::uuid,$2::numeric,$3::boolean', [a, epoch, false]);
  check(reserved.ok && reserved.reservation.requested_count === 1, 'reservation excludes leader already in lobby');
  const request = reserved.reservation.request_id;
  check((await call('social_party_reservation_server_validate', '$1::uuid,$2::numeric,$3::uuid,$4::uuid', [reporter, epoch, request, c])).status === 'not_reservation_member', 'server rejects unrelated reservation claimant');
  check((await call('social_party_reservation_server_validate', '$1::uuid,$2::numeric,$3::uuid,$4::uuid', [reporter, epoch, request, b])).ok, 'server validates actual reserved member');
  check((await call('social_party_reservation_cancel', '$1::uuid,$2::uuid', [b, request])).status === 'leader_only', 'follower cannot cancel leader reservation');
  check((await call('social_party_reservation_cancel', '$1::uuid,$2::uuid', [a, uuid(999)])).status === 'reservation_already_clear', 'wrong owned request ID cannot cancel active reservation');
  check((await call('social_party_reservation_snapshot', '$1::uuid', [a])).request_id === request, 'wrong request preserves active reservation');
  check((await call('social_party_reservation_cancel', '$1::uuid,$2::uuid', [a, request])).status === 'reservation_cancelled', 'leader cancels exact owned request');

  reserved = await call('social_party_reservation_request', '$1::uuid,$2::numeric,$3::boolean', [a, epoch, false]);
  const activationRequest = reserved.reservation.request_id, serverReservation = uuid(701);
  const activationSignature = '$1::uuid,$2::numeric,$3::uuid,$4::uuid,$5::jsonb,$6::int';
  const assignment = JSON.stringify([{ player_id: b, slot: 2 }]);
  check((await call('social_party_reservation_server_activate', activationSignature,
    [reporter, epoch, activationRequest, serverReservation, JSON.stringify([{ player_id: c, slot: 2 }]), 30])).status === 'assignment_mismatch', 'activation rejects unrelated assignment');
  check((await call('social_party_reservation_server_activate', activationSignature,
    [reporter, '987654321', activationRequest, serverReservation, assignment, 30])).status === 'authority_mismatch', 'activation rejects wrong authority epoch');
  check((await call('social_party_reservation_server_activate', activationSignature,
    [reporter, epoch, activationRequest, serverReservation, assignment, 30])).ok, 'server activates matching bounded seat assignment');
  let reservationState = await call('social_party_reservation_snapshot', '$1::uuid', [a]);
  check(reservationState.status === 'reserved' && reservationState.members[0].slot === 2, 'reserved slot published from server assignment');
  check((await call('social_party_reservation_server_validate', '$1::uuid,$2::numeric,$3::uuid,$4::uuid',
    [uuid(702), epoch, activationRequest, b])).status === 'reporter_mismatch', 'activated reservation bound to reporting server');
  check((await call('social_party_reservation_server_admitted', '$1::uuid,$2::uuid,$3::uuid,$4::uuid',
    [reporter, activationRequest, serverReservation, b])).status === 'reservation_completed', 'actual member admission completes whole reservation');
  check((await call('social_party_reservation_server_admitted', '$1::uuid,$2::uuid,$3::uuid,$4::uuid',
    [reporter, activationRequest, serverReservation, b])).ok === false, 'completed reservation cannot admit twice');
  reserved = await call('social_party_reservation_request', '$1::uuid,$2::numeric,$3::boolean', [a, epoch, false]);
  const expiredRequest = reserved.reservation.request_id;
  await db.query("update prime.social_party_reservations set expires_at=now()-interval '1 second' where request_id=$1::uuid", [expiredRequest]);
  check((await call('social_party_reservation_server_validate', '$1::uuid,$2::numeric,$3::uuid,$4::uuid',
    [reporter, epoch, expiredRequest, b])).status === 'reservation_closed', 'server validation expires elapsed pending reservation');
  check(await call('social_party_reservation_snapshot', '$1::uuid', [a]) === null, 'expired reservation absent from active snapshot');

  // Exercise each handler directly, without letting snapshot/validate expire
  // the row first. This reproduces delayed messages arriving after a lease.
  const activate = requestId => call('social_party_reservation_server_activate', activationSignature,
    [reporter, epoch, requestId, serverReservation, assignment, 30]);
  const admit = requestId => call('social_party_reservation_server_admitted', '$1::uuid,$2::uuid,$3::uuid,$4::uuid',
    [reporter, requestId, serverReservation, b]);
  async function newRequest() {
    const result = await call('social_party_reservation_request', '$1::uuid,$2::numeric,$3::boolean', [a, epoch, false]);
    check(result.ok && result.reservation, 'fresh request available for isolated expiry regression');
    return result.reservation.request_id;
  }
  async function expiredActivation(label, reserved, fallback = false) {
    const requestId = await newRequest();
    if (reserved) check((await activate(requestId)).ok, 'prepare valid reserved lease');
    if (reserved && !fallback) {
      await db.query("update prime.social_party_reservations set reserved_until=clock_timestamp()-interval '1 second', expires_at=clock_timestamp()+interval '1 minute' where request_id=$1::uuid", [requestId]);
    } else {
      await db.query("update prime.social_party_reservations set reserved_until=null, expires_at=clock_timestamp()-interval '1 second' where request_id=$1::uuid", [requestId]);
    }
    if (!reserved) check((await admit(requestId)).ok === false, 'expired pending request cannot admit before activation');
    const before = (await db.query('select expires_at,reserved_until from prime.social_party_reservations where request_id=$1::uuid', [requestId])).rows[0];
    check((await activate(requestId)).status === 'reservation_closed', label);
    const after = (await db.query('select status,expires_at,reserved_until from prime.social_party_reservations where request_id=$1::uuid', [requestId])).rows[0];
    check(after.status === 'expired' && String(after.expires_at) === String(before.expires_at)
      && String(after.reserved_until) === String(before.reserved_until), 'expiry cannot renew either persisted deadline');
    check((await activate(requestId)).ok === false && (await admit(requestId)).ok === false, 'retired lease cannot be activated or admitted again');
    check(await one("select count(*)::int as value from prime.social_party_reservation_members where request_id=$1::uuid and status='admitted'", [requestId]) === 0, 'expired activation cannot admit a member');
  }
  await expiredActivation('expired pending request cannot activate without prior validation', false);
  await expiredActivation('reserved_until expiry wins over future expires_at during reactivation', true);
  await expiredActivation('reserved reactivation honors expires_at fallback when reserved_until is null', true, true);
  for (const fallback of [false, true]) {
    const requestId = await newRequest();
    check((await admit(requestId)).ok === false, 'pending request cannot admit before server activation');
    check((await activate(requestId)).ok, 'live pending lease still activates');
    check((await activate(requestId)).ok, 'live reserved lease still permits matching server refresh');
    await db.query(fallback
      ? "update prime.social_party_reservations set reserved_until=null, expires_at=clock_timestamp()-interval '1 second' where request_id=$1::uuid"
      : "update prime.social_party_reservations set reserved_until=clock_timestamp()-interval '1 second', expires_at=clock_timestamp()+interval '1 minute' where request_id=$1::uuid", [requestId]);
    check((await admit(requestId)).status === 'reservation_closed', 'admission enforces ' + (fallback ? 'expires_at fallback' : 'reserved_until deadline despite future expires_at'));
    check((await one('select status as value from prime.social_party_reservations where request_id=$1::uuid', [requestId])) === 'expired', 'expired admission permanently retires reservation');
    check((await activate(requestId)).ok === false && (await admit(requestId)).ok === false, 'late activation/admission cannot resurrect expired reservation');
    check(await one("select count(*)::int as value from prime.social_party_reservation_members where request_id=$1::uuid and status='admitted'", [requestId]) === 0, 'rejected late admission leaves member state untouched');
  }
  for (const admission of [false, true]) {
    const requestId = await newRequest();
    if (admission) check((await activate(requestId)).ok, 'prepare valid reservation for admission wall-clock regression');
    await db.exec('begin');
    try {
      await db.query(admission
        ? "update prime.social_party_reservations set reserved_until=clock_timestamp()+interval '50 milliseconds' where request_id=$1::uuid"
        : "update prime.social_party_reservations set expires_at=clock_timestamp()+interval '50 milliseconds' where request_id=$1::uuid", [requestId]);
      // This models a request whose transaction began in time but reaches the
      // handler after its lease. No concurrent database or long sleep needed.
      await new Promise(resolve => setTimeout(resolve, 100));
      check(await one(`select now() < ${admission ? 'reserved_until' : 'expires_at'} and clock_timestamp() > ${admission ? 'reserved_until' : 'expires_at'} as value from prime.social_party_reservations where request_id=$1::uuid`, [requestId]), 'transaction-start clock remains valid while actual lease has elapsed');
      check((await (admission ? admit(requestId) : activate(requestId))).status === 'reservation_closed', (admission ? 'admission' : 'activation') + ' checks wall clock rather than stale transaction start');
      await db.exec('commit');
    } catch (error) { await db.exec('rollback'); throw error; }
  }
  if (names.some(name => name.endsWith('_native_party_authority.sql'))) {
    await checkNativeParty({ db, check, call, uuid, rejected });
  }
  if (names.some(name => name.endsWith('_native_presence_authority.sql'))) {
    await checkNativePresence({ db, check, call, uuid, rejected });
  }
  if (names.some(name => name.endsWith('_native_social_account_witness.sql'))) {
    await checkNativeSocial({ db, check, call, uuid, rejected });
  }
  if (names.some(name => name.endsWith('_native_party_travel_and_membership_fence.sql'))) {
    await checkNativePartyTravel({ db, check, call, uuid, rejected });
  }
  if (names.some(name => name.endsWith('_native_party_group_reservations_v1.sql'))) {
    await checkNativePartyReservationsV1({ db, check, call, uuid, rejected });
  }

  const tables = (await db.query("select c.relname,c.relrowsecurity from pg_class c join pg_namespace n on n.oid=c.relnamespace where n.nspname='prime' and c.relkind='r' and c.relname not in ('players','player_profiles')")).rows;
  check(tables.length >= 15 && tables.every(row => row.relrowsecurity), 'every Social table enables RLS');
  // Schema usage makes these table/execute denial checks stronger than a schema
  // visibility denial. This grant belongs only to the disposable fixture.
  await db.exec('grant usage on schema prime,auth to anon,authenticated');
  for (const role of ['anon', 'authenticated']) {
    await db.exec(`set role ${role}`);
    try {
      for (const { relname } of tables) await rejected(() => db.query(`select * from prime.${relname}`), `${role} cannot directly read ${relname}`, '42501');
      await rejected(() => call('social_mutate', '$1::uuid,$2::text,$3::text', [a, pb, 'block_player']), `${role} cannot impersonate actor through SQL function`, '42501');
      await rejected(() => db.query('delete from prime.social_party_members'), `${role} cannot directly mutate party`, '42501');
      await rejected(() => activate(expiredRequest), `${role} cannot execute server activation`, '42501');
      await rejected(() => admit(expiredRequest), `${role} cannot execute server admission`, '42501');
    } finally { await db.exec('reset role'); }
  }
  // RLS remains a second fence even if a future deployment accidentally grants
  // SELECT. Roll back those temporary grants after querying as a real role.
  await db.exec('begin; grant select on all tables in schema prime to authenticated; set local role authenticated;');
  await db.query("select set_config('request.jwt.claim.sub',$1,true)", [a]);
  check((await db.query('select player_id from prime.social_profiles')).rows.every(row => row.player_id === a), 'RLS excludes other profile identities after hypothetical SELECT grant');
  check((await db.query('select player_id from prime.social_profiles')).rows.length === 1, 'RLS still permits actor own profile');
  check((await db.query('select player_id from prime.social_settings')).rows.every(row => row.player_id === a), 'privacy RLS excludes other accounts');
  await db.exec('rollback');
  const postgres = await one('select version() as value');
  console.log(postgres);
  console.log(`SOCIAL DB PASS ${checks} assertions; ${names.length} exact repository migrations; isolated in-memory PostgreSQL.`);
} finally { await db.close(); }
