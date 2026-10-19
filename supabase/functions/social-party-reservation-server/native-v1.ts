// Pure bounded input codec. Authentication and HMAC verification stay in the
// existing reporter endpoint; SQL rechecks deadlines and authority after locks.
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const zero = "00000000-0000-0000-0000-000000000000";
const integer = (x: unknown, min: number, max: number) => Number.isSafeInteger(x) && (x as number)>=min && (x as number)<=max;
const id = (x: unknown) => typeof x === "string" && uuid.test(x);
const u64 = (x: unknown) => typeof x === "string" && /^[1-9][0-9]{0,19}$/.test(x) && BigInt(x)<=18446744073709551615n;
const timestamp = (x: unknown) => typeof x === "string" && x.length<=40 && /^\d{4}-\d\d-\d\dT/.test(x) && Number.isFinite(Date.parse(x));
export function nativeReservationInput(body: Record<string, unknown>): Record<string, unknown> | null {
  const action = body.action;
  if (!["validate", "activate", "claim_admission", "admitted", "cancel"].includes(action as string)
    || !id(body.request_id) || !id(body.party_id) || !integer(body.party_revision,0,Number.MAX_SAFE_INTEGER)
    || !id(body.travel_id) || !integer(body.travel_revision,1,1000000) || !integer(body.match_id,1,65535)
    || !u64(body.authority_epoch) || !integer(body.operation_generation,1,Number.MAX_SAFE_INTEGER)
    || !timestamp(body.operation_deadline)) return null;
  const result: Record<string, unknown> = { action, request_id: body.request_id, party_id: body.party_id,
    party_revision: body.party_revision, travel_id: body.travel_id, travel_revision: body.travel_revision,
    match_id: body.match_id, authority_epoch: body.authority_epoch, operation_generation: body.operation_generation,
    operation_deadline: body.operation_deadline };
  if (action === "validate") {
    if (!["leader","member"].includes(body.purpose as string)) return null;
    result.purpose = body.purpose;
  } else {
    if (!id(body.reservation_id) && !(action === "cancel" && body.reservation_id === zero)) return null;
    result.reservation_id = body.reservation_id;
  }
  if (action === "activate") {
    const assignments = body.assignments;
    if (!timestamp(body.expires_at) || !Array.isArray(assignments) || assignments.length<1 || assignments.length>8
      || assignments.some(x => typeof x !== "object" || x === null || !id(x.player_id) || !integer(x.slot,0,7))
      || new Set(assignments.map(x=>x.player_id.toLowerCase())).size !== assignments.length
      || new Set(assignments.map(x=>x.slot)).size !== assignments.length) return null;
    result.assignments = assignments.map(x=>({player_id:x.player_id.toLowerCase(),slot:x.slot}));
    result.expires_at = body.expires_at;
  }
  if (action === "claim_admission" || action === "admitted") {
    if (!integer(body.slot,0,7) || !u64(body.connection_id) || !u64(body.claim_nonce)
      || (action === "admitted" && body.peer_created !== true)) return null;
    result.slot = body.slot; result.connection_id = body.connection_id; result.claim_nonce = body.claim_nonce;
    if (action === "admitted") result.peer_created = true;
  }
  if (action === "cancel") {
    if (!["group","self"].includes(body.cancel_scope as string) || !["cancelled","rejected","expired"].includes(body.status as string)) return null;
    result.cancel_scope = body.cancel_scope; result.status = body.status;
  }
  return result;
}
