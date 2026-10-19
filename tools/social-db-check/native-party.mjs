import assert from 'node:assert/strict';

// Uses only synthetic identities and the actual saved native Party functions.
export async function checkNativeParty({ db, check, call, uuid, rejected }) {
  const actors = [1201, 1202, 1203, 1204].map(uuid);
  const [a, b, c, stranger] = actors;
  for (let index = 0; index < actors.length; index++) {
    await db.query('insert into prime.players("Id") values($1::uuid)', [actors[index]]);
    await db.query('insert into prime.player_profiles("PlayerId","DisplayName") values($1::uuid,$2)', [actors[index], `Synthetic Native Party ${index}`]);
  }
  const snapshot = actor => call('social_native_party_snapshot', '$1::uuid', [actor]);
  const actionSignature = '$1::uuid,$2::text,$3::bigint,$4::uuid,$5::text,$6::uuid,$7::text';
  const action = (actor, verb, witness, options = {}) => call('social_native_party_action', actionSignature,
    [actor, verb, options.revision ?? witness.server_revision,
      options.partyId ?? (witness.party_id || null), options.target ?? null,
      options.inviteId ?? null, options.self ?? witness.self_prime_id]);
  const legacy = (actor, verb, target = null, invite = null) =>
    call('social_party_action', '$1::uuid,$2::text,$3::text,$4::uuid', [actor, verb, target, invite]);
  const mutate = (actor, target, verb) => call('social_mutate', '$1::uuid,$2::text,$3::text', [actor, target, verb]);
  let sa = await snapshot(a), sb = await snapshot(b), sc = await snapshot(c);
  const pa = sa.self_prime_id, pb = sb.self_prime_id, pc = sc.self_prime_id;
  check(sa.party_id === '' && sa.leader_prime_id === '' && sa.members.length === 0
    && sa.incoming.length === 0 && sa.outgoing.length === 0 && Number.isSafeInteger(sa.server_revision), 'native snapshot has bounded flat empty-party contract');
  for (const [target, primeId] of [[b, pb], [c, pc]]) {
    check((await mutate(a, primeId, 'send_request')).ok, 'native fixture creates real friend request');
    check((await mutate(target, pa, 'accept_request')).ok, 'native fixture accepts real friendship');
  }
  check((await action(a, 'invite', sa, { target: pb, self: pb })).status === 'account_changed', 'expected self identity must match authenticated actor');
  check((await action(a, 'invite', sa, { target: pb, revision: -1 })).status === 'party_changed', 'negative native revision rejected');
  check((await action(a, 'invite', sa, { target: pb, revision: sa.server_revision + 1 })).status === 'party_changed', 'future native revision rejected');
  check((await action(a, 'invite', sa, { target: pb, partyId: uuid(1234) })).status === 'party_changed', 'first invite must witness empty party identity');
  let result = await action(a, 'invite', sa, { target: pb });
  check(result.ok && result.snapshot.party_id.length > 0, 'first native invite creates party from exact empty witness');
  let after = result.snapshot;
  sb = await snapshot(b);
  check(after.server_revision > sa.server_revision && sb.server_revision > 0, 'party/member/invite creation advances both relevant accounts');
  const invite = sb.incoming[0];
  check(invite && invite.from_prime_id === pa && invite.to_prime_id === pb
    && after.outgoing[0].invite_id === invite.invite_id, 'incoming/outgoing invitation contains exact directional identities');
  check((await action(a, 'invite', sa, { target: pc })).status === 'party_changed', 'old empty-party witness cannot invite into newly created party');
  const foreignInvite = { inviteId: invite.invite_id, partyId: invite.party_id };
  check((await action(stranger, 'accept_invite', await snapshot(stranger), foreignInvite)).status === 'invite_changed', 'third account cannot accept another recipient invitation');
  check((await action(b, 'cancel_invite', sb, foreignInvite)).status === 'invite_changed', 'incoming invite cannot be cancelled as outgoing');
  check((await action(a, 'decline_invite', after, foreignInvite)).status === 'invite_changed', 'outgoing invite cannot be declined as incoming');
  check((await action(b, 'accept_invite', sb, { ...foreignInvite, partyId: uuid(1234) })).status === 'invite_changed', 'invitation target party ID must match');
  check((await action(b, 'accept_invite', sb, { ...foreignInvite, inviteId: uuid(1234) })).status === 'invite_changed', 'invitation ID must match pending incoming row');
  result = await action(b, 'accept_invite', sb, foreignInvite);
  check(result.ok && result.snapshot.members.length === 2, 'exact incoming invitation accepts membership');
  check(result.snapshot.server_revision > sb.server_revision, 'acceptance advances recipient revision');
  sa = await snapshot(a); sb = await snapshot(b);
  check(sa.server_revision > after.server_revision, 'joining member advances existing member revision');
  check(sa.outgoing.length === 0 && sb.incoming.length === 0, 'accepted invite retired in both directions');
  check((await action(b, 'accept_invite', result.snapshot, foreignInvite)).status === 'invite_changed', 'consumed invite cannot be accepted with a fresh witness');

  await call('social_presence_heartbeat', '$1::uuid,$2::uuid,$3::text,$4::text,$5::boolean,$6::uuid',
    [b, uuid(1290), 'in_match', 'SYNTHETIC PRIVATE ROOM', false, null]);
  after = await snapshot(a);
  check(after.server_revision === sa.server_revision && (await snapshot(b)).server_revision === sb.server_revision, 'presence heartbeat does not invalidate native authority revision');
  check(after.members.find(member => member.prime_id === pb).presence === 'match', 'permitted activity maps into native Party presence');
  await call('social_privacy_update', '$1::uuid,$2::text,$3::text,$4::text,$5::boolean', [b, 'hidden', 'private', 'friends', false]);
  after = await snapshot(a);
  check(after.members.find(member => member.prime_id === pb).presence === 'unknown', 'hidden member presence yields unknown rather than inferred activity');
  check(JSON.stringify(after).includes('SYNTHETIC PRIVATE ROOM') === false, 'flat Party snapshot contains no private room information');
  check(after.server_revision === sa.server_revision, 'privacy-only presentation update preserves native authority witness');

  check((await action(b, 'promote', sb, { target: pa })).status === 'leader_only', 'fresh follower witness does not confer leader authority');
  result = await action(a, 'promote', sa, { target: pb });
  check(result.ok && result.snapshot.leader_prime_id === pb && result.snapshot.server_revision > sa.server_revision, 'promotion changes leader and revision');
  check((await snapshot(b)).server_revision > sb.server_revision, 'promotion advances new leader revision');
  check((await action(a, 'leave', sa)).status === 'party_changed', 'pre-promotion witness cannot act after leader change');
  sb = await snapshot(b);
  check((await action(b, 'promote', sb, { target: pa })).ok, 'new leader can promote previous leader back');
  sa = await snapshot(a);
  check((await legacy(a, 'invite', pc)).ok, 'legacy endpoint can mutate same party');
  after = await snapshot(a); sc = await snapshot(c);
  check(after.server_revision > sa.server_revision && sc.server_revision > 0, 'legacy invitation advances native witnesses');
  check((await action(a, 'leave', sa)).status === 'party_changed', 'legacy mutation invalidates delayed native action');
  const cInvite = sc.incoming[0];
  check((await action(a, 'cancel_invite', after, { partyId: cInvite.party_id, inviteId: cInvite.invite_id })).ok, 'native cancellation uses exact outgoing invitation');
  check((await snapshot(c)).server_revision > sc.server_revision, 'invite cancellation advances target revision');

  sa = await snapshot(a); sb = await snapshot(b);
  check((await mutate(a, pb, 'block_player')).ok, 'block follows existing social authority');
  after = await snapshot(a);
  check(after.server_revision > sa.server_revision && (await snapshot(b)).server_revision > sb.server_revision, 'block cleanup advances remaining and removed member revisions');
  check(after.members.length === 1 && (await snapshot(b)).party_id === '', 'block removes conflicting Party membership');
  check((await action(a, 'leave', sa)).status === 'party_changed', 'block cleanup invalidates old native witness');
  check((await action(a, 'invite', after, { target: pb })).status === 'blocked', 'fresh native witness cannot override block policy');

  result = await action(a, 'invite', after, { target: pc });
  check(result.ok, 'current native leader may create another invitation');
  sc = await snapshot(c);
  const expiringInvite = sc.incoming[0];
  await db.query("update prime.social_party_invites set expires_at=clock_timestamp()-interval '1 second' where invite_id=$1::uuid", [expiringInvite.invite_id]);
  const expired = await snapshot(c);
  check(expired.server_revision > sc.server_revision && expired.incoming.length === 0, 'expired pending invitation is retired and advances native revision');
  check((await action(c, 'accept_invite', sc, { partyId: expiringInvite.party_id, inviteId: expiringInvite.invite_id })).status === 'party_changed', 'pre-expiry native witness cannot accept expired invitation');

  sa = await snapshot(a);
  check((await action(a, 'invite', sa, { target: pc })).ok, 'current witness can replace expired invitation');
  sc = await snapshot(c);
  const finalInvite = sc.incoming[0];
  check((await action(c, 'accept_invite', sc, { partyId: finalInvite.party_id, inviteId: finalInvite.invite_id })).ok, 'new current incoming invitation accepts');
  sa = await snapshot(a); sc = await snapshot(c);
  result = await action(c, 'leave', sc);
  check(result.ok && result.snapshot.party_id === '' && result.snapshot.server_revision > sc.server_revision, 'native leave retires membership and advances departing witness');
  after = await snapshot(a);
  check(after.server_revision > sa.server_revision && after.members.length === 1, 'native leave advances remaining member witness');
  result = await action(a, 'disband', after);
  check(result.ok && result.snapshot.party_id === '' && result.snapshot.server_revision > after.server_revision, 'native disband clears party and advances final member revision');

  sa = await snapshot(a);
  check((await action(a, 'invite', sa, { target: pc })).ok, 'prepare pending invitation for transaction wall-clock regression');
  sc = await snapshot(c);
  const delayedInvite = sc.incoming[0];
  const delayedOptions = { partyId: delayedInvite.party_id, inviteId: delayedInvite.invite_id };
  await db.exec('begin');
  try {
    await db.query("update prime.social_party_invites set expires_at=clock_timestamp()+interval '50 milliseconds' where invite_id=$1::uuid", [delayedInvite.invite_id]);
    // Simulate time spent waiting for lifecycle authority after the transaction
    // started: now() is still live, but the invitation's real deadline passed.
    await new Promise(resolve => setTimeout(resolve, 100));
    const clock = (await db.query('select now() < expires_at and clock_timestamp() > expires_at as elapsed from prime.social_party_invites where invite_id=$1::uuid', [delayedInvite.invite_id])).rows[0];
    check(clock.elapsed === true, 'pending invite deadline passes while transaction-start clock still precedes it');
    check((await action(c, 'accept_invite', sc, delayedOptions)).status === 'party_changed', 'native accept expires invitation using wall clock after transaction begins');
    const retired = (await db.query("select status,responded_at is not null as responded from prime.social_party_invites where invite_id=$1::uuid", [delayedInvite.invite_id])).rows[0];
    check(retired.status === 'expired' && retired.responded === true, 'wall-clock expiry durably retires pending invitation');
    check((await db.query('select count(*)::int as count from prime.social_party_members where player_id=$1::uuid', [c])).rows[0].count === 0, 'late native acceptance creates no party membership');
    await db.exec('commit');
  } catch (error) { await db.exec('rollback'); throw error; }
  const delayedAfter = await snapshot(c);
  check(delayedAfter.incoming.length === 0 && delayedAfter.party_id === ''
    && (await action(c, 'accept_invite', delayedAfter, delayedOptions)).status === 'invite_changed', 'fresh witness cannot resurrect wall-clock-expired invitation');

  await db.exec('grant usage on schema prime to anon,authenticated');
  for (const role of ['anon', 'authenticated']) {
    await db.exec(`set role ${role}`);
    try {
      await rejected(() => snapshot(a), `${role} cannot read native Party actor parameter`, '42501');
      await rejected(() => action(a, 'leave', after), `${role} cannot call native Party mutation`, '42501');
      for (const signature of [
        'prime.social_party_action_legacy(uuid,text,text,uuid)',
        'prime.social_mutate_legacy(uuid,text,text)',
        'prime.social_party_expire_legacy(uuid)',
      ]) {
        const rows = (await db.query('select has_function_privilege(current_user,$1,\'EXECUTE\') allowed', [signature])).rows;
        check(rows[0].allowed === false, `${role} cannot bypass native revision wrappers via ${signature}`);
      }
    } finally { await db.exec('reset role'); }
  }
  assert((await snapshot(a)).self_prime_id === pa);
}
