// Synthetic identities and the actual saved SQL authorities only. This is not
// an Edge/JWT emulator, a live backend check, or a network admission claim.
const actors = uuid => [1401, 1402, 1403, 1404, 1405].map(uuid);
const reporters = uuid => [1461, 1462, 1463].map(uuid);
const seededEpoch = '140001', conflictEpoch = '140002';
const reportSignature = '$1::uuid,$2::uuid,$3::numeric,$4::bigint,$5::numeric,$6::boolean,$7::boolean,$8::bigint';
const travelSignature = '$1::uuid,$2::text,$3::bigint,$4::uuid,$5::text,$6::uuid,$7::int,$8::inet,$9::int,$10::numeric,$11::int,$12::text,$13::text';

// Run immediately before the new migration: ownership backfill must preserve
// real predecessor rows, including refusing ambiguous live reporter ownership.
export async function prepareNativePartyTravel({ db, uuid }) {
  const [a, b, c, d, e] = actors(uuid);
  const [r1, r2, disabled] = reporters(uuid);
  for (const [index, actor] of [a, b, c, d, e].entries()) {
    await db.query('insert into prime.players("Id") values($1::uuid)', [actor]);
    await db.query('insert into prime.player_profiles("PlayerId","DisplayName") values($1::uuid,$2)', [actor, `Synthetic Native Travel ${index}`]);
  }
  for (const [index, reporter] of [r1, r2, disabled].entries()) {
    await db.query('insert into public.project_prime_career_reporters(server_id,display_name,key_hash,enabled) values($1::uuid,$2,$3,$4)',
      [reporter, `Synthetic Reporter ${index}`, (index + 1).toString(16).repeat(64), reporter !== disabled]);
  }
  for (const [actor, epoch, reporter] of [[c, seededEpoch, r1], [d, conflictEpoch, r1], [e, conflictEpoch, r2]]) {
    await db.query("insert into prime.social_lobby_memberships(player_id,authority_epoch,reporter_id,client_id,lobby_eligible,expires_at) values($1::uuid,$2::numeric,$3::uuid,1,true,clock_timestamp()+interval '1 minute')", [actor, epoch, reporter]);
  }
}

