# Project Status

- Status: ACTIVE — PREVIEW 2 RELEASE EXECUTION
- Updated: 2026-08-10 Asia/Shanghai
- Project path: `C:\Users\Hao\Documents\New project 2\worktrees\SurvivalcraftApi-MPCore`
- Branch or Worktree: `codex/api-mp-integration`
- Integrated compatibility commit: `c46f05abf01ed5ebe30e47a3bf1102da5e9ed04d` (`codex/api145-compat`, fast-forwarded without conflicts)
- Release state: `API_1.9.3.1_MP-preview.2` is the active candidate; Preview 1 remains immutable.

## Active compatibility track

- State transition: `ACTIVE COMPATIBILITY -> ACTIVE PREVIEW 2 RELEASE PREPARATION`（枪械核心加载、世界重进和弹道探针已收敛；DayZ 在 Preview 2 后继续）。
- Goal: preserve API 1.9.3.1 while providing additive common shims for API 1.8, a loader/member bridge for API 1.7, and exact-break adapters for API 1.4–1.6. All third-party packages and their DLLs remain immutable inputs.
- Priority: Guns 2.8 has completed stable Android world creation, exit, reentry and functional projectile probes. Preview 2 packaging is now the active target; DayZ remains the next compatibility target. Siyah Akrep remains observation-only and receives no package-specific work because it is a single-player story add-on; only the shared firearms dependency remains in scope.
- Out of scope for the first milestone: arbitrary API 1.x binary emulation, silent fallback for unknown members, Siyah Akrep gameplay, and multiplayer synchronization of legacy custom weapon state.
- Completion evidence for C1: 24 deterministic compatibility tests, a hash-locked Android Guns 2.8 new-world/reentry run with two successful projectile probes and clean logs, plus clean API 1.9 binary and runtime regressions.
- Exact next action: bump the release contract to Preview 2, rebuild all public artifacts from a clean commit, and rerun the release and Android compatibility gates before tagging or publishing.
- Detailed plan: `docs/LegacyApi145Compatibility.md`.

## Goal

Ship the next developer preview that combines Survivalcraft API 1.9.3.1, the completed M1–M13 multiplayer core and the verified layered legacy-mod compatibility host, using one release contract for Windows x64, a separately deployable Relay Server and an Android ARM64 APK.

## Completed

