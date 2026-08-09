# Project Status

- Status: ACTIVE
- Updated: 2026-08-09 Asia/Shanghai
- Project path: `C:\Users\Hao\Documents\New project 2\worktrees\SurvivalcraftApi-MPCore`
- Branch or Worktree: `codex/api-mp-integration`

## Goal

Complete M12 by making every core multiplayer mutation converge under deterministic packet loss through acknowledged retries and server-side idempotency, hardening packet ingress and decompression limits, and proving all twelve Direct and Relay scenarios through replayable background-only automation.

## Completed

- M11 reports all 22 core headless multiplayer features as covered.
- Direct, Relay, strict replay, and the default no-loss soak rotation passed at the M11 baseline.
- Packet compression and decompression now enforce 16 MiB compressed and 64 MiB decompressed ceilings with L0 regression tests.
- TestAutomation now requires both `--test-automation` and a random `--control-pipe`.
- The weekly workflow now executes all twelve soak scenarios.

## Current state

- The worktree is clean at `5566d870` and tracks `origin/codex/api-mp-integration`.
- Duplicate and reorder handling is covered, but not every typed mutation has application-level acknowledgement and retry semantics.
- The M12 foundation fast gate passes 120/120 platform tests and 109/109 game tests.

## Next action

- Implement the bounded application-reliable frame, acknowledgement, retry, and per-session receive window.

## Verification

- `inspect_project.py`: clean branch at `5566d870`.
- `scripts/Test-Multiplayer.ps1 -Mode fast`: platform 120/120 and game 109/109 passed.

## Changes and artifacts

- Modified: packet serializer, TestAutomation startup, game-node launcher, weekly workflow, testing documentation, and `STATUS.md`.
- Untracked: `STATUS.md`, `Survivalcraft.Test/Tests/PacketCompressionBoundsTest.cs`.
- Deliverables: none yet.

## Decisions, assumptions, and blockers

- All game-process validation remains headless with no foreground game window.
- Production UI and pixel-level reality testing remain outside M12.
- No blocker is currently known.
