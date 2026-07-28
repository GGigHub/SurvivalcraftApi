# API Multiplayer Integration

This branch is the long-lived integration line for combining the Survivalcraft API/mod runtime with the multiplayer runtime. It deliberately keeps upstream integration, protocol changes, and scenario coverage in reviewable commits.

## Baselines

- Common ancestor: `API_1.9.2.1` (`675efcde9459121c5374ee82f88d680efdb943e4`).
- Multiplayer baseline: `SCAPI1.9_MP` plus the deterministic multiplayer test host (`6980b22233775aa2277ae038e662769eaee4ce55`).
- API baseline: `SCAPI1.9` (`027b6b2fd2b761c75ee80d63bb07923cffebff70`, API 1.9.3).
- Integration branch: `codex/api-mp-integration`.

At the integration point, the API line contained 99 commits after the common ancestor and the multiplayer line contained 149. The API line changed 114 files, the multiplayer line changed 618, and only 38 paths overlapped. This makes a staged integration practical: most work is independent, while the small overlapping surface can be resolved and tested explicitly.

## Merge policy

The first merge preserves multiplayer authority, prediction, player/session flow, and dynamic clothing behavior while adopting the API line's loader lifecycle, mod settings, rendering/animation updates, and API 1.9.3 version identity. Important conflict decisions are:

- `ComponentMiner` keeps the API pending-action flow and the multiplayer `predictionOnly` and `showHitValue` semantics.
- `ComponentClothing` keeps multiplayer inventory/slot behavior and adopts the API clothed-texture hooks.
- `ComponentHumanModel` combines multiplayer rowing state with API in-hand transforms.
- `LoadingScreen` uses every loader registered by an API mod entity.
- `ModsManager` excludes disabled mods from load ordering.
- `PlayerScreen` keeps multiplayer player selection and safely handles a missing skin texture.
- Settings expose both Multiplayer and Mod Settings.

Every upstream merge must pass an ANGLE build, deterministic L0/L1 tests, and at least the real multi-process core scenario before it is accepted.

## Compatibility contract

Transport connectivity is not evidence that two game instances can safely share a world. A client must complete an application-level compatibility handshake before it can send `ConnectPacket` or receive a project snapshot.

The initial contract is intentionally strict:

1. The multiplayer protocol revision must match exactly.
2. The API compatibility version must match exactly until a tested compatibility range is defined.
3. Enabled user mods are represented by a canonical manifest entry containing package name, version, and API version.
4. Built-in `SurvivalCraft`, `FastDebug`, and `Network` entities are excluded from the user-mod manifest because protocol and API versions already cover them.
5. The client and server manifests must match exactly, including duplicates and missing or extra packages being rejected with structured diagnostics.
6. `GameplayImpactLevel` and `NonPersistentMod` are not network-compatibility declarations and must not be used to silently waive a mismatch.

Exact manifest equality is conservative but safe. A later schema revision may add an explicit network scope (for example required, client-optional, or server-only); optional behavior will only be enabled after corresponding isolation tests exist.

## Handshake sequence

```text
transport connected
    -> client sends CompatibilityHello
    -> server canonicalizes and validates versions and mod manifest
    -> server sends CompatibilityResult
       -> accepted: client sends ConnectPacket and normal project sync begins
       -> rejected: client records a stable code and detailed differences, then disconnects
```

The server must reject a `ConnectPacket` received before compatibility acceptance. Compatibility failures must be observable through the automated control channel; relying on a generic transport disconnect reason is insufficient.

## Automated quality gates

The integration line uses progressively broader gates:

- L0: pure tests for canonicalization, duplicate detection, deterministic ordering, version rules, and mismatch diagnostics.
- L1: in-process packet serialization and handshake state-machine tests.
- L2: real server/client processes for accepted compatibility, protocol mismatch, API mismatch, mod mismatch, reconnect, terrain synchronization, and persistence.
- Fault/replay: each failed scenario retains process logs, control transcripts, state snapshots, seeds, ports, and launch arguments under `TestResults`.

The test host must expose typed commands and observations for compatibility overrides and results. Overrides are test-scoped dependency injection; production discovery always reads the actual loaded mod set.

## First milestone definition of done

The first complete API multiplayer milestone requires all of the following:

- API 1.9.3 and multiplayer compile and start in the same binaries.
- A matching server and client complete compatibility negotiation and join a world.
- Protocol, API, and mod-manifest mismatches are rejected before project transfer with machine-readable diagnostics.
- Core connect, terrain sync, reconnect, and persistence scenarios pass without manual input.
- Failures produce enough artifacts for an AI developer to reproduce and diagnose them without watching the UI.

This milestone establishes a safe development mainline. It does not claim that every third-party mod is multiplayer-correct; mod-defined state replication remains a separate capability that must be declared and tested.
