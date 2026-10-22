#!/usr/bin/env node
import assert from "node:assert/strict";
import { createHash, randomBytes } from "node:crypto";
import { createReadStream } from "node:fs";
import fs from "node:fs/promises";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import { performance } from "node:perf_hooks";
import { createPk3UploadService } from "./service.mjs";
import { CHUNK_BYTES, MAX_INPUT_BYTES, sessionKey } from "./validation.mjs";

const MIB = 1024 * 1024;
const FIXTURE_BLOCK_BYTES = MIB;
const CLIENT_STREAM_BYTES = MIB;
const INTERRUPTED_PREFIX_BYTES = 256 * 1024;
const REQUEST_TIMEOUT_MS = 30_000;
const JOB_TIMEOUT_MS = 60_000;
const MAX_RESPONSE_BYTES = 64 * 1024;
const STUB_PACKAGE = Buffer.from(
  "Synthetic PK3 service benchmark marker; this is not a .ppmap package.\n");
const STUB_MAP_ID = "00000000-0000-4000-8000-000000000001";

function parseArgs(argv) {
  let mib = 32;
  let output = null;
  for (let index = 0; index < argv.length; index++) {
    const argument = argv[index];
    if (argument === "--help" || argument === "-h") return { help: true };
    if (argument === "--mib" || argument.startsWith("--mib=")) {
      const value = argument === "--mib" ? argv[++index] : argument.slice(6);
      mib = Number(value);
    } else if (argument === "--output" || argument.startsWith("--output=")) {
      output = argument === "--output" ? argv[++index] : argument.slice(9);
      if (!output) throw new Error("--output requires a path.");
    } else {
      throw new Error("Unknown argument: " + argument);
    }
  }
  if (!Number.isInteger(mib) || mib < 1 || mib > 128) {
    throw new Error("--mib must be a whole number from 1 to 128.");
  }
  return { mib, output: output ? path.resolve(output) : null };
}

function help() {
  process.stdout.write(
    "Usage: node tools/pk3-upload/service-bench.mjs [--mib 1..128] [--output FILE]\n" +
    "Streams a deterministic synthetic byte fixture through the local PK3 upload service.\n" +
    "Default size: 32 MiB. The fixture is not a valid PK3 and conversion is stubbed.\n");
}

function* deterministicBlocks(totalBytes) {
  let state = 0x50504d31;
  let produced = 0;
  while (produced < totalBytes) {
    const length = Math.min(FIXTURE_BLOCK_BYTES, totalBytes - produced);
    const block = Buffer.allocUnsafe(length);
    for (let index = 0; index < length; index++) {
      state ^= state << 13;
      state ^= state >>> 17;
      state ^= state << 5;
      block[index] = state & 0xff;
    }
    produced += length;
    yield block;
  }
}

async function createFixture(file, totalBytes) {
  const handle = await fs.open(file, "wx", 0o600);
  const hash = createHash("sha256");
  let offset = 0;
  try {
    for (const block of deterministicBlocks(totalBytes)) {
      hash.update(block);
      let written = 0;
      while (written < block.length) {
        const result = await handle.write(
          block, written, block.length - written, offset + written);
        if (result.bytesWritten <= 0) throw new Error("Fixture write made no progress.");
        written += result.bytesWritten;
      }
      offset += block.length;
    }
    await handle.sync();
  } finally {
    await handle.close();
  }
  const info = await fs.stat(file);
  assert.equal(info.size, totalBytes, "generated fixture must contain real bytes, not a sparse extent");
  return hash.digest("hex");
}

async function hashFile(file) {
  const hash = createHash("sha256");
  for await (const block of createReadStream(file, { highWaterMark: CLIENT_STREAM_BYTES }))
    hash.update(block);
  return hash.digest("hex");
}

function openRequest(url, method, ticket, headers = {}) {
  const request = http.request(url, {
    method,
    headers: {
      Authorization: "Bearer " + ticket,
      Accept: "application/json",
      ...headers,
    },
  });
  let settled = false;
  let timer;
  const response = new Promise((resolve, reject) => {
    const finish = (callback, value) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      callback(value);
    };
    timer = setTimeout(() => {
      request.destroy(new Error("Local HTTP request exceeded its timeout."));
    }, REQUEST_TIMEOUT_MS);
    request.on("error", error => finish(reject, error));
    request.on("response", incoming => {
      const blocks = [];
      let bytes = 0;
      incoming.on("data", block => {
        bytes += block.length;
        if (bytes > MAX_RESPONSE_BYTES) {
          finish(reject, new Error("Service response exceeded the metadata bound."));
          incoming.destroy();
          return;
        }
        blocks.push(block);
      });
      incoming.on("end", () => finish(resolve, {
        status: incoming.statusCode || 0,
        body: Buffer.concat(blocks),
      }));
      incoming.on("error", error => finish(reject, error));
    });
  });
  return { request, response };
}

