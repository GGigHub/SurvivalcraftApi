# Project Status

- Status: M14 MULTIPLAYER PREVIEW 1 + ANDROID LOCAL AUTOMATION COMPLETE
- Updated: 2026-08-09 Asia/Shanghai
- Project path: `C:\Users\Hao\Documents\New project 2\worktrees\SurvivalcraftApi-MPCore`
- Branch or Worktree: `codex/api-mp-integration`
- Release tag: `API_1.9.3.1_MP-preview.1`

## Goal

Ship the first developer preview that combines Survivalcraft API 1.9.3.1 with the completed M1–M13 multiplayer core, using one release contract for Windows x64, a separately deployable Relay Server, and an Android ARM64 APK, then prove the public packages through checksums, static inspection and background-only fresh-extract smoke tests.

## Completed

- API compatibility remains `1.9.3.1`; Multiplayer and Relay share release version `1.9.3.1-mp-preview.1`, assembly/file version `1.9.3.1`, and tag `API_1.9.3.1_MP-preview.1` through `build/MultiplayerRelease.props`.
- `release/multiplayer-preview.json` is the authoritative release contract for tag/title, API/game versions, wire revision 17, Automation API 16, Snapshot schema 5, Android `arm64-v8a`, versionCode 19311, versionName, and artifact names.
- Release notes summarize M1–M13, the 22/22 covered core feature contract, install/test cautions and the remaining real-device/public-network limits.
- `Build-MultiplayerPreview.ps1` produces deterministic-container Windows/Relay ZIPs, an Android ARM64 APK, a source/toolchain manifest and `SHA256SUMS.txt`. Dirty builds are explicitly marked and cannot be published.
- `Test-MultiplayerPreviewRelease.ps1` validates every checksum, public-package isolation, APK ABI/version/signature/internal scmod, then injects TestAutomation only into a disposable Windows extraction for hidden startup, Direct and separate-runtime Relay tests.
- `Publish-CnbMultiplayerPreview.ps1` creates or updates a CNB prerelease, obtains pre-signed upload URLs, confirms every upload and verifies remote size/hash without printing credentials.
- Relay mod-channel automation now accepts `--relay-runtime`, so the release test uses the actual standalone Relay archive rather than a combined developer runtime.
- Public Windows and Android packages contain `InternalMods/Survivalcraft.Multiplayer.scmod` and do not contain `Survivalcraft.TestAutomation.scmod`.
- Android device testing now uses a separate package and `/sdcard/Survivalcraft2.4_API1.9_AutoTest`, a fixed semantic JSON command bridge, automatic wake/keyguard handling, temporary USB stay-awake with verified restoration, and background-only host orchestration.
- Pad mode is covered on both new and existing worlds. A three-cycle baseline produced 18 successful relay codes while exercising live relay endpoint failover; deterministic ADB daemon failure injection also recovered without manual action.
- The Chinese-community corpus is source-allowlisted and SHA-256 locked. `SurvivalcraftRuins 0.4.0`, `SurvivalcraftTravelMap 1.0.0`, and their compatibility group all passed isolated load, world creation, relay creation and reentry on Android.
- `Test-AndroidLocal.ps1` is the single local entry for L0 source contracts, test APK build/install, device stability, community mod matrix and final zero-residue checks.

## Verification

- Release contract tests: 4/4 passed.
- Final fast gate: platform L0/L1 132/132 and game L0/L1 129/129 passed after the Android automation additions.
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
- End-to-end orchestration smoke: `TestResults/android-local/orchestration-smoke/summary.json` (source contracts, ADB recovery, relay regression, 3/3 mod cases and final cleanliness all passed).
- Final default local gate: `TestResults/android-local/final-default/summary.json` (fresh test APK build/install, 18 unique global and room codes, 1 injected ADB recovery, 6 endpoint failovers, 3/3 mod cases, zero setting-restore failures and zero final residue).

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
- Public-internet relay creation and endpoint failover are covered on the device; cross-machine firewall/NAT, cross-carrier joins and larger curated external-mod sets remain the next release-reality track.
- Every automated game launch remains headless with `CreateNoWindow=true`; no foreground game window is permitted in release automation.

## Next action

- Add a second clean Android client to automate real room-code join and disconnect/reconnect, then expand the locked community corpus and cross-carrier/NAT matrix while keeping public-package leak guards green.
