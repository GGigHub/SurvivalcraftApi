using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Engine;
using Xunit;
using Xunit.Abstractions;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class IndustriaSyncTest : IClassFixture<IndustriaGameFixture>, IDisposable
{
	private readonly IndustriaGameFixture fixture;

	private readonly ITestOutputHelper output;

	private GameContext server => fixture.Server;

	private GameContext client => fixture.Client;

	public IndustriaSyncTest(IndustriaGameFixture fixture, ITestOutputHelper output)
	{
		this.fixture = fixture;
		this.output = output;
	}

	[Fact]
	[TestOrder(Priority = 1)]
	public async Task IndustriaAndRecipaediaExLoadInDependencyOrder()
	{
		await IndustriaSyncTools.EnsureWorldAndClientAsync(fixture, "industria-sync");
		string serverState = await IndustriaSyncTools.GetModsStateAsync(server);
		string clientState = await IndustriaSyncTools.GetModsStateAsync(client);
		output.WriteLine("[server] " + serverState);
		output.WriteLine("[client] " + clientState);
		Assert.Contains("com.recipaediaex", serverState, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("com.IndustrialEra2", serverState, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("scienew=loaded", serverState, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("voltnet-present", serverState, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("com.recipaediaex", clientState, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("com.IndustrialEra2", clientState, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("scienew=loaded", clientState, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("voltnet-present", clientState, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	[TestOrder(Priority = 2)]
	public async Task DeviceBlockTerrainAndBlockEntitiesSynchronize()
	{
		Point3 origin = await IndustriaSyncTools.SetupMachineBlockAsync(fixture);
		await IndustriaSyncTools.WaitForFingerprintMatchAsync(
			server,
			client,
			() => IndustriaSyncTools.GetTerrainFingerprintAsync(server, origin, -2, -2, -2, 7, 3, 2),
			() => IndustriaSyncTools.GetTerrainFingerprintAsync(client, origin, -2, -2, -2, 7, 3, 2),
			"Device block terrain fingerprint");
	}

	[Fact]
	[TestOrder(Priority = 3)]
	public async Task ElectricFurnaceSmeltingStateDoesNotSynchronize()
	{
		Point3 origin = await IndustriaSyncTools.SetupMachineBlockAsync(fixture);
		Point3 furnace = origin + new Point3(2, 0, 0);
		string addResult = await IndustriaSyncTools.AddElectricFurnaceOreAsync(server, furnace);
		Assert.Equal("added", addResult);
		await Task.Delay(4000);
		string serverVolt = await IndustriaSyncTools.GetVoltNetStateAsync(server);
		string serverFp = await IndustriaSyncTools.GetElectricFurnaceStateAsync(server, furnace);
		string clientFp = await IndustriaSyncTools.PrepareClientDeviceReadAsync(client, origin, () => IndustriaSyncTools.GetElectricFurnaceStateAsync(client, furnace));
		output.WriteLine("Evidence: serverVolt=" + serverVolt);
		output.WriteLine("Evidence: server=" + serverFp + " / client=" + clientFp);
		Dictionary<string, string> serverValues = Parse(serverFp);
		Dictionary<string, string> clientValues = Parse(clientFp);
		Assert.True(clientValues.ContainsKey("fire"), "Client did not expose furnace fire time: " + clientFp);
		Assert.True(GetFloat(clientValues, "fire") <= 0.01f, "Client furnace fire time unexpectedly exceeded 0: " + clientFp);
		Assert.True(GetFloat(clientValues, "heat") <= 0.01f, "Client furnace heat unexpectedly exceeded 0: " + clientFp);
		Assert.True(GetFloat(clientValues, "progress") <= 0.01f, "Client furnace progress unexpectedly exceeded 0: " + clientFp);
		Assert.True(GetFloat(serverValues, "fire") <= 0.01f, "Server furnace fire time unexpectedly exceeded 0 (see mod ordering bug): " + serverFp);
		Assert.True(GetFloat(serverValues, "heat") <= 0.01f, "Server furnace heat unexpectedly exceeded 0 (see mod ordering bug): " + serverFp);
	}

	[Fact]
	[TestOrder(Priority = 4)]
	public async Task ElectricFurnaceInventorySlotsDoNotSynchronizeToClient()
	{
		Point3 origin = await IndustriaSyncTools.SetupMachineBlockAsync(fixture);
		Point3 furnace = origin + new Point3(2, 0, 0);
		string addResult = await IndustriaSyncTools.AddElectricFurnaceOreAsync(server, furnace);
		Assert.Equal("added", addResult);
		string serverSlots = Parse(await IndustriaSyncTools.GetElectricFurnaceStateAsync(server, furnace)).GetValueOrDefault("slots", string.Empty);
		Assert.Contains("39:", serverSlots);
		await Task.Delay(12000);
		string clientSlots = Parse(await IndustriaSyncTools.PrepareClientDeviceReadAsync(client, origin, () => IndustriaSyncTools.GetElectricFurnaceStateAsync(client, furnace))).GetValueOrDefault("slots", string.Empty);
		output.WriteLine("Slots sync evidence: server=" + serverSlots + " / client=" + clientSlots);
		Assert.NotEqual(serverSlots, clientSlots);
		Assert.DoesNotContain("39:", clientSlots);
	}

	[Fact]
	[TestOrder(Priority = 5)]
	public async Task VoltNetTopologySynchronizesButWorkStateDiverges()
	{
		Point3 origin = await IndustriaSyncTools.SetupMachineBlockAsync(fixture);
		await Task.Delay(3000);
		string serverVolt = await IndustriaSyncTools.GetVoltNetStateAsync(server);
		string clientVolt = await IndustriaSyncTools.GetVoltNetStateAsync(client);
		output.WriteLine("Evidence: server=" + serverVolt + " / client=" + clientVolt);
		string serverTopology = serverVolt.Split(';')[0];
		string clientTopology = clientVolt.Split(';')[0];
		Assert.Equal(serverTopology, clientTopology);
		Assert.NotEqual(serverVolt, clientVolt);
	}

	[Fact]
	[TestOrder(Priority = 6)]
	public async Task FluidTankContentsDoNotSynchronizeToClient()
	{
		Point3 origin = await IndustriaSyncTools.SetupMachineBlockAsync(fixture);
		Point3 tank = origin + new Point3(4, 0, 0);
		string addResult = await IndustriaSyncTools.AddFluidTankWaterAsync(server, tank, 10f);
		Assert.Contains("value=", addResult);
		Assert.Contains("volume=", addResult);
		await Task.Delay(12000);
		string serverTank = await IndustriaSyncTools.GetFluidTankStateAsync(server, tank);
		string clientTank = await IndustriaSyncTools.PrepareClientDeviceReadAsync(client, origin, () => IndustriaSyncTools.GetFluidTankStateAsync(client, tank));
		output.WriteLine("Tank sync evidence: server=" + serverTank + " / client=" + clientTank);
		Assert.Contains("value=18;", serverTank);
		Assert.Contains("volume=10", serverTank);
		Assert.NotEqual(serverTank, clientTank);
		Assert.Contains("value=0;", clientTank);
	}

	[Fact]
	[TestOrder(Priority = 7)]
	public async Task GalvanicCellBatteryEnergyDoesNotSynchronize()
	{
		Point3 origin = await IndustriaSyncTools.SetupMachineBlockAsync(fixture);
		Point3 cell = origin + new Point3(3, 0, 0);
		await Task.Delay(5000);
		string serverCell = await IndustriaSyncTools.GetGalvanicCellStateAsync(server, cell);
		string clientCell = await IndustriaSyncTools.GetGalvanicCellStateAsync(client, cell);
		output.WriteLine("Evidence: server=" + serverCell + " / client=" + clientCell);
		Assert.Contains("energy=", serverCell);
		Assert.Contains("energy=", clientCell);
		Dictionary<string, string> serverValues = Parse(serverCell);
		Dictionary<string, string> clientValues = Parse(clientCell);
		float.TryParse(serverValues.GetValueOrDefault("energy", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out float serverEnergy);
		float.TryParse(clientValues.GetValueOrDefault("energy", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out float clientEnergy);
		Assert.True(serverEnergy < 20f, "Server battery was not draining: " + serverCell);
		Assert.NotEqual(serverCell, clientCell);
		Assert.Equal(20f, clientEnergy);
	}

	public void Dispose()
	{
		fixture.ThrowIfAnyContextHasError();
	}

	private static float GetFloat(Dictionary<string, string> values, string key)
	{
		return float.TryParse(values.GetValueOrDefault(key, string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : -1f;
	}

	private static Dictionary<string, string> Parse(string value)
	{
		return value
			.Split(';', StringSplitOptions.RemoveEmptyEntries)
			.Select(p => p.Split('=', 2))
			.Where(p => p.Length == 2)
			.ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
	}
}