async function requestJson(baseUrl, route, ticket, method, value = undefined) {
  const body = value === undefined ? null : Buffer.from(JSON.stringify(value));
  const headers = body ? {
    "Content-Type": "application/json",
    "Content-Length": String(body.length),
  } : {};
  const { request, response } = openRequest(new URL(route, baseUrl), method, ticket, headers);
  if (body) request.end(body);
  else request.end();
  const result = await response;
  let data;
  try { data = JSON.parse(result.body.toString("utf8")); }
  catch { throw new Error("Service returned invalid JSON for " + method + " " + route); }
  return { status: result.status, data };
}

function sourceRoute(hash, roomName, levelName, operation = "", offset = undefined) {
  const query = new URLSearchParams({ roomName, levelName });
  if (offset !== undefined) query.set("offset", String(offset));
  return `/v1/sources/${hash}${operation}?${query}`;
}

function validateState(value, expectedHash, expectedBytes) {
  assert.ok(value && typeof value === "object" && !Array.isArray(value),
    "upload state must be an object");
  assert.equal(value.hash, expectedHash);
  assert.equal(value.bytes, expectedBytes);
  assert.ok(Number.isSafeInteger(value.offset) && value.offset >= 0 &&
    value.offset <= expectedBytes, "upload offset must be in range");
  assert.equal(value.chunkBytes, CHUNK_BYTES);
  assert.equal(typeof value.complete, "boolean");
  return value;
}

async function reserveUpload(baseUrl, ticket, hash, roomName, levelName, bytes) {
  const response = await requestJson(baseUrl,
    sourceRoute(hash, roomName, levelName), ticket, "POST", {
      bytes, roomName, levelName, rightsConfirmed: true,
    });
  assert.equal(response.status, 201, "new upload session should be created");
  const state = validateState(response.data, hash, bytes);
  assert.equal(state.offset, 0);
  return state;
}

async function getUploadState(baseUrl, ticket, hash, roomName, levelName, bytes) {
  const response = await requestJson(baseUrl,
    sourceRoute(hash, roomName, levelName), ticket, "GET");
  assert.equal(response.status, 200, "authoritative upload state should be available");
  return validateState(response.data, hash, bytes);
}

async function putRange(baseUrl, ticket, sourceFile, hash, roomName, levelName,
                        offset, length) {
  assert.ok(length > 0 && length <= CHUNK_BYTES, "PUT range must fit the service chunk limit");
  const route = sourceRoute(hash, roomName, levelName, "", offset);
  const { request, response } = openRequest(new URL(route, baseUrl), "PUT", ticket, {
    "Content-Type": "application/octet-stream",
    "Content-Length": String(length),
  });
  const stream = createReadStream(sourceFile, {
    start: offset,
    end: offset + length - 1,
    highWaterMark: CLIENT_STREAM_BYTES,
  });
  stream.on("error", error => request.destroy(error));
  stream.pipe(request);
  const result = await response;
  let data;
  try { data = JSON.parse(result.body.toString("utf8")); }
  catch { throw new Error("Service returned invalid JSON for a source PUT."); }
  return { status: result.status, data };
}

function writeRequestBlock(request, block) {
  return new Promise((resolve, reject) => {
    request.write(block, error => error ? reject(error) : resolve());
  });
}

async function waitForFileSize(file, expectedSize, atLeast = false) {
  const deadline = Date.now() + REQUEST_TIMEOUT_MS;
  while (Date.now() < deadline) {
    try {
      const info = await fs.stat(file);
      if (atLeast ? info.size >= expectedSize : info.size === expectedSize) return;
    } catch (error) {
      if (error?.code !== "ENOENT") throw error;
    }
    await new Promise(resolve => setTimeout(resolve, 5));
  }
  throw new Error("Service upload file did not reach expected size " + expectedSize + ".");
}

