import http from "node:http";
import path from "node:path";
import { createHash, randomUUID } from "node:crypto";
import { createReadStream } from "node:fs";
import fs from "node:fs/promises";
import {
  CHUNK_BYTES, MAX_INPUT_BYTES, MAX_OUTPUT_BYTES,
  MAX_UPLOADS_PER_CREATOR, MAX_JOBS_PER_CREATOR,
  validHash, validJobId, validOrigin, validStart,
  validRoomName, validLevelName, canonicalState, sessionKey,
} from "./validation.mjs";

const DAY_MS = 24 * 60 * 60 * 1000;
const MAX_DISK_BYTES = 2 * 1024 * 1024 * 1024;
const MAX_JOBS = 16;
const DISK_MARGIN_BYTES = 256 * 1024 * 1024;
const UNSUPPORTED_DIRECTORY_SYNC = new Set(["EINVAL", "ENOTSUP", "EISDIR", "EPERM", "EBADF"]);

async function digest(file) {
  const h = createHash("sha256");
  for await (const chunk of createReadStream(file, { highWaterMark: 1024 * 1024 }))
    h.update(chunk);
  return h.digest("hex");
}
async function readJson(file) {
  try { return JSON.parse(await fs.readFile(file, "utf8")); }
  catch (error) {
    if (error?.code === "ENOENT") return null;
    if (error instanceof SyntaxError) return null;
    throw error;
  }
}
async function syncDirectory(directory) {
  let handle;
  try {
    handle = await fs.open(directory, "r");
    await handle.sync();
  } catch (error) {
    // Directory handles cannot be synced on some supported filesystems.
    if (!UNSUPPORTED_DIRECTORY_SYNC.has(error?.code)) throw error;
  } finally { await handle?.close(); }
}
async function atomicJson(file, value) {
  const temp = file + "." + randomUUID() + ".tmp";
  let handle;
  try {
    handle = await fs.open(temp, "wx", 0o600);
    await handle.writeFile(JSON.stringify(value));
    await handle.sync();
    const completed = handle;
    handle = null;
    await completed.close();
    await fs.rename(temp, file);
    await syncDirectory(path.dirname(file));
  } finally {
    try { await handle?.close(); }
    finally { await fs.rm(temp, { force: true }); }
  }
}
async function files(root) {
  try { return await fs.readdir(root, { withFileTypes: true }); }
  catch (e) { if (e.code === "ENOENT") return []; throw e; }
}
function pendingJobId(name) {
  const prefix = ".pending-";
  if (!name.startsWith(prefix)) return null;
  const id = name.slice(prefix.length);
  return validJobId(id) ? id : null;
}
function validCreatorId(value) {
  return typeof value === "string" && /^hunter:[a-f0-9-]{36}$/i.test(value);
}
function jobSessionKey(job) {
  if (!validCreatorId(job?.creatorId) || !validHash(job?.sourceHash) ||
      !validRoomName(job?.roomName) || !validLevelName(job?.levelName)) return null;
  const key = sessionKey(job.creatorId, job.sourceHash, job.roomName, job.levelName ?? "");
  return job.sessionKey == null || job.sessionKey === key ? key : null;
}
function validJobMetadata(job, id, maxBytes) {
  return !!job && typeof job === "object" && job.id === id && validJobId(id) &&
    jobSessionKey(job) != null && Number.isSafeInteger(job.bytes) &&
    job.bytes > 0 && job.bytes <= maxBytes && Number.isFinite(job.createdAt);
}
async function regularFileStat(file) {
  try {
    const info = await fs.lstat(file);
    return info.isFile() && !info.isSymbolicLink() ? info : null;
  } catch (error) {
    if (error?.code === "ENOENT") return null;
    throw error;
  }
}

