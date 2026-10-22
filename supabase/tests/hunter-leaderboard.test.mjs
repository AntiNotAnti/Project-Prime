import test from "node:test";
import assert from "node:assert/strict";
import {
  InvalidLeaderboardQuery, metrics, modeNames, publicIdValid,
  parseLeaderboardRequest,
} from "../functions/hunter-leaderboard/contract.ts";

const parse = (query = "") =>
  parseLeaderboardRequest(new URL("https://example.org/hunter-leaderboard?" + query));

test("only whitelisted authoritative career metrics are accepted", () => {
  for (const metric of metrics) assert.equal(parse("metric=" + metric).metric, metric);
  assert.throws(() => parse("metric=rating"), InvalidLeaderboardQuery);
  assert.throws(() => parse("metric=drop%20table"), InvalidLeaderboardQuery);
});

test("mode values follow native GameMode enum numbers, including Insta-Gib", () => {
  assert.equal(modeNames[3], "Battle");
  assert.equal(modeNames[4], "Battle Teams");
  assert.equal(modeNames[15], "Insta-Gib");
  assert.equal(modeNames[19], "Gun Game");
  assert.equal(modeNames[23], "One in the Chamber");
  assert.equal(parse("mode=3").mode, "3");
  assert.throws(() => parse("mode=999"), InvalidLeaderboardQuery);
  assert.throws(() => parse("mode=../private"), InvalidLeaderboardQuery);
});

test("page, search and identity-directory filters are strictly bounded", () => {
  assert.equal(parse().page, 1);
  assert.equal(parse("page=100").pageSize, 25);
  assert.equal(parse("only_public=1").onlyPublic, true);
  assert.equal(parse("only_public=1&page=201").page, 201);
  assert.equal(parse("only_public=1&page=500").page, 500);
  assert.throws(() => parse("only_public=1&page=501"), InvalidLeaderboardQuery);
  assert.throws(() => parse("only_public=1&mode=3&page=101"), InvalidLeaderboardQuery);
  assert.throws(() => parse("page=101"), InvalidLeaderboardQuery);
  assert.throws(() => parse("page=-1"), InvalidLeaderboardQuery);
  assert.throws(() => parse("only_public=maybe"), InvalidLeaderboardQuery);
  assert.throws(() => parse("q=" + "x".repeat(41)), InvalidLeaderboardQuery);
  assert.throws(() => parse("q=" + encodeURIComponent("\n")), InvalidLeaderboardQuery);
});

test("public profile ID shape cannot contain auth UUID or traversal", () => {
  assert.equal(publicIdValid("f".repeat(32)), true);
  assert.equal(publicIdValid("f".repeat(32)+"/../"), false);
  assert.equal(publicIdValid("nothunter"), false);
  assert.throws(() => parse("hunter="+"f".repeat(33)), InvalidLeaderboardQuery);
});