- API compatibility remains `1.9.3.1`; Multiplayer and Relay share release version `1.9.3.1-mp-preview.2`, assembly/file version `1.9.3.1`, and tag `API_1.9.3.1_MP-preview.2` through `build/MultiplayerRelease.props`.
- `release/multiplayer-preview.json` is the authoritative release contract for tag/title, API/game versions, wire revision 17, Automation API 16, Snapshot schema 5, Android `arm64-v8a`, versionCode 19312, versionName, and artifact names.
- Release notes summarize M1–M13, the 22/22 covered core feature contract, install/test cautions and the remaining real-device/public-network limits.
- `Build-MultiplayerPreview.ps1` produces deterministic-container Windows/Relay ZIPs, an Android ARM64 APK, a source/toolchain manifest and `SHA256SUMS.txt`. Dirty builds are explicitly marked and cannot be published.
- `Test-MultiplayerPreviewRelease.ps1` validates every checksum, public-package isolation, APK ABI/version/signature/internal scmod, then injects TestAutomation only into a disposable Windows extraction for hidden startup, Direct and separate-runtime Relay tests.
- `Publish-CnbMultiplayerPreview.ps1` creates or updates a CNB prerelease, obtains pre-signed upload URLs, confirms every upload and verifies remote size/hash without printing credentials.
- Relay mod-channel automation now accepts `--relay-runtime`, so the release test uses the actual standalone Relay archive rather than a combined developer runtime.
- Public Windows and Android packages contain `InternalMods/Survivalcraft.Multiplayer.scmod` and do not contain `Survivalcraft.TestAutomation.scmod`.
- Android device testing now uses a separate package and `/sdcard/Survivalcraft2.4_API1.9_AutoTest`, a fixed semantic JSON command bridge, automatic wake/keyguard handling, temporary USB stay-awake with verified restoration, and background-only host orchestration.
- Pad mode is covered on both new and existing worlds. A three-cycle baseline produced 18 successful relay codes while exercising live relay endpoint failover; deterministic ADB daemon failure injection also recovered without manual action.
- The Chinese-community corpus is source-allowlisted and SHA-256 locked. It now contains 9 real API 1.9.x DLL mods across world generation, maps, UI hooks, player health/state, kill events, recipes/items, projection UI and entity AI; every success path also gates the device `Bugs/Game.log` against ERROR/FATAL output.
- `Test-AndroidLocal.ps1` is the single local entry for L0 source contracts, test APK build/install, device stability, community mod matrix and final zero-residue checks.
- A separate six-package classic corpus now locks Wonderful Era, DayZ, Guns 2.8, Siyah Akrep 2.8 and required support mods. The runner models dependency-aware subjects, exact startup class substitutions, declarative startup-dialog actions, custom registered screen names, 512 MiB package support and non-gating observational failures.
- Content assets now carry their source package. Bitmap fonts pair a glyph list with a unique same-source texture, fixing legacy `.lst`/`.png` overlays that otherwise collide with a core `.webp` while still failing closed on ambiguity.
- Guns 2.8 now uses an exact package/assembly-hash in-memory host adapter. The unchanged source package completes new-world/reentry lifecycles while host-managed gun data, recipes, maps and projectile updates are verified by a semantic world probe.
- Historical API 1.9 binary shapes are restored with additive host shims. A read-only Mono.Cecil preflight is integrated into the matrix and fails before device launch on unresolved current-host references or ambiguous name-only Harmony targets.

## Verification

- Release contract tests: 6/6 passed.
- Final fast gate: platform L0/L1 134/134 and game L0/L1 153/153 passed after the legacy host compatibility and binary-preflight additions.
- Coverage trust passed with 22/22 covered features, 103 production hooks, 113 packet classes and zero open features.
- Release build produced all three public packages plus release manifest, notes and checksums.
- Android static verification passed: only `arm64-v8a`, versionCode 19311, versionName `API1.9.3.1-MP-preview.1`, embedded Multiplayer scmod present, and APK signature schemes v1/v2/v3 valid with the repository development certificate.
- Fresh-extract hidden doctor reached `MainMenuScreen` with the multiplayer runtime bound.
- Fresh-extract Direct core scenario passed with seed `20260809`.
- Fresh-extract Relay mod-channel scenario passed with the Relay process launched from the separately extracted Relay archive; Optional isolation, raw ingress rejection, reconnect and deduplication remained green.
- Release verification summary: `TestResults/release-verification/API_1.9.3.1_MP-preview.1/verification-summary.json`.
- No Survivalcraft or release Relay process remained after validation.
- Coverage Contract remains 22/22 core headless multiplayer features `covered`; M12 deterministic-loss convergence and M13 per-session mod-channel isolation remain the underlying reliability baseline.
- Android device stability evidence: `TestResults/android-device-stability/20260809-223211/summary.json` (3 cycles, 18 relay attempts, 1 injected ADB recovery, 6 transport failovers).
- Android community-mod evidence: `TestResults/android-mod-matrix/20260809-225612/summary.json` (3/3 gated cases, 6 relay attempts, zero cleanup residue).
- Expanded real-API evidence: `TestResults/android-mod-matrix/real-api-expanded-log-gated/summary.json` (9 hash-locked mods, 12/12 single/stack cases, 24 relay attempts, 12/12 Game.log audits, zero errors and warnings, followed by empty-corpus cleanup).
- Latest main-corpus regression: `TestResults/android-mod-matrix/main-corpus-double-hash-final/summary.json` (the read-only preflight resolved 1,919 member and 2,929 type references across 9 packages/31 assemblies, verified 35 name-only Harmony targets with zero ambiguity, then passed all 12 gated cases/24 world entries, all Game.log audits and final empty-corpus cleanup).
- Classic API 1.9 evidence: `TestResults/android-mod-matrix/classic-api19-double-hash-final/summary.json` (the integrated read-only preflight resolved 2,350 member and 621 type references with zero unresolved references or Harmony ambiguity; BLDG, Accessory and Wonderful Era with both dependencies then completed six total world entries, all three Game.log audits were clean, and final empty-corpus cleanup passed).
- Classic legacy evidence: `TestResults/android-mod-matrix/legacy-firearms-double-hash-final/summary.json` proves the unchanged Guns 2.8 package completes new world plus reentry through all 15 registered migrations, with two projectile probes and 0 runtime errors/warnings. DayZ still fails deterministically on missing `SubsystemSeasons`; Siyah Akrep remains deferred and observation-only.
- End-to-end orchestration smoke: `TestResults/android-local/orchestration-smoke/summary.json` (source contracts, ADB recovery, relay regression, 3/3 mod cases and final cleanliness all passed).
- Final default local gate: `TestResults/android-local/final-default/summary.json` (fresh test APK build/install, 18 unique global and room codes, 1 injected ADB recovery, 6 endpoint failovers, 3/3 mod cases, zero setting-restore failures and zero final residue).
- Legacy compatibility L0: 24/24 game tests passed for historical binary member shapes, pre-1.8.1 rewrite boundaries, exact-hash package adaptation, old getter migration and Harmony-safe particle migration; the machine-readable layered ledger contract also passed.

