using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public sealed class AnimalPositionSyncTest : IClassFixture<GameFixture>, IDisposable {
    private const int MaxObservedAnimals = 10;
    private const int MaxConvergenceAttempts = 20;
    private static readonly TimeSpan ConvergencePollInterval = TimeSpan.FromMilliseconds(100);
    public const float MaxPositionDistance = 5f;

    private const string GetExistingAnimalsCode = """
        var project = Game.GameManager.Project;
        if (project == null) {
            return string.Empty;
        }

        return string.Join(
            ";",
            project.Entities
                .Select(entity => new {
                    Entity = entity,
                    Creature = entity.FindComponent<ComponentCreature>(),
                    Body = entity.FindComponent<ComponentBody>()
                })
                .Where(item =>
                    item.Creature != null
                    && item.Entity.FindComponent<ComponentPlayer>() == null
                    && item.Body != null)
                .OrderBy(item => item.Entity.Id)
                .Take(__MAX_COUNT__)
                .Select(item => string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}|{1}|{2}",
                    item.Entity.Id,
                    item.Entity.ValuesDictionary.DatabaseObject.Name,
                    item.Creature.DisplayName)));
        """;

    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public AnimalPositionSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact]
    public async Task TestExistingAnimalBodyPositionsStayCloseAtFiveTenTwentyThirtyAndSixtySeconds() {
        await GameTools.CreateTestWorld(server, "animal-position-observer-sync");
        await GameTools.ConnectTestServer(client, server.GamePort);

        AnimalObservationTarget[] animals = await GetExistingAnimalsAsync(server);
        Assert.NotEmpty(animals);
        Assert.True(animals.Length <= MaxObservedAnimals, $"Observed too many animals: {animals.Length}.");

        Stopwatch stopwatch = Stopwatch.StartNew();
        List<AnimalPositionObservation> observations = [];
        observations.AddRange(await ObserveExistingAnimalPositionsAtAsync(animals, stopwatch, TimeSpan.FromSeconds(5)));
        observations.AddRange(await ObserveExistingAnimalPositionsAtAsync(animals, stopwatch, TimeSpan.FromSeconds(10)));
        observations.AddRange(await ObserveExistingAnimalPositionsAtAsync(animals, stopwatch, TimeSpan.FromSeconds(20)));
        observations.AddRange(await ObserveExistingAnimalPositionsAtAsync(animals, stopwatch, TimeSpan.FromSeconds(30)));
        observations.AddRange(await ObserveExistingAnimalPositionsAtAsync(animals, stopwatch, TimeSpan.FromSeconds(60)));

        AnimalPositionObservation[] outOfRangeObservations = observations
            .Where(observation => !observation.IsWithinRange)
            .ToArray();

        Assert.Contains(observations, observation => observation.ClientChunkLoaded && observation.ClientState.Exists);
        Assert.True(
            outOfRangeObservations.Length == 0,
            "Animal positions exceeded 5 blocks:\n" + string.Join("\n", observations));
    }

    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }

    private async Task<IReadOnlyList<AnimalPositionObservation>> ObserveExistingAnimalPositionsAtAsync(
        IReadOnlyList<AnimalObservationTarget> animals,
        Stopwatch stopwatch,
        TimeSpan elapsedSinceConnect) {
        TimeSpan remaining = elapsedSinceConnect - stopwatch.Elapsed;
        if (remaining > TimeSpan.Zero) {
            await Task.Delay(remaining, fixture.ErrorCts.Token);
        }

        List<AnimalPositionObservation> observations = [];
        foreach (AnimalObservationTarget animal in animals) {
            AnimalPositionObservation? observation = await ObserveAnimalPositionAsync(
                elapsedSinceConnect,
                animal);
            if (observation.HasValue) {
                observations.Add(observation.Value);
            }
        }

        return observations;
    }

    private async Task<AnimalPositionObservation?> ObserveAnimalPositionAsync(
        TimeSpan elapsedSinceConnect,
        AnimalObservationTarget animal) {
        for (int attempt = 0; attempt < MaxConvergenceAttempts; attempt++) {
            Task<BodySyncState> serverStateTask = CreatureSyncTools.GetBodyStateAsync(server, animal.Id);
            Task<BodySyncState> clientStateTask = CreatureSyncTools.GetBodyStateAsync(client, animal.Id);
            await Task.WhenAll(serverStateTask, clientStateTask);

            BodySyncState serverState = await serverStateTask;
            if (!serverState.Exists) {
                return null;
            }

            BodySyncState clientState = await clientStateTask;
            int chunkX = (int)MathF.Floor(serverState.Position.X) >> 4;
            int chunkZ = (int)MathF.Floor(serverState.Position.Z) >> 4;
            ChunkLoadState clientChunkState = await TerrainStreamingTools.GetChunkLoadStateAsync(client, chunkX, chunkZ);
            AnimalPositionObservation observation = new(
                elapsedSinceConnect,
                animal,
                serverState,
                clientState,
                clientChunkState.Loaded,
                clientState.Exists
                    ? MathF.Sqrt(Vector3Dto.DistanceSquared(serverState.Position, clientState.Position))
                    : float.PositiveInfinity);

            if (observation.IsWithinRange || attempt == MaxConvergenceAttempts - 1) {
                return observation;
            }

            await Task.Delay(ConvergencePollInterval, fixture.ErrorCts.Token);
        }

        throw new InvalidOperationException("Animal convergence loop terminated unexpectedly.");
    }

    private static async Task<AnimalObservationTarget[]> GetExistingAnimalsAsync(GameContext context) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GetExistingAnimalsCode.Replace("__MAX_COUNT__", MaxObservedAnimals.ToString(CultureInfo.InvariantCulture)))
            ?? string.Empty;

        return raw
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseAnimalObservationTarget)
            .Where(animal => animal.Id > 0)
            .Distinct()
            .Take(MaxObservedAnimals)
            .ToArray();
    }

    private static AnimalObservationTarget ParseAnimalObservationTarget(string value) {
        string[] parts = value.Split('|', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) {
            return default;
        }

        return new AnimalObservationTarget(id, parts[1], parts[2]);
    }
}

public readonly record struct AnimalObservationTarget(int Id, string TemplateName, string DisplayName) {
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Animal {Id} ({TemplateName}, {DisplayName})");
}

public readonly record struct AnimalPositionObservation(
    TimeSpan ElapsedSinceConnect,
    AnimalObservationTarget Animal,
    BodySyncState ServerState,
    BodySyncState ClientState,
    bool ClientChunkLoaded,
    float Distance) {
    public bool IsWithinRange =>
        ServerState.Exists
        && (!ClientChunkLoaded
            || (ClientState.Exists
                && ServerState.Id == ClientState.Id
                && Distance <= AnimalPositionSyncTest.MaxPositionDistance));

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"t={ElapsedSinceConnect.TotalSeconds:R}s {Animal} distance={Distance:R}, clientChunkLoaded={ClientChunkLoaded}, Server={ServerState.Position} velocity={ServerState.Velocity}, Client={ClientState.Position} velocity={ClientState.Velocity}");
}