async function cutRange(baseUrl, ticket, sourceFile, sourcePart, hash,
                        roomName, levelName, offset, declaredLength, cutBytes) {
  assert.ok(cutBytes > 0 && cutBytes < declaredLength,
    "interruption must end strictly inside the declared PUT body");
  const route = sourceRoute(hash, roomName, levelName, "", offset);
  const { request, response } = openRequest(new URL(route, baseUrl), "PUT", ticket, {
    "Content-Type": "application/octet-stream",
    "Content-Length": String(declaredLength),
  });
  const stream = createReadStream(sourceFile, {
    start: offset,
    end: offset + cutBytes - 1,
    highWaterMark: Math.min(CLIENT_STREAM_BYTES, cutBytes),
  });
  let sentBytes = 0;
  try {
    for await (const block of stream) {
      await writeRequestBlock(request, block);
      sentBytes += block.length;
    }
    assert.equal(sentBytes, cutBytes, "fault injector must write exactly its bounded prefix");
    await waitForFileSize(sourcePart, offset + cutBytes, true);
    stream.destroy();
    request.destroy(new Error("Synthetic source PUT cut before Content-Length."));
    const outcome = await response.then(
      result => ({ result }), error => ({ error }));
    if (outcome.result) {
      throw new Error("Interrupted PUT unexpectedly received HTTP " + outcome.result.status + ".");
    }
    await waitForFileSize(sourcePart, offset);
    return {
      sentBytes,
      clientError: outcome.error?.code || "synthetic-client-destroy",
    };
  } catch (error) {
    stream.destroy();
    request.destroy(error instanceof Error ? error : undefined);
    await response.catch(() => {});
    throw error;
  }
}

async function uploadRange(baseUrl, identity, sourceFile, expectedHash,
                           roomName, levelName, offset, length, metrics) {
  metrics.putRequests++;
  metrics.payloadBytesSent += length;
  const response = await putRange(baseUrl, identity.ticket, sourceFile,
    expectedHash, roomName, levelName, offset, length);
  assert.equal(response.status, 200, "source PUT should be accepted");
  const state = validateState(response.data, expectedHash, metrics.totalBytes);
  assert.equal(state.offset, offset + length, "accepted PUT must advance the server offset");
  return state;
}

async function uploadRemaining(baseUrl, identity, sourceFile, expectedHash,
                               roomName, levelName, state, metrics) {
  while (state.offset < metrics.totalBytes) {
    const length = Math.min(CHUNK_BYTES, metrics.totalBytes - state.offset);
    state = await uploadRange(baseUrl, identity, sourceFile, expectedHash,
      roomName, levelName, state.offset, length, metrics);
    metrics.confirmedBytes = state.offset;
  }
  return state;
}

async function waitForJob(baseUrl, ticket, jobId) {
  const route = "/v1/conversions/" + encodeURIComponent(jobId);
  const deadline = Date.now() + JOB_TIMEOUT_MS;
  let polls = 0;
  while (Date.now() < deadline) {
    polls++;
    const response = await requestJson(baseUrl, route, ticket, "GET");
    assert.equal(response.status, 200, "conversion job should be readable");
    if (response.data.status === "ready") return { job: response.data, polls };
    if (response.data.status === "failed") throw new Error("Stub conversion job failed.");
    await new Promise(resolve => setTimeout(resolve, 25));
  }
  throw new Error("Conversion job did not become ready before the bounded timeout.");
}

async function completeOnceAndVerify(baseUrl, identity, hash, roomName,
                                     levelName, expectedHash, serviceRoot,
                                     converterCounts) {
  const route = sourceRoute(hash, roomName, levelName, "/complete");
  const first = await requestJson(baseUrl, route, identity.ticket, "POST");
  assert.equal(first.status, 202, "complete should queue one conversion job");
  const duplicate = await requestJson(baseUrl, route, identity.ticket, "POST");
  assert.equal(duplicate.status, 202, "duplicate complete should return the existing job");
  assert.equal(duplicate.data.jobId, first.data.jobId,
    "duplicate complete must reuse the same job id");
  const ready = await waitForJob(baseUrl, identity.ticket, first.data.jobId);
  assert.equal(ready.job.status, "ready");
  assert.equal(converterCounts.get(roomName), 1,
    "duplicate complete must invoke the converter stub only once");
  assert.equal(await hashFile(path.join(
    serviceRoot, "jobs", first.data.jobId, "input.pk3")), expectedHash,
  "promoted immutable input must match the source digest");
  return {
    jobId: first.data.jobId,
    jobStatus: ready.job.status,
    duplicateCompleteSameJob: true,
    converterInvocations: converterCounts.get(roomName),
    jobPolls: ready.polls,
  };
}

