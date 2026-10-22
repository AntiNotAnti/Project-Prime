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

async function fixture(overrides = {}) {
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
    ...overrides,
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

test("one creator can convert two BSP variants from the same .pk3 independently", async () => {
  const env = await fixture();
  try {
    const input = Buffer.from("two-level fixture");
    const hash = sum(input);
    const a = "/v1/sources/" + hash + "?roomName=Q3_LEVEL_ONE&levelName=map1";
    const b = "/v1/sources/" + hash + "?roomName=Q3_LEVEL_TWO&levelName=map2";
    const meta = (roomName, levelName) => JSON.stringify({
      bytes: input.length, roomName, levelName, rightsConfirmed: true,
    });
    assert.equal((await env.request(a, "POST", meta("Q3_LEVEL_ONE", "map1"))).status, 201);
    assert.equal((await env.request(b, "POST", meta("Q3_LEVEL_TWO", "map2"))).status, 201);
    assert.equal((await env.request(a + "&offset=0", "PUT", input.subarray(0, 4))).status, 200);
    const one = await env.request(a), two = await env.request(b);
    assert.equal((await one.json()).offset, 4);
    assert.equal((await two.json()).offset, 0);
    const mismatch = await env.request(b, "POST", meta("Q3_LEVEL_ONE", "map1"));
    assert.equal(mismatch.status, 400);
  } finally { await env.cleanup(); }
});

test("restart promotes each durable staged-job crash state to the same job once", { timeout: 15000 }, async () => {
  const phases = [
    ["before input move", "before-input", "11111111-1111-4111-8111-111111111111"],
    ["after input move", "after-input", "22222222-2222-4222-8222-222222222222"],
    ["after final directory move", "after-final", "33333333-3333-4333-8333-333333333333"],
    ["legacy input move before metadata", "legacy-no-metadata", "44444444-4444-4444-8444-444444444444"],
  ];
  for (const [label, phase, id] of phases) {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), "prime-pk3-crash-"));
    const input = Buffer.from("durable recovery source for " + phase);
    const hash = sum(input), roomName = "Q3_RECOVERY", levelName = "q3dm17";
    const key = sessionKey(creator, hash, roomName, levelName);
    const uploadDirectory = path.join(root, "uploads", key);
    const jobRoot = path.join(root, "jobs");
    const createdAt = Date.now();
    const session = {
      hash, creatorId: creator, bytes: input.length, offset: input.length,
      roomName, levelName, createdAt, updatedAt: createdAt, jobId: null,
    };
    const stagedJob = {
      id, creatorId: creator, sourceHash: hash, bytes: input.length,
      roomName, levelName, sessionKey: key, createdAt, updatedAt: createdAt,
      status: "queued", stage: "Queued",
    };
    await fs.mkdir(uploadDirectory, { recursive: true });
    await fs.mkdir(jobRoot, { recursive: true });
    await fs.writeFile(path.join(uploadDirectory, "session.json"), JSON.stringify(session));
    if (phase === "before-input") {
      await fs.writeFile(path.join(uploadDirectory, "source.part"), input);
      const pending = path.join(jobRoot, ".pending-" + id);
      await fs.mkdir(pending);
      await fs.writeFile(path.join(pending, "job.json"), JSON.stringify(stagedJob));
    } else if (phase === "after-input") {
      const pending = path.join(jobRoot, ".pending-" + id);
      await fs.mkdir(pending);
      await fs.writeFile(path.join(pending, "job.json"), JSON.stringify(stagedJob));
      await fs.writeFile(path.join(pending, "input.pk3"), input);
    } else {
      const finalDirectory = path.join(jobRoot, id);
      await fs.mkdir(finalDirectory);
      if (phase !== "legacy-no-metadata")
        await fs.writeFile(path.join(finalDirectory, "job.json"), JSON.stringify(stagedJob));
      await fs.writeFile(path.join(finalDirectory, "input.pk3"), input);
    }

    let invocations = 0;
    const api = createPk3UploadService({
      root, allowedOrigins: ["https://projectprime.gg"],
      verifyTicket: async () => ({ creatorId: creator }),
      runConverter: async ({ input: source, output }) => {
        invocations++;
        assert.deepEqual(await fs.readFile(source), input);
        const bytes = Buffer.from("verified " + phase);
        await fs.writeFile(output, bytes);
        return { packageHash: sum(bytes), packageBytes: bytes.length,
          contentHash: "c".repeat(64), mapId: "cccccccc-cccc-4ccc-cccc-cccccccccccc" };
      },
    });
    try {
      await api.init();
      await new Promise(resolve => api.server.listen(0, "127.0.0.1", resolve));
      const address = "http://127.0.0.1:" + api.server.address().port;
      const finalDirectory = path.join(jobRoot, id);
      let job;
      for (let attempt = 0; attempt < 100; attempt++) {
        job = JSON.parse(await fs.readFile(path.join(finalDirectory, "job.json"), "utf8"));
        if (job.status === "ready") break;
        await new Promise(resolve => setTimeout(resolve, 10));
      }
      assert.equal(job.status, "ready", label);
      assert.equal(job.id, id, label);
      assert.equal(invocations, 1, label);
      assert.deepEqual(await fs.readFile(path.join(finalDirectory, "input.pk3")), input, label);
      const linkedSession = JSON.parse(await fs.readFile(path.join(uploadDirectory, "session.json"), "utf8"));
      assert.equal(linkedSession.jobId, id, label);
      const completed = await fetch(`${address}/v1/sources/${hash}/complete?roomName=${roomName}&levelName=${levelName}`, {
        method: "POST", headers: { Authorization: bearer },
      });
      assert.equal(completed.status, 202, label);
      assert.deepEqual(await completed.json(), { jobId: id }, label);
      assert.equal(invocations, 1, label);
    } finally {
      await api.close();
      await fs.rm(root, { recursive: true, force: true });
    }
  }
});

