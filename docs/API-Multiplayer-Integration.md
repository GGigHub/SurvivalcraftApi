# API Multiplayer Integration

This branch is the long-lived integration line for combining the Survivalcraft API/mod runtime with the multiplayer runtime. It deliberately keeps upstream integration, protocol changes, and scenario coverage in reviewable commits.

## Baselines

- Common ancestor: `API_1.9.2.1` (`675efcde9459121c5374ee82f88d680efdb943e4`).
- Multiplayer baseline: `SCAPI1.9_MP` plus the deterministic multiplayer test host (`6980b22233775aa2277ae038e662769eaee4ce55`).
- API baseline: `SCAPI1.9` (`e5536b02f2b9ebbbc83fdd0efc90c8b87f496993`, API 1.9.3.1). The initial integration point was `027b6b2fd2b761c75ee80d63bb07923cffebff70` (API 1.9.3).
- Integration branch: `codex/api-mp-integration`.

At the integration point, the API line contained 99 commits after the common ancestor and the multiplayer line contained 149. The API line changed 114 files, the multiplayer line changed 618, and only 38 paths overlapped. This makes a staged integration practical: most work is independent, while the small overlapping surface can be resolved and tested explicitly.

The API 1.9.3.1 follow-up synchronizes 14 additional `SCAPI1.9` commits. It adds live mod-setting refresh and schema hardening, moves Windows OpenXR initialization off the main thread, updates Jint and NCalcSync, and adopts the 1.9.3.1 package/resource identity. The merge is accepted only after the fast, trust, headless doctor, real compatibility-matrix, and real core-multiplayer gates pass.

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
7. After the core manifest matches, both peers exchange a canonical catalog of namespaced mod channels. Channel name, version, direction, and payload limit must also match exactly.

Exact manifest equality is conservative but safe. A later schema revision may add an explicit network scope (for example required, client-optional, or server-only); optional behavior will only be enabled after corresponding isolation tests exist.

## Handshake sequence

```text
transport connected
    -> client sends CompatibilityHello
    -> server canonicalizes and validates versions and mod manifest
    -> server sends CompatibilityResult
       -> accepted: client sends ModChannelCatalog
       -> server validates the exact channel contract
       -> server sends ModChannelCatalogResult
       -> accepted: client sends ConnectPacket and normal project sync begins
       -> rejected: client records a stable code and detailed differences, then disconnects
```

The server must reject a `ConnectPacket` received before compatibility acceptance. Compatibility failures must be observable through the automated control channel; relying on a generic transport disconnect reason is insufficient.

The implemented wire contract uses protocol revision `14`. Revision 1 established the core contract, revision 2 added API mod channels, revision 3 added monotonic terrain revisions, revision 4 added entity lifecycle revisions and tombstones, and revision 5 added lifecycle-bound Inventory/Health/Damage revisions plus stable mutation identities. Revisions 6 through 10 then added authoritative environment/player mechanics, mount lifecycle, logic/electricity/moving-block state, typed session admission, and furnace/container state respectively. Revision 11 added Project-global monotonic world-fire revisions with an active-state plus 4096-change tombstone window and server-generated 64-bit explosion event IDs. Revision 12 reconciled the upstream transport split: explosion cells and sound travel in one bounded `ExplosionPacket`; packet IDs `120` through `122` added furniture-hammer requests and correlated `PlayerDigRequestPacket`/`PlayerDigConfirmationPacket` recovery. Revision 13 adds stable furniture-hammer request identities and structured accepted/rejected results. Revision 14 makes creative flight an authoritative request/state protocol with stable request IDs, structured results, entity lifecycle revisions and an independent monotonic state revision. The server authenticates the player and current gameplay rules; duplicate IDs execute once, older requests cannot restore superseded intent, and rejection responses heal optimistic local state. The server also validates the equipped furniture hammer, durability, reach, source cells and bounded design, and requires the requested design's normalized material multiset to equal the authoritative source before it registers a design or mutates terrain, inventory and pickables. A lost normal dig packet can be recovered without trusting the client's claimed block value: the server checks the authenticated player, reach, line of sight and first terrain hit, recomputes the authoritative dig result, and emits one correlated confirmation. Client-side `AddExplosion` is rejected before it can enter simulation, while client-authored fire and explosion state packets are ignored by the server. Duplicate, reordered, or delayed frames therefore cannot regress terrain or gameplay state, resurrect an old entity or fire generation, execute a copied mutation twice, or apply one explosion part more than once. Packet IDs `113`/`114` carry the core manifest result, `115`/`116` carry the channel catalog result, and `255` carries the stable mod-message envelope. `ApiModManifest`, `StructuredCompatibilityResult`, and `ModPacketChannel` are mandatory capabilities. The client gives the complete two-stage application handshake 15 seconds before producing `HandshakeTimeout`; direct and relay hosts remove any non-accepted transport session after 20 seconds. Until acceptance, both client receive paths and server send paths admit only the packet required by the current handshake stage, with `ConnectPacket` retaining an explicit defensive check.

