import { createHash } from "node:crypto";

export const MAX_INPUT_BYTES = 128 * 1024 * 1024;
export const MAX_OUTPUT_BYTES = 512 * 1024 * 1024;
export const CHUNK_BYTES = 8 * 1024 * 1024;
export const MAX_UPLOADS_PER_CREATOR = 2;
export const MAX_JOBS_PER_CREATOR = 2;
export const ID_PATTERN = /^[0-9a-f]{64}$/;
export const JOB_PATTERN = /^[a-f0-9-]{36}$/;

const RESERVED_NAME = /^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$/i;
export function validRoomName(value) {
  return typeof value === "string" && value.length >= 1 && value.length <= 40 &&
    /^[A-Za-z0-9][A-Za-z0-9 _-]*$/.test(value) && value.trim() === value &&
    !RESERVED_NAME.test(value);
}
export function validLevelName(value) {
  return value == null || typeof value === "string" && value.length > 0 &&
    value.length <= 80 && /^[A-Za-z0-9_.-]+$/.test(value) &&
    !value.startsWith(".") && value !== "..";
}
export function validHash(value) {
  return typeof value === "string" && ID_PATTERN.test(value);
}
export function validJobId(value) {
  return typeof value === "string" && JOB_PATTERN.test(value);
}
export function validOrigin(origin) {
  if (typeof origin !== "string" || !origin) return false;
  try {
    const url = new URL(origin);
    return (url.protocol === "https:" ||
      (url.protocol === "http:" && ["localhost", "127.0.0.1"].includes(url.hostname))) &&
      url.username === "" && url.password === "" && url.pathname === "/" &&
      url.search === "" && url.hash === "" && url.origin === origin;
  } catch { return false; }
}
export function sessionKey(creatorId, hash) {
  if (!/^hunter:[a-f0-9-]{36}$/i.test(creatorId) || !validHash(hash))
    throw new Error("Invalid immutable upload identity.");
  return createHash("sha256").update(creatorId + "\0" + hash).digest("hex");
}
export function validStart(body, maxBytes = MAX_INPUT_BYTES) {
  if (body == null || typeof body !== "object" || Array.isArray(body) ||
    !Number.isSafeInteger(body.bytes) || body.bytes <= 0 || body.bytes > maxBytes ||
    !validRoomName(body.roomName) || !validLevelName(body.levelName) ||
    body.rightsConfirmed !== true) return false;
  return true;
}
export function canonicalState(value) {
  return value && typeof value === "object" &&
    validHash(value.hash) && /^hunter:[a-f0-9-]{36}$/i.test(value.creatorId) &&
    Number.isSafeInteger(value.bytes) && value.bytes > 0 &&
    Number.isSafeInteger(value.offset) && value.offset >= 0 && value.offset <= value.bytes &&
    validRoomName(value.roomName) && validLevelName(value.levelName);
}
