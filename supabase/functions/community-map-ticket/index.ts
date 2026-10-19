/// <reference types="npm:@supabase/functions-js@2.117.2/src/edge-runtime.d.ts" />
import { readObjectBounded, RequestBodyError } from "../_shared/request-body.ts";

const enc = new TextEncoder();
const dec = new TextDecoder("utf-8", { fatal: true });
const ticketPrefix = "ppm1";
const ticketLifetimeSeconds = 15 * 60;

function json(status: number, value: unknown) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "content-type": "application/json" },
  });
}

function b64url(bytes: Uint8Array) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/g, "");
}

function fromB64url(value: string) {
  const base64 = value.replaceAll("-", "+").replaceAll("_", "/")
    + "=".repeat((4 - value.length % 4) % 4);
  const binary = atob(base64);
  return Uint8Array.from(binary, (c) => c.charCodeAt(0));
}

async function signingKey() {
  const service = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!service) throw new Error("Supabase service role is unavailable");
  return await crypto.subtle.importKey(
    "raw",
    enc.encode("project-prime-community-map-ticket:v1:" + service),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign", "verify"],
  );
}

type TicketPayload = {
  v: number;
  sub: string;
  iat: number;
  exp: number;
};

async function verifyTicket(ticket: string) {
  if (ticket.length > 4096) return null;
  const parts = ticket.split(".");
  if (parts.length !== 3 || parts[0] !== ticketPrefix) return null;
  let payloadBytes: Uint8Array;
  let signature: Uint8Array;
  try {
    payloadBytes = fromB64url(parts[1]);
    signature = fromB64url(parts[2]);
  } catch {
    return null;
  }
  const key = await signingKey();
  if (!await crypto.subtle.verify("HMAC", key, signature, enc.encode(parts[1]))) {
    return null;
  }
  let payload: TicketPayload;
  try {
    payload = JSON.parse(dec.decode(payloadBytes));
  } catch {
    return null;
  }
  const now = Math.floor(Date.now() / 1000);
  if (payload === null || typeof payload !== "object" || Array.isArray(payload) || payload.v !== 1
    || !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(payload.sub)
    || !Number.isInteger(payload.iat) || !Number.isInteger(payload.exp)
    || payload.iat > now + 60 || payload.exp <= now || payload.exp <= payload.iat
    || payload.exp - payload.iat > ticketLifetimeSeconds + 60) {
    return null;
  }
  return payload;
}

Deno.serve(async (req: Request) => {
  if (req.method !== "POST") return json(405, { error: "method_not_allowed" });

  let body: Record<string, unknown>;
  try {
    body = await readObjectBounded(req, 8192);
  } catch (error) {
    return json(error instanceof RequestBodyError ? error.status : 400, { error: "invalid_json" });
  }

  if (body.action === "verify") {
    let payload: TicketPayload | null;
    try { payload = typeof body.ticket === "string" ? await verifyTicket(body.ticket) : null; }
    catch { return json(503, { error: "ticket_service_unavailable" }); }
    if (!payload) return json(401, { error: "invalid_ticket" });
    return json(200, {
      creator_id: `hunter:${payload.sub}`,
      moderator: false,
      expires_at: new Date(payload.exp * 1000).toISOString(),
    });
  }

  if (body.action !== undefined && body.action !== "mint") {
    return json(400, { error: "invalid_action" });
  }

  const auth = req.headers.get("authorization") ?? "";
  if (!/^Bearer \S+$/.test(auth)) {
    return json(401, { error: "authentication_required" });
  }
  const jwt = auth.slice(7).trim();
  if (!jwt) return json(401, { error: "authentication_required" });

  let subject: string;
  try {
    const response = await fetch(`${Deno.env.get("SUPABASE_URL")}/auth/v1/user`, {
      headers: { authorization: "Bearer " + jwt, apikey: Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")! },
    });
    if (!response.ok) return json(401, { error: "invalid_session" });
    const user = await response.json();
    if (typeof user?.id !== "string" || !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(user.id))
      return json(401, { error: "invalid_session" });
    subject = user.id;
  } catch { return json(503, { error: "account_service_unavailable" }); }

  const now = Math.floor(Date.now() / 1000);
  const exp = now + ticketLifetimeSeconds;
  const payload = enc.encode(JSON.stringify({
    v: 1,
    sub: subject,
    iat: now,
    exp,
  }));
  const payloadText = b64url(payload);
  let sig: Uint8Array;
  try {
    const key = await signingKey();
    sig = new Uint8Array(await crypto.subtle.sign("HMAC", key, enc.encode(payloadText)));
  } catch { return json(503, { error: "ticket_service_unavailable" }); }
  return json(200, {
    ticket: `${ticketPrefix}.${payloadText}.${b64url(sig)}`,
    expires_at: new Date(exp * 1000).toISOString(),
  });
});
