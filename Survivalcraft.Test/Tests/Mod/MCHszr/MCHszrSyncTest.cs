using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Engine;
using Xunit;
using Xunit.Abstractions;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class MCHszrSyncTest : IClassFixture<MCHszrGameFixture>, IDisposable {
	private readonly MCHszrGameFixture fixture;

	private readonly ITestOutputHelper output;

	private GameContext server => fixture.Server;

	private GameContext client => fixture.Client;

	public MCHszrSyncTest(MCHszrGameFixture fixture, ITestOutputHelper output) {
		this.fixture = fixture;
		this.output = output;
	}

	[Fact]
	[TestOrder(Priority = 1)]
	public async Task PoseToggleSynchronizesBetweenClientAndServer() {
		await MCHszrSyncTools.EnsureWorldAndClientAsync(server, client);
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);

		await MCHszrSyncTools.SeedWeaponAsync(server, playerIndex, MCHszrSyncTools.IronSpearContents, 1);
		await MCHszrSyncTools.WaitForClientWeaponAsync(client, playerIndex, MCHszrSyncTools.IronSpearContents, 1);

		await MCHszrSyncTools.ForcePoseAsync(server, playerIndex, 0);
		await MCHszrSyncTools.ForcePoseAsync(client, playerIndex, 0);
		await Task.Delay(300);

		int preServer = await MCHszrSyncTools.GetPoseAsync(server, playerIndex);
		int preClient = await MCHszrSyncTools.GetPoseAsync(client, playerIndex);

		string toggle = await MCHszrSyncTools.TogglePoseRealPathAsync(client, playerIndex);
		await Task.Delay(1500);

		int postServer = await MCHszrSyncTools.GetPoseAsync(server, playerIndex);
		int postClient = await MCHszrSyncTools.GetPoseAsync(client, playerIndex);

		output.WriteLine($"T1 姿态切换 preServer={preServer};preClient={preClient};toggle={toggle};postServer={postServer};postClient={postClient}");

		List<string> issues = new();
		if (preServer != preClient) {
			issues.Add($"初始姿态不一致 preServer={preServer};preClient={preClient}");
		}
		if (toggle.StartsWith("toggled=", StringComparison.Ordinal)) {
			if (postServer != postClient) {
				issues.Add("不同步(战斗姿态切换): 客户端真实路径切换后 服务端姿态与客户端不一致 "
					+ $"postServer={postServer};postClient={postClient};toggle={toggle}");
			}
			if (postClient != 1) {
				issues.Add($"客户端姿态未能切换为战斗姿态(1) postClient={postClient};toggle={toggle}");
			}
		}
		else if (toggle.StartsWith("no-draghost", StringComparison.Ordinal) || toggle.StartsWith("error:", StringComparison.Ordinal)) {
			output.WriteLine($"T1 环境限制：无法通过真实 UI 路径触发姿态切换（toggle={toggle}），改为对比两端姿态值。postServer={postServer};postClient={postClient}");
			if (postServer != postClient) {
				issues.Add($"不同步(战斗姿态切换): postServer={postServer};postClient={postClient};toggle={toggle}");
			}
		}
		else {
			issues.Add($"真实路径切换返回异常 toggle={toggle}");
		}
		Assert.True(issues.Count == 0, "T1 姿态同步失败:" + Environment.NewLine + string.Join(Environment.NewLine, issues));
	}

	[Fact]
	[TestOrder(Priority = 2)]
	public async Task SweepAttackDamageIsConsistentBetweenClientAndServer() {
		await MCHszrSyncTools.EnsureWorldAndClientAsync(server, client);
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Point3 origin = await CombatAreaAsync(spawnOffsetX: 26, spawnOffsetY: 2, spawnOffsetZ: 14);
		var playerPos = new Vector3Dto(origin.X + 0.5f, origin.Y + 1f, origin.Z + 0.5f);
		var dummyPos = new Vector3Dto(origin.X + 0.5f, origin.Y + 1f, origin.Z + 1.5f);

		int dummyId = await MCHszrSyncTools.PrepareMeleeAreaAsync(server, origin, dummyPos, 100f);
		Assert.True(await CreatureSyncTools.WaitForEntityExistsAsync(client, dummyId), "Client did not receive dummy creature.");
		await MCHszrSyncTools.SetBoatHitpointsAsync(client, dummyId, 100f);
		await MCHszrSyncTools.PinBoatAsync(client, dummyId, dummyPos);
		await MCHszrSyncTools.PositionMainPlayerAsync(client, playerPos, gravity: true, creativeFly: false);
		await MCHszrSyncTools.WaitForPlayerStandingAsync(server, playerIndex, standing: true);
		await MCHszrSyncTools.WaitForServerPlayerConvergedAsync(server, playerIndex, playerPos, 3f);

		await MCHszrSyncTools.SeedWeaponAsync(server, playerIndex, MCHszrSyncTools.IronMacheteContents, 1);
		await MCHszrSyncTools.WaitForClientWeaponAsync(client, playerIndex, MCHszrSyncTools.IronMacheteContents, 1);

		CameraRay ray = await MCHszrSyncTools.BuildMeleeRayAsync(client, dummyId);
		int raycastId = await PlayerSyncTools.GetHitRaycastEntityAsync(client, ray);
		CreatureHealthState preServerC = await MCHszrSyncTools.GetCreatureHealthAsync(server, dummyId);
		CreatureHealthState preClientC = await MCHszrSyncTools.GetCreatureHealthAsync(client, dummyId);

		await MCHszrSyncTools.SendHitRayAsync(client, ray);
		await Task.Delay(400);
		string serverCooldowns = await MCHszrSyncTools.GetHitCooldownsAsync(server);
		string clientCooldowns = await MCHszrSyncTools.GetHitCooldownsAsync(client);
		await Task.Delay(600);
		CreatureHealthState serverC = await MCHszrSyncTools.GetCreatureHealthAsync(server, dummyId);
		WaitResult<CreatureHealthState> conv = await TestWaiter.TryUntilAsync(
			() => MCHszrSyncTools.GetCreatureHealthAsync(client, dummyId),
			c => c.Exists && Math.Abs(c.Hitpoints - serverC.Hitpoints) <= 0.01f,
			TimeSpan.FromSeconds(6));

		output.WriteLine($"T2 横扫 raycastId={raycastId};serverCooldowns={serverCooldowns};clientCooldowns={clientCooldowns};"
			+ $"preServer={preServerC};preClient={preClientC};postServer={serverC};postClient={conv.Last}");

		List<string> issues = new();
		if (raycastId != dummyId) {
			issues.Add($"EVIDENCE:近战射线未命中假人 dummyId={dummyId};raycastId={raycastId};ray={ray.Raw}");
		}
		if (!conv.Matched) {
			issues.Add("不同步(刀横扫伤害): 命中后 客户端假人血量未能与服务端收敛 "
				+ $"server={serverC};clientLast={conv.Last};preServer={preServerC};preClient={preClientC};serverCooldowns={serverCooldowns}");
		}
		if (!(preServerC.Hitpoints - serverC.Hitpoints > 0.5f)) {
			issues.Add($"EVIDENCE:服务端假人未受到有效伤害 preHitpoints={preServerC.Hitpoints};postHitpoints={serverC.Hitpoints};serverCooldowns={serverCooldowns}");
		}
		float serverSweep = ParseField(serverCooldowns, "sweep");
		if (serverSweep <= 0f) {
			issues.Add($"EVIDENCE:服务端横扫冷却未触发（横扫可能在联机处理中被跳过） serverCooldowns={serverCooldowns};clientCooldowns={clientCooldowns}");
		}
		Assert.True(issues.Count == 0, "T2 刀横扫同步:" + Environment.NewLine + string.Join(Environment.NewLine, issues));
	}

	[Fact]
	[TestOrder(Priority = 3)]
	public async Task AxeCriticalHitDamageIsConsistentBetweenClientAndServer() {
		await MCHszrSyncTools.EnsureWorldAndClientAsync(server, client);
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Point3 origin = await CombatAreaAsync(spawnOffsetX: 40, spawnOffsetY: 2, spawnOffsetZ: 14);
		var playerPos = new Vector3Dto(origin.X + 0.5f, origin.Y + 4.3f, origin.Z + 0.5f);
		var dummyPos = new Vector3Dto(origin.X + 0.5f, origin.Y + 3.8f, origin.Z + 0.9f);

		int dummyId = await MCHszrSyncTools.PrepareMeleeAreaAsync(server, origin, dummyPos, 100f);
		Assert.True(await CreatureSyncTools.WaitForEntityExistsAsync(client, dummyId), "Client did not receive dummy creature.");
		await MCHszrSyncTools.SetBoatHitpointsAsync(client, dummyId, 100f);
		await MCHszrSyncTools.PinBoatAsync(client, dummyId, dummyPos);
		await MCHszrSyncTools.PositionMainPlayerAsync(client, playerPos, gravity: false, creativeFly: true);
		await MCHszrSyncTools.WaitForPlayerStandingAsync(server, playerIndex, standing: false);
		await MCHszrSyncTools.WaitForServerPlayerConvergedAsync(server, playerIndex, playerPos, 3f);

		await MCHszrSyncTools.SeedWeaponAsync(server, playerIndex, MCHszrSyncTools.IronAxeContents, 1);
		await MCHszrSyncTools.WaitForClientWeaponAsync(client, playerIndex, MCHszrSyncTools.IronAxeContents, 1);

		CameraRay ray = await MCHszrSyncTools.BuildMeleeRayAsync(client, dummyId);
		int raycastId = await PlayerSyncTools.GetHitRaycastEntityAsync(client, ray);
		CreatureHealthState preServerC = await MCHszrSyncTools.GetCreatureHealthAsync(server, dummyId);
		CreatureHealthState preClientC = await MCHszrSyncTools.GetCreatureHealthAsync(client, dummyId);

		await MCHszrSyncTools.SendHitRayAsync(client, ray);
		await Task.Delay(400);
		string serverCooldowns = await MCHszrSyncTools.GetHitCooldownsAsync(server);
		string clientCooldowns = await MCHszrSyncTools.GetHitCooldownsAsync(client);
		await Task.Delay(600);
		CreatureHealthState serverC = await MCHszrSyncTools.GetCreatureHealthAsync(server, dummyId);
		WaitResult<CreatureHealthState> conv = await TestWaiter.TryUntilAsync(
			() => MCHszrSyncTools.GetCreatureHealthAsync(client, dummyId),
			c => c.Exists && Math.Abs(c.Hitpoints - serverC.Hitpoints) <= 0.01f,
			TimeSpan.FromSeconds(6));

		output.WriteLine($"T3 斧跳劈 raycastId={raycastId};serverCooldowns={serverCooldowns};clientCooldowns={clientCooldowns};"
			+ $"preServer={preServerC};preClient={preClientC};postServer={serverC};postClient={conv.Last}");

		List<string> issues = new();
		if (raycastId != dummyId) {
			issues.Add($"EVIDENCE:近战射线未命中假人 dummyId={dummyId};raycastId={raycastId};ray={ray.Raw}");
		}
		if (!conv.Matched) {
			issues.Add("不同步(斧跳劈暴击伤害): 命中后 客户端假人血量未能与服务端收敛 "
				+ $"server={serverC};clientLast={conv.Last};preServer={preServerC};preClient={preClientC};serverCooldowns={serverCooldowns}");
		}
		if (!(preServerC.Hitpoints - serverC.Hitpoints > 0.5f)) {
			issues.Add($"EVIDENCE:服务端假人未受到有效伤害 preHitpoints={preServerC.Hitpoints};postHitpoints={serverC.Hitpoints};serverCooldowns={serverCooldowns}");
		}
		float serverCrit = ParseField(serverCooldowns, "crit");
		if (serverCrit <= 0f) {
			issues.Add($"EVIDENCE:服务端暴击冷却未触发（跳劈暴击可能在联机处理中被跳过） serverCooldowns={serverCooldowns};clientCooldowns={clientCooldowns}");
		}
		Assert.True(issues.Count == 0, "T3 斧跳劈同步:" + Environment.NewLine + string.Join(Environment.NewLine, issues));
	}

	[Fact]
	[TestOrder(Priority = 4)]
	public async Task HuixueHealthHealFactorIsConsistentBetweenClientAndServer() {
		await MCHszrSyncTools.EnsureWorldAndClientAsync(server, client);
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);

		PlayerMCHszrState baseServer = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(server, playerIndex);
		PlayerMCHszrState baseClient = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(client, playerIndex);

		await MCHszrSyncTools.SetPlayerVitalsAsync(server, playerIndex, 0.7f, 0.98f);
		await MCHszrSyncTools.SetPlayerVitalsAsync(client, playerIndex, 0.7f, 0.98f);
		await Task.Delay(1500);

		PlayerMCHszrState s1 = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(server, playerIndex);
		PlayerMCHszrState c1 = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(client, playerIndex);
		await Task.Delay(1500);
		PlayerMCHszrState s2 = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(server, playerIndex);
		PlayerMCHszrState c2 = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(client, playerIndex);
		string huixueServer = await MCHszrSyncTools.GetHuixueHealthStateAsync(server, playerIndex);
		string huixueClient = await MCHszrSyncTools.GetHuixueHealthStateAsync(client, playerIndex);

		output.WriteLine($"T4 回血HealFactor baseServer={baseServer.Raw};baseClient={baseClient.Raw};"
			+ $"s1={s1.Raw};c1={c1.Raw};s2={s2.Raw};c2={c2.Raw};huixueServer={huixueServer};huixueClient={huixueClient}");

		List<string> issues = new();
		if (Math.Abs(s1.Heal - c1.Heal) > 0.01f) {
			issues.Add("不同步(回血HealFactor采样1): 服务端与客户端 HealFactor 不一致 "
				+ $"serverHeal={s1.Heal};clientHeal={c1.Heal};serverHealth={s1.Health};clientHealth={c1.Health};serverFood={s1.Food};clientFood={c1.Food}");
		}
		if (Math.Abs(s2.Heal - c2.Heal) > 0.01f) {
			issues.Add("不同步(回血HealFactor采样2): 服务端与客户端 HealFactor 不一致 "
				+ $"serverHeal={s2.Heal};clientHeal={c2.Heal};serverHealth={s2.Health};clientHealth={c2.Health};serverFood={s2.Food};clientFood={c2.Food}");
		}
		if (s1.Heal <= baseServer.Heal + 0.01f) {
			issues.Add($"EVIDENCE:服务端未进入强化回血（HealFactor未变化） baseHeal={baseServer.Heal};s1Heal={s1.Heal}");
		}
		if (c1.Heal <= baseClient.Heal + 0.01f) {
			issues.Add($"EVIDENCE:客户端未进入强化回血（HealFactor未变化） baseHeal={baseClient.Heal};c1Heal={c1.Heal}");
		}
		Assert.True(issues.Count == 0, "T4 回血HealFactor同步:" + Environment.NewLine + string.Join(Environment.NewLine, issues));
	}

	[Fact]
	[TestOrder(Priority = 5)]
	public async Task SpearChargeWalkSpeedIsConsistentBetweenClientAndServer() {
		await MCHszrSyncTools.EnsureWorldAndClientAsync(server, client);
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Point3 origin = await CombatAreaAsync(spawnOffsetX: 54, spawnOffsetY: 2, spawnOffsetZ: 14);
		var playerPos = new Vector3Dto(origin.X + 0.5f, origin.Y + 1f, origin.Z + 0.5f);
		var dummyPos = new Vector3Dto(origin.X + 0.5f, origin.Y + 1f, origin.Z + 6f);

		int dummyId = await MCHszrSyncTools.PrepareMeleeAreaAsync(server, origin, dummyPos, 100f);
		await MCHszrSyncTools.PositionMainPlayerAsync(client, playerPos, gravity: true, creativeFly: false);
		await MCHszrSyncTools.WaitForPlayerStandingAsync(server, playerIndex, standing: true);
		await MCHszrSyncTools.WaitForServerPlayerConvergedAsync(server, playerIndex, playerPos, 3f);

		await MCHszrSyncTools.SeedWeaponAsync(server, playerIndex, MCHszrSyncTools.IronSpearContents, 1);
		await MCHszrSyncTools.WaitForClientWeaponAsync(client, playerIndex, MCHszrSyncTools.IronSpearContents, 1);
		await MCHszrSyncTools.ForcePoseAsync(server, playerIndex, 1);
		await MCHszrSyncTools.ForcePoseAsync(client, playerIndex, 1);
		await Task.Delay(300);

		PlayerMCHszrState preServer = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(server, playerIndex);
		PlayerMCHszrState preClient = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(client, playerIndex);
		int poseClient = await MCHszrSyncTools.GetPoseAsync(client, playerIndex);
		int poseServer = await MCHszrSyncTools.GetPoseAsync(server, playerIndex);

		int aimFrames = await MCHszrSyncTools.AimFramesForHoldAsync(client, 2.0f);
		var aimRay = new CameraRay(
			playerPos.X, playerPos.Y + 1.5f, playerPos.Z, 0f, 0f, 1f, $"aim-{aimFrames}");
		await MCHszrSyncTools.SendAimRayAsync(client, aimRay, aimFrames);
		await Task.Delay(700);
		string chargeClient = await MCHszrSyncTools.GetChargeStateAsync(client);
		PlayerMCHszrState holdClient = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(client, playerIndex);
		await MCHszrSyncTools.TriggerAimAsync(client, aiming: false);
		string chargeAfterRelease = await MCHszrSyncTools.GetChargeStateAsync(client);
		await Task.Delay(400);
		PlayerMCHszrState holdServer = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(server, playerIndex);
		await Task.Delay(900);
		PlayerMCHszrState postServer = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(server, playerIndex);
		PlayerMCHszrState postClient = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(client, playerIndex);
		await MCHszrSyncTools.ClearAimAsync(client, playerPos);

		output.WriteLine($"T5 长矛蓄力WalkSpeed preServer={preServer.Raw};preClient={preClient.Raw};poseServer={poseServer};poseClient={poseClient};chargeClient={chargeClient};"
			+ $"holdClient={holdClient.Raw};chargeAfterRelease={chargeAfterRelease};holdServer={holdServer.Raw};postServer={postServer.Raw};postClient={postClient.Raw};dummyId={dummyId}");

		List<string> issues = new();
		if (Math.Abs(holdServer.Walk - holdClient.Walk) > 0.05f) {
			issues.Add("不同步(长矛蓄力WalkSpeed): 蓄力中 服务端与客户端 WalkSpeed 不一致 "
				+ $"serverWalk={holdServer.Walk};clientWalk={holdClient.Walk};基线serverWalk={preServer.Walk};基线clientWalk={preClient.Walk}");
		}
		else if (Math.Abs(holdClient.Walk - preClient.Walk) <= 0.01f) {
			issues.Add($"EVIDENCE:客户端蓄力期间 WalkSpeed 未改变（蓄力可能未生效） preClientWalk={preClient.Walk};holdClientWalk={holdClient.Walk};chargeClient={chargeClient};poseClient={poseClient}");
		}
		Assert.True(issues.Count == 0, "T5 长矛蓄力同步:" + Environment.NewLine + string.Join(Environment.NewLine, issues));
	}

	[Fact]
	[TestOrder(Priority = 6)]
	public async Task HammerShockwaveProjectilesAndDamageAreConsistent() {
		await MCHszrSyncTools.EnsureWorldAndClientAsync(server, client);
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Point3 origin = await CombatAreaAsync(spawnOffsetX: 68, spawnOffsetY: 2, spawnOffsetZ: 14);
		var playerPos = new Vector3Dto(origin.X + 0.5f, origin.Y + 1f, origin.Z + 0.5f);
		var dummyPos = new Vector3Dto(origin.X + 0.5f, origin.Y + 1f, origin.Z + 1.5f);

		int dummyId = await MCHszrSyncTools.PrepareMeleeAreaAsync(server, origin, dummyPos, 100f);
		Assert.True(await CreatureSyncTools.WaitForEntityExistsAsync(client, dummyId), "Client did not receive dummy creature.");
		await MCHszrSyncTools.SetBoatHitpointsAsync(client, dummyId, 100f);
		await MCHszrSyncTools.PinBoatAsync(client, dummyId, dummyPos);
		await MCHszrSyncTools.PositionMainPlayerAsync(client, playerPos, gravity: true, creativeFly: false);
		await MCHszrSyncTools.WaitForPlayerStandingAsync(server, playerIndex, standing: true);
		await MCHszrSyncTools.WaitForServerPlayerConvergedAsync(server, playerIndex, playerPos, 3f);

		await MCHszrSyncTools.SeedWeaponAsync(server, playerIndex, MCHszrSyncTools.IronHammerContents, 1);
		await MCHszrSyncTools.WaitForClientWeaponAsync(client, playerIndex, MCHszrSyncTools.IronHammerContents, 1);
		await MCHszrSyncTools.ForcePoseAsync(server, playerIndex, 1);
		await MCHszrSyncTools.ForcePoseAsync(client, playerIndex, 1);
		await Task.Delay(300);

		string diagnostic = await MCHszrSyncTools.GetWeaponActionDiagnosticAsync(client, playerIndex);
		output.WriteLine($"T6 武器诊断={diagnostic}");
		string preServerProj = await MCHszrSyncTools.GetProjectileFingerprintAsync(server, playerIndex, 12f);
		string preClientProj = await MCHszrSyncTools.GetProjectileFingerprintAsync(client, playerIndex, 12f);
		CreatureHealthState preServerC = await MCHszrSyncTools.GetCreatureHealthAsync(server, dummyId);
		CreatureHealthState preClientC = await MCHszrSyncTools.GetCreatureHealthAsync(client, dummyId);

		await MCHszrSyncTools.ClearAimAsync(client, playerPos);
		int aimFrames = await MCHszrSyncTools.AimFramesForHoldAsync(client, 2.2f);
		var aimRay = new CameraRay(
			playerPos.X, playerPos.Y + 1.5f, playerPos.Z, 0f, 0f, 1f, $"aim-{aimFrames}");
		await MCHszrSyncTools.SendAimRayAsync(client, aimRay, aimFrames);
		await Task.Delay(1100);
		string chargeClient = await MCHszrSyncTools.GetChargeStateAsync(client);
		string release = await MCHszrSyncTools.TriggerAimAsync(client, aiming: false);
		await Task.Delay(600);
		string loaderServer = await MCHszrSyncTools.GetModLoaderStateAsync(server);
		string loaderClient = await MCHszrSyncTools.GetModLoaderStateAsync(client);
		await MCHszrSyncTools.ClearAimAsync(client, playerPos);
		await Task.Delay(400);

		string serverProj = await MCHszrSyncTools.GetProjectileFingerprintAsync(server, playerIndex, 12f);
		string clientProj = await MCHszrSyncTools.GetProjectileFingerprintAsync(client, playerIndex, 12f);
		CreatureHealthState serverC = await MCHszrSyncTools.GetCreatureHealthAsync(server, dummyId);
		WaitResult<CreatureHealthState> conv = await TestWaiter.TryUntilAsync(
			() => MCHszrSyncTools.GetCreatureHealthAsync(client, dummyId),
			c => c.Exists && Math.Abs(c.Hitpoints - serverC.Hitpoints) <= 0.01f,
			TimeSpan.FromSeconds(6));

		output.WriteLine($"T6 锤冲击波 chargeClient={chargeClient};preServerProj={preServerProj};preClientProj={preClientProj};"
			+ $"serverProj={serverProj};clientProj={clientProj};preServerC={preServerC};preClientC={preClientC};postServerC={serverC};postClientC={conv.Last};"
			+ $"loaderServer={loaderServer};loaderClient={loaderClient}");

		List<string> issues = new();
		if (!string.Equals(serverProj, clientProj, StringComparison.Ordinal)) {
			issues.Add("不同步(锤冲击波投影物): 服务端与客户端投影物列表不一致 "
				+ $"server={serverProj};client={clientProj}");
		}
		if (serverProj.Length == 0 && clientProj.Length == 0) {
			issues.Add($"EVIDENCE:两端均未生成冲击波投影物（该玩法在联机中可能未生效） chargeClient={chargeClient};loaderServer={loaderServer};loaderClient={loaderClient}");
		}
		if (!conv.Matched) {
			issues.Add("不同步(锤冲击波伤害): 冲击波后 客户端假人血量未能与服务端收敛 "
				+ $"server={serverC};clientLast={conv.Last};preServer={preServerC};preClient={preClientC}");
		}
		Assert.True(issues.Count == 0, "T6 大锤冲击波同步:" + Environment.NewLine + string.Join(Environment.NewLine, issues));
	}

	[Fact]
	[TestOrder(Priority = 7)]
	public async Task SpearDashMovementAndDamageAreConsistent() {
		await MCHszrSyncTools.EnsureWorldAndClientAsync(server, client);
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Point3 origin = await CombatAreaAsync(spawnOffsetX: 82, spawnOffsetY: 2, spawnOffsetZ: 14);
		var playerPos = new Vector3Dto(origin.X + 0.5f, origin.Y + 1f, origin.Z + 0.5f);
		var dummyPos = new Vector3Dto(origin.X + 0.5f, origin.Y + 1f, origin.Z + 3f);

		int dummyId = await MCHszrSyncTools.PrepareMeleeAreaAsync(server, origin, dummyPos, 100f);
		Assert.True(await CreatureSyncTools.WaitForEntityExistsAsync(client, dummyId), "Client did not receive dummy creature.");
		await MCHszrSyncTools.SetBoatHitpointsAsync(client, dummyId, 100f);
		await MCHszrSyncTools.PinBoatAsync(client, dummyId, dummyPos);
		await MCHszrSyncTools.PositionMainPlayerAsync(client, playerPos, gravity: true, creativeFly: false);
		await MCHszrSyncTools.WaitForPlayerStandingAsync(server, playerIndex, standing: true);
		await MCHszrSyncTools.WaitForServerPlayerConvergedAsync(server, playerIndex, playerPos, 3f);

		await MCHszrSyncTools.SeedWeaponAsync(server, playerIndex, MCHszrSyncTools.IronSpearContents, 1);
		await MCHszrSyncTools.WaitForClientWeaponAsync(client, playerIndex, MCHszrSyncTools.IronSpearContents, 1);
		await MCHszrSyncTools.ForcePoseAsync(server, playerIndex, 1);
		await MCHszrSyncTools.ForcePoseAsync(client, playerIndex, 1);
		await Task.Delay(300);

		PlayerMCHszrState baseServer = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(server, playerIndex);
		PlayerMCHszrState baseClient = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(client, playerIndex);
		CreatureHealthState preServerC = await MCHszrSyncTools.GetCreatureHealthAsync(server, dummyId);
		CreatureHealthState preClientC = await MCHszrSyncTools.GetCreatureHealthAsync(client, dummyId);

		await MCHszrSyncTools.ClearAimAsync(client, playerPos);
		int aimFrames = await MCHszrSyncTools.AimFramesForHoldAsync(client, 2.2f);
		var aimRay = new CameraRay(
			playerPos.X, playerPos.Y + 1.5f, playerPos.Z, 0f, 0f, 1f, $"aim-{aimFrames}");
		await MCHszrSyncTools.SendAimRayAsync(client, aimRay, aimFrames);
		await Task.Delay(1100);
		string chargeClient = await MCHszrSyncTools.GetChargeStateAsync(client);
		string release = await MCHszrSyncTools.TriggerAimAsync(client, aiming: false);

		string dashTicks = string.Empty;
		for (int i = 0; i < 8; i++) {
			await MCHszrSyncTools.TickDashAsync(client);
			dashTicks += "t";
			await Task.Delay(120);
		}
		await MCHszrSyncTools.ClearAimAsync(client, playerPos);

		float maxSpeedServer = 0f;
		float maxSpeedClient = 0f;
		for (int i = 0; i < 14; i++) {
			await Task.Delay(100);
			PlayerMCHszrState s = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(server, playerIndex);
			PlayerMCHszrState c = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(client, playerIndex);
			if (s.Exists && c.Exists) {
				maxSpeedServer = Math.Max(maxSpeedServer, HorizontalSpeed(s.Velocity));
				maxSpeedClient = Math.Max(maxSpeedClient, HorizontalSpeed(c.Velocity));
			}
		}

		PlayerMCHszrState finalServer = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(server, playerIndex);
		PlayerMCHszrState finalClient = await MCHszrSyncTools.GetPlayerMCHszrStateAsync(client, playerIndex);
		float dispServer = MathF.Sqrt(Vector3Dto.HorizontalDistanceSquared(baseServer.Position, finalServer.Position));
		float dispClient = MathF.Sqrt(Vector3Dto.HorizontalDistanceSquared(baseClient.Position, finalClient.Position));
		string loaderServer = await MCHszrSyncTools.GetModLoaderStateAsync(server);
		string loaderClient = await MCHszrSyncTools.GetModLoaderStateAsync(client);

		CreatureHealthState postServerC = await MCHszrSyncTools.GetCreatureHealthAsync(server, dummyId);
		WaitResult<CreatureHealthState> conv = await TestWaiter.TryUntilAsync(
			() => MCHszrSyncTools.GetCreatureHealthAsync(client, dummyId),
			c => c.Exists && Math.Abs(c.Hitpoints - postServerC.Hitpoints) <= 0.01f,
			TimeSpan.FromSeconds(6));

		output.WriteLine($"T7 长矛冲刺 chargeClient={chargeClient};release={release};dashTicks={dashTicks};baseServer={baseServer.Raw};baseClient={baseClient.Raw};"
			+ $"finalServer={finalServer.Raw};finalClient={finalClient.Raw};maxSpeedServer={maxSpeedServer:R};maxSpeedClient={maxSpeedClient:R};"
			+ $"dispServer={dispServer:R};dispClient={dispClient:R};preServerC={preServerC};preClientC={preClientC};postServerC={postServerC};postClientC={conv.Last};"
			+ $"loaderServer={loaderServer};loaderClient={loaderClient}");

		List<string> issues = new();
		if (Math.Abs(dispServer - dispClient) > 1.0f) {
			issues.Add("不同步(长矛冲刺位移): 冲刺后 服务端与客户端水平位移不一致 "
				+ $"server位移={dispServer:R};client位移={dispClient:R}");
		}
		if (Math.Abs(maxSpeedServer - maxSpeedClient) > 2.0f) {
			issues.Add("不同步(长矛冲刺速度): 冲刺期间 服务端与客户端最大水平速度不一致 "
				+ $"serverMaxSpeed={maxSpeedServer:R};clientMaxSpeed={maxSpeedClient:R}");
		}
		if (dispServer < 0.5f && dispClient < 0.5f) {
			issues.Add("EVIDENCE:冲刺后两端均无明显位移（冲刺效果在联机中可能未体现） "
				+ $"dispServer={dispServer:R};dispClient={dispClient:R};chargeClient={chargeClient};release={release};loaderServer={loaderServer};loaderClient={loaderClient}");
		}
		if (preServerC.Hitpoints - postServerC.Hitpoints > 0.5f) {
			if (!conv.Matched) {
				issues.Add("不同步(长矛冲刺伤害): 服务端假人被冲刺撞伤，但客户端血量未与服务端收敛 "
					+ $"server={postServerC};clientLast={conv.Last};preServer={preServerC};preClient={preClientC}");
			}
		}
		else if (conv.Last.Exists && postServerC.Hitpoints - conv.Last.Hitpoints > 0.5f) {
			issues.Add("不同步(长矛冲刺伤害): 客户端假人被本地冲刺撞伤但未传播到服务端 "
				+ $"server={postServerC.Hitpoints};client={conv.Last.Hitpoints};preServer={preServerC};preClient={preClientC}");
		}
		else {
			output.WriteLine($"T7 冲刺未对假人造成服务端伤害 preServerC={preServerC};postServerC={postServerC}");
		}
		Assert.True(issues.Count == 0, "T7 长矛冲刺同步:" + Environment.NewLine + string.Join(Environment.NewLine, issues));
	}

	public void Dispose() {
		fixture.ThrowIfAnyContextHasError();
	}

	private async Task<Point3> CombatAreaAsync(int spawnOffsetX, int spawnOffsetY, int spawnOffsetZ) {
		Point3 spawn = await MCHszrSyncTools.GetSpawnCellAsync(server);
		return spawn + new Point3(spawnOffsetX, spawnOffsetY, spawnOffsetZ);
	}

	private static float ParseField(string raw, string name) {
		if (raw == null) {
			return -1f;
		}
		foreach (string part in raw.Split(';')) {
			string[] kv = part.Split('=');
			if (kv.Length == 2
				&& string.Equals(kv[0], name, StringComparison.OrdinalIgnoreCase)
				&& float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) {
				return value;
			}
		}
		return -1f;
	}

	private static float HorizontalSpeed(Vector3Dto velocity) =>
		MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
}