async function runFullScenario({ baseUrl, serviceRoot, identity, sourceFile,
                                 sourceHash, totalBytes, roomName,
                                 converterCounts, interrupted }) {
  const levelName = "q3dm17";
  const metrics = {
    totalBytes,
    putRequests: 0,
    putRetries: 0,
    recoveryGets: 0,
    payloadBytesSent: 0,
    confirmedBytes: 0,
    completionRequests: 0,
    jobPolls: 0,
    injectedCutBytes: 0,
    cutOffset: null,
    offsetAfterCut: null,
  };
  let state = await reserveUpload(baseUrl, identity.ticket, sourceHash,
    roomName, levelName, totalBytes);
  let injected = false;
  let injectedClientError = null;
  const cutOffset = totalBytes > CHUNK_BYTES ? CHUNK_BYTES : 0;

  while (state.offset < totalBytes) {
    if (interrupted && !injected && state.offset === cutOffset) {
      const declaredLength = Math.min(CHUNK_BYTES, totalBytes - state.offset);
      const cutBytes = Math.min(INTERRUPTED_PREFIX_BYTES,
        Math.max(1, Math.floor(declaredLength / 4)));
      const sourcePart = path.join(serviceRoot, "uploads",
        sessionKey(identity.creatorId, sourceHash, roomName, levelName), "source.part");
      metrics.putRequests++;
      metrics.payloadBytesSent += cutBytes;
      const cut = await cutRange(baseUrl, identity.ticket, sourceFile, sourcePart,
        sourceHash, roomName, levelName, state.offset, declaredLength, cutBytes);
      injectedClientError = cut.clientError;
      metrics.injectedCutBytes = cut.sentBytes;
      metrics.cutOffset = state.offset;
      state = await getUploadState(baseUrl, identity.ticket, sourceHash,
        roomName, levelName, totalBytes);
      metrics.recoveryGets++;
      metrics.offsetAfterCut = state.offset;
      assert.equal(state.offset, cutOffset,
        "an incomplete body must not advance the authoritative upload offset");
      assert.equal((await fs.stat(sourcePart)).size, cutOffset,
        "the service must truncate the incomplete tail while preserving its confirmed prefix");
      metrics.putRetries++;
      injected = true;
      continue;
    }
    const length = Math.min(CHUNK_BYTES, totalBytes - state.offset);
    state = await uploadRange(baseUrl, identity, sourceFile, sourceHash,
      roomName, levelName, state.offset, length, metrics);
    metrics.confirmedBytes = state.offset;
  }
  assert.ok(!interrupted || injected, "interrupted scenario must execute its fault");
  assert.equal(metrics.confirmedBytes, totalBytes);

  metrics.completionRequests = 2;
  const completion = await completeOnceAndVerify(baseUrl, identity, sourceHash,
    roomName, levelName, sourceHash, serviceRoot, converterCounts);
  metrics.jobPolls = completion.jobPolls;
  return {
    metrics,
    job: completion,
    interruption: interrupted ? {
      kind: "mid-body connection destroy",
      offsetBeforeCut: metrics.cutOffset,
      offsetAfterAuthoritativeGet: metrics.offsetAfterCut,
      bytesSentBeforeCut: metrics.injectedCutBytes,
      clientError: injectedClientError,
      sameOffsetChunkRetried: true,
    } : null,
  };
}