test("staged-job metadata and source must match before restart promotion", { timeout: 10000 }, async () => {
  const cases = [
    ["owner", job => { job.creatorId = alternate; }],
    ["source hash", job => { job.sourceHash = "f".repeat(64); }],
    ["room variant", job => { job.roomName = "Q3_OTHER_ROOM"; }],
    ["level variant", job => { job.levelName = "q3dm18"; }],
    ["byte count", job => { job.bytes++; }],
    ["source digest", (_job, bytes) => { bytes[0] ^= 0xff; }],
  ];
  for (let index = 0; index < cases.length; index++) {
    const [label, alter] = cases[index];
    const root = await fs.mkdtemp(path.join(os.tmpdir(), "prime-pk3-invalid-stage-"));
    const input = Buffer.from("staged source validation " + label);
    const hash = sum(input), roomName = "Q3_VALIDATE", levelName = "q3dm17";
    const key = sessionKey(creator, hash, roomName, levelName);
    const id = `${index + 4}4444444-4444-4444-8444-444444444444`;
    const uploadDirectory = path.join(root, "uploads", key);
    const pendingDirectory = path.join(root, "jobs", ".pending-" + id);
    const createdAt = Date.now();
    const session = {
      hash, creatorId: creator, bytes: input.length, offset: input.length,
      roomName, levelName, createdAt, updatedAt: createdAt, jobId: null,
    };
    const stagedJob = {
      id, creatorId: creator, sourceHash: hash, bytes: input.length,
      roomName, levelName, sessionKey: key, createdAt, updatedAt: createdAt,
      status: "queued", stage: "Queued",
    };
    const stagedBytes = Buffer.from(input);
    alter(stagedJob, stagedBytes);
    await fs.mkdir(uploadDirectory, { recursive: true });
    await fs.mkdir(pendingDirectory, { recursive: true });
    await fs.writeFile(path.join(uploadDirectory, "session.json"), JSON.stringify(session));
    await fs.writeFile(path.join(pendingDirectory, "job.json"), JSON.stringify(stagedJob));
    await fs.writeFile(path.join(pendingDirectory, "input.pk3"), stagedBytes);

    let invocations = 0;
    const api = createPk3UploadService({
      root, allowedOrigins: ["https://projectprime.gg"],
      verifyTicket: async () => ({ creatorId: creator }),
      runConverter: async () => { invocations++; throw new Error("Invalid staged input was queued."); },
    });
    try {
      await api.init();
      await new Promise(resolve => api.server.listen(0, "127.0.0.1", resolve));
      await new Promise(resolve => setTimeout(resolve, 20));
      const unchangedSession = JSON.parse(await fs.readFile(path.join(uploadDirectory, "session.json"), "utf8"));
      assert.equal(unchangedSession.jobId, null, label);
      assert.equal(await fs.stat(path.join(root, "jobs", id)).then(() => true, () => false), false, label);
      assert.equal(await fs.stat(pendingDirectory).then(() => true, () => false), true, label);
      assert.deepEqual(await fs.readFile(path.join(pendingDirectory, "input.pk3")), stagedBytes, label);
      assert.equal(invocations, 0, label);
    } finally {
      await api.close();
      await fs.rm(root, { recursive: true, force: true });
    }
  }
});

