# Project Status

- Status: M12 COMPLETE
- Updated: 2026-08-09 Asia/Shanghai
- Project path: `C:\Users\Hao\Documents\New project 2\worktrees\SurvivalcraftApi-MPCore`
- Branch or Worktree: `codex/api-mp-integration`

## Goal

Make every core multiplayer mutation converge under deterministic packet loss through acknowledged retries and server-side idempotency, harden packet ingress and decompression limits, and prove all twelve Direct and Relay scenarios through replayable background-only automation.

## Completed

- Wire revision 16 adds per-channel application-reliable Data/ACK frames for accepted sessions. Pending and receive windows are bounded to 4096 deliveries and 64 MiB, retry from 0.25 to 2 seconds, time out after 30 seconds, preserve ordered delivery, suppress duplicate effects, and disconnect fail closed on malformed frames or exhausted delivery.
- High-rate replaceable streams remain outside the acknowledgement layer; their newer revision or full baseline heals loss. Stable mutations retain their existing server-side request/event identities and idempotency checks. Missing-entity recovery requests now use bounded exponential backoff to avoid reliability feedback loops.
- Packet ingress enforces 16 MiB compressed and 64 MiB decompressed ceilings before allocation or dispatch. The reliability envelope and probe use packet IDs 123 and 124.
- TestAutomation startup requires both `--test-automation` and a random `--control-pipe`. All game, relay, and host validation remains headless with `CreateNoWindow=true`.
- Automation API 15 and Snapshot schema 4 expose reliable created/retried/acknowledged/duplicate/timed-out/failed counts plus pending deliveries and bytes.
- `GuaranteeReliableLoss` sends a classified production-path probe and deterministically drops a reliable Data frame, so every loss iteration must prove a real retry instead of passing by probability. Soak evidence fails on missing loss/retry/acknowledgement, any timeout/failure, non-empty final reliability windows, production-path gaps, or resource-budget violations.
- The weekly workflow and local soak runner rotate all twelve core scenarios.

## Current state

- Coverage Contract remains 22/22 core headless multiplayer features `covered`.
- The M12 feature set and evidence are complete on `codex/api-mp-integration`; the branch is ready for the next milestone.
- Production-surface inventory contains 103 network hooks and 113 packet classes.

## Next action

- Start M13 from this branch after choosing the next bounded slice. Candidate work is release/device reality coverage:公网 relay/NAT, multi-machine latency, graphics/input smoke, and third-party mod compatibility matrices.

## Verification

- Final fast gate: platform L0/L1 122/122 and game L0/L1 122/122 passed.
- Relay loss soak: seed `20260809`, twelve of twelve scenarios passed in `TestResults/m12-loss-full-relay-final-v5`.
- Direct loss soak: seed `20260809`, twelve of twelve scenarios passed in `TestResults/m12-loss-full-direct-final-v3`.
- Every loss iteration observed at least three application-reliable retries. Both transports ended with zero reliability failures, zero timeouts, zero pending deliveries/bytes, zero evidence failures, and zero resource-budget violations.
- Targeted Relay strict replays passed for survival state, guaranteed-loss motion, lifecycle recovery throttling, and reliable terminal health state.
- `git diff --check` passed. No Survivalcraft, RelayServer, or TestHost process remained after validation.

## Changes and artifacts

- Runtime: application reliability, four Direct/Relay transport paths, packet ingress bounds, lifecycle recovery throttling, and observability.
- Automation: API/schema upgrade, classified reliability probe, deterministic loss scheduling, twelve-scenario soak evidence, resource gates, and background-only startup guards.
- Tests: reliability unit tests, policy/ingress/lifecycle regressions, protocol/coverage updates, strict replays, and full Direct/Relay loss rotations.
- Primary evidence: `TestResults/m12-loss-full-relay-final-v5/soak-summary.json` and `TestResults/m12-loss-full-direct-final-v3/soak-summary.json`.

## Decisions, assumptions, and blockers

- “Complete” means the 22-feature core headless protocol contract is automatically playable and convergent in real local Direct and Relay processes under deterministic 1–3% application-layer loss.
- Pixel appearance, interaction feel, arbitrary third-party mod correctness, real公网 NAT/router behavior, and cross-device graphics drivers remain release-reality work rather than claims of M12.
- No blocker is currently known.