async function runCancellationScenario({ baseUrl, serviceRoot, identity, sourceFile,
                                        sourceHash, totalBytes, converterCounts }) {
  const roomName = "Q3_BENCH_CANCEL";
  const levelName = "q3dm17";
  const metrics = {
    totalBytes, putRequests: 0, putRetries: 0, recoveryGets: 0,
    payloadBytesSent: 0, confirmedBytes: 0,
  };
  let state = await reserveUpload(baseUrl, identity.ticket, sourceHash,
    roomName, levelName, totalBytes);
  const confirmedPrefix = Math.min(CHUNK_BYTES, Math.max(1, Math.floor(totalBytes / 2)));
  state = await uploadRange(baseUrl, identity, sourceFile, sourceHash,
    roomName, levelName, state.offset, confirmedPrefix, metrics);
  metrics.confirmedBytes = state.offset;

  const remaining = totalBytes - state.offset;
  const declaredLength = Math.min(CHUNK_BYTES, remaining);
  const cutBytes = Math.min(INTERRUPTED_PREFIX_BYTES,
    Math.max(1, Math.floor(declaredLength / 4)));
  const sourcePart = path.join(serviceRoot, "uploads",
    sessionKey(identity.creatorId, sourceHash, roomName, levelName), "source.part");
  metrics.putRequests++;
  metrics.payloadBytesSent += cutBytes;
  const cut = await cutRange(baseUrl, identity.ticket, sourceFile, sourcePart,
    sourceHash, roomName, levelName, state.offset, declaredLength, cutBytes);
  state = await getUploadState(baseUrl, identity.ticket, sourceHash,
    roomName, levelName, totalBytes);
  metrics.recoveryGets++;
  assert.equal(state.offset, confirmedPrefix,
    "client cancellation must preserve the last fully confirmed prefix");
  const authoritativeOffsetAfterCancel = state.offset;
  assert.equal((await fs.stat(sourcePart)).size, confirmedPrefix,
    "client cancellation must remove only the partial tail");
  metrics.putRetries++;

  state = await uploadRemaining(baseUrl, identity, sourceFile, sourceHash,
    roomName, levelName, state, metrics);
  const completion = await completeOnceAndVerify(baseUrl, identity, sourceHash,
    roomName, levelName, sourceHash, serviceRoot, converterCounts);
  return {
    confirmedPrefixBytes: confirmedPrefix,
    cutBytes: cut.sentBytes,
    authoritativeOffsetAfterCancel,
    resumedAtOffset: confirmedPrefix,
    resumedToCompletion: true,
    job: completion,
    requests: metrics,
  };
}

async function measure(work) {
  const startRssBytes = process.memoryUsage().rss;
  let peakRssBytes = startRssBytes;
  const sampler = setInterval(() => {
    peakRssBytes = Math.max(peakRssBytes, process.memoryUsage().rss);
  }, 20);
  const cpuStart = process.cpuUsage();
  const started = performance.now();
  let result;
  try { result = await work(); }
  finally { clearInterval(sampler); }
  peakRssBytes = Math.max(peakRssBytes, process.memoryUsage().rss);
  const elapsedMs = performance.now() - started;
  const cpu = process.cpuUsage(cpuStart);
  const cpuUserMs = cpu.user / 1000;
  const cpuSystemMs = cpu.system / 1000;
  return {
    ...result,
    measurement: {
      elapsedMs: Number(elapsedMs.toFixed(2)),
      processCpuUserMs: Number(cpuUserMs.toFixed(2)),
      processCpuSystemMs: Number(cpuSystemMs.toFixed(2)),
      processCpuTotalMs: Number((cpuUserMs + cpuSystemMs).toFixed(2)),
      startRssBytes,
      peakRssBytes,
      peakRssGrowthBytes: Math.max(0, peakRssBytes - startRssBytes),
      endRssBytes: process.memoryUsage().rss,
    },
  };
}