test("ambiguous legacy inputs stay intact and count toward disk quota", async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "prime-pk3-ambiguous-legacy-"));
  const jobRoot = path.join(root, "jobs");
  const oldInput = Buffer.from("legacy source shared by two upload variants");
  const oldHash = sum(oldInput), oldId = "12121212-1212-4121-8121-121212121212";
  const createdAt = Date.now();
  await fs.mkdir(jobRoot, { recursive: true });
  const legacyDirectory = path.join(jobRoot, oldId);
  await fs.mkdir(legacyDirectory);
  await fs.writeFile(path.join(legacyDirectory, "input.pk3"), oldInput);
  const legacySessions = [];
  for (const roomName of ["Q3_LEGACY_A", "Q3_LEGACY_B"]) {
    const key = sessionKey(creator, oldHash, roomName, "q3dm17");
    const directory = path.join(root, "uploads", key);
    await fs.mkdir(directory, { recursive: true });
    await fs.writeFile(path.join(directory, "session.json"), JSON.stringify({
      hash: oldHash, creatorId: creator, bytes: oldInput.length, offset: oldInput.length,
      roomName, levelName: "q3dm17", createdAt, updatedAt: createdAt, jobId: null,
    }));
    legacySessions.push(path.join(directory, "session.json"));
  }

  const currentInput = Buffer.from("source that must remain if disk quota rejects it");
  const currentHash = sum(currentInput), currentRoom = "Q3_QUOTA";
  const currentKey = sessionKey(creator, currentHash, currentRoom, "");
  const currentDirectory = path.join(root, "uploads", currentKey);
  await fs.mkdir(currentDirectory, { recursive: true });
  await fs.writeFile(path.join(currentDirectory, "source.part"), currentInput);
  await fs.writeFile(path.join(currentDirectory, "session.json"), JSON.stringify({
    hash: currentHash, creatorId: creator, bytes: currentInput.length,
    offset: currentInput.length, roomName: currentRoom, levelName: null,
    createdAt, updatedAt: createdAt, jobId: null,
  }));

  const sparseBytes = 600 * 1024 * 1024;
  const orphanIds = [
    "13131313-1313-4131-8131-131313131313",
    "14141414-1414-4141-8141-141414141414",
    "15151515-1515-4151-8151-151515151515",
    "16161616-1616-4161-8161-161616161616",
  ];
  const orphanInputs = [];
  for (const id of orphanIds) {
    const directory = path.join(jobRoot, id);
    await fs.mkdir(directory);
    const input = path.join(directory, "input.pk3");
    const handle = await fs.open(input, "w", 0o600);
    await handle.truncate(sparseBytes);
    await handle.close();
    orphanInputs.push(input);
  }

  let invocations = 0;
  const api = createPk3UploadService({
    root, allowedOrigins: ["https://projectprime.gg"],
    verifyTicket: async () => ({ creatorId: creator }),
    runConverter: async () => { invocations++; throw new Error("Unlinked legacy input was queued."); },
  });
  try {
    await api.init();
    await new Promise(resolve => api.server.listen(0, "127.0.0.1", resolve));
    const address = "http://127.0.0.1:" + api.server.address().port;
    const response = await fetch(`${address}/v1/sources/${currentHash}/complete?roomName=${currentRoom}`, {
      method: "POST", headers: { Authorization: bearer },
    });
    assert.equal(response.status, 507);
    assert.deepEqual(await fs.readFile(path.join(currentDirectory, "source.part")), currentInput);
    assert.equal(await fs.stat(path.join(legacyDirectory, "job.json")).then(() => true, () => false), false);
    assert.deepEqual(await fs.readFile(path.join(legacyDirectory, "input.pk3")), oldInput);
    for (const file of legacySessions)
      assert.equal(JSON.parse(await fs.readFile(file, "utf8")).jobId, null);
    for (const input of orphanInputs) assert.equal((await fs.stat(input)).size, sparseBytes);
    assert.equal(invocations, 0);
  } finally {
    await api.close();
    await fs.rm(root, { recursive: true, force: true });
  }
});

