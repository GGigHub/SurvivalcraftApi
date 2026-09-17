using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Engine;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class SushitoolSyncTest : IClassFixture<SushitoolGameFixture>, IDisposable
{
	private const int PlanksBlockIndex = 21;

	private readonly SushitoolGameFixture fixture;

	private GameContext server => fixture.Server;

	private GameContext client => fixture.Client;

	public SushitoolSyncTest(SushitoolGameFixture fixture)
	{
		this.fixture = fixture;
	}

	[Fact]
	[TestOrder(Priority = 1)]
	public async Task SushiPackagesInstallAndSushiToolAssemblyIsPatched()
	{
		await EnsureWorldAndClientAsync();
		string serverFingerprint = await SushitoolSyncTools.GetInstallFingerprintAsync(server);
		string clientFingerprint = await SushitoolSyncTools.GetInstallFingerprintAsync(client);
		AssertContains(serverFingerprint, "sushibase=True");
		AssertContains(serverFingerprint, "sushitool=True");
		AssertContains(serverFingerprint, "sushitouch=True");
		AssertContains(serverFingerprint, "compatnet=True");
		AssertContains(serverFingerprint, "network=True");
		AssertContains(serverFingerprint, "testautomation=True");
		AssertContains(serverFingerprint, "syncboxSetSlotValue=True");
		AssertContains(serverFingerprint, "pkgs=");
		Assert.Contains("SushiBase", serverFingerprint, StringComparison.Ordinal);
		Assert.Contains("SushiTool", serverFingerprint, StringComparison.Ordinal);
		Assert.Contains("SushiTouch", serverFingerprint, StringComparison.Ordinal);
		AssertContains(clientFingerprint, "sushibase=True");
		AssertContains(clientFingerprint, "sushitool=True");
		AssertContains(clientFingerprint, "sushitouch=True");
		AssertContains(clientFingerprint, "compatnet=True");
		AssertContains(clientFingerprint, "syncboxSetSlotValue=True");
	}

	[Fact]
	[TestOrder(Priority = 2)]
	public async Task SushiSyncBoxSharedChannelInventoryPartialSync()
	{
		await EnsureWorldAndClientAsync();
		Point3 origin = await SushitoolSyncTools.GetPlayerCellAsync(client);
		Assert.Equal(await SushitoolSyncTools.GetPlayerCellAsync(server), origin);
		Point3 boxA = origin + new Point3(2, 0, 0);
		Point3 boxB = origin + new Point3(4, 0, 0);
		await SushitoolSyncTools.WaitForTerrainSyncAsync(client, server, boxA);
		AssertContains(await SushitoolSyncTools.PlaceSushiBlockAsync(server, boxA, SushitoolSyncTools.SushiSyncBoxBlockTypeName, 0), "placed:");
		AssertContains(await SushitoolSyncTools.PlaceSushiBlockAsync(server, boxB, SushitoolSyncTools.SushiSyncBoxBlockTypeName, 0), "placed:");
		await SushitoolSyncTools.WaitForBlockEntityAsync(client, boxA);
		await SushitoolSyncTools.WaitForBlockEntityAsync(client, boxB);
		await Task.Delay(400);

		string serverRest = await SushitoolSyncTools.GetChannelFingerprintAsync(server);
		string clientRest = await SushitoolSyncTools.GetChannelFingerprintAsync(client);
		string restEvidence = $"rest server[]={serverRest}; client[]={clientRest}";

		await SushitoolSyncTools.MutateChannelInventoryAsync(server, 0, 0, PlanksBlockIndex, 2);
		await Task.Delay(1200);
		string serverAfterContent = await SushitoolSyncTools.GetChannelFingerprintAsync(server);
		string clientAfterContent = await SushitoolSyncTools.WaitForChannelMatchAsync(
			client, serverAfterContent, new[] { "slotCapacity", "ch0=" });

		Assert.True(
			string.Equals(NormalizeChannel(serverAfterContent), NormalizeChannel(clientAfterContent), StringComparison.Ordinal),
			"SushiTool shared-channel item content did NOT reach the client even with matching slot counts. "
			+ $"{restEvidence}; after-content server[]={serverAfterContent}; client[]={clientAfterContent}. "
			+ $"serverInvReg[]={await SushitoolSyncTools.GetInventoryRegistrationDiagAsync(server)}; "
			+ $"clientInvReg[]={await SushitoolSyncTools.GetInventoryRegistrationDiagAsync(client)}.");

		string resizeResult = await SushitoolSyncTools.ResizeChannelInventoryAsync(server, 0, 30, PlanksBlockIndex, 3);
		AssertContains(resizeResult, "resized:30");
		await SushitoolSyncTools.MutateChannelInventoryAsync(server, 0, 0, PlanksBlockIndex, 2);
		await Task.Delay(1200);

		string serverAfterResize = await SushitoolSyncTools.GetChannelFingerprintAsync(server);
		string clientAfterResize = await SushitoolSyncTools.WaitForChannelMatchAsync(
			client, serverAfterResize, new[] { "slotCapacity", "ch0=" });

		Assert.True(
			string.Equals(NormalizeChannel(serverAfterResize), NormalizeChannel(clientAfterResize), StringComparison.Ordinal),
			"SushiTool shared-channel slot COUNT did NOT stay in sync after a server-side resize. "
			+ $"{restEvidence}; after-content server[]={serverAfterContent}; client[]={clientAfterContent}; "
			+ $"after-resize server[]={serverAfterResize}; client[]={clientAfterResize}. "
			+ "Observation: item content reaches the client when both sides agree on slot count, but the "
			+ "capacity/count itself never syncs (server=30, client=36) because CompatNet's ApplyChannel refuses "
			+ "channel snapshots whose slot count disagrees with the local inventory and nothing resizes the "
			+ "client-side SushiSyncInventory. Shared channel inventories therefore end up partially desynchronized.");
	}

	[Fact]
	[TestOrder(Priority = 3)]
	public async Task SushiMachineStartAndStopStateSynchronizesBothDirections()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Assert.Equal(await SushitoolSyncTools.GetPlayerCellAsync(client), await SushitoolSyncTools.GetPlayerCellAsync(server));
		Point3 origin = await SushitoolSyncTools.GetPlayerCellAsync(server);
		Point3 digger = origin + new Point3(1, 0, 0);
		await SushitoolSyncTools.WaitForTerrainSyncAsync(client, server, digger);
		AssertContains(await SushitoolSyncTools.PlaceSushiBlockAsync(server, digger, SushitoolSyncTools.SushiDiggerBlockTypeName, 0), "placed:");
		await SushitoolSyncTools.WaitForBlockEntityAsync(client, digger);
		await Task.Delay(400);

		string serverRest = await SushitoolSyncTools.GetMachineFingerprintAsync(server, digger);
		string clientRest = await SushitoolSyncTools.GetMachineFingerprintAsync(client, digger);
		int machineEntityId = ParseMachineEntityId(serverRest);
		Assert.True(machineEntityId > 0, serverRest);

		string setResult = await SushitoolSyncTools.SetMachineWorkStateAsync(server, digger, loopWork: true, startWork: true);
		AssertContains(setResult, "set");
		Assert.Contains("m_startWork=True", await SushitoolSyncTools.GetMachineFingerprintAsync(server, digger), StringComparison.Ordinal);
		await Task.Delay(1200);
		string clientAfterSet = await SushitoolSyncTools.GetMachineFingerprintAsync(client, digger);

		string controlResult = await SushitoolSyncTools.SendMachineControlRequestAsync(client, playerIndex, machineEntityId, enabled: false);
		AssertContains(controlResult, "queued=True");
		await Task.Delay(700);
		string serverPostRequest = await SushitoolSyncTools.GetMachineFingerprintAsync(server, digger);
		string serverFinal = serverPostRequest;
		string clientFinal = await SushitoolSyncTools.GetMachineFingerprintAsync(client, digger);
		IList<string> diffs = CompareMachineFingerprints(serverFinal, clientFinal);
		bool serverStopped = serverPostRequest.Contains("m_startWork=False", StringComparison.Ordinal);
		Assert.True(
			diffs.Count == 0,
			"SushiTool machine state did NOT stay in sync after both directions were exercised. "
			+ $"server[]={serverFinal}; client[]={clientFinal}; differing=[{string.Join(",", diffs)}]; "
			+ $"clientAfterSet={clientAfterSet}. "
			+ $"\nRequestObservations: machineEntityId={machineEntityId} serverStopped={serverStopped}. "
			+ "\nObservation: the client queues and transmits the control request and the server handler applies its "
			+ $"MachineInteractionDistanceSquared=64f (8-block) proximity guard (so whether the server stops depends "
			+ $"on player proximity). REGARDLESS, the server→client scalar state snapshots never mirror: server is "
			+ $"working but client stays at its initial state, because CompatNetStateRuntime keys each record by the "
			+ $"SERVER entity id (id={machineEntityId}) and the client creates its own block entity with an "
			+ $"independent id, so the client's FindEntity(record.EntityId) cannot resolve the record and the "
			+ $"snapshot is dropped.");
	}

	[Fact]
	[TestOrder(Priority = 4)]
	public async Task SushiTrainSpawnsAndRidingControlIsObservedOnBothSides()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Point3 origin = await SushitoolSyncTools.GetPlayerCellAsync(server);
		for (int i = 0; i <= 5; i++)
		{
			Point3 rail = origin + new Point3(0, 0, -i);
			await SushitoolSyncTools.WaitForTerrainSyncAsync(client, server, rail);
			AssertContains(await SushitoolSyncTools.PlaceSushiBlockAsync(server, rail, SushitoolSyncTools.SushiRailBlockTypeName, 0), "placed:");
		}
		Point3 spawnCell = origin + new Point3(0, 1, 0);
		await SushitoolSyncTools.SpawnSushiTrainAsync(server, spawnCell);
		await SushitoolSyncTools.WaitForTrainAsync(server);
		await SushitoolSyncTools.WaitForTrainAsync(client);
		await Task.Delay(300);

		string serverInitial = await SushitoolSyncTools.GetTrainFingerprintAsync(server);
		string clientInitial = await SushitoolSyncTools.GetTrainFingerprintAsync(client);
		int trainEntityId = ParseTrainEntityId(serverInitial);
		Assert.True(trainEntityId > 0, serverInitial);

		PlayerRuntimeState player = await PlayerSyncTools.GetPlayerStateAsync(server, playerIndex);
		Assert.True(player.EntityId > 0, $"Client player has no entity on the server: {player}");
		int clientTrainEntityId = ParseTrainEntityId(clientInitial);
		Assert.True(clientTrainEntityId > 0, clientInitial);
		string mountResult = await SushitoolSyncTools.MountClientPlayerOnTrainAsync(client, playerIndex, clientTrainEntityId);
		AssertContains(mountResult, "mounted:");
		await SushitoolSyncTools.WaitForTrainRiderAsync(client, 0, serverInitial);
		await SushitoolSyncTools.WaitForTrainRiderAsync(server, player.EntityId, clientInitial);

		string controlResult = await SushitoolSyncTools.SendTrainControlRequestAsync(client, playerIndex, trainEntityId, speedDelta: 1, turnDelta: 0);
		AssertContains(controlResult, "queued=True");
		AssertContains(await SushitoolSyncTools.DriveSushiTrainAsync(server, trainEntityId), "driven");
		await Task.Delay(3000);

		string serverAfterControl = await SushitoolSyncTools.GetTrainFingerprintAsync(server);
		string clientAfterControl = await SushitoolSyncTools.GetTrainFingerprintAsync(client);
		Assert.True(HasAnyRider(clientAfterControl), $"Client train does not report a rider: {clientAfterControl}");
		Assert.True(HasAnyRider(serverAfterControl), $"Server train does not report a rider: {serverAfterControl}");

		string serverFinal = await SushitoolSyncTools.GetTrainFingerprintAsync(server);
		string clientFinal = await SushitoolSyncTools.GetTrainFingerprintAsync(client);
		Vector3 serverPosition = ParseTrainPosition(serverFinal);
		Vector3 clientPosition = ParseTrainPosition(clientFinal);
		float distance = Vector3.Distance(serverPosition, clientPosition);
		Assert.True(
			distance <= 2f,
			"SushiTool train position drifted too far from the server-authoritative position. "
			+ $"server[]={serverFinal}; client[]={clientFinal}; distance={distance.ToString("R", CultureInfo.InvariantCulture)}");
	}

	public void Dispose()
	{
		fixture.ThrowIfAnyContextHasError();
	}

	private async Task EnsureWorldAndClientAsync()
	{
		if (!(await SushitoolSyncTools.IsProjectReadyAsync(server)))
		{
			await PlayerSyncTools.CreateCreativeTestWorldAsync(server, "sushitool-sync");
		}
		if (!(await SushitoolSyncTools.IsProjectReadyAsync(client)))
		{
			await GameTools.ConnectTestServer(client, server.GamePort);
		}
		await PlayerSyncTools.WaitForPlayerEntityAsync(playerIndex: await PlayerSyncTools.WaitForClientPlayerIndexAsync(client), context: server);
	}

	private static void AssertContains(string fingerprint, string expected)
	{
		Assert.Contains(expected, fingerprint, StringComparison.Ordinal);
	}

	private static string NormalizeChannel(string fingerprint)
	{
		int index = fingerprint.IndexOf("slotCapacity", StringComparison.Ordinal);
		return index >= 0 ? fingerprint[index..] : fingerprint;
	}

	private static int ParseMachineEntityId(string fingerprint)
	{
		string[] segments = fingerprint.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string segment in segments)
		{
			string[] parts = segment.Split('=');
			if (parts.Length == 2 && parts[0] == "id" && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
			{
				return value;
			}
		}
		return -1;
	}

	private static int ParseTrainEntityId(string fingerprint)
	{
		if (string.IsNullOrEmpty(fingerprint) || fingerprint == "no-trains")
		{
			return -1;
		}
		int colon = fingerprint.IndexOf(':');
		if (colon <= 0)
		{
			return -1;
		}
		return int.TryParse(fingerprint[..colon], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : -1;
	}

	private static Vector3 ParseTrainPosition(string fingerprint)
	{
		int p = fingerprint.IndexOf(":p=", StringComparison.Ordinal);
		if (p < 0)
		{
			return Vector3.Zero;
		}
		int start = p + ":p=".Length;
		int end = fingerprint.IndexOf(':', start);
		if (end < 0)
		{
			end = fingerprint.Length;
		}
		string[] parts = fingerprint[start..end].Split(',');
		if (parts.Length != 3)
		{
			return Vector3.Zero;
		}
		return new Vector3(
			float.Parse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture),
			float.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture),
			float.Parse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture));
	}

	private static bool HasAnyRider(string fingerprint)
	{
		int start = fingerprint.IndexOf("rider=", StringComparison.Ordinal);
		if (start < 0)
		{
			return false;
		}
		int next = fingerprint.IndexOf('|', start);
		string value = next >= 0 ? fingerprint[start..next] : fingerprint[start..];
		return value != "rider=-1";
	}

	private static IList<string> CompareMachineFingerprints(string serverFingerprint, string clientFingerprint)
	{
		IList<string> diffs = new List<string>();
		string[] ignored = { "id", "m_infoText", "m_virtualPlace", "m_coordinates", "m_workOffset", "m_width", "m_height", "m_depth" };
		Dictionary<string, string> serverMap = ParseFields(serverFingerprint);
		Dictionary<string, string> clientMap = ParseFields(clientFingerprint);
		foreach (string key in serverMap.Keys.Union(clientMap.Keys))
		{
			if (ignored.Contains(key, StringComparer.Ordinal))
			{
				continue;
			}
			string serverValue = serverMap.TryGetValue(key, out string s) ? s : "<missing>";
			string clientValue = clientMap.TryGetValue(key, out string c) ? c : "<missing>";
			if (!string.Equals(serverValue, clientValue, StringComparison.Ordinal))
			{
				diffs.Add($"{key}:server={serverValue};client={clientValue}");
			}
		}
		return diffs;
	}

	private static Dictionary<string, string> ParseFields(string fingerprint)
	{
		Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (string segment in fingerprint.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
		{
			int equals = segment.IndexOf('=');
			if (equals > 0)
			{
				map[segment[..equals]] = segment[(equals + 1)..];
			}
		}
		return map;
	}
}