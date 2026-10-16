import assert from "node:assert/strict";
import test from "node:test";
import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import os from "node:os";
import { createPk3UploadService } from "./service.mjs";
import {
  MAX_INPUT_BYTES, validHash, validLevelName, validRoomName, validOrigin,
  validStart, sessionKey,
} from "./validation.mjs";

const bearer = "Bearer ppm1.accepted.signature";
const other = "Bearer ppm1.other.signature";
const creator = "hunter:aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa";
const alternate = "hunter:bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb";
const sum = bytes => createHash("sha256").update(bytes).digest("hex");

test("source metadata forbids path traversal, zip bombs by size, and missing content rights", () => {
  assert.equal(validHash("f".repeat(64)), true);
  assert.equal(validHash("f".repeat(64)+"/../"), false);
  assert.equal(validRoomName("Q3 PRIME ARENA"), true);
  assert.equal(validRoomName("../escape"), false);
  assert.equal(validRoomName("COM1"), false);
  assert.equal(validRoomName("arena;rm -rf /"), false);
  assert.equal(validLevelName("q3dm17"), true);
  assert.equal(validLevelName("../../etc/passwd"), false);
  assert.equal(validStart({ bytes: MAX_INPUT_BYTES, roomName: "ARENA",
    rightsConfirmed: true }), true);
  assert.equal(validStart({ bytes: MAX_INPUT_BYTES + 1, roomName: "ARENA",
    rightsConfirmed: true }), false);
  assert.equal(validStart({ bytes: 2, roomName: "ARENA" }), false);
  assert.notEqual(sessionKey(creator, "a".repeat(64)),
    sessionKey(alternate, "a".repeat(64)));
  assert.notEqual(sessionKey(creator, "a".repeat(64), "Q3_MAP_ONE", "q3dm1"),
    sessionKey(creator, "a".repeat(64), "Q3_MAP_TWO", "q3dm1"));
  assert.notEqual(sessionKey(creator, "a".repeat(64), "Q3_MAP_ONE", "q3dm1"),
    sessionKey(creator, "a".repeat(64), "Q3_MAP_ONE", "q3dm2"));
});
test("cross-origin allowlist accepts only exact HTTPS origins", () => {
  assert.equal(validOrigin("https://projectprime.gg"), true);
  assert.equal(validOrigin("http://localhost:3000"), true);
  assert.equal(validOrigin("https://bad.example.com/projectprime.gg"), false);
  assert.equal(validOrigin("https://projectprime.gg@evil.example"), false);
  assert.equal(validOrigin("https://projectprime.gg/path"), false);
  assert.equal(validOrigin("http://projectprime.gg"), false);
});

async function fixture() {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "prime-pk3-api-"));
  let invocation = 0;
  const api = createPk3UploadService({
    root, allowedOrigins: ["https://projectprime.gg"],
    verifyTicket: async ticket => ticket === bearer.slice(7)
      ? { creatorId: creator } : ticket === other.slice(7)
        ? { creatorId: alternate } : null,
    runConverter: async ({ output, roomName }) => {
      invocation++;
      const bytes = Buffer.from("test verified package for " + roomName);
      await fs.writeFile(output, bytes);
      return {
        packageHash: sum(bytes),
        packageBytes: bytes.length,
        contentHash: "c".repeat(64),
        mapId: "cccccccc-cccc-4ccc-cccc-cccccccccccc",
        warnings: ["Review unsupported shader behavior"],
      };
    },
  });
  await api.init();
  await new Promise(resolve => api.server.listen(0, "127.0.0.1", resolve));
  const address = "http://127.0.0.1:" + api.server.address().port;
  async function request(route, method = "GET", body, token = bearer, origin) {
    const headers = { Authorization: token };
    if (origin) headers.Origin = origin;
    if (typeof body === "string") headers["Content-Type"] = "application/json";
    if (Buffer.isBuffer(body)) headers["Content-Type"] = "application/octet-stream";
    return fetch(address + route, { method, headers, body, redirect: "error" });
  }
  return {
    root, api, request,
    converted: () => invocation,
    async cleanup() { await api.close(); await fs.rm(root, { recursive: true, force: true }); },
  };
}