Compatibility diagnostics are bounded to 32 machine-readable issues so every rejection remains encodable within the 1 MiB result frame. Catalog and message codecs independently reject null, truncated, trailing, oversized, or non-canonical data before a mod handler can observe it.

## Namespaced mod message channel

The stable API is `ModLoader.RegisterNetworkChannel`, which returns a `ModNetworkChannel` handle. Package identity always comes from the owning `ModLoader`; a mod cannot claim another namespace through the public helper. Sending is explicit through `SendToServer`, `SendToClient`, or `Broadcast`, and receive dispatch verifies the negotiated version, allowed direction, per-channel payload limit, authenticated session, loaded Project, per-peer rate window, and message deduplication identity.

Registrations live for the mod runtime rather than one world. Peer traffic state is cleared on direct or relay disconnect and on Project disposal; registrations remain available across world reload. Mod unloading unregisters each loader in a `finally` block, and full mod-runtime initialization clears any stale catalog. Handler exceptions are logged and converted into an isolated `HandlerFailed` dispatch result without terminating the network session.

Direct and relay transports use the same envelope and checks. The relay protocol includes an authoritative host-to-relay peer disconnect command so a client that never finishes compatibility negotiation cannot retain a relay session indefinitely. Detailed third-party usage and limits are documented in [ModMultiplayerChannels.md](ModMultiplayerChannels.md).

## Automated quality gates

The integration line uses progressively broader gates:

- L0: pure tests for canonicalization, identifier/count bounds, duplicate detection, deterministic ordering, version rules, typed control frames, and mismatch diagnostics.
- L1: bounded wire-codec round trips, malformed/truncated payload rejection, limiter/deduplicator behavior, and mod registration/unload lifecycle.
- L2: real server/client processes for accepted compatibility, protocol/API/mod/channel mismatch, direct and relay mod messages, handler isolation, reconnect, terrain loadedness/streaming/revision convergence, targeted lost-dig recovery, entity generation/tombstone and bounded-drift creature-body convergence, inventory/container authority, health/damage/respawn convergence, revisioned world-fire lifecycle, correlated explosion events, world reload, and persistence.
- Fault/replay: each failed scenario retains process logs, control transcripts, state snapshots, seeds, ports, and launch arguments under `TestResults`.

The test host must expose typed commands and observations for compatibility overrides and results. Overrides are test-scoped dependency injection; production discovery always reads the actual loaded mod set.

## First milestone definition of done

The first complete API multiplayer milestone requires all of the following:

- API 1.9.3 and multiplayer compile and start in the same binaries.
- A matching server and client complete compatibility negotiation and join a world.
- Protocol, API, and mod-manifest mismatches are rejected before project transfer with machine-readable diagnostics.
- Core connect, terrain sync, reconnect, and persistence scenarios pass without manual input.
- Failures produce enough artifacts for an AI developer to reproduce and diagnose them without watching the UI.