## Release artifacts

- `Survivalcraft-API-1.9.3.1-MP-preview.1-Windows-x64.zip`
- `Survivalcraft-RelayServer-1.9.3.1-MP-preview.1-win-x64.zip`
- `Survivalcraft-API-1.9.3.1-MP-preview.1-Android-arm64.apk`
- `API_1.9.3.1_MP-preview.1-release-manifest.json`
- `API_1.9.3.1_MP-preview.1-release-notes.md`
- `SHA256SUMS.txt`

The final publishable copies are generated under `artifacts/releases/API_1.9.3.1_MP-preview.1` from the clean tagged commit; build artifacts remain ignored by Git.

## Decisions, assumptions and remaining reality gaps

- This is a developer preview, not a stable release. “Complete” means the scripted packaging and core headless Direct/Relay contract are green, not that every UI path, device, router, NAT or third-party mod is proven.
- Android is ARM64-only and development-key signed. The .NET Android build succeeds and the APK passes static validation, but the dependency build emits an upstream warning that MonoMod.Core does not declare Android ARM64 support. Only real-device launch/gameplay can resolve that compatibility risk.
- Android evidence currently comes from one Huawei NOH-AN01 on Android 12. A second Android client actually joining the room, broader vendors/OS versions, graphics correctness, thermal/power soak and upgrade/install-over-old-data behavior remain device-test responsibilities.
- Historical device evidence covers public-internet relay creation and endpoint failover. Current public relay availability is externally operated and is not treated as a client-code release gate; local Relay packaging and protocol gates remain mandatory.
- Wonderful Era can coexist with the multiplayer host lifecycle, but its community page states that it does not currently support multiplayer. No claim is made that its custom gameplay state synchronizes. Guns now passes local lifecycle and projectile probes through deliberate host compatibility, but its custom state is not yet proven across clients; DayZ remains under compatibility development and Siyah Akrep remains deferred.
- Every automated game launch remains headless with `CreateNoWindow=true`; no foreground game window is permitted in release automation.

## Next action

- Prepare Preview 2 from the merged main development line: update the release contract and notes, produce clean Windows/Relay/Android artifacts, run release verification and background Android compatibility gates, then tag and publish only after all mandatory checks pass.
