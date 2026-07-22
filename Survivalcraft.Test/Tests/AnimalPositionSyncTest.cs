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
            BodySyncState serverState = await CreatureSyncTools.GetBodyStateAsync(server, animal.Id);
            if (!serverState.Exists) {
                continue;
            }

            BodySyncState clientState = await CreatureSyncTools.GetBodyStateAsync(client, animal.Id);
            observations.Add(new AnimalPositionObservation(
                elapsedSinceConnect,
                animal,
                serverState,
                clientState,
                clientState.Exists
                    ? MathF.Sqrt(Vector3Dto.DistanceSquared(serverState.Position, clientState.Position))
                    : float.PositiveInfinity));
        }

        return observations;
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
    float Distance) {
    public bool IsWithinRange =>
        ClientState.Exists
        && ServerState.Exists
        && ServerState.Id == ClientState.Id
        && Distance <= AnimalPositionSyncTest.MaxPositionDistance;

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"t={ElapsedSinceConnect.TotalSeconds:R}s {Animal} distance={Distance:R}, Server={ServerState.Position}, Client={ClientState.Position}");
}