test("pending conversions remain in creator queue quotas", async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "prime-pk3-pending-quota-"));
  const input = Buffer.from("source waiting for conversion quota");
  const hash = sum(input), roomName = "Q3_QUOTA", levelName = null;
  const key = sessionKey(creator, hash, roomName, "");
  const uploadDirectory = path.join(root, "uploads", key);
  const jobRoot = path.join(root, "jobs");
  const createdAt = Date.now();
  const session = {
    hash, creatorId: creator, bytes: input.length, offset: input.length,
    roomName, levelName, createdAt, updatedAt: createdAt, jobId: null,
  };
  await fs.mkdir(uploadDirectory, { recursive: true });
  await fs.mkdir(jobRoot, { recursive: true });
  await fs.writeFile(path.join(uploadDirectory, "source.part"), input);
  await fs.writeFile(path.join(uploadDirectory, "session.json"), JSON.stringify(session));
  const pendingInputs = [];
  for (const [index, id] of [
    "55555555-5555-4555-8555-555555555555",
    "66666666-6666-4666-8666-666666666666",
  ].entries()) {
    const bytes = Buffer.from("reserved pending source " + index);
    const sourceHash = sum(bytes), name = `Q3_PENDING_${index}`;
    const sessionId = sessionKey(creator, sourceHash, name, "");
    const directory = path.join(jobRoot, ".pending-" + id);
    await fs.mkdir(directory);
    await fs.writeFile(path.join(directory, "job.json"), JSON.stringify({
      id, creatorId: creator, sourceHash, bytes: bytes.length,
      roomName: name, levelName: null, sessionKey: sessionId,
      createdAt, updatedAt: createdAt, status: "queued", stage: "Queued",
    }));
    await fs.writeFile(path.join(directory, "input.pk3"), bytes);
    pendingInputs.push([path.join(directory, "input.pk3"), bytes]);
  }
  let invocations = 0;
  const api = createPk3UploadService({
    root, allowedOrigins: ["https://projectprime.gg"],
    verifyTicket: async () => ({ creatorId: creator }),
    runConverter: async () => { invocations++; throw new Error("Unlinked pending job was queued."); },
  });
  try {
    await api.init();
    await new Promise(resolve => api.server.listen(0, "127.0.0.1", resolve));
    const address = "http://127.0.0.1:" + api.server.address().port;
    const response = await fetch(`${address}/v1/sources/${hash}/complete?roomName=${roomName}`, {
      method: "POST", headers: { Authorization: bearer },
    });
    assert.equal(response.status, 507);
    assert.deepEqual(await fs.readFile(path.join(uploadDirectory, "source.part")), input);
    assert.equal(JSON.parse(await fs.readFile(path.join(uploadDirectory, "session.json"), "utf8")).jobId, null);
    for (const [file, bytes] of pendingInputs) assert.deepEqual(await fs.readFile(file), bytes);
    assert.equal(invocations, 0);
  } finally {
    await api.close();
    await fs.rm(root, { recursive: true, force: true });
  }
});