export function createPk3UploadService({
  root, allowedOrigins, verifyTicket, runConverter,
  maxBytes = MAX_INPUT_BYTES, maxOutputBytes = MAX_OUTPUT_BYTES,
  now = () => Date.now(),
} = {}) {
  if (!path.isAbsolute(root || "") || !Array.isArray(allowedOrigins) ||
      !allowedOrigins.length || allowedOrigins.some(value => !validOrigin(value)) ||
      typeof verifyTicket !== "function" || typeof runConverter !== "function" ||
      !Number.isInteger(maxBytes) || maxBytes < 1 || maxBytes > MAX_INPUT_BYTES ||
      !Number.isInteger(maxOutputBytes) || maxOutputBytes > MAX_OUTPUT_BYTES) {
    throw new Error("PK3 upload service is not safely configured.");
  }

  const originSet = new Set(allowedOrigins);
  const uploadRoot = path.join(root, "uploads");
  const jobRoot = path.join(root, "jobs");
  const locked = new Set();
  // Serialize global reservations so different creator sessions cannot race
  // past total disk/job quotas during concurrent POST admissions.
  let admissionTail = Promise.resolve();
  async function withAdmission(action) {
    const previous = admissionTail;
    let release;
    admissionTail = new Promise(resolve => { release = resolve; });
    await previous;
    try { return await action(); }
    finally { release(); }
  }
  // Bound polling and admission traffic per verified creator, rather than
  // the reverse-proxy's shared loopback IP.
  const requestWindows = new Map();
  function allowCreatorRequest(creatorId) {
    const current = now();
    let window = requestWindows.get(creatorId);
    if (!window || current - window.start >= 60_000) {
      window = { start: current, count: 0 };
      requestWindows.set(creatorId, window);
    }
    window.count++;
    if (requestWindows.size > 1024) {
      for (const [id, record] of requestWindows)
        if (current - record.start >= 60_000) requestWindows.delete(id);
      if (requestWindows.size > 1024) requestWindows.clear();
    }
    return window.count <= 180;
  }
  let activeJob = null;
  let activeCancellation = null;
  let activeCompletion = Promise.resolve();
  let closing = false;
  let service;
  let cleanupTimer;

  function send(res, status, value, extra = {}) {
    if (res.headersSent) return;
    res.writeHead(status, {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "private, no-store",
      "X-Content-Type-Options": "nosniff",
      ...extra,
    });
    res.end(JSON.stringify(value));
  }
  function statePublic(s) {
    return {
      hash: s.hash, bytes: s.bytes, offset: s.offset,
      chunkBytes: CHUNK_BYTES, complete: !!s.jobId, jobId: s.jobId ?? null,
    };
  }
  function jobPublic(j) {
    return {
      jobId: j.id, status: j.status, stage: j.stage || j.status,
      hash: j.status === "ready" ? j.outputHash : null,
      bytes: j.status === "ready" ? j.outputBytes : null,
      mapId: j.status === "ready" ? j.mapId : null,
      roomName: j.roomName, levelName: j.levelName ?? null,
      warnings: j.status === "ready" ? j.warnings || [] : [],
      reason: j.status === "failed" ? "Conversion failed. Review map format or retry in Studio." : null,
    };
  }
  async function getSession(owner, hash, roomName, levelName) {
    const key = sessionKey(owner, hash, roomName, levelName ?? "");
    const dir = path.join(uploadRoot, key);
    const meta = await readJson(path.join(dir, "session.json"));
    if (!canonicalState(meta) || meta.creatorId !== owner || meta.hash !== hash ||
        meta.roomName !== roomName || (meta.levelName ?? null) !== (levelName ?? null))
      return null;
    return { ...meta, dir, key };
  }
  async function getJob(owner, id) {
    if (!validJobId(id)) return null;
    const dir = path.join(jobRoot, id);
    const job = await readJson(path.join(dir, "job.json"));
    if (!job || job.id !== id || job.creatorId !== owner ||
        !validHash(job.sourceHash)) return null;
    return { ...job, dir };
  }

  async function sessionForJob(job) {
    const key = jobSessionKey(job);
    if (!key) return null;
    const dir = path.join(uploadRoot, key);
    const meta = await readJson(path.join(dir, "session.json"));
    if (!canonicalState(meta) || meta.creatorId !== job.creatorId ||
        meta.hash !== job.sourceHash || meta.bytes !== job.bytes ||
        meta.offset !== meta.bytes || meta.roomName !== job.roomName ||
        (meta.levelName ?? null) !== (job.levelName ?? null) ||
        (meta.jobId != null && meta.jobId !== job.id)) return null;
    return { ...meta, dir, key };
  }

  async function sourceMatchesJob(file, job) {
    const info = await regularFileStat(file);
    return !!info && info.size === job.bytes && await digest(file) === job.sourceHash;
  }

  async function ensureJobInput(folder, job, session) {
    const input = path.join(folder, "input.pk3");
    if (await regularFileStat(input)) return sourceMatchesJob(input, job);
    if (!session) return false;
    const sourcePart = path.join(session.dir, "source.part");
    if (!await sourceMatchesJob(sourcePart, job)) return false;
    await fs.rename(sourcePart, input);
    await syncDirectory(session.dir);
    await syncDirectory(folder);
    return sourceMatchesJob(input, job);
  }

  async function linkJobSession(job) {
    const session = await sessionForJob(job);
    if (!session || session.jobId === job.id) return !!session;
    const { dir: _dir, key: _key, ...saved } = session;
    await atomicJson(path.join(session.dir, "session.json"), {
      ...saved, jobId: job.id, updatedAt: now(),
    });
    return true;
  }

  async function promotePending(directory, id) {
    const job = await readJson(path.join(directory, "job.json"));
    if (!validJobMetadata(job, id, maxBytes) || job.status !== "queued") return false;
    const session = await sessionForJob(job);
    if (!session || (session.jobId != null && session.jobId !== id)) return false;
    if (!await ensureJobInput(directory, job, session)) return false;

    const finalDirectory = path.join(jobRoot, id);
    try { await fs.lstat(finalDirectory); return false; }
    catch (error) { if (error?.code !== "ENOENT") throw error; }
    await fs.rename(directory, finalDirectory);
    await syncDirectory(jobRoot);
    const finalJob = await readJson(path.join(finalDirectory, "job.json"));
    if (!validJobMetadata(finalJob, id, maxBytes)) return false;
    return linkJobSession(finalJob);
  }

  async function recoverLegacyJob(directory, id) {
    const manifest = path.join(directory, "job.json");
    if (await regularFileStat(manifest)) return false;
    const input = path.join(directory, "input.pk3");
    const inputInfo = await regularFileStat(input);
    if (!inputInfo || inputInfo.size < 1 || inputInfo.size > maxBytes) return false;
    const sourceHash = await digest(input);
    const matches = [];
    for (const item of await files(uploadRoot)) {
      if (!item.isDirectory() || !validHash(item.name)) continue;
      const sessionFile = path.join(uploadRoot, item.name, "session.json");
      const session = await readJson(sessionFile);
      if (!canonicalState(session) || session.jobId != null ||
          session.hash !== sourceHash || session.bytes !== inputInfo.size ||
          session.offset !== session.bytes ||
          sessionKey(session.creatorId, session.hash, session.roomName, session.levelName ?? "") !== item.name ||
          await regularFileStat(path.join(uploadRoot, item.name, "source.part"))) continue;
      matches.push({ session, dir: path.join(uploadRoot, item.name), key: item.name });
      if (matches.length > 1) return false;
    }
    if (matches.length !== 1) return false;
    const { session, key } = matches[0];
    const job = {
      id, creatorId: session.creatorId, sourceHash, bytes: inputInfo.size,
      roomName: session.roomName, levelName: session.levelName ?? null,
      sessionKey: key, createdAt: Number.isFinite(session.createdAt) ? session.createdAt : now(),
      updatedAt: now(), status: "queued", stage: "Recovered after interruption",
    };
    await atomicJson(manifest, job);
    return linkJobSession(job);
  }

  async function recoverJobs() {
    for (const item of await files(jobRoot)) {
      const id = pendingJobId(item.name);
      if (!item.isDirectory() || !id) continue;
      try { await promotePending(path.join(jobRoot, item.name), id); }
      catch (error) {
        console.error("PK3 staged job recovery deferred", id, error?.name || "unknown");
      }
    }

    for (const item of await files(jobRoot)) {
      if (!item.isDirectory() || !validJobId(item.name)) continue;
      try {
        const directory = path.join(jobRoot, item.name);
        let job = await readJson(path.join(directory, "job.json"));
        if (!job) {
          await recoverLegacyJob(directory, item.name);
          job = await readJson(path.join(directory, "job.json"));
        }
        if (!validJobMetadata(job, item.name, maxBytes) || job.status !== "queued") continue;
        const session = await sessionForJob(job);
        if (session?.jobId === job.id) continue;
        if (!session || !await ensureJobInput(directory, job, session)) continue;
        await linkJobSession(job);
      } catch (error) {
        console.error("PK3 queued job recovery deferred", item.name, error?.name || "unknown");
      }
    }
  }

  async function startJobQueue() {
    if (activeJob || closing) return;
    const candidates = [];
    for (const entry of await files(jobRoot)) {
      if (!entry.isDirectory() || !validJobId(entry.name)) continue;
      const j = await readJson(path.join(jobRoot, entry.name, "job.json"));
      if (!validJobMetadata(j, entry.name, maxBytes) || j.status !== "queued") continue;
      const session = await sessionForJob(j);
      if (session?.jobId === j.id) candidates.push(j);
    }
    candidates.sort((a, b) => a.createdAt - b.createdAt);
    const job = candidates[0];
    if (!job || activeJob || closing) return;
    activeJob = job.id;
    activeCancellation = new AbortController();
    let complete;
    activeCompletion = new Promise(resolve => { complete = resolve; });
    const folder = path.join(jobRoot, job.id);
    const input = path.join(folder, "input.pk3");
    const output = path.join(folder, "output.ppmap");
    const saved = { ...job, status: "running", stage: "Converting BSP and textures" };
    try {
      if (!await sourceMatchesJob(input, job))
        throw new Error("Worker input did not match its validated source manifest.");
      await atomicJson(path.join(folder, "job.json"), saved);
      const result = await runConverter({
        input, output, roomName: job.roomName,
        levelName: job.levelName || null, cwd: folder,
        signal: activeCancellation.signal,
      });
      const size = (await fs.stat(output)).size;
      if (size < 1 || size > maxOutputBytes || !validHash(result.packageHash) ||
          !/^[a-f0-9-]{36}$/i.test(result.mapId) ||
          !validHash(result.contentHash) || result.packageBytes !== size ||
          await digest(output) !== result.packageHash) {
        throw new Error("Worker output did not match its validated package report.");
      }
      await atomicJson(path.join(folder, "job.json"), {
        ...saved, status: "ready", stage: "Portable .ppmap verified",
        outputHash: result.packageHash, outputBytes: size, mapId: result.mapId,
        warnings: Array.isArray(result.warnings)
          ? result.warnings.filter(x => typeof x === "string")
              .slice(0, 32).map(x => x.slice(0, 250)) : [],
        finishedAt: now(),
      });
    } catch (error) {
      // Preserve no worker-internal path/stack traces in public job state.
      if (!closing) console.error("PK3 conversion job failed", job.id, error?.name || "unknown");
      await fs.rm(output, { force: true });
      await atomicJson(path.join(folder, "job.json"), {
        ...saved, status: closing ? "queued" : "failed",
        stage: closing ? "Interrupted by service shutdown" : "Conversion failed", finishedAt: now(),
      });
    } finally {
      activeJob = null;
      activeCancellation = null;
      complete();
      if (!closing) setImmediate(() => void startJobQueue().catch(console.error));
    }
  }

  async function capacity(creatorId, requestedBytes, forConversion = false) {
    let pending = 0, count = 0;
    const jobSourceSessions = new Set();
    let jobs = 0, perCreator = 0, used = 0;
    for (const item of await files(jobRoot)) {
      if (!item.isDirectory()) continue;
      const stagedId = pendingJobId(item.name);
      const id = stagedId || (validJobId(item.name) ? item.name : null);
      if (!id) continue;
      const directory = path.join(jobRoot, item.name);
      const job = await readJson(path.join(directory, "job.json"));
      const valid = validJobMetadata(job, id, maxBytes);
      const inputInfo = await regularFileStat(path.join(directory, "input.pk3"));
      jobs++;
      if (valid && job.creatorId === creatorId) perCreator++;
      else if (!valid && job && validCreatorId(job.creatorId) && job.creatorId === creatorId)
        perCreator++;
      if (valid) {
        const session = await sessionForJob(job);
        const key = jobSessionKey(job);
        if (inputInfo) {
          used += Math.max(job.bytes, inputInfo.size) +
            (Number.isSafeInteger(job.outputBytes) && job.outputBytes > 0
              ? Math.min(job.outputBytes, maxOutputBytes) : 0);
          if (session && key) jobSourceSessions.add(key);
        } else if (!session || !await regularFileStat(path.join(session.dir, "source.part"))) {
          used += job.bytes;
        }
      } else if (inputInfo) {
        used += inputInfo.size;
      }
    }

    for (const item of await files(uploadRoot)) {
      if (!item.isDirectory() || !validHash(item.name)) continue;
      const s = await readJson(path.join(uploadRoot, item.name, "session.json"));
      if (s && !s.jobId && now() - s.updatedAt < DAY_MS &&
          !jobSourceSessions.has(item.name)) {
        pending += s.bytes; if (s.creatorId === creatorId) count++;
      }
    }
    used += pending;
    if ((!forConversion && count >= MAX_UPLOADS_PER_CREATOR) ||
        perCreator >= MAX_JOBS_PER_CREATOR ||
        jobs >= MAX_JOBS || used + requestedBytes > MAX_DISK_BYTES)
      return false;
    if (typeof fs.statfs === "function") {
      const stat = await fs.statfs(root);
      if (Number(stat.bavail) * Number(stat.bsize) <
          requestedBytes + DISK_MARGIN_BYTES) return false;
    }
    return true;
  }
  async function cleanup() {
    const cutoff = now() - DAY_MS;
    const preservedSessions = new Set();
    const jobEntries = await files(jobRoot);
    for (const item of jobEntries) {
      if (!item.isDirectory()) continue;
      const id = pendingJobId(item.name) || (validJobId(item.name) ? item.name : null);
      if (!id) continue;
      const directory = path.join(jobRoot, item.name);
      const job = await readJson(path.join(directory, "job.json"));
      if (job && jobSessionKey(job) && await regularFileStat(path.join(directory, "input.pk3")))
        preservedSessions.add(jobSessionKey(job));
    }
    for (const item of await files(uploadRoot)) {
      if (!item.isDirectory() || !validHash(item.name) || locked.has(item.name)) continue;
      const dir = path.join(uploadRoot, item.name);
      const info = await readJson(path.join(dir, "session.json"));
      if ((!info || !info.jobId) && preservedSessions.has(item.name)) continue;
      if (!info || Number(info.updatedAt || info.createdAt) < cutoff)
        await fs.rm(dir, { recursive: true, force: true });
    }
    for (const item of jobEntries) {
      if (!item.isDirectory()) continue;
      const pendingId = pendingJobId(item.name);
      const id = pendingId || (validJobId(item.name) ? item.name : null);
      if (!id || id === activeJob) continue;
      const dir = path.join(jobRoot, item.name);
      const info = await readJson(path.join(dir, "job.json"));
      if (pendingId) {
        const key = info && jobSessionKey(info);
        if ((key && locked.has(key)) || await regularFileStat(path.join(dir, "input.pk3"))) continue;
        if (!info || Number(info.updatedAt || info.createdAt) < cutoff)
          await fs.rm(dir, { recursive: true, force: true });
        continue;
      }
      if (!info && await regularFileStat(path.join(dir, "input.pk3"))) continue;
      if (!info || Number(info.updatedAt || info.finishedAt || info.createdAt) < cutoff)
        await fs.rm(dir, { recursive: true, force: true });
    }
  }
  async function init() {
    await fs.mkdir(root, { recursive: true, mode: 0o700 });
    const info = await fs.lstat(root);
    if (!info.isDirectory() || info.isSymbolicLink())
      throw new Error("PK3 service data root must be a private directory.");
    await fs.mkdir(uploadRoot, { recursive: true, mode: 0o700 });
    await fs.mkdir(jobRoot, { recursive: true, mode: 0o700 });
    await recoverJobs();
    // Jobs interrupted by a service restart may be replayed from the same
    // immutable hashed source, without accepting duplicate source uploads.
    for (const item of await files(jobRoot)) {
      if (!item.isDirectory() || !validJobId(item.name)) continue;
      const p = path.join(jobRoot, item.name, "job.json");
      const job = await readJson(p);
      if (job?.status === "running") {
        await fs.rm(path.join(jobRoot, item.name, "output.ppmap"), { force: true });
        await atomicJson(p, { ...job, status: "queued", stage: "Restarted after interruption" });
      }
    }
    await cleanup();
    cleanupTimer = setInterval(() => void cleanup().catch(console.error), 30 * 60 * 1000);
    cleanupTimer.unref();
    setImmediate(() => void startJobQueue().catch(console.error));
  }

  service = http.createServer(async (req, res) => {
    const origin = req.headers.origin;
    if (origin) {
      if (!originSet.has(origin)) return send(res, 403, { error: "origin_denied" });
      res.setHeader("Access-Control-Allow-Origin", origin);
      res.setHeader("Vary", "Origin");
      res.setHeader("Access-Control-Expose-Headers",
        "X-Prime-SHA256, Content-Disposition, Retry-After");
    }
    if (req.method === "OPTIONS") {
      if (!origin || !originSet.has(origin))
        return send(res, 403, { error: "origin_denied" });
      res.writeHead(204, {
        "Access-Control-Allow-Methods": "GET, POST, PUT, OPTIONS",
        "Access-Control-Allow-Headers": "Authorization, Content-Type",
        "Access-Control-Max-Age": "600",
      });
      return res.end();
    }
    if (req.method === "GET" && req.url === "/health")
      return send(res, 200, { ok: true });

    let identity;
    try {
      if (typeof req.headers.authorization !== "string" ||
          !/^Bearer ppm1\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/.test(req.headers.authorization) ||
          req.headers.authorization.length > 3100) {
        return send(res, 401, { error: "hunter_license_required" });
      }
      identity = await verifyTicket(req.headers.authorization.slice(7));
      if (!identity || !/^hunter:[a-f0-9-]{36}$/i.test(identity.creatorId))
        return send(res, 401, { error: "hunter_license_invalid" });
      if (!allowCreatorRequest(identity.creatorId))
        return send(res, 429, { error: "creator_request_rate_limited" },
          { "Retry-After": "30" });
    } catch {
      return send(res, 503, { error: "identity_service_unavailable" });
    }

    try {
      const url = new URL(req.url || "/", "http://localhost");
      const owner = identity.creatorId;
      const source = /^\/v1\/sources\/([0-9a-f]{64})(?:\/(complete))?$/.exec(url.pathname);
      const job = /^\/v1\/conversions\/([0-9a-f-]{36})(?:\/(package))?$/.exec(url.pathname);
      if (source) {
        const hash = source[1], operation = source[2];
        // A single PK3 can carry many playable BSPs. Scope every upload
        // session to source + selected level + destination runtime name.
        const variantRoom = url.searchParams.get("roomName");
        const variantLevel = url.searchParams.get("levelName");
        if (!validRoomName(variantRoom) || !validLevelName(variantLevel))
          return send(res, 400, { error: "invalid_conversion_variant" });
        const key = sessionKey(owner, hash, variantRoom, variantLevel ?? "");
        const dir = path.join(uploadRoot, key);
        if (req.method === "GET" && !operation) {
          const s = await getSession(owner, hash, variantRoom, variantLevel);
          return s ? send(res, 200, statePublic(s)) :
            send(res, 404, { error: "source_not_found" });
        }
        if (locked.has(key)) return send(res, 409, { error: "upload_busy" });
        locked.add(key);
        try {
          if (req.method === "POST" && !operation) {
            let body = "";
            for await (const block of req) {
              body += block.toString("utf8");
              if (body.length > 4096) return send(res, 413, { error: "metadata_too_large" });
            }
            let parsed;
            try { parsed = JSON.parse(body); }
            catch { return send(res, 400, { error: "invalid_json" }); }
            if (!validStart(parsed, maxBytes) ||
                parsed.roomName !== variantRoom ||
                (parsed.levelName ?? null) !== variantLevel)
              return send(res, 400, { error: "invalid_source_metadata" });
            return await withAdmission(async () => {
            let state = await getSession(owner, hash, variantRoom, variantLevel);
            if (state) {
              if (state.bytes !== parsed.bytes || state.roomName !== parsed.roomName ||
                  state.levelName !== (parsed.levelName ?? null))
                return send(res, 409, { error: "source_metadata_changed" });
              return send(res, 200, statePublic(state));
            }
            if (!await capacity(owner, parsed.bytes))
              return send(res, 507, { error: "creator_quota_or_storage_exceeded" });
            await fs.mkdir(dir, { recursive: true, mode: 0o700 });
            const meta = {
              hash, creatorId: owner, bytes: parsed.bytes, offset: 0,
              roomName: parsed.roomName, levelName: parsed.levelName ?? null,
              createdAt: now(), updatedAt: now(), jobId: null,
            };
            const handle = await fs.open(path.join(dir, "source.part"), "wx", 0o600);
            await handle.close();
            await atomicJson(path.join(dir, "session.json"), meta);
            return send(res, 201, statePublic(meta));
            });
          }
          let state = await getSession(owner, hash, variantRoom, variantLevel);
          if (!state) return send(res, 404, { error: "source_not_found" });
          if (req.method === "PUT" && !operation) {
            if (state.jobId) return send(res, 409, { error: "source_already_converted" });
            const offset = url.searchParams.get("offset");
            const length = Number(req.headers["content-length"]);
            if (!/^(0|[1-9]\d*)$/.test(offset || "") ||
                Number(offset) !== state.offset ||
                !Number.isSafeInteger(length) || length < 1 ||
                length > CHUNK_BYTES || length > state.bytes - state.offset)
              return send(res, 409, { error: "invalid_upload_range", offset: state.offset });
            const data = await fs.open(path.join(dir, "source.part"), "r+");
            let written = 0;
            try {
              for await (const chunk of req) {
                written += chunk.length;
                if (written > length) throw new Error("Body exceeds declared upload length.");
                let position = 0;
                while (position < chunk.length) {
                  const part = await data.write(chunk, position, chunk.length - position,
                    state.offset + written - chunk.length + position);
                  if (part.bytesWritten <= 0) throw new Error("Upload short write.");
                  position += part.bytesWritten;
                }
              }
              if (written !== length) throw new Error("Upload incomplete.");
              await data.sync();
              state.offset += written;
              state.updatedAt = now();
              await atomicJson(path.join(dir, "session.json"), state);
            } catch {
              await data.truncate(state.offset);
              return send(res, 400, { error: "upload_body_incomplete" });
            } finally { await data.close(); }
            return send(res, 200, statePublic(state));
          }
          if (req.method === "POST" && operation === "complete") {
            return await withAdmission(async () => {
            await recoverJobs();
            state = await getSession(owner, hash, variantRoom, variantLevel);
            if (!state) return send(res, 404, { error: "source_not_found" });
            if (state.jobId) {
              setImmediate(() => void startJobQueue().catch(console.error));
              return send(res, 202, { jobId: state.jobId });
            }
            if (state.offset !== state.bytes) return send(res, 409,
              { error: "upload_incomplete", offset: state.offset });
            if (await digest(path.join(dir, "source.part")) !== hash)
              return send(res, 422, { error: "source_sha256_mismatch" });
            if (!await capacity(owner, 0, true))
              return send(res, 507, { error: "conversion_queue_full" });
            const id = randomUUID();
            const pendingFolder = path.join(jobRoot, ".pending-" + id);
            const folder = path.join(jobRoot, id);
            const jobMeta = {
              id, creatorId: owner, sourceHash: hash, bytes: state.bytes,
              roomName: state.roomName, levelName: state.levelName,
              sessionKey: key,
              createdAt: now(), updatedAt: now(), status: "queued", stage: "Queued",
            };
            await fs.mkdir(pendingFolder, { mode: 0o700 });
            await syncDirectory(jobRoot);
            await atomicJson(path.join(pendingFolder, "job.json"), jobMeta);
            await fs.rename(path.join(dir, "source.part"), path.join(pendingFolder, "input.pk3"));
            await syncDirectory(dir);
            await syncDirectory(pendingFolder);
            await fs.rename(pendingFolder, folder);
            await syncDirectory(jobRoot);
            if (!await linkJobSession(jobMeta))
              throw new Error("Staged conversion no longer matches its upload session.");
            setImmediate(() => void startJobQueue().catch(console.error));
            return send(res, 202, { jobId: id });
            });
          }
        } finally { locked.delete(key); }
      }
      if (job && req.method === "GET") {
        const data = await getJob(owner, job[1]);
        if (!data) return send(res, 404, { error: "conversion_not_found" });
        if (!job[2]) return send(res, 200, jobPublic(data));
        if (data.status !== "ready" || !validHash(data.outputHash))
          return send(res, 409, { error: "conversion_not_ready" });
        const file = path.join(data.dir, "output.ppmap");
        const length = (await fs.stat(file)).size;
        if (length !== data.outputBytes || length < 1 || length > maxOutputBytes)
          return send(res, 409, { error: "conversion_output_invalid" });
        res.writeHead(200, {
          "Content-Type": "application/octet-stream",
          "Cache-Control": "private, no-store",
          "Content-Length": String(length),
          "Content-Disposition": 'attachment; filename="' + data.outputHash + '.ppmap"',
          "X-Prime-SHA256": data.outputHash,
          "X-Content-Type-Options": "nosniff",
        });
        const stream = createReadStream(file, { highWaterMark: 256 * 1024 });
        stream.on("error", () => res.destroy());
        return stream.pipe(res);
      }
      return send(res, 404, { error: "route_not_found" });
    } catch (error) {
      console.error("PK3 API request error", error?.name || "unknown");
      if (!res.headersSent) send(res, 500, { error: "pk3_service_error" });
      else res.destroy();
    }
  });

  return {
    server: service,
    init,
    async close() {
      closing = true;
      clearInterval(cleanupTimer);
      activeCancellation?.abort(new Error("Conversion service is shutting down."));
      await new Promise(resolve => service.close(() => resolve()));
      await activeCompletion;
    },
  };
}