async function main() {
  const options = parseArgs(process.argv.slice(2));
  if (options.help) return help();
  const totalBytes = options.mib * MIB;
  if (totalBytes > MAX_INPUT_BYTES) throw new Error("Fixture exceeds the service input limit.");

  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "project-prime-pk3-service-bench-"));
  const serviceRoot = path.join(tempRoot, "service");
  const sourceFile = path.join(tempRoot, "synthetic-source.fixture");
  const ticketOwners = new Map();
  const converterCounts = new Map();
  let api;
  let result;
  try {
    await fs.chmod(tempRoot, 0o700);
    if (options.output) {
      const relative = path.relative(tempRoot, options.output);
      const outputIsInsideTempRoot = relative === "" ||
        (!path.isAbsolute(relative) && relative !== ".." &&
          !relative.startsWith(".." + path.sep));
      if (outputIsInsideTempRoot)
        throw new Error("--output must be outside the private temporary benchmark root.");
    }
    const fixtureStarted = performance.now();
    const sourceHash = await createFixture(sourceFile, totalBytes);
    const fixtureGenerationMs = Number((performance.now() - fixtureStarted).toFixed(2));
    await fs.mkdir(serviceRoot, { mode: 0o700 });

    api = createPk3UploadService({
      root: serviceRoot,
      allowedOrigins: ["https://projectprime.gg"],
      verifyTicket: async ticket => {
        const creatorId = ticketOwners.get(ticket);
        return creatorId ? { creatorId } : null;
      },
      runConverter: async ({ input, output, roomName, signal }) => {
        signal.throwIfAborted();
        converterCounts.set(roomName, (converterCounts.get(roomName) || 0) + 1);
        if (await hashFile(input) !== sourceHash)
          throw new Error("Stub converter received source bytes with the wrong digest.");
        await fs.writeFile(output, STUB_PACKAGE, { mode: 0o600, flag: "wx" });
        return {
          packageHash: createHash("sha256").update(STUB_PACKAGE).digest("hex"),
          packageBytes: STUB_PACKAGE.length,
          contentHash: "c".repeat(64),
          mapId: STUB_MAP_ID,
        };
      },
    });
    await api.init();
    await new Promise((resolve, reject) => {
      const onError = error => {
        api.server.off("listening", onListening);
        reject(error);
      };
      const onListening = () => {
        api.server.off("error", onError);
        resolve();
      };
      api.server.once("error", onError);
      api.server.once("listening", onListening);
      api.server.listen(0, "127.0.0.1");
    });
    const address = api.server.address();
    const baseUrl = `http://127.0.0.1:${address.port}`;
    const issueIdentity = index => {
      const creatorId = `hunter:00000000-0000-4000-8000-${String(index).padStart(12, "0")}`;
      const ticket = `ppm1.${randomBytes(18).toString("base64url")}.${randomBytes(24).toString("base64url")}`;
      ticketOwners.set(ticket, creatorId);
      return { creatorId, ticket };
    };

    const baselineIdentity = issueIdentity(1);
    const interruptedIdentity = issueIdentity(2);
    const cancellationIdentity = issueIdentity(3);
    const baseline = await measure(() => runFullScenario({
      baseUrl, serviceRoot, identity: baselineIdentity, sourceFile,
      sourceHash, totalBytes, roomName: "Q3_BENCH_BASELINE",
      converterCounts, interrupted: false,
    }));
    const interrupted = await measure(() => runFullScenario({
      baseUrl, serviceRoot, identity: interruptedIdentity, sourceFile,
      sourceHash, totalBytes, roomName: "Q3_BENCH_INTERRUPTED",
      converterCounts, interrupted: true,
    }));
    const cancellation = await runCancellationScenario({
      baseUrl, serviceRoot, identity: cancellationIdentity, sourceFile,
      sourceHash, totalBytes, converterCounts,
    });

    result = {
      schemaVersion: 1,
      generatedAt: new Date().toISOString(),
      environment: {
        node: process.version,
        platform: process.platform,
        arch: process.arch,
        cpuModel: os.cpus()[0]?.model || null,
        httpTimeoutMs: REQUEST_TIMEOUT_MS,
        jobTimeoutMs: JOB_TIMEOUT_MS,
        uploadChunkLimitBytes: CHUNK_BYTES,
        fixtureStreamBlockBytes: FIXTURE_BLOCK_BYTES,
        clientStreamHighWaterMarkBytes: CLIENT_STREAM_BYTES,
      },
      fixture: {
        bytes: totalBytes,
        mib: options.mib,
        sha256: sourceHash,
        generatedOnDiskWithoutSparseExtents: true,
        format: "deterministic synthetic bytes; not a valid PK3 archive",
        generationMs: fixtureGenerationMs,
      },
      method: {
        http: "loopback HTTP against createPk3UploadService",
        ticket: "random fake ppm1 token resolved by an in-process owner verifier; no external identity calls",
        converter: "stub verifies the complete source SHA-256 and writes a marker; no PK3 parsing, conversion, or real .ppmap output",
        processMetrics: "CPU and sampled RSS cover this Node process, including harness client, service, and stub worker",
      },
      scenarios: { baseline, interrupted, cancellation },
      limitations: [
        "Synthetic transfer data exercises service admission, streamed PUT storage, recovery, source digest validation, and asynchronous job lifecycle only.",
        "This is not a valid large PK3 and does not measure ZIP validation, real conversion, geometry, game loading, deployment storage, or remote network throughput.",
      ],
    };
  } finally {
    try {
      if (api) await api.close();
    } finally {
      await fs.rm(tempRoot, { recursive: true, force: true });
    }
  }

  const serialized = JSON.stringify(result, null, 2) + "\n";
  if (options.output) {
    await fs.mkdir(path.dirname(options.output), { recursive: true });
    await fs.writeFile(options.output, serialized, { encoding: "utf8", flag: "w" });
    process.stdout.write(JSON.stringify({ output: options.output, mib: options.mib }) + "\n");
  } else {
    process.stdout.write(serialized);
  }
}

main().catch(error => {
  process.stderr.write("PK3 upload service benchmark failed: " +
    (error instanceof Error ? error.message : String(error)) + "\n");
  process.exitCode = 1;
});
