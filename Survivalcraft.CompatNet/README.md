# Survivalcraft.CompatNet

`Survivalcraft.CompatNet` keeps the original community mods unchanged and
rewrites only their in-memory assembly images during the preload phase. Every
adapter uses a stable non-zero ID and the shared `CompatNetPacket` envelope.
Normal entity, body, terrain, inventory, clothing, projectile, health, and
electricity updates continue to use `Survivalcraft.Multiplayer` packets.

## Current adapters

| PackageName | Assembly | Adapter ID | Current coverage |
|---|---|---:|---|
| `gunsmod.ysf.scmod` | `gunsmod.dll` | 1 | Server-authoritative projectiles and missiles |
| `zh.command` | `CommandBlock.dll` | 2 | Command edits, execution, and electric results |
| `Elytron` | `Elytron.dll` | 3 | Server-authoritative double-jump gliding and firework intent; remote update guard |
| `ysf.neorxna` | `neorxna.dll` | 4 | Server-authoritative dash intent with server-side state checks; remote update guard |
| `Enchantment` | `Enchantment.dll` | 5 | Magic-removal and enchanting-table requests; disables duplicate repair/fishing/recall simulation |
| `MCHszr` | `MCHszr.dll` | 6 | Server-authoritative combat pose, charge/release, and food requests; remote update guard |
| `SushiBase` | `SushiBase.dll` | 7 | Remote-player update guard; ordinary input/inventory uses standard packets |
| `SushiTool` | `SushiTool.dll` | 8 | All non-player updates, bounded scalar snapshots, shared-channel inventory sync, machine controls/configuration, and train input/recycle requests |
| `Tp.Subnautica` | `sc-subnautica-mod.dll` | 9 | Remote-body update guard, mermaid feed/quest requests, and bounded mermaid state snapshots |
| `Tp.WonderfulEra` | `WonderfulEra.dll` | 10 | Remote-body update guard, server-authoritative custom vehicle control, and bounded scalar snapshots |
| `com.IndustrialEra2` | `SCIENEW.dll` | 11 | All non-player updates, bounded scalar machine state, and entity tank contents; fluid/electric topology still needs typed sync |
| `SushiTouch` | `SushiTouch.dll` | 12 | Marker adapter; local touch input follows standard input packets |

The update guard is deliberately conservative: it applies only on a pure
client. Player-only adapters affect
non-main player entities; remote-body adapters affect non-player entities with
a `ComponentBody`; world-authoritative adapters affect all non-player entities.
IndustrialEra2 additionally suppresses the target mod's authoritative
`IUpdateable` subsystems, while leaving its presentation/debug subsystems local.
It never suppresses server, listen-server, or local-player updates. This avoids
duplicate simulation while leaving the server authoritative.

## Protocol rules

Client input or a GUI action must become a small request packet. The server
must authenticate the sender, validate the entity/position/equipment/range and
cooldown, then invoke the original mod method. The server's standard packets
carry ordinary side effects; an adapter-specific broadcast is needed only for
state that the standard packets do not represent.

Do not serialize an entire custom component every frame. Use typed, bounded
payloads with an entity ID and a sequence/state value. `EntityLoadPacket` is an
initial spawn/resync mechanism, not a live state replication protocol.

## Conservative state snapshot baseline

The scoped adapters (IDs 7-11) now have a small server-to-client state bridge
for state that the normal multiplayer packets do not expose. The server samples target-mod
`IUpdateable` components every 0.2 seconds, sends only changed fields, and
performs a full refresh every 5 seconds. It accepts only bounded scalar values
(`bool`, integer, floating point, `Point3`, `Vector3`, and `Quaternion`) whose
field names match the state whitelist. Each batch is limited to 128 records and
each component to 24 values; GUI, collections, random state, textures, and
arbitrary object graphs are excluded.

The snapshot scope follows the update guard: remote-player, remote-body, and
all-non-player adapters do not apply state to the local player. This is an
initial convergence layer for progress, timers, health, energy, temperature,
and similar scalar state. It is not a replacement for typed request protocols,
inventory deltas, or electric-network topology.

## SushiTool channel inventory protocol

SushiTool's `ComponentSushiSyncBox` is an entity-facing component, but its actual
slots live in `SubsystemSushiSyncBox.SushiSyncInventories`, a shared world
dictionary. The adapter adds an in-memory `SetSlotValue` bridge and explicit
mutation notifications so ordinary `InventorySyncPacket` corrections can update
the shared slot instead of the component's unused base slot list.