This milestone established the safe integration baseline.

## Second milestone definition of done

The mod-capability milestone additionally requires:

- a namespaced channel contract that does not allocate third-party global packet IDs;
- exact pre-Project channel negotiation with bounded machine-readable errors;
- direction, identity, payload, rate, duplicate, session, and Project checks before dispatch;
- registration cleanup on mod unload and peer-state cleanup across disconnect and world reload;
- isolated handler failures that do not terminate a compatible session;
- typed automation and real headless direct plus relay scenarios, including reconnect and fault injection;
- a third-party developer contract describing authority, limits, lifecycle, and repeatable gates.

This still does not claim that every third-party mod is multiplayer-correct; each mod must implement and test an authoritative state model over its declared channels.

## Implemented milestones

The API 1.9.3.1 merge, two-stage compatibility handshake, stable mod channel, typed diagnostics, real-process mismatch matrix, direct and relay message scenarios, reconnect, revisioned terrain streaming, revisioned entity lifecycle, authoritative creature bodies, authoritative creative flight, inventory/container authority, furnace processing, furniture-hammer authority, health/damage/respawn authority, authoritative time/weather/lightning, lifecycle-aware sleep/wake and player-fire state, revisioned world-fire lifecycle, correlated explosion events, lifecycle-bound authoritative mount/dismount, world reload, and persistence checks are implemented on this branch. Remote non-player bodies use a 0.25-second playback delay and discard stale interpolation history when playback falls more than three blocks behind the newest authoritative snapshot, bounding visual drift without loosening convergence assertions. Server-side remote-player corrections remain smooth for ordinary movement, while discontinuities larger than four blocks apply in the receiving packet handler so a following interaction cannot be authorized against a stale pre-teleport position. The covered gameplay gates use one server and three clients in both transports, apply deterministic duplication/reordering, and preserve strict typed replay contracts. `creative-fly-multiplayer` version 2 proves that the host production UI path advances an authoritative revision and broadcasts state, while remote requests receive structured accepted/rejected results; it then covers targeted duplication/reordering, stale-state rejection, late join, stable-identity reconnect and save/reload in six strictly replayed actions. `terrain-interaction-multiplayer` version 3 deliberately drops every normal client `PlayerDigPacket`, then requires the correlated request/confirmation fallback and authoritative terrain convergence in Direct, Relay and strict replay. `inventory-health-multiplayer` version 3 records fifteen production actions and verifies typed container-open results, ownership and range rejection, real fuel consumption/cooking output, independent furnace revisions, and an authoritative furniture-hammer request. Before the furniture commit it proves missing-tool, out-of-range, empty-source, legal-but-forged-material and rate-limit rejection without state mutation; the accepted request then proves one design registration, one source consumption and one produced pickable despite repeated same-ID packets, followed by late join, reconnect and save/reload convergence. `environment-mechanics-multiplayer` records ten actions and verifies non-creative environment rejection with same-revision healing of optimistic client state, forged environment/player-fire/world-fire rejection, local and packet-level explosion authority, cross-player sleep rejection, monotonic fire revisions and bounded removal tombstones, correlated chunked explosion cells and sound, late join, stable player identity across entity-generation replacement, and save/reload persistence. Only currently present world fire is reconstructed for late join, reconnect, and reload; lightning, explosions, and removed-fire history are deliberately not replayed. `mount-multiplayer` records seven actions and verifies session ownership, forged-state and range rejection, both sides of the rider/mount relation, stale revision and reused-entity lifecycle rejection, late join, rider removal, reconnect, and safe-dismount persistence. Creative-flight and furniture-hammer synchronization are therefore listed as `covered/mutation`; the stable `fast`, `trust`, `doctor`, `compatibility`, `core`, `relay`, and affected gameplay gates remain required before merge. The legacy non-L2 collection is retained as a headless migration audit while its remaining UI/log assertions are converted to typed state checks.
