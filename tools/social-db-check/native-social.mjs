// Synthetic identities only, executing the exact migration functions.
export async function checkNativeSocial({ db, check, call, uuid, rejected }) {
  const [a, b, c] = [2401, 2402, 2403].map(uuid);
  for (const [index, actor] of [a, b, c].entries()) {
    await db.query('insert into prime.players("Id") values($1::uuid)', [actor]);
    await db.query('insert into prime.player_profiles("PlayerId","DisplayName") values($1::uuid,$2)',
      [actor, `Synthetic Native Friends ${index}`]);
  }
  const read = actor => call('social_snapshot', '$1::uuid', [actor]);
  const [pa, pb, pc] = await Promise.all([a, b, c].map(async actor => (await read(actor)).self.prime_id));
  const mutate = (actor, expected, target, action) => call('social_native_mutate',
    '$1::uuid,$2::text,$3::text,$4::text', [actor, expected, target, action]);
  const before = JSON.stringify([await read(a), await read(b), await read(c)]);
  for (const expected of [pa, null, '', pc.toLowerCase()]) {
    const result = await mutate(c, expected, pb, 'block_player');
    check(result.ok === false && result.status === 'account_changed',
      'native Friends rejects foreign, missing or noncanonical account witness');
  }
  check(JSON.stringify([await read(a), await read(b), await read(c)]) === before,
    'wrong-account native mutation changes no relationship for either identity');
  check((await mutate(uuid(2499), pa, pb, 'send_request')).status === 'account_changed',
    'unprovisioned authenticated actor cannot borrow an existing public identity');
  check((await mutate(a, pa, pb, 'send_request')).ok,
    'exact native self witness delegates request creation to existing authority');
  check((await read(b)).incoming_requests.some(row => row.prime_id === pa),
    'native request is attributed to actual authenticated actor');
  check((await mutate(c, pa, pb, 'cancel_request')).status === 'account_changed'
    && (await read(b)).incoming_requests.some(row => row.prime_id === pa),
    'changed account cannot cancel original account request');
  check((await mutate(b, pb, pa, 'accept_request')).ok
    && (await read(a)).friends.some(row => row.prime_id === pb),
    'current recipient witness permits real friend acceptance');
  check((await mutate(c, pa, pb, 'remove_friend')).status === 'account_changed'
    && (await read(a)).friends.some(row => row.prime_id === pb),
    'changed account cannot remove original account friendship');
  check((await mutate(a, pa, pb, 'remove_friend')).ok
    && !(await read(a)).friends.some(row => row.prime_id === pb),
    'same-account native removal uses unchanged relationship rules');
  const properties = (await db.query("select p.prosecdef,p.proconfig,pg_get_functiondef(p.oid) as definition from pg_proc p join pg_namespace n on n.oid=p.pronamespace where n.nspname='prime' and p.proname='social_native_mutate'")).rows[0];
  check(properties.prosecdef === false && properties.proconfig.includes('search_path=prime, pg_temp')
    && properties.definition.includes('pg_advisory_xact_lock(6384340261223951'),
    'native Friends wrapper preserves invoker security, pinned path and lifecycle lock');
  await db.exec('grant usage on schema prime to anon,authenticated');
  for (const role of ['anon', 'authenticated']) {
    await db.exec(`set role ${role}`);
    try {
      await rejected(() => mutate(a, pa, pb, 'send_request'),
        `${role} cannot impersonate native Friends actor directly`, '42501');
    } finally { await db.exec('reset role'); }
  }
}
