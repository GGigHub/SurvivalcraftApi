# Project Status

- Status: M13 COMPLETE
- Updated: 2026-08-09 Asia/Shanghai
- Project path: `C:\Users\Hao\Documents\New project 2\worktrees\SurvivalcraftApi-MPCore`
- Branch or Worktree: `codex/api-mp-integration`

## Goal

Define safe third-party API mod multiplayer scopes, negotiate only mutually available mod channels per session, enforce that negotiated boundary on every send and receive path, and prove Required/Optional/LocalOnly behavior through replayable background-only Direct and Relay automation.

## Completed

- Wire revision 16 adds per-channel application-reliable Data/ACK frames for accepted sessions. Pending and receive windows are bounded to 4096 deliveries and 64 MiB, retry from 0.25 to 2 seconds, time out after 30 seconds, preserve ordered delivery, suppress duplicate effects, and disconnect fail closed on malformed frames or exhausted delivery.
- High-rate replaceable streams remain outside the acknowledgement layer; their newer revision or full baseline heals loss. Stable mutations retain their existing server-side request/event identities and idempotency checks. Missing-entity recovery requests now use bounded exponential backoff to avoid reliability feedback loops.
- Packet ingress enforces 16 MiB compressed and 64 MiB decompressed ceilings before allocation or dispatch. The reliability envelope and probe use packet IDs 123 and 124.
- TestAutomation startup requires both `--test-automation` and a random `--control-pipe`. All game, relay, and host validation remains headless with `CreateNoWindow=true`.
- Automation API 15 and Snapshot schema 4 expose reliable created/retried/acknowledged/duplicate/timed-out/failed counts plus pending deliveries and bytes.
- `GuaranteeReliableLoss` sends a classified production-path probe and deterministically drops a reliable Data frame, so every loss iteration must prove a real retry instead of passing by probability. Soak evidence fails on missing loss/retry/acknowledgement, any timeout/failure, non-empty final reliability windows, production-path gaps, or resource-budget violations.
- The weekly workflow and local soak runner rotate all twelve core scenarios.
- Wire revision 17 requires `ScopedModManifest`. Undeclared legacy mods remain `Required`; `Optional` mods may be absent from either peer; `LocalOnly` mods stay out of the wire manifest and cannot register network channels. Common copies remain exact on scope, version, API target, direction, channel version, and payload limit.
- The server now computes an exact per-session channel intersection and returns it to the client. The client accepts only a canonical subset of its local catalog, while send-to-server, send-to-client, filtered broadcast, and receive dispatch all enforce the negotiated session set.
- Unnegotiated raw traffic fails closed with `UnnegotiatedChannel` after consuming bounded per-peer rate-limit and deduplication state. Session and project cleanup clear the appropriate channel and peer state.
- Automation API 16 and Snapshot schema 5 expose per-session negotiated channel counts and a classified raw-envelope probe. Direct, Relay, and four-client mixed sessions cover both one-sided Optional directions, normal API rejection, filtered broadcast, receiver-side isolation, reconnect, and cleanup.

## Current state

- Coverage Contract remains 22/22 core headless multiplayer features `covered`.
- M13 is complete on `codex/api-mp-integration`; M12 loss-convergence evidence remains the underlying reliability baseline.
- Each accepted session retains its own canonical negotiated mod-channel set and fingerprint; a peer missing an Optional mod receives no traffic for that mod.
- Production-surface inventory contains 103 network hooks and 113 packet classes.

## Next action

- Start M14 with representative external sample mods and release-reality validation across separate machines/public relay/NAT, then add player-facing compatibility diagnostics where automation evidence identifies ambiguity.

## Verification

- Final fast gate: platform L0/L1 128/128 and game L0/L1 127/127 passed; targeted mod-network runtime tests passed 14/14.
- Direct compatibility matrix passed all 11 rejection/timeout cases plus server-only and client-only Optional isolation in `TestResults/m13-final-direct`.
- Relay mod-channel matrix passed 21/21 steps, including both Optional directions, duplicate isolation, handler failure isolation, and reconnect in `TestResults/m13-final-relay`.
- Four-client mixed sessions passed in Direct and Relay: three negotiated recipients received the filtered broadcast, the fourth zero-channel client received none, raw traffic failed closed in both directions, and each run observed the expected 32 rate-limited messages. Evidence is in `TestResults/m13-mixed-session-final`.
- The exact shared-channel Direct core scenario passed message delivery, deduplication, handler isolation, reconnect, and world reload in `TestResults/m13-final-core`.
- Coverage trust passed with 22/22 covered features, 103 hooks, 113 packets, and zero open features in `TestResults/coverage-trust-20260809-202448.json`.
- Relay loss soak: seed `20260809`, twelve of twelve scenarios passed in `TestResults/m12-loss-full-relay-final-v5`.
- Direct loss soak: seed `20260809`, twelve of twelve scenarios passed in `TestResults/m12-loss-full-direct-final-v3`.
- Every loss iteration observed at least three application-reliable retries. Both transports ended with zero reliability failures, zero timeouts, zero pending deliveries/bytes, zero evidence failures, and zero resource-budget violations.
- `git diff --check` passed. No Survivalcraft, RelayServer, or TestHost process remained after validation.

## Changes and artifacts

- Runtime: scoped mod manifests, exact per-session channel negotiation, all-path send/receive isolation, filtered broadcast, cleanup, and fail-closed raw ingress.
- Automation: API/schema upgrade, negotiated-count snapshots, raw-envelope probes, symmetric Optional matrices, and mixed four-client session assertions.
- Tests and docs: protocol/codec/security regressions, real-process Direct/Relay evidence, `NetworkScope` author guidance, and updated automation contracts.
- Primary M13 evidence: `TestResults/m13-final-direct`, `TestResults/m13-final-relay`, `TestResults/m13-mixed-session-final`, and `TestResults/m13-final-core`.

## Decisions, assumptions, and blockers

- M12 “complete” means the 22-feature core headless protocol contract is automatically playable and convergent in real local Direct and Relay processes under deterministic 1–3% application-layer loss.
- M13 completion means missing Optional mods are admitted without exposing their handlers or traffic, common Optional mods remain contract-exact, Required mismatches reject, LocalOnly mods cannot enter the wire contract, and all boundaries have replayable Direct and Relay evidence.
- Pixel appearance, interaction feel, arbitrary third-party mod correctness, real公网 NAT/router behavior, and cross-device graphics drivers remain release-reality work rather than claims of M13.
- Every real game-process verification in this milestone ran headless with hidden windows; same-process mod code is still trusted application code rather than a sandbox boundary.
- No blocker is currently known.
