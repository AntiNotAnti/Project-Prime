// Executes actual repository SQL against the caller's disposable PGlite DB.
export async function checkNativePresence({ db, check, call, uuid, rejected }) {
  const [a, b, stranger] = [2301, 2302, 2303].map(uuid);
  for (const [index, actor] of [a, b, stranger].entries()) {
    await db.query('insert into prime.players("Id") values($1::uuid)', [actor]);
    await db.query('insert into prime.player_profiles("PlayerId","DisplayName") values($1::uuid,$2)', [actor, `Synthetic Native Presence ${index}`]);
  }
  const snapshot = actor => call('social_native_presence_snapshot', '$1::uuid', [actor]);
  const exactSettings = value => {
    const { presence_visibility, activity_visibility, invite_policy, do_not_disturb } = value.settings;
    return { presence_visibility, activity_visibility, invite_policy, do_not_disturb };
  };
  const action = (actor, verb, self, options = {}) => call('social_native_presence_action',
    '$1::uuid,$2::text,$3::text,$4::uuid,$5::text,$6::text,$7::jsonb,$8::jsonb',
    [actor, verb, self, options.session ?? null, options.activity ?? null, options.room ?? null,
      options.expected ? JSON.stringify(options.expected) : null, options.settings ? JSON.stringify(options.settings) : null]);
  const sa = await snapshot(a), sb = await snapshot(b);
  const pa = sa.self_prime_id, pb = sb.self_prime_id;
  const session = uuid(2401);
  check(sa.expires_after_seconds === 45 && Array.isArray(sa.players)
    && Object.keys(exactSettings(sa)).length === 4, 'native presence exposes self, four privacy fields, players and authoritative TTL');
  const changed = { presence_visibility: 'hidden', activity_visibility: 'private', invite_policy: 'nobody', do_not_disturb: true };
  check((await action(a, 'heartbeat', pb, { session, activity: 'online' })).status === 'account_changed',
    'wrong account witness cannot publish native presence');
  check((await action(a, 'set_privacy', pb, { expected: exactSettings(sa), settings: changed })).status === 'account_changed',
    'wrong account witness cannot change native privacy');
  check((await snapshot(a)).settings.presence_visibility === sa.settings.presence_visibility,
    'rejected wrong-account privacy preserves owner settings');

  check((await action(b, 'heartbeat', pb, { session, activity: 'lobby', room: 'SYNTHETIC JOIN CLAIM' })).ok,
    'correct native owner publishes bounded session heartbeat');
  let rows = (await db.query('select player_id,joinable,lobby_id from prime.social_presence_sessions where session_id=$1::uuid', [session])).rows;
  check(rows.length === 1 && rows[0].player_id === b && !rows[0].joinable && rows[0].lobby_id === null,
    'native heartbeat cannot manufacture a server-verified joinable lobby');
  check((await action(b, 'leave', pa, { session })).status === 'account_changed',
    'wrong expected account cannot retire an otherwise valid owner session');
  check((await action(a, 'leave', pa, { session })).ok,
    'foreign-session leave is safely idempotent for the verified caller');
  check((await db.query('select player_id from prime.social_presence_sessions where session_id=$1::uuid', [session])).rows[0]?.player_id === b,
    'foreign caller leave never removes another account session');
  await action(a, 'heartbeat', pa, { session, activity: 'menu' });
  rows = (await db.query('select player_id from prime.social_presence_sessions where session_id=$1::uuid', [session])).rows;
  check(rows.length === 2 && rows.some(row => row.player_id === b) && rows.some(row => row.player_id === a),
    'reusing a session UUID creates only caller-owned row and cannot overwrite foreign presence');
  await action(a, 'leave', pa, { session });
  check((await db.query('select player_id from prime.social_presence_sessions where session_id=$1::uuid', [session])).rows[0]?.player_id === b,
    'owned leave removes only caller composite session identity');

  let current = await snapshot(a);
  const original = exactSettings(current);
  let result = await action(a, 'set_privacy', pa, { expected: original, settings: changed });
  check(result.ok && JSON.stringify(exactSettings(result.snapshot)) === JSON.stringify(changed),
    'native privacy CAS updates all four exact settings and returns authoritative readback');
  check((await action(a, 'set_privacy', pa, { expected: original, settings: original })).status === 'privacy_changed',
    'stale privacy witness cannot overwrite a newer preference');
  check(JSON.stringify(exactSettings(await snapshot(a))) === JSON.stringify(changed),
    'rejected stale privacy write leaves current preference intact');
  check((await action(a, 'set_privacy', pa, { settings: original })).status === 'privacy_changed',
    'missing expected privacy witness cannot write');
  check((await action(a, 'set_privacy', pa, { expected: changed, settings: { ...changed, do_not_disturb: 'true' } })).status === 'invalid_privacy',
    'nonboolean DND is rejected by actual native SQL');
  check((await action(a, 'set_privacy', pa, { expected: changed, settings: { ...changed, do_not_disturb: false } })).ok,
    'DND-only CAS change remains an explicit owner-controlled mutation');

  await call('social_mutate', '$1::uuid,$2::text,$3::text', [a, pb, 'send_request']);
  await call('social_mutate', '$1::uuid,$2::text,$3::text', [b, pa, 'accept_request']);
  await action(b, 'heartbeat', pb, { session, activity: 'in_match', room: 'SYNTHETIC PRIVATE ROOM' });
  let view = await snapshot(a);
  check(view.players.find(player => player.prime_id === pb)?.activity === 'in_match'
    && view.players.find(player => player.prime_id === pb)?.room_key === 'SYNTHETIC PRIVATE ROOM',
    'native snapshot retains existing friend-authorized activity and room visibility');
  view = await snapshot(stranger);
  check(view.players.find(player => player.prime_id === pb)?.activity === 'online'
    && view.players.find(player => player.prime_id === pb)?.room_key === null,
    'public presence does not expose friends-only activity to strangers');
  async function privacyB(next) {
    const before = await snapshot(b);
    return action(b, 'set_privacy', pb, { expected: exactSettings(before), settings: next });
  }
  check((await privacyB(changed)).ok && !(await snapshot(a)).players.some(player => player.prime_id === pb),
    'hidden presence preference excludes even a verified friend');
  const friendSettings = { presence_visibility: 'friends', activity_visibility: 'friends', invite_policy: 'friends', do_not_disturb: false };
  check((await privacyB(friendSettings)).ok && !(await snapshot(stranger)).players.some(player => player.prime_id === pb),
    'friends-only presence excludes unrelated accounts');
  await db.query("update prime.social_presence_sessions set updated_at=clock_timestamp()-interval '46 seconds' where player_id=$1::uuid", [b]);
  check(!(await snapshot(a)).players.some(player => player.prime_id === pb),
    'native snapshot honors authoritative 45-second TTL without wall-clock sleeps');
  for (let n = 0; n < 5; n++) await action(b, 'heartbeat', pb, { session: uuid(2410 + n), activity: 'online' });
  check((await db.query('select count(*)::int count from prime.social_presence_sessions where player_id=$1::uuid', [b])).rows[0].count === 4,
    'native heartbeats retain existing four-session bound');
  await call('social_mutate', '$1::uuid,$2::text,$3::text', [a, pb, 'block_player']);
  check(!(await snapshot(a)).players.some(player => player.prime_id === pb),
    'native presence never bypasses existing blocked-relationship visibility');
  check(!JSON.stringify(await snapshot(a)).includes(b), 'native snapshot contains no private auth UUID for another player');

  await db.exec('grant usage on schema prime to anon,authenticated');
  for (const role of ['anon', 'authenticated']) {
    await db.exec(`set role ${role}`);
    try {
      await rejected(() => snapshot(a), `${role} cannot supply actor to native presence snapshot`, '42501');
      await rejected(() => action(a, 'leave', pa, { session }), `${role} cannot supply actor to native presence mutation`, '42501');
    } finally { await db.exec('reset role'); }
  }
}