Adding a channel is a client request with no mutable payload. The server invokes
the original `AddChannel` method with the authenticated sender's player, and
then publishes the new channel in the next bounded snapshot. Channel snapshots
contain the complete slot list plus channel name, color, and order metadata for
each changed channel. They are limited to 63 channels, 128 slots per channel,
and 64 characters per channel name; a missing shared inventory can be created
on the client. This covers the shared contents that entity-addressed inventory
packets cannot identify.

The network mod's move, drag-process, and world-drop hooks are also redirected
when one endpoint is a SushiTool channel. The request carries channel indices
instead of local inventory IDs; the server applies the same capacity/process
rules as the standard inventory packets, corrects the player's ordinary
inventory, and forces a channel snapshot to reconcile the optimistic client UI.

Channel name/color edits and order changes are also redirected to authenticated
server requests. The client may update the dialog optimistically, but the
server's authoritative metadata snapshot wins after validation. Full refreshes
also remove stale channels and order entries on the client. Machine
Start/OverStop buttons and committed machine settings are forwarded as bounded
entity-scoped requests; the server validates the SushiMachine type and
interaction distance before changing its state. Settings use the target
machine's own `CreateWorkConfig`/`ExtractWorkConfig`/`PasteConfig` contract and
are capped at 8192 characters. Machine progress is converged by the scalar
snapshot baseline. Train control, recycle, and call actions use separate
validated requests; call events are broadcast and replay the target mod's own
audio/noise implementation on each client.

SushiTool trains are a special case of the update guard. The local player's
currently ridden train is allowed to process local input; speed/turn edges are
sent as bounded deltas, while remote trains remain server-owned. Train recycle
is intercepted on the client and executed only after the server confirms that
the authenticated player is the current rider, preventing duplicate pickables.

## Subnautica mermaid protocol

Subnautica's mermaid feed, quest-accept, and quest-delivery paths are patched
with narrow in-memory seams. A pure client sends only the mermaid entity ID,
feed kind, or quest ID; the server authenticates the sender, checks the
interaction range and current inventory, then invokes the original mod method.
Successful feed/delivery mutations use the standard inventory dirty/sync path.

Mermaid state snapshots carry affection, relationship state, target player,
active quest, progress, and a bounded completed-quest list. The client applies
the fixed quest definition by ID and does not run the remote mermaid update
loop. Gift actions, kill-event attribution, achievement persistence, and
non-mermaid creature AI still need dedicated seams.

## IndustrialEra2 tank protocol

IndustrialEra2 exposes machine fluids through the reflection-only `ITank`
surface. Adapter 11 samples every target-assembly tank component on the server
and sends changed `EntityId`, component type, fluid value, and volume records at
bounded intervals. The client applies a record through the mod's own
`RemoveTankFluid`/`AddTankFluid` methods, then refreshes the optional fluid
visual hooks. Non-finite volumes, oversized records, player entities, and
version-mismatched components are ignored. This covers ordinary machine tanks,
including emptying a tank, without serializing the mod's private object graph.

The client does not run IndustrialEra2's authoritative fluid-transfer or
electric-network simulation ticks. Network topology, pipe-group internals,
complex machine actions, vehicles, and other non-tank state still need a
dedicated protocol.

## WonderfulEra vehicle control

WonderfulEra writes its custom vehicle `MoveOrder`, `TurnOrder`, and
`FlyOrder` values from `OnPlayerControlOtherMount`; these values are not part of
the ordinary player-body packet. The in-memory hook sends a throttled request
with the mounted vehicle entity ID and bounded input values. The server accepts
it only when that player is currently mounted on the named vehicle, applies the
orders before the vehicle update, and lets the normal body packets replicate
the resulting position/rotation/velocity. The client keeps the original input
hook for camera and rowing feedback while the scoped guard suppresses its
duplicate vehicle simulation.

## Remaining work

The current set is a safe multiplayer baseline, not a claim of complete parity
for every feature in the ten mods. The next typed protocols should cover:

- SushiTool machine fluids and custom inventory opening.
- Subnautica gift actions, kill-event attribution, achievement persistence, and special creature attacks.
- WonderfulEra mount transitions, projectiles, and custom inventory opening.
- IndustrialEra2 complex machines, fluid/electric networks, vehicles, and physics.

The typed protocols above intentionally cover the highest-risk input and
transaction paths. The scalar snapshot baseline covers convergence of simple
state only; effects with no standard packet representation (for example fishing
loot, custom AI, custom inventory contents, complex machine fluids, vehicles,
and electric-network topology) still need separate validated protocols before
they can claim complete parity.

Some original `.scmod` files in the community archive are protected ZIP-like
containers with an author notice prefix. The adapters therefore match the
runtime `modinfo.json` package name and assembly name, not the archive file
name on disk.