test("owner-only chunked upload, retry/resume, SHA validation and queued conversion", async () => {
  const env = await fixture();
  try {
    const input = Buffer.from("a valid-looking source archive fixture");
    const hash = sum(input), uri = "/v1/sources/" + hash +
      "?roomName=Q3_TEST_WEB&levelName=q3dm17";
    const meta = JSON.stringify({
      bytes: input.length, roomName: "Q3_TEST_WEB",
      levelName: "q3dm17", rightsConfirmed: true,
    });

    const preflight = await fetch(env.api.server.address() &&
      "http://127.0.0.1:" + env.api.server.address().port + uri, {
      method: "OPTIONS", headers: {
        Origin: "https://projectprime.gg",
        "Access-Control-Request-Method": "PUT",
      },
    });
    assert.equal(preflight.status, 204);
    assert.equal(preflight.headers.get("access-control-allow-origin"),
      "https://projectprime.gg");

    const preflightBad = await env.request(uri, "GET", undefined,
      bearer, "https://evil.example");
    assert.equal(preflightBad.status, 403);
    assert.equal(preflightBad.headers.get("access-control-allow-origin"), null);
    const denied = await env.request(uri, "POST", meta, "Bearer ppm1.nope.token");
    assert.equal(denied.status, 401);

    const created = await env.request(uri, "POST", meta);
    assert.equal(created.status, 201);
    assert.equal((await created.json()).offset, 0);
    const differentOwner = await env.request(uri, "GET", undefined, other);
    assert.equal(differentOwner.status, 404);

    const first = await env.request(uri + "&offset=0", "PUT", input.subarray(0, 10));
    assert.equal(first.status, 200);
    assert.equal((await first.json()).offset, 10);
    const outOfOrder = await env.request(uri + "&offset=0", "PUT", input.subarray(10, 12));
    assert.equal(outOfOrder.status, 409);
    const resumed = await env.request(uri + "&offset=10", "PUT", input.subarray(10));
    assert.equal(resumed.status, 200);
    assert.equal((await resumed.json()).offset, input.length);

    const submission = await env.request(uri.replace("?", "/complete?"), "POST");
    assert.equal(submission.status, 202);
    const { jobId } = await submission.json();
    assert.equal(jobId.length, 36);
    const otherJob = await env.request("/v1/conversions/" + jobId, "GET", undefined, other);
    assert.equal(otherJob.status, 404);
    let latest = null;
    for (let n = 0; n < 100; n++) {
      const status = await env.request("/v1/conversions/" + jobId);
      assert.equal(status.status, 200);
      latest = await status.json();
      if (latest.status === "ready") break;
      await new Promise(resolve => setTimeout(resolve, 20));
    }
    assert.equal(latest.status, "ready");
    assert.equal(latest.warnings.length, 1);
    const packageResponse = await env.request("/v1/conversions/" + jobId + "/package");
    assert.equal(packageResponse.status, 200);
    const bytes = Buffer.from(await packageResponse.arrayBuffer());
    assert.equal(packageResponse.headers.get("x-prime-sha256"), sum(bytes));
    assert.equal(latest.hash, sum(bytes));
    assert.equal(latest.mapId, "cccccccc-cccc-4ccc-cccc-cccccccccccc");
    assert.equal(env.converted(), 1);
  } finally { await env.cleanup(); }
});

test("incorrect SHA is rejected before conversion without moving source to a job", async () => {
  const env = await fixture();
  try {
    const input = Buffer.from("test");
    const wrongHash = "a".repeat(64);
    const uri = "/v1/sources/" + wrongHash + "?roomName=Q3_WRONG_SHA";
    assert.equal((await env.request(uri, "POST", JSON.stringify({
      bytes: input.length, roomName: "Q3_WRONG_SHA",
      rightsConfirmed: true,
    }))).status, 201);
    assert.equal((await env.request(uri + "&offset=0", "PUT", input)).status, 200);
    assert.equal((await env.request(uri.replace("?", "/complete?"), "POST")).status, 422);
    assert.equal(env.converted(), 0);
  } finally { await env.cleanup(); }
});

test("identical source SHA does not share a conversion session across Hunters", async () => {
  const env = await fixture();
  try {
    const data = Buffer.from("shared bytes"), hash = sum(data);
    const uri = "/v1/sources/" + hash + "?roomName=Q3_OWNER_SCOPE";
    const metadata = JSON.stringify({
      bytes: data.length, roomName: "Q3_OWNER_SCOPE", rightsConfirmed: true,
    });
    const a = await env.request(uri, "POST", metadata);
    const b = await env.request(uri, "POST", metadata, other);
    assert.equal(a.status, 201);
    assert.equal(b.status, 201);
    const aa = await env.request(uri, "GET");
    const bb = await env.request(uri, "GET", undefined, other);
    assert.equal((await aa.json()).offset, 0);
    assert.equal((await bb.json()).offset, 0);
  } finally { await env.cleanup(); }
});

test("creator-scoped rate limiter denies excess API calls without affecting another Hunter", async () => {
  const env = await fixture();
  try {
    const route = "/v1/conversions/aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa";
    let throttled = false;
    for (let i = 0; i < 190; i++) {
      const result = await env.request(route);
      if (result.status === 429) {
        throttled = true;
        assert.equal(result.headers.get("retry-after"), "30");
        break;
      }
      assert.equal(result.status, 404);
    }
    assert.equal(throttled, true);
    assert.equal((await env.request(route, "GET", undefined, other)).status, 404);
  } finally { await env.cleanup(); }
});