export async function checkNativePartyTravel({ db, check, call, uuid, rejected }) {
  const [a, b, c, d, e] = actors(uuid);
  const [r1, r2, disabled] = reporters(uuid);
  const scalar = async (sql, args = []) => (await db.query(sql, args)).rows[0]?.value;
  const state = actor => call('social_native_party_travel_state', '$1::uuid', [actor]);
  const report = (actor, epoch, sequence, options = {}) => call('social_native_lobby_membership_report', reportSignature,
    [options.reporter ?? r1, actor, epoch, options.client ?? 1, sequence, options.eligible ?? true,
      options.leave ?? false, options.expiry ?? Math.floor(Date.now() / 1000) + 300]);
  const membershipCount = (actor, epoch) => scalar('select count(*)::int as value from prime.social_lobby_memberships where player_id=$1::uuid and authority_epoch=$2::numeric', [actor, epoch]);
  const action = (actor, verb, witness, options = {}) => {
    const travel = Object.hasOwn(options, 'travel') ? options.travel : witness.travel;
    return call('social_native_party_travel_action', travelSignature,
      [actor, verb, options.revision ?? witness.server_revision, options.party ?? witness.party_id,
        options.self ?? witness.self_prime_id, travel?.travel_id ?? null, travel?.revision ?? 0,
        options.host ?? '8.8.8.8', 27015, options.epoch ?? '140101', 57,
        options.room ?? 'SYNTHETIC NATIVE ROOM A', 'SYNTHETIC NATIVE SERVER']);
  };

  check(await scalar('select reporter_id::text as value from prime.social_native_authority_epochs where authority_epoch=$1::numeric', [seededEpoch]) === r1,
    'migration preserves unique existing reporter ownership');
  check(await scalar('select count(*)::int as value from prime.social_native_authority_epochs where authority_epoch=$1::numeric', [conflictEpoch]) === 0,
    'migration never guesses owner of conflicting legacy epoch');
  check((await report(c, seededEpoch, 1, { reporter: r2 })).status === 'authority_owner_changed', 'different reporter cannot claim backfilled epoch');
  check((await report(d, conflictEpoch, 1)).status === 'authority_owner_changed', 'live conflicting predecessor membership blocks first reporter claim');
  check((await report(e, conflictEpoch, 1, { reporter: r2 })).status === 'authority_owner_changed', 'live conflict blocks either reporter rather than picking arrival order');
  await db.query('delete from prime.social_lobby_memberships where authority_epoch=$1::numeric and reporter_id=$2::uuid', [conflictEpoch, r2]);
  check((await report(d, conflictEpoch, 1)).ok, 'unambiguous surviving reporter may claim previously conflicted epoch');

  const epoch = '140010';
  let result = await report(c, epoch, 10);
  check(result.ok && result.player_id === c && await membershipCount(c, epoch) === 1, 'positive native heartbeat records verified player membership');
  check((await report(c, epoch, 9)).status === 'stale_report' && (await report(c, epoch, 10)).status === 'stale_report', 'lower and duplicate positive report sequences rejected');
  check((await report(c, epoch, 20, { leave: true })).ok && await membershipCount(c, epoch) === 0, 'positive leave removes exact membership');
  for (const sequence of [0, 10, 19, 20]) {
    check((await report(c, epoch, sequence)).status === 'stale_report', `leave tombstone rejects delayed heartbeat ${sequence}`);
  }
  check(await membershipCount(c, epoch) === 0, 'delayed native heartbeat cannot resurrect departed membership');
  check(await scalar('select reporter_sequence::text as value from prime.social_native_membership_fences where reporter_id=$1::uuid and authority_epoch=$2::numeric and player_id=$3::uuid', [r1, epoch, c]) === '20',
    'membership deletion retains sequence tombstone');
  check((await report(c, epoch, 21)).ok, 'strictly newer native report may reestablish membership');
  await db.query("update prime.social_lobby_memberships set expires_at=clock_timestamp()-interval '1 second' where player_id=$1::uuid and authority_epoch=$2::numeric", [c, epoch]);
  await db.query('delete from prime.social_lobby_memberships where expires_at<=clock_timestamp() and player_id=$1::uuid', [c]);
  check((await report(c, epoch, 21)).status === 'stale_report' && await membershipCount(c, epoch) === 0, 'TTL cleanup cannot erase native sequence floor');
  check((await report(c, epoch, 1)).status === 'stale_report', 'reused process epoch cannot silently reset sequence');
  check((await report(c, '140011', 1)).ok, 'fresh process authority epoch permits new positive sequence');
  check((await report(d, epoch, 1)).ok, 'per-player sequence floor does not reject independent participant');
  await db.query('delete from prime.social_lobby_memberships where authority_epoch=$1::numeric', [epoch]);
  check((await report(c, epoch, 22, { reporter: r2 })).status === 'authority_owner_changed', 'epoch reporter owner survives deletion of every membership');
  check((await report(c, '140012', 1, { reporter: disabled })).status === 'report_expired', 'disabled reporter cannot establish membership or epoch');
  check((await report(c, '140012', 1, { reporter: uuid(1499) })).status === 'report_expired', 'unregistered reporter cannot establish membership or epoch');
  check((await report(c, '140012', 1, { expiry: Math.floor(Date.now() / 1000) - 61 })).status === 'report_expired', 'expired signed-ticket deadline rejected at database admission');
  check(await scalar('select count(*)::int as value from prime.social_native_authority_epochs where authority_epoch=140012') === 0, 'rejected reporter/ticket does not reserve epoch ownership');
  for (const [badEpoch, badSequence, options] of [['0', 1, {}], ['18446744073709551616', 1, {}], ['1.5', 1, {}], ['140013', -1, {}], ['140013', '9223372036854775808', {}], ['140013', '1.5', {}], ['140013', 1, { client: 0 }]]) {
    check((await report(c, badEpoch, badSequence, options)).status === 'invalid_membership', 'database independently validates native report numeric bounds');
  }
  check((await report(c, '140014', '9223372036854775807')).ok
    && (await report(c, '140014', '9223372036854775807')).status === 'stale_report', 'maximum signed sequence round-trips without JavaScript precision loss');
  check((await report(c, '18446744073709551615', 1)).ok, 'maximum unsigned epoch round-trips as exact decimal');
  // Compatibility boundary: legacy zero reports are intentionally unordered
  // until a positive native report creates a permanent floor.
  check((await report(c, '140015', 0, { leave: true })).ok && (await report(c, '140015', 0)).ok,
    'legacy zero sequence remains explicitly unordered before first positive floor');
  check((await report(c, '140015', 1, { leave: true })).ok && (await report(c, '140015', 0)).status === 'stale_report',
    'first positive sequence permanently fences legacy zero reports');
  await db.exec('begin');
  try {
    const expiry = await scalar('select ceil(extract(epoch from clock_timestamp()))::bigint-60 as value');
    await new Promise(resolve => setTimeout(resolve, 1100));
    check(await scalar("select now()<to_timestamp($1::bigint)+interval '60 seconds' and clock_timestamp()>to_timestamp($1::bigint)+interval '60 seconds' as value", [expiry]),
      'signed ticket grace deadline passes while transaction-start clock remains valid');
    check((await report(c, '140016', 1, { expiry })).status === 'report_expired', 'membership admission rechecks signed ticket deadline after transaction begins');
    check(await membershipCount(c, '140016') === 0, 'elapsed signed ticket creates no membership');
    await db.exec('commit');
  } catch (error) { await db.exec('rollback'); throw error; }

  const initialA = await state(a), initialB = await state(b);
  const party = (actor, verb, target = null, invite = null) => call('social_party_action', '$1::uuid,$2::text,$3::text,$4::uuid', [actor, verb, target, invite]);
  check((await call('social_mutate', '$1::uuid,$2::text,$3::text', [a, initialB.self_prime_id, 'send_request'])).ok, 'native travel fixture creates actual friend request');
  check((await call('social_mutate', '$1::uuid,$2::text,$3::text', [b, initialA.self_prime_id, 'accept_request'])).ok, 'native travel fixture accepts actual friendship');
  check((await party(a, 'invite', initialB.self_prime_id)).ok, 'native travel fixture creates actual party invitation');
  const invitation = (await state(b)).incoming[0];
  check((await party(b, 'accept', null, invitation.invite_id)).ok, 'native travel fixture joins actual two-member party');
  let sa = await state(a), sb = await state(b);
  check((await action(a, 'publish_travel', sa, { self: sb.self_prime_id })).status === 'account_changed', 'travel mutation rejects wrong authenticated-self witness');
  check((await action(a, 'publish_travel', sa, { revision: sa.server_revision + 1 })).status === 'party_changed', 'travel mutation rejects stale or future party revision');
  check((await action(a, 'publish_travel', sa, { party: uuid(1498) })).status === 'party_changed', 'travel mutation rejects wrong party identity');
  check((await action(a, 'publish_travel', sa)).status === 'membership_unverified', 'native publish cannot register lobby without verified leader membership');
  check((await report(a, '140101', 1, { eligible: false })).ok, 'fixture records verified but ineligible leader membership');
  check((await action(a, 'publish_travel', sa)).status === 'membership_unverified', 'verified spectator/nonlobby membership cannot authorize publish');
  check((await report(a, '140101', 2)).ok, 'fixture establishes eligible leader lobby membership');
  check((await action(a, 'publish_travel', sa, { host: '127.0.0.1' })).ok === false, 'native publish preserves existing public endpoint validation');
  result = await action(a, 'publish_travel', sa);
  check(result.ok && result.snapshot.travel && result.snapshot.server_revision === sa.server_revision, 'native publish uses existing travel without changing membership revision');
  const travelA = result.snapshot;
  sb = await state(b);
  result = await action(b, 'follow_travel', sb);
  check(result.ok && result.locator && result.snapshot.travel.self_status === 'following', 'follower consent returns exact verified lobby locator');
  check(result.locator.lobby_id === sb.travel.lobby_id && result.locator.authority_epoch === '140101', 'locator binds current travel lobby and authority epoch');
  check((await action(b, 'joined_travel', sb)).status === 'admission_not_verified', 'follow consent alone is not verified lobby admission');
  check((await report(b, '140101', 1, { eligible: false })).ok, 'fixture records ineligible follower presence');
  check((await action(b, 'joined_travel', sb)).status === 'admission_not_verified', 'ineligible membership cannot claim joined response');
  check((await report(b, '140101', 2)).ok && (await action(b, 'joined_travel', sb)).ok, 'joined response requires current verified eligible destination membership');
  check((await action(b, 'decline_travel', sb)).ok, 'follower may explicitly decline existing verified travel');
  check((await action(c, 'follow_travel', sb)).status === 'account_changed', 'unrelated account cannot reuse follower travel witness');

  check((await report(a, '140102', 1)).ok, 'leader obtains independent verified replacement lobby membership');
  result = await action(a, 'publish_travel', travelA, { epoch: '140102', room: 'SYNTHETIC NATIVE ROOM B' });
  check(result.ok && result.snapshot.travel.authority_epoch === '140102'
    && result.snapshot.travel.revision !== travelA.travel.revision, 'exact current travel witness can publish distinct replacement destination');
  const travelB = result.snapshot;
  check((await action(a, 'clear_travel', travelA)).status === 'party_travel_stale', 'delayed Clear A cannot erase newer travel B');
  check((await action(a, 'publish_travel', travelA)).status === 'party_travel_stale', 'delayed Publish from A cannot overwrite newer travel B');
  check((await action(a, 'publish_travel', sa)).status === 'party_travel_stale', 'delayed empty travel witness cannot overwrite an existing destination');
  check((await state(a)).travel.authority_epoch === '140102', 'rejected stale writes preserve replacement travel B');
  check((await action(b, 'clear_travel', await state(b))).status === 'leader_only', 'exact travel witness does not confer leader clear authority');
  check((await action(a, 'clear_travel', travelB)).ok && (await state(a)).travel == null, 'current leader can clear exact witnessed travel');
  check((await action(a, 'clear_travel', travelB)).status === 'party_travel_stale', 'repeated exact Clear cannot affect a later logical destination');

  async function freshTravel() {
    await report(a, '140102', ++refreshSequence);
    return (await action(a, 'publish_travel', await state(a), { epoch: '140102', room: 'SYNTHETIC NATIVE ROOM B' })).snapshot;
  }
  let refreshSequence = 1;
  for (const deadline of ['travel', 'lobby', 'membership']) {
    const live = await freshTravel();
    check(live?.travel != null, 'fresh verified travel available for isolated wall-clock regression');
    const follower = await state(b);
    await db.exec('begin');
    try {
      const update = deadline === 'travel'
        ? 'update prime.social_party_travel set expires_at=clock_timestamp()+interval \'50 milliseconds\' where party_id=$1::uuid'
        : deadline === 'lobby'
          ? 'update prime.social_lobbies set expires_at=clock_timestamp()+interval \'50 milliseconds\' where lobby_id=$1::uuid'
          : 'update prime.social_lobby_memberships set expires_at=clock_timestamp()+interval \'50 milliseconds\' where player_id=$1::uuid and authority_epoch=140102';
      const id = deadline === 'travel' ? live.party_id : deadline === 'lobby' ? live.travel.lobby_id : a;
      await db.query(update, [id]);
      // Simulate an already-open transaction waiting until its real authority
      // deadline passes; no second database, external clock, or live API.
      await new Promise(resolve => setTimeout(resolve, 100));
      const table = deadline === 'travel' ? 'social_party_travel' : deadline === 'lobby' ? 'social_lobbies' : 'social_lobby_memberships';
      const key = deadline === 'travel' ? 'party_id' : deadline === 'lobby' ? 'lobby_id' : 'player_id';
      check(await scalar(`select now()<expires_at and clock_timestamp()>expires_at as value from prime.${table} where ${key}=$1::uuid${deadline === 'membership' ? ' and authority_epoch=140102' : ''}`, [id]), `${deadline} deadline passes while transaction-start clock remains valid`);
      check((await state(a)).travel == null, `native travel projection expires ${deadline} using wall clock`);
      result = await action(b, 'follow_travel', follower);
      check(result.status === (deadline === 'travel' ? 'party_travel_stale' : 'lobby_unavailable'), `follow rejects elapsed ${deadline} after transaction begins`);
      await db.exec('commit');
    } catch (error) { await db.exec('rollback'); throw error; }
  }

  for (const role of ['anon', 'authenticated']) {
    await db.exec(`set role ${role}`);
    try {
      await rejected(() => state(a), `${role} cannot project arbitrary native travel actor`, '42501');
      await rejected(() => action(a, 'publish_travel', sa), `${role} cannot mutate native travel actor`, '42501');
      await rejected(() => report(c, '140199', 1), `${role} cannot forge verified reporter membership`, '42501');
    } finally { await db.exec('reset role'); }
  }
  const invokers = (await db.query("select prosecdef from pg_proc p join pg_namespace n on n.oid=p.pronamespace where n.nspname='prime' and proname in ('social_native_lobby_membership_report','social_native_party_travel_state','social_native_party_travel_action')")).rows;
  check(invokers.length === 3 && invokers.every(row => !row.prosecdef), 'all native membership/travel functions retain invoker security');
  await db.exec('begin; grant select on prime.social_native_authority_epochs,prime.social_native_membership_fences to authenticated; set local role authenticated;');
  check((await db.query('select * from prime.social_native_authority_epochs')).rows.length === 0
    && (await db.query('select * from prime.social_native_membership_fences')).rows.length === 0,
    'native private ownership/fence tables remain RLS-invisible after hypothetical SELECT grant');
  await db.exec('rollback');
}