test("pending source bytes count toward the shared disk quota", async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "prime-pk3-pending-disk-quota-"));
  const input = Buffer.from("source reserved against pending disk usage");
  const hash = sum(input), roomName = "Q3_DISK", key = sessionKey(creator, hash, roomName, "");
  const uploadDirectory = path.join(root, "uploads", key);
  const jobRoot = path.join(root, "jobs");
  const createdAt = Date.now();
  await fs.mkdir(uploadDirectory, { recursive: true });
  await fs.mkdir(jobRoot, { recursive: true });
  await fs.writeFile(path.join(uploadDirectory, "source.part"), input);
  await fs.writeFile(path.join(uploadDirectory, "session.json"), JSON.stringify({
    hash, creatorId: creator, bytes: input.length, offset: input.length,
    roomName, levelName: null, createdAt, updatedAt: createdAt, jobId: null,
  }));

  const owners = [
    "hunter:10000000-0000-4000-8000-000000000001",
    "hunter:20000000-0000-4000-8000-000000000002",
    "hunter:30000000-0000-4000-8000-000000000003",
    "hunter:40000000-0000-4000-8000-000000000004",
  ];
  const ids = [
    "77777777-7777-4777-8777-777777777777",
    "88888888-8888-4888-8888-888888888888",
    "99999999-9999-4999-8999-999999999999",
    "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
  ];
  const sparseBytes = 600 * 1024 * 1024;
  const stagedInputs = [];
  for (let index = 0; index < owners.length; index++) {
    const owner = owners[index], id = ids[index];
    const sourceHash = sum(Buffer.from("pending reservation " + index));
    const name = `Q3_DISK_${index}`;
    const directory = path.join(jobRoot, ".pending-" + id);
    await fs.mkdir(directory);
    await fs.writeFile(path.join(directory, "job.json"), JSON.stringify({
      id, creatorId: owner, sourceHash, bytes: 1, roomName: name,
      levelName: null, sessionKey: sessionKey(owner, sourceHash, name, ""),
      createdAt, updatedAt: createdAt, status: "queued", stage: "Queued",
    }));
    const source = path.join(directory, "input.pk3");
    const handle = await fs.open(source, "w", 0o600);
    await handle.truncate(sparseBytes);
    await handle.close();
    stagedInputs.push(source);
  }

  let invocations = 0;
  const api = createPk3UploadService({
    root, allowedOrigins: ["https://projectprime.gg"],
    verifyTicket: async () => ({ creatorId: creator }),
    runConverter: async () => { invocations++; throw new Error("Unlinked pending job was queued."); },
  });
  try {
    await api.init();
    await new Promise(resolve => api.server.listen(0, "127.0.0.1", resolve));
    const address = "http://127.0.0.1:" + api.server.address().port;
    const response = await fetch(`${address}/v1/sources/${hash}/complete?roomName=${roomName}`, {
      method: "POST", headers: { Authorization: bearer },
    });
    assert.equal(response.status, 507);
    assert.deepEqual(await fs.readFile(path.join(uploadDirectory, "source.part")), input);
    for (const source of stagedInputs)
      assert.equal((await fs.stat(source)).size, sparseBytes);
    assert.equal(invocations, 0);
  } finally {
    await api.close();
    await fs.rm(root, { recursive: true, force: true });
  }
});

test("shutdown aborts active conversion and preserves its immutable input for restart", { timeout: 10000 }, async () => {
  let started;
  const running = new Promise(resolve => { started = resolve; });
  let cancelled = false;
  const env = await fixture({
    runConverter: async ({ signal }) => {
      started();
      await new Promise((resolve, reject) => {
        const abort = () => { cancelled = true; reject(signal.reason); };
        if (signal.aborted) abort();
        else signal.addEventListener("abort", abort, { once: true });
      });
    },
  });
  let restarted;
  try {
    const input = Buffer.from("shutdown recovery source");
    const uri = "/v1/sources/" + sum(input) + "?roomName=Q3_SHUTDOWN";
    assert.equal((await env.request(uri, "POST", JSON.stringify({
      bytes: input.length, roomName: "Q3_SHUTDOWN", rightsConfirmed: true,
    }))).status, 201);
    assert.equal((await env.request(uri + "&offset=0", "PUT", input)).status, 200);
    const submission = await env.request(uri.replace("?", "/complete?"), "POST");
    const { jobId } = await submission.json();
    await running;
    await env.api.close();
    assert.equal(cancelled, true);
    const folder = path.join(env.root, "jobs", jobId);
    const state = JSON.parse(await fs.readFile(path.join(folder, "job.json"), "utf8"));
    assert.equal(state.status, "queued");
    assert.deepEqual(await fs.readFile(path.join(folder, "input.pk3")), input);
    assert.equal(await fs.stat(path.join(folder, "output.ppmap")).then(() => true, () => false), false);
    let resumed = 0;
    restarted = createPk3UploadService({
      root: env.root, allowedOrigins: ["https://projectprime.gg"],
      verifyTicket: async () => ({ creatorId: creator }),
      runConverter: async ({ input: source, output }) => {
        resumed++;
        assert.deepEqual(await fs.readFile(source), input);
        const bytes = Buffer.from("verified resumed package");
        await fs.writeFile(output, bytes);
        return { packageHash: sum(bytes), packageBytes: bytes.length,
          contentHash: "c".repeat(64), mapId: "cccccccc-cccc-4ccc-cccc-cccccccccccc" };
      },
    });
    await restarted.init();
    let recovered;
    for (let attempt = 0; attempt < 100; attempt++) {
      recovered = JSON.parse(await fs.readFile(path.join(folder, "job.json"), "utf8"));
      if (recovered.status === "ready") break;
      await new Promise(resolve => setTimeout(resolve, 10));
    }
    assert.equal(recovered.status, "ready");
    assert.equal(resumed, 1);
    assert.equal(recovered.id, jobId);
  } finally { if (restarted) await restarted.close(); await env.cleanup(); }
});
