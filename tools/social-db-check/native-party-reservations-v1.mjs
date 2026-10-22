// Actual saved SQL, synthetic identities, no Edge/HMAC emulation, sockets,
// datagrams, gameplay admission or live backend. Reserved/claim is never Joined.
export async function checkNativePartyReservationsV1({ db, check, call, uuid, rejected }) {
  const [a, b, c, d, stranger] = [1501, 1502, 1503, 1504, 1505].map(uuid);
  const reporter = uuid(1561), otherReporter = uuid(1562), epoch = '150001';
  const scalar = async (sql, args = []) => (await db.query(sql, args)).rows[0]?.value;
  for (const [index, actor] of [a, b, c, d, stranger].entries()) {
    await db.query('insert into prime.players("Id") values($1::uuid)', [actor]);
    await db.query('insert into prime.player_profiles("PlayerId","DisplayName") values($1::uuid,$2)', [actor, `Synthetic Native Group ${index}`]);
  }
  for (const [index, id] of [reporter, otherReporter].entries()) {
    await db.query('insert into public.project_prime_career_reporters(server_id,display_name,key_hash) values($1::uuid,$2,$3)',
      [id, `Synthetic Group Reporter ${index}`, (index + 10).toString(16).repeat(64)]);
  }
  let capability = await call('social_native_reservation_capability', '$1::uuid', [reporter]);
  check(capability.ok && capability.native_v1 && capability.version === 1 && capability.claim_max_ms === 2000, 'enabled reporter receives bounded native V1 backend capability');
  await db.query('update public.project_prime_career_reporters set enabled=false where server_id=$1::uuid', [otherReporter]);
  capability = await call('social_native_reservation_capability', '$1::uuid', [otherReporter]);
  check(!capability.ok && !capability.native_v1, 'reporter disabled after endpoint authentication cannot advertise backend capability');
  await db.query('update public.project_prime_career_reporters set enabled=true where server_id=$1::uuid', [otherReporter]);
  const state = actor => call('social_native_party_travel_state', '$1::uuid', [actor]);
  const party = (actor, action, target = null, invitation = null) => call('social_party_action', '$1::uuid,$2::text,$3::text,$4::uuid', [actor, action, target, invitation]);
  const friends = (actor, target, action) => call('social_mutate', '$1::uuid,$2::text,$3::text', [actor, target, action]);
  const pa = (await state(a)).self_prime_id;
  for (const member of [b, c, d]) {
    const id = (await state(member)).self_prime_id;
    check((await friends(a, id, 'send_request')).ok && (await friends(member, pa, 'accept_request')).ok, 'group fixture creates actual privacy-eligible friendship');
    check((await party(a, 'invite', id)).ok, 'group fixture creates actual party invitation');
    const invitation = (await state(member)).incoming[0];
    check((await party(member, 'accept', null, invitation.invite_id)).ok, 'group fixture joins actual party authority');
  }
  let sequence = 0;
  const membership = (actor, eligible = true) => call('social_native_lobby_membership_report', '$1::uuid,$2::uuid,$3::numeric,$4::bigint,$5::numeric,$6::boolean,$7::boolean,$8::bigint',
    [reporter, actor, epoch, 1, ++sequence, eligible, false, Math.floor(Date.now() / 1000) + 300]);
  check((await membership(a)).ok, 'group leader has native positive-sequence verified reporter membership');
  const beforeTravel = await state(a);
  const publication = await call('social_native_party_travel_action', '$1::uuid,$2::text,$3::bigint,$4::uuid,$5::text,$6::uuid,$7::int,$8::inet,$9::int,$10::numeric,$11::int,$12::text,$13::text',
    [a, 'publish_travel', beforeTravel.server_revision, beforeTravel.party_id, pa, null, 0, '8.8.4.4', 27015, epoch, 57, 'SYNTHETIC GROUP ROOM', 'SYNTHETIC GROUP SERVER']);
  check(publication.ok && publication.snapshot.travel != null, 'group destination comes from actual verified native travel');
  const travel = publication.snapshot.travel;
  let requestNumber = 1570;
  const clientInput = async (actor, request, options = {}) => {
    const current = await state(actor);
    return { expected_self_prime_id: current.self_prime_id, expected_revision: current.server_revision,
      party_id: publication.snapshot.party_id, travel_id: travel.travel_id, travel_revision: travel.revision,
      match_id: 1, authority_epoch: epoch, request_id: request, operation_generation: 1,
      host: '8.8.4.4', port: 27015, protocol: 57, room_key: 'SYNTHETIC GROUP ROOM', ...options };
  };
  const client = (actor, action, input) => call('social_native_party_reservation_action', '$1::uuid,$2::text,$3::jsonb', [actor, action, JSON.stringify(input)]);
  const opDeadline = () => new Date(Date.now() + 9000).toISOString();
  const serverInput = (reservation, action, options = {}) => ({ action, request_id: reservation.request_id,
    party_id: reservation.party_id, party_revision: reservation.party_revision, travel_id: reservation.travel_id,
    travel_revision: reservation.travel_revision, match_id: reservation.match_id, authority_epoch: reservation.authority_epoch,
    operation_generation: reservation.operation_generation, operation_deadline: opDeadline(), ...options });
  const server = (actor, reservation, action, options = {}, identity = reporter, ticket = new Date(Date.now() + 300000).toISOString()) =>
    call('social_native_reservation_server', '$1::uuid,$2::uuid,$3::timestamptz,$4::jsonb', [identity, actor, ticket, JSON.stringify(serverInput(reservation, action, options))]);
  const snapshot = async (actor, reservation) => client(actor, 'reservation_snapshot', await clientInput(actor, reservation.request_id));
  const cancel = async reservation => client(a, 'cancel_reservation', await clientInput(a, reservation.request_id));
  let active;
  async function create() {
    if (active) await cancel(active);
    await membership(a);
    const input = await clientInput(a, uuid(requestNumber++));
    const result = await client(a, 'reserve_intent', input);
    check(result.ok && result.status === 'pending' && result.reservation.native_v1 === true, 'fresh exact leader intent creates pending native reservation');
    active = result.reservation;
    return { reservation: active, input };
  }
  const assignments = [{ player_id: b, slot: 2 }, { player_id: c, slot: 3 }, { player_id: d, slot: 4 }];
  async function activate(reservation, options = {}) {
    return server(a, reservation, 'activate', { reservation_id: uuid(1590), assignments,
      expires_at: new Date(Date.now() + 60000).toISOString(), ...options });
  }
  const claimOptions = (slot = 2, nonce = '99', connection = '18446744073709551615') =>
    ({ reservation_id: uuid(1590), slot, claim_nonce: nonce, connection_id: connection });

  let { reservation: first, input: firstInput } = await create();
  check(first.requested_count === 3 && first.reservation_id == null && first.self_slot == null, 'pending cohort excludes present leader and grants no slots before activation');
  const privateProjection = JSON.stringify(first);
  check(!privateProjection.includes('reporter_id') && !privateProjection.includes('roster') && !privateProjection.includes(reporter)
    && !privateProjection.includes(a) && !privateProjection.includes(b), 'client reservation projection omits private reporter and roster UUID metadata');
  const ordinary = await state(b);
  check(ordinary.reservation?.request_id === first.request_id, 'ordinary native Party snapshot captures current group intent marker');
  const followed = await call('social_native_party_travel_action', '$1::uuid,$2::text,$3::bigint,$4::uuid,$5::text,$6::uuid,$7::int,$8::inet,$9::int,$10::numeric,$11::int,$12::text,$13::text',
    [b, 'follow_travel', ordinary.server_revision, ordinary.party_id, ordinary.self_prime_id, travel.travel_id, travel.revision, null, null, null, null, null, null]);
  check(followed.ok && followed.snapshot.reservation?.request_id === first.request_id, 'Follow response snapshot retains same group intent rather than silently becoming solo');
  check(!JSON.stringify(ordinary.reservation).includes('roster') && !JSON.stringify(followed.snapshot.reservation).includes(reporter)
    && !JSON.stringify(followed.snapshot.reservation).includes(a), 'ordinary and Follow marker projection expose no private account or reporter metadata');
  await db.query('update public.project_prime_career_reporters set enabled=false where server_id=$1::uuid', [reporter]);
  check((await state(b)).reservation?.request_id === first.request_id, 'reporter capability disappearance preserves existing group intent evidence');
  check((await server(a, first, 'validate', { purpose: 'leader' })).status === 'authority_owner_changed', 'preserved group marker does not bypass disabled reporter admission gate');
  await db.query('update public.project_prime_career_reporters set enabled=true where server_id=$1::uuid', [reporter]);
  await db.exec('begin');
  try {
    await db.query('update prime.social_party_travel set revision=revision+1 where party_id=$1::uuid', [ordinary.party_id]);
    check((await state(b)).reservation == null, 'ordinary snapshot omits group intent tied to a different travel revision');
    await db.exec('rollback');
  } catch (error) { await db.exec('rollback'); throw error; }
  check(Date.parse(first.expires_at) <= Date.now() + 60000 && Date.parse(first.expires_at) <= Date.parse(travel.expires_at), 'native request deadline clipped to sixty seconds and original travel');
  const repeat = await client(a, 'reserve_intent', firstInput);
  check(repeat.ok && repeat.reservation.expires_at === first.expires_at, 'identical pending intent never renews fixed deadline');
  check((await client(a, 'reserve_intent', { ...firstInput, host: '1.1.1.1' })).status === 'reservation_stale', 'same request cannot replace immutable locator');
  check((await client(a, 'reserve_intent', { ...firstInput, operation_generation: 2 })).status === 'reservation_stale', 'same request cannot replace operation generation');
  check((await client(b, 'reserve_intent', await clientInput(b, uuid(requestNumber++)))).status === 'leader_only', 'follower cannot create a leader reservation');
  check((await client(stranger, 'reservation_snapshot', firstInput)).status === 'account_changed', 'other account cannot borrow authenticated leader witness');
  check((await client(b, 'reservation_snapshot', await clientInput(b, ''))).reservation.request_id === first.request_id, 'current follower discovers exact current reservation without arbitrary private ID lookup');
  check((await server(a, first, 'validate', { purpose: 'leader' })).ok, 'verified owning reporter validates exact leader witness');
  check((await server(b, first, 'validate', { purpose: 'leader' })).status === 'not_reservation_member', 'member ticket cannot act as leader proof');
  check((await server(b, first, 'validate', { purpose: 'member' })).ok, 'verified cohort member may attach to exact reservation');
  check((await server(stranger, first, 'validate', { purpose: 'member' })).status === 'party_changed', 'unrelated verified actor cannot attach');
  check((await server(a, first, 'validate', { purpose: 'leader' }, otherReporter)).status === 'authority_owner_changed', 'different enabled reporter cannot act for frozen epoch');
  check((await server(a, first, 'validate', { purpose: 'leader', match_id: 2 })).status === 'reservation_stale', 'mismatched current match cannot reuse reservation');
  check((await server(a, first, 'validate', { purpose: 'leader', operation_generation: 2 })).status === 'reservation_stale', 'stale server operation generation rejected');
  check((await server(a, first, 'validate', { purpose: 'leader', operation_deadline: new Date(Date.now() - 1).toISOString() })).status === 'operation_expired', 'expired worker operation rejected after lock');
  check((await server(a, first, 'validate', { purpose: 'leader', operation_deadline: new Date(Date.now() + 11000).toISOString() })).status === 'operation_expired', 'worker cannot invent deadline longer than ten seconds');
  check((await client(b, 'cancel_reservation', await clientInput(b, first.request_id))).status === 'member_not_reserved', 'pending follower HTTP decline cannot invalidate immutable unactivated cohort');
  check((await server(b, first, 'cancel', { reservation_id: '00000000-0000-0000-0000-000000000000', cancel_scope: 'self', status: 'cancelled' })).status === 'member_not_reserved', 'pending server self-decline cannot invalidate immutable unactivated cohort');
  check(await scalar('select count(*)::int as value from prime.social_party_reservation_members where request_id=$1::uuid and (native_declined or status=\'declined\')', [first.request_id]) === 0, 'rejected pending decline preserves every exact cohort member for later activation');
  check((await activate(first, { assignments: [{ player_id: b, slot: 2 }, { player_id: c, slot: 2 }, { player_id: d, slot: 4 }] })).status === 'invalid_assignments', 'activation rejects duplicate occupied assignment slots');
  check((await activate(first, { assignments: [{ player_id: b, slot: 2 }, { player_id: c, slot: 3 }, { player_id: stranger, slot: 4 }] })).status === 'invalid_assignments', 'activation rejects noncohort assignment identity');
  check((await activate(first, { assignments: assignments.slice(0, 2) })).status === 'invalid_assignments', 'activation requires all exact frozen missing-member assignments');
  let activation = await activate(first);
  check(activation.ok && activation.status === 'reserved' && Date.parse(activation.expires_at) <= Date.now() + 15000, 'backend activation grants only original clipped fifteen-second lease');
  check((await activate(first)).expires_at === activation.expires_at, 'identical activation never extends deadline');
  check((await activate(first, { reservation_id: uuid(1591) })).status === 'reservation_mismatch', 'retry cannot replace server reservation identity');
  let claim = await server(b, first, 'claim_admission', claimOptions());
  check(claim.ok && claim.status === 'admitting' && Date.parse(claim.claim_expires_at) <= Date.now() + 2000
    && Date.parse(claim.claim_expires_at) <= Date.parse(activation.expires_at), 'linearized member claim is at most two seconds and never Joined');
  check((await server(b, first, 'claim_admission', claimOptions())).claim_expires_at === claim.claim_expires_at, 'duplicate exact claim never renews short deadline');
  check((await server(b, first, 'claim_admission', claimOptions(2, '100'))).status === 'claim_mismatch', 'replacement claim nonce cannot steal existing admission');
  check((await server(b, first, 'claim_admission', claimOptions(2, '99', '42'))).status === 'claim_mismatch', 'replacement connection cannot steal existing admission');
  check((await snapshot(b, first)).reservation.self_status === 'admitting', 'client status truthfully distinguishes claim from admitted peer');
  const cancelled = await cancel(first);
  check(cancelled.ok && cancelled.reservation.cancellation_pending, 'group cancellation after claim exposes pending settlement truthfully');
  check((await server(b, first, 'claim_admission', claimOptions())).claim_expires_at === claim.claim_expires_at, 'cancel-afterclaim exact replay retains original short claim only');
  check((await server(c, first, 'claim_admission', claimOptions(3, '101', '43'))).status === 'reservation_closed', 'cancel-beforeclaim rejects every unclaimed member');
  check((await server(b, first, 'admitted', { ...claimOptions(), peer_created: false })).status === 'claim_mismatch', 'backend admitted requires actual peer-created assertion');
  check((await server(b, first, 'admitted', { ...claimOptions(3), peer_created: true })).status === 'claim_mismatch', 'admitted binds exact assigned slot');
  check((await server(b, first, 'admitted', { ...claimOptions(), peer_created: true })).status === 'member_admitted', 'already linearized claim may settle after group cancellation');
  check((await server(b, first, 'admitted', { ...claimOptions(), peer_created: true })).ok, 'exact admitted acknowledgement is idempotent');
  check((await server(c, first, 'admitted', { ...claimOptions(3, '101', '43'), peer_created: true })).ok === false, 'unclaimed member cannot fabricate admitted acknowledgement');
  check((await activate(first)).status === 'reservation_closed', 'late activation cannot resurrect cancelled operation tombstone');

  let next = await create();
  check((await activate(next.reservation)).ok, 'fresh request can activate after prior group cancellation');
  check((await client(b, 'cancel_reservation', await clientInput(b, next.reservation.request_id))).ok, 'follower decline acts on self without leader authority');
  check((await server(b, next.reservation, 'claim_admission', claimOptions())).status === 'member_not_reserved', 'self-decline tombstone rejects later member claim');
  check((await server(c, next.reservation, 'claim_admission', claimOptions(3, '111', '44'))).ok, 'self-decline does not cancel other reserved group members');
  check((await scalar('select native_cancelled as value from prime.social_party_reservations where request_id=$1::uuid', [next.reservation.request_id])) === false, 'self-decline does not become group cancellation');

  next = await create();
  check((await activate(next.reservation)).ok, 'fresh activation available for claim-expiry regression');
  const shortClaim = await server(b, next.reservation, 'claim_admission', { ...claimOptions(), operation_deadline: new Date(Date.now() + 50).toISOString() });
  check(shortClaim.ok, 'short claim uses actual clipped worker operation deadline');
  await new Promise(resolve => setTimeout(resolve, 100));
  check((await server(b, next.reservation, 'claim_admission', claimOptions())).status === 'claim_expired', 'expired claim replay cannot obtain a fresh two-second authorization');
  check((await server(b, next.reservation, 'admitted', { ...claimOptions(), peer_created: true })).status === 'claim_expired', 'first actual-peer acknowledgement after short claim expires is rejected');
  check((await snapshot(b, next.reservation)).reservation.self_status !== 'admitting', 'elapsed claim is no longer projected as admitting');

  for (const boundary of ['ticket', 'operation', 'reservation']) {
    next = await create();
    await db.exec('begin');
    try {
      const deadline = new Date(Date.now() + 50).toISOString();
      if (boundary === 'reservation') check((await activate(next.reservation, { expires_at: deadline })).ok, 'actual activation clips effective reservation to short original server hold');
      await new Promise(resolve => setTimeout(resolve, 100));
      const options = { purpose: 'leader', ...(boundary === 'operation' ? { operation_deadline: deadline } : {}) };
      const result = await server(a, next.reservation, 'validate', options, reporter, boundary === 'ticket' ? deadline : new Date(Date.now() + 300000).toISOString());
      check(result.status === (boundary === 'ticket' ? 'ticket_expired' : boundary === 'operation' ? 'operation_expired' : 'reservation_expired'), `native ${boundary} uses post-lock wall clock rather than transaction-start time`);
      await db.exec('rollback');
    } catch (error) { await db.exec('rollback'); throw error; }
  }

  next = await create();
  await db.query("update prime.social_settings set do_not_disturb=true where player_id=$1::uuid", [c]);
  check((await server(a, next.reservation, 'validate', { purpose: 'leader' })).status === 'privacy_changed', 'current DND privacy invalidates a previously frozen group intent');
  await db.query('update prime.social_settings set do_not_disturb=false where player_id=$1::uuid', [c]);
  await db.query('update prime.social_profiles set native_party_revision=native_party_revision+1 where player_id=$1::uuid', [d]);
  check((await server(a, next.reservation, 'validate', { purpose: 'leader' })).status === 'party_changed', 'any frozen roster revision change invalidates delayed server work');
  await cancel(next.reservation);

  check((await membership(b)).ok, 'verified present follower belongs to exact destination epoch');
  next = await create();
  check(next.reservation.requested_count === 2, 'verified present follower is excluded from missing-seat cohort');
  check((await server(b, next.reservation, 'validate', { purpose: 'member' })).status === 'not_reservation_member', 'already-present follower cannot also attach as missing reserved member');
  check((await activate(next.reservation, { assignments: assignments.slice(1) })).ok, 'activation assigns only exact remaining missing members');
  await db.query('delete from prime.social_lobby_memberships where player_id=$1::uuid and authority_epoch=$2::numeric', [b, epoch]);

  next = await create();
  for (const [name, signature, args] of [
    ['social_party_reservation_server_validate', '$1::uuid,$2::numeric,$3::uuid,$4::uuid', [reporter, epoch, next.reservation.request_id, b]],
    ['social_party_reservation_server_activate', '$1::uuid,$2::numeric,$3::uuid,$4::uuid,$5::jsonb,$6::int', [reporter, epoch, next.reservation.request_id, uuid(1590), JSON.stringify(assignments), 30]],
    ['social_party_reservation_server_admitted', '$1::uuid,$2::uuid,$3::uuid,$4::uuid', [reporter, next.reservation.request_id, uuid(1590), b]],
    ['social_party_reservation_server_cancel', '$1::uuid,$2::uuid,$3::uuid,$4::text', [reporter, next.reservation.request_id, uuid(1590), 'rejected']],
  ]) check((await call(name, signature, args)).status === 'native_v1_required', `${name} cannot bypass native witness/claim gates`);
  await rejected(() => db.query("update prime.social_party_reservations set expires_at=expires_at+interval '1 second' where request_id=$1::uuid", [next.reservation.request_id]), 'native fixed deadline is immutable even through legacy SQL owner');
  for (const role of ['anon', 'authenticated']) {
    await db.exec(`set role ${role}`);
    try {
      await rejected(() => client(a, 'reserve_intent', next.input), `${role} cannot directly impersonate native group actor`, '42501');
      await rejected(() => server(a, next.reservation, 'validate', { purpose: 'leader' }), `${role} cannot directly impersonate native reporter`, '42501');
      await rejected(() => call('social_native_reservation_projection', '$1::uuid,$2::uuid', [next.reservation.request_id, b]), `${role} cannot read private native reservation projection`, '42501');
      check(await scalar("select has_function_privilege(current_user,'prime.social_native_party_travel_state_pre_reservation_v1(uuid)','EXECUTE') as value") === false, `${role} cannot bypass closed snapshot wrapper through predecessor function`);
    } finally { await db.exec('reset role'); }
  }
  const invokers = (await db.query("select prosecdef from pg_proc p join pg_namespace n on n.oid=p.pronamespace where n.nspname='prime' and proname like 'social_native_reservation%' or n.nspname='prime' and proname='social_native_party_reservation_action'")).rows;
  check(invokers.length === 6 && invokers.every(row => !row.prosecdef), 'all six native reservation entry/helper functions retain invoker security');
  await db.exec('begin');
  try {
    check((await activate(next.reservation, { expires_at: new Date(Date.now() + 50).toISOString() })).ok, 'short fixed hold available for ordinary marker expiry check');
    await new Promise(resolve => setTimeout(resolve, 100));
    check((await state(b)).reservation == null, 'ordinary snapshot omits group marker after original activation hold expires');
    await db.exec('rollback');
  } catch (error) { await db.exec('rollback'); throw error; }
}
