using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Coverage;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Scenarios;
using Survivalcraft.Testing.Soak;

namespace Survivalcraft.TestHost;

internal static class Program {
    private static async Task<int> Main(string[] args) {
        try {
            if (args.Length == 0 || IsHelp(args[0])) {
                PrintHelp();
                return args.Length == 0 ? 2 : 0;
            }

            string repositoryRoot = FindRepositoryRoot();
            if (args[0].Equals("coverage", StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 1);
                CoverageTrustReport report = await BuildCoverageTrustReportAsync(
                    repositoryRoot).ConfigureAwait(false);
                PrintCoverageTrustReport(report);
                if (options.TryGetValue("output", out string? output)
                    && !string.IsNullOrWhiteSpace(output)) {
                    string outputPath = Path.GetFullPath(output);
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                    await File.WriteAllTextAsync(
                        outputPath,
                        JsonSerializer.Serialize(report, ControlProtocol.JsonOptions))
                        .ConfigureAwait(false);
                    Console.WriteLine($"output={outputPath}");
                }
                return report.IsStructurallyValid ? 0 : 1;
            }
            if (args[0].Equals("doctor", StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 1);
                string runtime = ResolveRuntime(repositoryRoot, options);
                await RunDoctorAsync(
                    runtime,
                    GetTimeSpan(options, "startup-timeout", TimeSpan.FromSeconds(90)),
                    GetFlag(options, "static-only")).ConfigureAwait(false);
                return 0;
            }
            if (args[0].Equals("soak", StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 1);
                MultiplayerSoakSummary summary = await RunSoakAsync(
                    repositoryRoot,
                    options).ConfigureAwait(false);
                PrintSoakResult(summary);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(CoreMultiplayerScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                CoreMultiplayerScenarioOptions scenarioOptions = BuildScenarioOptions(
                    repositoryRoot,
                    options,
                    replay: null);
                CoreMultiplayerScenarioResult result = await new CoreMultiplayerScenario()
                    .RunAsync(scenarioOptions)
                    .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(CompatibilityMatrixScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                CompatibilityMatrixScenarioOptions scenarioOptions =
                    BuildCompatibilityScenarioOptions(repositoryRoot, options, replay: null);
                CompatibilityMatrixScenarioResult result =
                    await new CompatibilityMatrixScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(RelayModChannelScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                RelayModChannelScenarioOptions scenarioOptions =
                    BuildRelayScenarioOptions(repositoryRoot, options, replay: null);
                RelayModChannelScenarioResult result = await new RelayModChannelScenario()
                    .RunAsync(scenarioOptions)
                    .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(CreativeFlyMultiplayerScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                CreativeFlyMultiplayerScenarioOptions scenarioOptions =
                    BuildCreativeFlyScenarioOptions(repositoryRoot, options, replay: null);
                CreativeFlyMultiplayerScenarioResult result =
                    await new CreativeFlyMultiplayerScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(TerrainInteractionMultiplayerScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                TerrainInteractionMultiplayerScenarioOptions scenarioOptions =
                    BuildTerrainInteractionScenarioOptions(repositoryRoot, options, replay: null);
                TerrainInteractionMultiplayerScenarioResult result =
                    await new TerrainInteractionMultiplayerScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(PlayerMotionMultiplayerScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                PlayerMotionMultiplayerScenarioOptions scenarioOptions =
                    BuildPlayerMotionScenarioOptions(repositoryRoot, options, replay: null);
                PlayerMotionMultiplayerScenarioResult result =
                    await new PlayerMotionMultiplayerScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(TerrainStreamingMultiplayerScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                TerrainStreamingMultiplayerScenarioOptions scenarioOptions =
                    BuildTerrainStreamingScenarioOptions(repositoryRoot, options, replay: null);
                TerrainStreamingMultiplayerScenarioResult result =
                    await new TerrainStreamingMultiplayerScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(EntityCreatureMultiplayerScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                EntityCreatureMultiplayerScenarioOptions scenarioOptions =
                    BuildEntityCreatureScenarioOptions(repositoryRoot, options, replay: null);
                EntityCreatureMultiplayerScenarioResult result =
                    await new EntityCreatureMultiplayerScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(PickableProjectileMultiplayerScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                PickableProjectileMultiplayerScenarioOptions scenarioOptions =
                    BuildPickableProjectileScenarioOptions(repositoryRoot, options, replay: null);
                PickableProjectileMultiplayerScenarioResult result =
                    await new PickableProjectileMultiplayerScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(InventoryHealthMultiplayerScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                InventoryHealthMultiplayerScenarioOptions scenarioOptions =
                    BuildInventoryHealthScenarioOptions(repositoryRoot, options, replay: null);
                InventoryHealthMultiplayerScenarioResult result =
                    await new InventoryHealthMultiplayerScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(EnvironmentMechanicsMultiplayerScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                EnvironmentMechanicsMultiplayerScenarioOptions scenarioOptions =
                    BuildEnvironmentMechanicsScenarioOptions(repositoryRoot, options, replay: null);
                EnvironmentMechanicsMultiplayerScenarioResult result =
                    await new EnvironmentMechanicsMultiplayerScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(MountMultiplayerScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                MountMultiplayerScenarioOptions scenarioOptions =
                    BuildMountScenarioOptions(repositoryRoot, options, replay: null);
                MountMultiplayerScenarioResult result =
                    await new MountMultiplayerScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(LogicMultiplayerScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                LogicMultiplayerScenarioOptions scenarioOptions =
                    BuildLogicScenarioOptions(repositoryRoot, options, replay: null);
                LogicMultiplayerScenarioResult result =
                    await new LogicMultiplayerScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(SessionLifecycleMultiplayerScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                SessionLifecycleMultiplayerScenarioOptions scenarioOptions =
                    BuildSessionLifecycleScenarioOptions(repositoryRoot, options, replay: null);
                SessionLifecycleMultiplayerScenarioResult result =
                    await new SessionLifecycleMultiplayerScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("replay", StringComparison.OrdinalIgnoreCase)) {
                if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal)) {
                    throw new ArgumentException("replay requires the source artifact directory.");
                }
                string sourceArtifactDirectory = Path.GetFullPath(args[1]);
                Dictionary<string, string?> options = ParseOptions(args, 2);
                string replayV2Path = Path.Combine(sourceArtifactDirectory, "replay-v2.json");
                if (File.Exists(replayV2Path)) {
                    ScenarioReplayV2 replayV2 = await ScenarioArtifactWriter.ReadReplayV2Async(
                        sourceArtifactDirectory).ConfigureAwait(false);
                    ValidateReplayV2ProtocolVersions(replayV2);
                    ScenarioActionRecordV2[] actionRecords =
                        await ScenarioArtifactWriter.ReadActionsV2Async(sourceArtifactDirectory)
                            .ConfigureAwait(false);
                    if (replayV2.Scenario.Equals(
                            CreativeFlyMultiplayerScenario.ScenarioName,
                            StringComparison.Ordinal)) {
                        ValidateReplayV2ScenarioVersion(
                            replayV2,
                            CreativeFlyMultiplayerScenario.ScenarioVersion);
                        SetCreativeFlyCommand[] replayActions = actionRecords.Select(record => {
                            if (!string.Equals(record.Role, "client-a", StringComparison.Ordinal)) {
                                throw new InvalidDataException(
                                    $"Replay v2 action role '{record.Role}' is not supported by "
                                    + $"{CreativeFlyMultiplayerScenario.ScenarioName} version "
                                    + $"{CreativeFlyMultiplayerScenario.ScenarioVersion}.");
                            }
                            if (!string.Equals(
                                    record.Command,
                                    ControlCommandNames.SetCreativeFly,
                                    StringComparison.Ordinal)) {
                                throw new InvalidDataException(
                                    $"Replay v2 contains unsupported action '{record.Command}'.");
                            }
                            return record.Payload.Deserialize<SetCreativeFlyCommand>(ControlProtocol.JsonOptions)
                                ?? throw new InvalidDataException(
                                    $"Replay v2 action '{record.Receipt.ActionId}' has an invalid payload.");
                        }).ToArray();
                        CreativeFlyMultiplayerScenarioOptions v2Options =
                            BuildCreativeFlyScenarioOptions(
                                repositoryRoot,
                                options,
                                replayV2,
                                replayActions);
                        CreativeFlyMultiplayerScenarioResult v2Result =
                            await new CreativeFlyMultiplayerScenario()
                                .RunAsync(v2Options)
                                .ConfigureAwait(false);
                        PrintResult(v2Result);
                        return 0;
                    }
                    if (replayV2.Scenario.Equals(
                            TerrainInteractionMultiplayerScenario.ScenarioName,
                            StringComparison.Ordinal)) {
                        ValidateReplayV2ScenarioVersion(
                            replayV2,
                            TerrainInteractionMultiplayerScenario.ScenarioVersion);
                        TerrainInteractionReplayPlan replayPlan =
                            TerrainInteractionMultiplayerScenario.ParseReplayActions(actionRecords);
                        TerrainInteractionMultiplayerScenarioOptions v2Options =
                            BuildTerrainInteractionScenarioOptions(
                                repositoryRoot,
                                options,
                                replayV2,
                                replayPlan);
                        TerrainInteractionMultiplayerScenarioResult v2Result =
                            await new TerrainInteractionMultiplayerScenario()
                                .RunAsync(v2Options)
                                .ConfigureAwait(false);
                        PrintResult(v2Result);
                        return 0;
                    }
                    if (replayV2.Scenario.Equals(
                            PlayerMotionMultiplayerScenario.ScenarioName,
                            StringComparison.Ordinal)) {
                        ValidateReplayV2ScenarioVersion(
                            replayV2,
                            PlayerMotionMultiplayerScenario.ScenarioVersion);
                        PlayerMotionReplayPlan replayPlan =
                            PlayerMotionMultiplayerScenario.ParseReplayActions(actionRecords);
                        PlayerMotionMultiplayerScenarioOptions v2Options =
                            BuildPlayerMotionScenarioOptions(
                                repositoryRoot,
                                options,
                                replayV2,
                                replayPlan);
                        PlayerMotionMultiplayerScenarioResult v2Result =
                            await new PlayerMotionMultiplayerScenario()
                                .RunAsync(v2Options)
                                .ConfigureAwait(false);
                        PrintResult(v2Result);
                        return 0;
                    }
                    if (replayV2.Scenario.Equals(
                            TerrainStreamingMultiplayerScenario.ScenarioName,
                            StringComparison.Ordinal)) {
                        ValidateReplayV2ScenarioVersion(
                            replayV2,
                            TerrainStreamingMultiplayerScenario.ScenarioVersion);
                        TerrainStreamingReplayPlan replayPlan =
                            TerrainStreamingMultiplayerScenario.ParseReplayActions(actionRecords);
                        TerrainStreamingMultiplayerScenarioOptions v2Options =
                            BuildTerrainStreamingScenarioOptions(
                                repositoryRoot,
                                options,
                                replayV2,
                                replayPlan);
                        TerrainStreamingMultiplayerScenarioResult v2Result =
                            await new TerrainStreamingMultiplayerScenario()
                                .RunAsync(v2Options)
                                .ConfigureAwait(false);
                        PrintResult(v2Result);
                        return 0;
                    }
                    if (replayV2.Scenario.Equals(
                            EntityCreatureMultiplayerScenario.ScenarioName,
                            StringComparison.Ordinal)) {
                        ValidateReplayV2ScenarioVersion(
                            replayV2,
                            EntityCreatureMultiplayerScenario.ScenarioVersion);
                        EntityCreatureReplayPlan replayPlan =
                            EntityCreatureMultiplayerScenario.ParseReplayActions(actionRecords);
                        EntityCreatureMultiplayerScenarioOptions v2Options =
                            BuildEntityCreatureScenarioOptions(
                                repositoryRoot,
                                options,
                                replayV2,
                                replayPlan);
                        EntityCreatureMultiplayerScenarioResult v2Result =
                            await new EntityCreatureMultiplayerScenario()
                                .RunAsync(v2Options)
                                .ConfigureAwait(false);
                        PrintResult(v2Result);
                        return 0;
                    }
                    if (replayV2.Scenario.Equals(
                            PickableProjectileMultiplayerScenario.ScenarioName,
                            StringComparison.Ordinal)) {
                        ValidateReplayV2ScenarioVersion(
                            replayV2,
                            PickableProjectileMultiplayerScenario.ScenarioVersion);
                        PickableProjectileReplayPlan replayPlan =
                            PickableProjectileMultiplayerScenario.ParseReplayActions(actionRecords);
                        PickableProjectileMultiplayerScenarioOptions v2Options =
                            BuildPickableProjectileScenarioOptions(
                                repositoryRoot,
                                options,
                                replayV2,
                                replayPlan);
                        PickableProjectileMultiplayerScenarioResult v2Result =
                            await new PickableProjectileMultiplayerScenario()
                                .RunAsync(v2Options)
                                .ConfigureAwait(false);
                        PrintResult(v2Result);
                        return 0;
                    }
                    if (replayV2.Scenario.Equals(
                            InventoryHealthMultiplayerScenario.ScenarioName,
                            StringComparison.Ordinal)) {
                        ValidateReplayV2ScenarioVersion(
                            replayV2,
                            InventoryHealthMultiplayerScenario.ScenarioVersion);
                        InventoryHealthReplayPlan replayPlan =
                            InventoryHealthMultiplayerScenario.ParseReplayActions(actionRecords);
                        InventoryHealthMultiplayerScenarioOptions v2Options =
                            BuildInventoryHealthScenarioOptions(
                                repositoryRoot,
                                options,
                                replayV2,
                                replayPlan);
                        InventoryHealthMultiplayerScenarioResult v2Result =
                            await new InventoryHealthMultiplayerScenario()
                                .RunAsync(v2Options)
                                .ConfigureAwait(false);
                        PrintResult(v2Result);
                        return 0;
                    }
                    if (replayV2.Scenario.Equals(
                            EnvironmentMechanicsMultiplayerScenario.ScenarioName,
                            StringComparison.Ordinal)) {
                        ValidateReplayV2ScenarioVersion(
                            replayV2,
                            EnvironmentMechanicsMultiplayerScenario.ScenarioVersion);
                        EnvironmentMechanicsReplayPlan replayPlan =
                            EnvironmentMechanicsMultiplayerScenario.ParseReplayActions(actionRecords);
                        EnvironmentMechanicsMultiplayerScenarioOptions v2Options =
                            BuildEnvironmentMechanicsScenarioOptions(
                                repositoryRoot,
                                options,
                                replayV2,
                                replayPlan);
                        EnvironmentMechanicsMultiplayerScenarioResult v2Result =
                            await new EnvironmentMechanicsMultiplayerScenario()
                                .RunAsync(v2Options)
                                .ConfigureAwait(false);
                        PrintResult(v2Result);
                        return 0;
                    }
                    if (replayV2.Scenario.Equals(
                            MountMultiplayerScenario.ScenarioName,
                            StringComparison.Ordinal)) {
                        ValidateReplayV2ScenarioVersion(
                            replayV2,
                            MountMultiplayerScenario.ScenarioVersion);
                        MountReplayPlan replayPlan =
                            MountMultiplayerScenario.ParseReplayActions(actionRecords);
                        MountMultiplayerScenarioOptions v2Options =
                            BuildMountScenarioOptions(
                                repositoryRoot,
                                options,
                                replayV2,
                                replayPlan);
                        MountMultiplayerScenarioResult v2Result =
                            await new MountMultiplayerScenario()
                                .RunAsync(v2Options)
                                .ConfigureAwait(false);
                        PrintResult(v2Result);
                        return 0;
                    }
                    if (replayV2.Scenario.Equals(
                            LogicMultiplayerScenario.ScenarioName,
                            StringComparison.Ordinal)) {
                        ValidateReplayV2ScenarioVersion(
                            replayV2,
                            LogicMultiplayerScenario.ScenarioVersion);
                        LogicReplayPlan replayPlan =
                            LogicMultiplayerScenario.ParseReplayActions(actionRecords);
                        LogicMultiplayerScenarioOptions v2Options =
                            BuildLogicScenarioOptions(
                                repositoryRoot,
                                options,
                                replayV2,
                                replayPlan);
                        LogicMultiplayerScenarioResult v2Result =
                            await new LogicMultiplayerScenario()
                                .RunAsync(v2Options)
                                .ConfigureAwait(false);
                        PrintResult(v2Result);
                        return 0;
                    }
                    if (replayV2.Scenario.Equals(
                            SessionLifecycleMultiplayerScenario.ScenarioName,
                            StringComparison.Ordinal)) {
                        ValidateReplayV2ScenarioVersion(
                            replayV2,
                            SessionLifecycleMultiplayerScenario.ScenarioVersion);
                        SessionLifecycleMultiplayerScenarioOptions v2Options =
                            BuildSessionLifecycleScenarioOptions(
                                repositoryRoot,
                                options,
                                replayV2);
                        SessionLifecycleMultiplayerScenarioResult v2Result =
                            await new SessionLifecycleMultiplayerScenario()
                                .RunAsync(v2Options)
                                .ConfigureAwait(false);
                        PrintResult(v2Result);
                        return 0;
                    }
                    throw new InvalidDataException(
                        $"Replay v2 scenario '{replayV2.Scenario}' is not supported by this host.");
                }
                ScenarioReplay replay = await ScenarioArtifactWriter.ReadReplayAsync(
                    sourceArtifactDirectory).ConfigureAwait(false);
                if (replay.Scenario.Equals(
                        CoreMultiplayerScenario.ScenarioName,
                        StringComparison.Ordinal)) {
                    CoreMultiplayerScenarioOptions scenarioOptions = BuildScenarioOptions(
                        repositoryRoot,
                        options,
                        replay);
                    CoreMultiplayerScenarioResult result = await new CoreMultiplayerScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                    PrintResult(result);
                    return 0;
                }
                if (replay.Scenario.Equals(
                        CompatibilityMatrixScenario.ScenarioName,
                        StringComparison.Ordinal)) {
                    CompatibilityMatrixScenarioOptions scenarioOptions =
                        BuildCompatibilityScenarioOptions(repositoryRoot, options, replay);
                    CompatibilityMatrixScenarioResult result =
                        await new CompatibilityMatrixScenario()
                            .RunAsync(scenarioOptions)
                            .ConfigureAwait(false);
                    PrintResult(result);
                    return 0;
                }
                if (replay.Scenario.Equals(
                        RelayModChannelScenario.ScenarioName,
                        StringComparison.Ordinal)) {
                    RelayModChannelScenarioOptions scenarioOptions =
                        BuildRelayScenarioOptions(repositoryRoot, options, replay);
                    RelayModChannelScenarioResult result =
                        await new RelayModChannelScenario()
                            .RunAsync(scenarioOptions)
                            .ConfigureAwait(false);
                    PrintResult(result);
                    return 0;
                }
                throw new InvalidDataException(
                    $"Replay scenario '{replay.Scenario}' is not supported by this host.");
            }

            throw new ArgumentException(
                $"Unknown command '{string.Join(' ', args.Take(Math.Min(args.Length, 2)))}'.");
        }
        catch (CoreMultiplayerScenarioException exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception.InnerException);
            return 1;
        }
        catch (CompatibilityMatrixScenarioException exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception.InnerException);
            return 1;
        }
        catch (RelayModChannelScenarioException exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception.InnerException);
            return 1;
        }
        catch (CreativeFlyMultiplayerScenarioException exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception.InnerException);
            return 1;
        }
        catch (TerrainInteractionMultiplayerScenarioException exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception.InnerException);
            return 1;
        }
        catch (PlayerMotionMultiplayerScenarioException exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception.InnerException);
            return 1;
        }
        catch (TerrainStreamingMultiplayerScenarioException exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception.InnerException);
            return 1;
        }
        catch (EntityCreatureMultiplayerScenarioException exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception.InnerException);
            return 1;
        }
        catch (PickableProjectileMultiplayerScenarioException exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception.InnerException);
            return 1;
        }
        catch (InventoryHealthMultiplayerScenarioException exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception.InnerException);
            return 1;
        }
        catch (Exception exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static CoreMultiplayerScenarioOptions BuildScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplay? replay) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260728);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"core-multiplayer-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"core-multiplayer-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;

        NetworkFaultProfile baseProfile = replay?.NetworkFaults
            ?? new NetworkFaultProfile(Seed: seed);
        NetworkFaultProfile profile = baseProfile with {
            Seed = seed,
            BaseLatencyMilliseconds = GetInt32(
                options,
                "latency-ms",
                replay?.NetworkFaults.BaseLatencyMilliseconds ?? 12),
            JitterMilliseconds = GetInt32(
                options,
                "jitter-ms",
                replay?.NetworkFaults.JitterMilliseconds ?? 3),
            LossRate = GetDouble(
                options,
                "loss-rate",
                replay?.NetworkFaults.LossRate ?? 0),
            DuplicationRate = GetDouble(
                options,
                "duplication-rate",
                replay?.NetworkFaults.DuplicationRate ?? 0),
            ReorderRate = GetDouble(
                options,
                "reorder-rate",
                replay?.NetworkFaults.ReorderRate ?? 0),
            ReorderDelayMilliseconds = GetInt32(
                options,
                "reorder-delay-ms",
                replay?.NetworkFaults.ReorderDelayMilliseconds ?? 0),
            BandwidthBytesPerSecond = GetInt32(
                options,
                "bandwidth-bytes",
                replay?.NetworkFaults.BandwidthBytesPerSecond ?? 0)
        };

        return new CoreMultiplayerScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            profile,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(6)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"));
    }

    private static CreativeFlyMultiplayerScenarioOptions BuildCreativeFlyScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplayV2? replay,
        SetCreativeFlyCommand[]? replayActions = null) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260729);
        MultiplayerTransportKind replayTransport = replay == null
            ? MultiplayerTransportKind.Direct
            : ParseTransport(replay.Transport, "Replay transport");
        MultiplayerTransportKind transport = GetTransport(
            options,
            "transport",
            replayTransport);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"creative-fly-{transport.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"creative-fly-{transport.ToString().ToLowerInvariant()}-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;
        return new CreativeFlyMultiplayerScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            transport,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(6)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"),
            ReplayActions: replayActions);
    }

    private static TerrainInteractionMultiplayerScenarioOptions BuildTerrainInteractionScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplayV2? replay,
        TerrainInteractionReplayPlan? replayPlan = null) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260729);
        MultiplayerTransportKind replayTransport = replay == null
            ? MultiplayerTransportKind.Direct
            : ParseTransport(replay.Transport, "Replay transport");
        MultiplayerTransportKind transport = GetTransport(
            options,
            "transport",
            replayTransport);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"terrain-interaction-{transport.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"terrain-interaction-{transport.ToString().ToLowerInvariant()}-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;
        return new TerrainInteractionMultiplayerScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            transport,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(8)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"),
            NetworkFaults: BuildGameplayNetworkFaults(
                options,
                replay,
                TerrainInteractionMultiplayerScenario.CreateDefaultNetworkFaults(seed)),
            ReplayPlan: replayPlan);
    }

    private static PlayerMotionMultiplayerScenarioOptions BuildPlayerMotionScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplayV2? replay,
        PlayerMotionReplayPlan? replayPlan = null) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260730);
        MultiplayerTransportKind replayTransport = replay == null
            ? MultiplayerTransportKind.Direct
            : ParseTransport(replay.Transport, "Replay transport");
        MultiplayerTransportKind transport = GetTransport(
            options,
            "transport",
            replayTransport);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"player-motion-{transport.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"player-motion-{transport.ToString().ToLowerInvariant()}-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;
        return new PlayerMotionMultiplayerScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            transport,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(9)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"),
            NetworkFaults: BuildGameplayNetworkFaults(
                options,
                replay,
                PlayerMotionMultiplayerScenario.CreateDefaultNetworkFaults(seed)),
            ReplayPlan: replayPlan);
    }

    private static TerrainStreamingMultiplayerScenarioOptions BuildTerrainStreamingScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplayV2? replay,
        TerrainStreamingReplayPlan? replayPlan = null) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260731);
        MultiplayerTransportKind replayTransport = replay == null
            ? MultiplayerTransportKind.Direct
            : ParseTransport(replay.Transport, "Replay transport");
        MultiplayerTransportKind transport = GetTransport(
            options,
            "transport",
            replayTransport);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"terrain-streaming-{transport.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"terrain-streaming-{transport.ToString().ToLowerInvariant()}-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;
        return new TerrainStreamingMultiplayerScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            transport,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(8)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"),
            NetworkFaults: BuildGameplayNetworkFaults(
                options,
                replay,
                TerrainStreamingMultiplayerScenario.CreateDefaultNetworkFaults(seed)),
            ReplayPlan: replayPlan);
    }

    private static EntityCreatureMultiplayerScenarioOptions BuildEntityCreatureScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplayV2? replay,
        EntityCreatureReplayPlan? replayPlan = null) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260801);
        MultiplayerTransportKind replayTransport = replay == null
            ? MultiplayerTransportKind.Direct
            : ParseTransport(replay.Transport, "Replay transport");
        MultiplayerTransportKind transport = GetTransport(
            options,
            "transport",
            replayTransport);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"entity-creature-{transport.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"entity-creature-{transport.ToString().ToLowerInvariant()}-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;
        return new EntityCreatureMultiplayerScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            transport,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(9)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"),
            NetworkFaults: BuildGameplayNetworkFaults(
                options,
                replay,
                EntityCreatureMultiplayerScenario.CreateDefaultNetworkFaults(seed)),
            ReplayPlan: replayPlan);
    }

    private static PickableProjectileMultiplayerScenarioOptions BuildPickableProjectileScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplayV2? replay,
        PickableProjectileReplayPlan? replayPlan = null) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260806);
        MultiplayerTransportKind replayTransport = replay == null
            ? MultiplayerTransportKind.Direct
            : ParseTransport(replay.Transport, "Replay transport");
        MultiplayerTransportKind transport = GetTransport(
            options,
            "transport",
            replayTransport);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"pickable-projectile-{transport.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"pickable-projectile-{transport.ToString().ToLowerInvariant()}-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;
        return new PickableProjectileMultiplayerScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            transport,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(10)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"),
            NetworkFaults: BuildGameplayNetworkFaults(
                options,
                replay,
                PickableProjectileMultiplayerScenario.CreateDefaultNetworkFaults(seed)),
            ReplayPlan: replayPlan);
    }

    private static InventoryHealthMultiplayerScenarioOptions BuildInventoryHealthScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplayV2? replay,
        InventoryHealthReplayPlan? replayPlan = null) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260802);
        MultiplayerTransportKind replayTransport = replay == null
            ? MultiplayerTransportKind.Direct
            : ParseTransport(replay.Transport, "Replay transport");
        MultiplayerTransportKind transport = GetTransport(
            options,
            "transport",
            replayTransport);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"inventory-health-{transport.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"inventory-health-{transport.ToString().ToLowerInvariant()}-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;
        return new InventoryHealthMultiplayerScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            transport,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(10)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"),
            NetworkFaults: BuildGameplayNetworkFaults(
                options,
                replay,
                InventoryHealthMultiplayerScenario.CreateDefaultNetworkFaults(seed)),
            ReplayPlan: replayPlan);
    }

    private static EnvironmentMechanicsMultiplayerScenarioOptions BuildEnvironmentMechanicsScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplayV2? replay,
        EnvironmentMechanicsReplayPlan? replayPlan = null) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260803);
        MultiplayerTransportKind replayTransport = replay == null
            ? MultiplayerTransportKind.Direct
            : ParseTransport(replay.Transport, "Replay transport");
        MultiplayerTransportKind transport = GetTransport(
            options,
            "transport",
            replayTransport);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"environment-mechanics-{transport.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"environment-mechanics-{transport.ToString().ToLowerInvariant()}-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;
        return new EnvironmentMechanicsMultiplayerScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            transport,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(10)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"),
            NetworkFaults: BuildGameplayNetworkFaults(
                options,
                replay,
                EnvironmentMechanicsMultiplayerScenario.CreateDefaultNetworkFaults(seed)),
            ReplayPlan: replayPlan);
    }

    private static MountMultiplayerScenarioOptions BuildMountScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplayV2? replay,
        MountReplayPlan? replayPlan = null) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260804);
        MultiplayerTransportKind replayTransport = replay == null
            ? MultiplayerTransportKind.Direct
            : ParseTransport(replay.Transport, "Replay transport");
        MultiplayerTransportKind transport = GetTransport(
            options,
            "transport",
            replayTransport);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"mount-{transport.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"mount-{transport.ToString().ToLowerInvariant()}-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;
        return new MountMultiplayerScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            transport,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(10)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"),
            NetworkFaults: BuildGameplayNetworkFaults(
                options,
                replay,
                MountMultiplayerScenario.CreateDefaultNetworkFaults(seed)),
            ReplayPlan: replayPlan);
    }

    private static LogicMultiplayerScenarioOptions BuildLogicScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplayV2? replay,
        LogicReplayPlan? replayPlan = null) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260805);
        MultiplayerTransportKind replayTransport = replay == null
            ? MultiplayerTransportKind.Direct
            : ParseTransport(replay.Transport, "Replay transport");
        MultiplayerTransportKind transport = GetTransport(
            options,
            "transport",
            replayTransport);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"logic-{transport.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"logic-{transport.ToString().ToLowerInvariant()}-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;
        return new LogicMultiplayerScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            transport,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(12)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"),
            NetworkFaults: BuildGameplayNetworkFaults(
                options,
                replay,
                LogicMultiplayerScenario.CreateDefaultNetworkFaults(seed)),
            ReplayPlan: replayPlan);
    }

    private static SessionLifecycleMultiplayerScenarioOptions
        BuildSessionLifecycleScenarioOptions(
            string repositoryRoot,
            IReadOnlyDictionary<string, string?> options,
            ScenarioReplayV2? replay) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260730);
        MultiplayerTransportKind replayTransport = replay == null
            ? MultiplayerTransportKind.Direct
            : ParseTransport(replay.Transport, "Replay transport");
        MultiplayerTransportKind transport = GetTransport(
            options,
            "transport",
            replayTransport);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"session-lifecycle-{transport.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"session-lifecycle-{transport.ToString().ToLowerInvariant()}-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;
        return new SessionLifecycleMultiplayerScenarioOptions(
            runtime,
            artifactDirectory,
            transport,
            seed,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(75)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(12)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"),
            NetworkFaults: BuildGameplayNetworkFaults(
                options,
                replay,
                new NetworkFaultProfile(
                    Seed: seed + 50,
                    DuplicationRate: 1,
                    ReorderRate: 0.5,
                    ReorderDelayMilliseconds: 30)));
    }

    private static NetworkFaultProfile BuildGameplayNetworkFaults(
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplayV2? replay,
        NetworkFaultProfile defaultProfile) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(defaultProfile);
        NetworkFaultProfile profile = replay?.NetworkFaults ?? defaultProfile;
        return (profile with {
            Seed = GetInt32(options, "fault-seed", profile.Seed),
            BaseLatencyMilliseconds = GetInt32(
                options,
                "latency-ms",
                profile.BaseLatencyMilliseconds),
            JitterMilliseconds = GetInt32(
                options,
                "jitter-ms",
                profile.JitterMilliseconds),
            LossRate = GetDouble(options, "loss-rate", profile.LossRate),
            DuplicationRate = GetDouble(
                options,
                "duplication-rate",
                profile.DuplicationRate),
            ReorderRate = GetDouble(options, "reorder-rate", profile.ReorderRate),
            ReorderDelayMilliseconds = GetInt32(
                options,
                "reorder-delay-ms",
                profile.ReorderDelayMilliseconds),
            BandwidthBytesPerSecond = GetInt32(
                options,
                "bandwidth-bytes",
                profile.BandwidthBytesPerSecond)
        }).Validate();
    }

    private static void ValidateReplayV2ProtocolVersions(ScenarioReplayV2 replay) {
        if (replay.WireProtocolVersion != ControlProtocol.CurrentVersion
            || replay.AutomationApiVersion != AutomationProtocol.CurrentApiVersion
            || replay.SnapshotSchemaVersion != AutomationProtocol.CurrentSnapshotSchemaVersion) {
            throw new InvalidDataException(
                "Replay v2 protocol versions are incompatible with this host. "
                + $"Replay=wire:{replay.WireProtocolVersion},api:{replay.AutomationApiVersion},snapshot:{replay.SnapshotSchemaVersion}; "
                + $"Host=wire:{ControlProtocol.CurrentVersion},api:{AutomationProtocol.CurrentApiVersion},snapshot:{AutomationProtocol.CurrentSnapshotSchemaVersion}.");
        }
    }

    private static void ValidateReplayV2ScenarioVersion(
        ScenarioReplayV2 replay,
        int supportedVersion) {
        if (replay.Version != supportedVersion) {
            throw new InvalidDataException(
                $"Replay v2 scenario version {replay.Version} is incompatible with "
                + $"{replay.Scenario} version {supportedVersion}.");
        }
    }

    private static RelayModChannelScenarioOptions BuildRelayScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplay? replay) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260728);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"relay-mod-channel-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"relay-mod-channel-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;

        NetworkFaultProfile baseProfile = replay?.NetworkFaults
            ?? new NetworkFaultProfile(Seed: seed);
        NetworkFaultProfile profile = baseProfile with {
            Seed = seed,
            BaseLatencyMilliseconds = GetInt32(
                options,
                "latency-ms",
                replay?.NetworkFaults.BaseLatencyMilliseconds ?? 8),
            JitterMilliseconds = GetInt32(
                options,
                "jitter-ms",
                replay?.NetworkFaults.JitterMilliseconds ?? 2),
            LossRate = GetDouble(
                options,
                "loss-rate",
                replay?.NetworkFaults.LossRate ?? 0),
            DuplicationRate = GetDouble(
                options,
                "duplication-rate",
                replay?.NetworkFaults.DuplicationRate ?? 0),
            ReorderRate = GetDouble(
                options,
                "reorder-rate",
                replay?.NetworkFaults.ReorderRate ?? 0),
            ReorderDelayMilliseconds = GetInt32(
                options,
                "reorder-delay-ms",
                replay?.NetworkFaults.ReorderDelayMilliseconds ?? 0),
            BandwidthBytesPerSecond = GetInt32(
                options,
                "bandwidth-bytes",
                replay?.NetworkFaults.BandwidthBytesPerSecond ?? 0)
        };

        return new RelayModChannelScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            profile,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(6)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"));
    }

    private static CompatibilityMatrixScenarioOptions BuildCompatibilityScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplay? replay) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260728);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"compatibility-matrix-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"compatibility-matrix-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;
        return new CompatibilityMatrixScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(5)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"));
    }

    private static async Task RunDoctorAsync(
        string runtimeDirectory,
        TimeSpan startupTimeout,
        bool staticOnly) {
        Console.WriteLine($"runtime={runtimeDirectory}");
        string[] requiredFiles = [
            "Survivalcraft.exe",
            "Survivalcraft.dll",
            "Survivalcraft.Networking.dll",
            "Content.zip",
            Path.Combine("InternalMods", "Survivalcraft.Multiplayer.scmod"),
            Path.Combine("Mods", "Survivalcraft.TestAutomation.scmod")
        ];
        foreach (string relativePath in requiredFiles) {
            string path = Path.Combine(runtimeDirectory, relativePath);
            if (!File.Exists(path)) {
                throw new FileNotFoundException($"Required runtime file is missing: {path}", path);
            }
            Console.WriteLine($"ok file {relativePath}");
        }

        string modPath = Path.Combine(
            runtimeDirectory,
            "Mods",
            "Survivalcraft.TestAutomation.scmod");
        using (ZipArchive archive = ZipFile.OpenRead(modPath)) {
            string[] requiredEntries = [
                "modinfo.json",
                "Survivalcraft.TestAutomation.dll",
                "Survivalcraft.Testing.dll"
            ];
            foreach (string entry in requiredEntries) {
                if (!archive.Entries.Any(candidate =>
                    candidate.FullName.Equals(entry, StringComparison.OrdinalIgnoreCase))) {
                    throw new InvalidDataException(
                        $"Automation mod '{modPath}' does not contain '{entry}'.");
                }
                Console.WriteLine($"ok mod-entry {entry}");
            }
        }

        string multiplayerModPath = Path.Combine(
            runtimeDirectory,
            "InternalMods",
            "Survivalcraft.Multiplayer.scmod");
        string legacyMultiplayerModPath = Path.Combine(
            runtimeDirectory,
            "Mods",
            "Survivalcraft.Multiplayer.scmod");
        if (File.Exists(legacyMultiplayerModPath)) {
            throw new InvalidDataException(
                $"Multiplayer mod is duplicated in Mods and InternalMods: {legacyMultiplayerModPath}");
        }
        using (ZipArchive archive = ZipFile.OpenRead(multiplayerModPath)) {
            string[] requiredEntries = [
                "modinfo.json",
                "Survivalcraft.Multiplayer.dll"
            ];
            foreach (string entry in requiredEntries) {
                if (!archive.Entries.Any(candidate =>
                    candidate.FullName.Equals(entry, StringComparison.OrdinalIgnoreCase))) {
                    throw new InvalidDataException(
                        $"Multiplayer mod '{multiplayerModPath}' does not contain '{entry}'.");
                }
                Console.WriteLine($"ok multiplayer-mod-entry {entry}");
            }
        }

        Console.WriteLine($"ok protocol v{ControlProtocol.CurrentVersion}");
        if (staticOnly) {
            Console.WriteLine("DOCTOR PASSED (static only)");
            return;
        }

        string doctorRoot = Path.Combine(
            Path.GetTempPath(),
            "Survivalcraft.TestHost",
            $"doctor-{Guid.NewGuid():N}");
        GameNodeProcess? node = null;
        try {
            node = new GameNodeProcess(new GameNodeOptions(
                "doctor",
                runtimeDirectory,
                Path.Combine(doctorRoot, "data"),
                StartupTimeout: startupTimeout));
            await node.StartAsync().ConfigureAwait(false);
            NodeStatus status = await node.WaitForStatusAsync(
                value => value.ContentLoaded
                    && value.Screen == "MainMenuScreen"
                    && value.NetworkRuntimeBound,
                startupTimeout,
                "Doctor node did not reach MainMenu with the multiplayer runtime bound.").ConfigureAwait(false);
            Console.WriteLine(
                $"ok headless-control pid={node.ProcessId} screen={status.Screen} role={status.Role} networkBound={status.NetworkRuntimeBound}");
            Console.WriteLine("DOCTOR PASSED");
        }
        catch {
            if (node != null) {
                Console.Error.WriteLine(node.GetRecentLogText());
            }
            throw;
        }
        finally {
            if (node != null) {
                await node.DisposeAsync().ConfigureAwait(false);
            }
            TryDeleteDoctorDirectory(doctorRoot);
        }
    }

    private static void TryDeleteDoctorDirectory(string doctorRoot) {
        try {
            string fullPath = Path.GetFullPath(doctorRoot);
            string allowedRoot = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), "Survivalcraft.TestHost"));
            if (fullPath.StartsWith(
                    allowedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(fullPath)) {
                Directory.Delete(fullPath, recursive: true);
            }
        }
        catch {
            // A leftover doctor directory is preferable to hiding the actual result.
        }
    }

    private static void PrintResult(CoreMultiplayerScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"world={result.WorldDirectoryName}");
        Console.WriteLine(
            $"cell=({result.VerifiedCell.X},{result.VerifiedCell.Y},{result.VerifiedCell.Z}) "
            + $"value={result.VerifiedCell.Value}");
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(CompatibilityMatrixScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        foreach ((string name, string code) in result.RejectionCodes) {
            Console.WriteLine($"rejection.{name}={code}");
        }
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(RelayModChannelScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"room={result.RoomCode}");
        Console.WriteLine($"deduplicatedFrames={result.DeduplicatedFrames}");
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(CreativeFlyMultiplayerScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"transport={result.Transport}");
        Console.WriteLine($"actorPlayerIndex={result.ActorPlayerIndex}");
        if (!string.IsNullOrWhiteSpace(result.RoomCode)) {
            Console.WriteLine($"room={result.RoomCode}");
        }
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(TerrainInteractionMultiplayerScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"transport={result.Transport}");
        Console.WriteLine($"actorPlayerIndex={result.ActorPlayerIndex}");
        Console.WriteLine($"actions={result.ActionCount}");
        if (!string.IsNullOrWhiteSpace(result.RoomCode)) {
            Console.WriteLine($"room={result.RoomCode}");
        }
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(PlayerMotionMultiplayerScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"transport={result.Transport}");
        Console.WriteLine($"actorPlayerIndex={result.ActorPlayerIndex}");
        Console.WriteLine($"actions={result.ActionCount}");
        if (!string.IsNullOrWhiteSpace(result.RoomCode)) {
            Console.WriteLine($"room={result.RoomCode}");
        }
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(TerrainStreamingMultiplayerScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"transport={result.Transport}");
        Console.WriteLine($"actorPlayerIndex={result.ActorPlayerIndex}");
        Console.WriteLine($"actions={result.ActionCount}");
        if (!string.IsNullOrWhiteSpace(result.RoomCode)) {
            Console.WriteLine($"room={result.RoomCode}");
        }
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(EntityCreatureMultiplayerScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"transport={result.Transport}");
        Console.WriteLine($"actorPlayerIndex={result.ActorPlayerIndex}");
        Console.WriteLine($"actions={result.ActionCount}");
        if (!string.IsNullOrWhiteSpace(result.RoomCode)) {
            Console.WriteLine($"room={result.RoomCode}");
        }
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(PickableProjectileMultiplayerScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"transport={result.Transport}");
        Console.WriteLine($"actorPlayerIndex={result.ActorPlayerIndex}");
        Console.WriteLine($"actions={result.ActionCount}");
        if (!string.IsNullOrWhiteSpace(result.RoomCode)) {
            Console.WriteLine($"room={result.RoomCode}");
        }
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(InventoryHealthMultiplayerScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"transport={result.Transport}");
        Console.WriteLine($"actorPlayerIndex={result.ActorPlayerIndex}");
        Console.WriteLine($"actions={result.ActionCount}");
        if (!string.IsNullOrWhiteSpace(result.RoomCode)) {
            Console.WriteLine($"room={result.RoomCode}");
        }
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(EnvironmentMechanicsMultiplayerScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"transport={result.Transport}");
        Console.WriteLine($"actorPlayerIndex={result.ActorPlayerIndex}");
        Console.WriteLine($"actions={result.ActionCount}");
        if (!string.IsNullOrWhiteSpace(result.RoomCode)) {
            Console.WriteLine($"room={result.RoomCode}");
        }
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(MountMultiplayerScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"transport={result.Transport}");
        Console.WriteLine($"actorPlayerIndex={result.ActorPlayerIndex}");
        Console.WriteLine($"actions={result.ActionCount}");
        if (!string.IsNullOrWhiteSpace(result.RoomCode)) {
            Console.WriteLine($"room={result.RoomCode}");
        }
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(LogicMultiplayerScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"transport={result.Transport}");
        Console.WriteLine($"actorPlayerIndex={result.ActorPlayerIndex}");
        Console.WriteLine($"actions={result.ActionCount}");
        if (!string.IsNullOrWhiteSpace(result.RoomCode)) {
            Console.WriteLine($"room={result.RoomCode}");
        }
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(SessionLifecycleMultiplayerScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"transport={result.Transport}");
        Console.WriteLine($"compatibilityRejections={result.CompatibilityRejections}");
        Console.WriteLine($"stableIdentities={result.StableIdentityCount}");
        Console.WriteLine($"duplicatePlayerCreatesIgnored={result.DuplicatePlayerCreatesIgnored}");
        Console.WriteLine($"rateLimitedMessages={result.RateLimitedMessages}");
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static string ResolveRuntime(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options) {
        if (options.TryGetValue("runtime", out string? value)
            && !string.IsNullOrWhiteSpace(value)) {
            return Path.GetFullPath(value);
        }
        string configuration = options.TryGetValue("configuration", out string? configurationValue)
            && !string.IsNullOrWhiteSpace(configurationValue)
                ? configurationValue
                : "Debug";
        return Path.Combine(
            repositoryRoot,
            "Survivalcraft.Test",
            "bin",
            configuration,
            "net10.0-windows",
            "win-x64");
    }

    private static async Task<CoverageTrustReport> BuildCoverageTrustReportAsync(
        string repositoryRoot) {
        string coverageDirectory = Path.Combine(
            repositoryRoot,
            "Survivalcraft.Testing",
            "Coverage");
        CoverageManifest coverage = await CoverageManifestValidator.LoadAsync(
            Path.Combine(coverageDirectory, "core-multiplayer.json")).ConfigureAwait(false);
        ProductionSurfaceManifest inventory = await ProductionSurfaceCoverage.LoadAsync(
            Path.Combine(coverageDirectory, "production-surfaces.json")).ConfigureAwait(false);
        return CoverageTrustReportBuilder.Build(
            coverage,
            inventory,
            ProductionSurfaceCoverage.Discover(repositoryRoot));
    }

    private static async Task<MultiplayerSoakSummary> RunSoakAsync(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options) {
        string runtime = ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    $"multiplayer-soak-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");

        MultiplayerSoakSchedule schedule;
        if (options.TryGetValue("replay-schedule", out string? replayScheduleValue)) {
            if (string.IsNullOrWhiteSpace(replayScheduleValue)) {
                throw new ArgumentException("Option '--replay-schedule' expects a path.");
            }
            string[] generationOnlyOptions = [
                "seed",
                "iterations",
                "transport",
                "include-loss",
                "scenarios"
            ];
            string[] conflicts = generationOnlyOptions
                .Where(options.ContainsKey)
                .ToArray();
            if (conflicts.Length > 0) {
                throw new ArgumentException(
                    "A replayed soak schedule cannot be overridden by: "
                    + string.Join(", ", conflicts.Select(name => $"--{name}")));
            }
            schedule = await MultiplayerSoakSchedule.ReadAsync(
                replayScheduleValue).ConfigureAwait(false);
        }
        else {
            string[] scenarios = GetScenarioRotation(options);
            schedule = MultiplayerSoakSchedule.Generate(
                GetInt32(options, "seed", 20260730),
                GetInt32(options, "iterations", scenarios.Length),
                GetTransport(
                    options,
                    "transport",
                    MultiplayerTransportKind.Direct),
                GetFlag(options, "include-loss"),
                scenarios);
        }

        SoakResourceBudget defaults = new();
        SoakResourceBudget budget = new(
            ToBytesFromMebibytes(
                GetInt32(
                    options,
                    "max-working-set-mib",
                    checked((int)(defaults.MaximumPeakWorkingSetBytes / 1024 / 1024))),
                "max-working-set-mib"),
            ToBytesFromMebibytes(
                GetInt32(
                    options,
                    "max-private-mib",
                    checked((int)(defaults.MaximumPeakPrivateBytes / 1024 / 1024))),
                "max-private-mib"),
            ToBytesFromMebibytes(
                GetInt32(
                    options,
                    "max-growth-mib",
                    checked((int)(defaults.MaximumWorkingSetGrowthBytes / 1024 / 1024))),
                "max-growth-mib"),
            GetInt32(options, "max-handles", defaults.MaximumPeakHandles),
            GetInt32(options, "max-threads", defaults.MaximumPeakThreads),
            GetInt32(options, "minimum-resource-samples", defaults.MinimumSamples),
            GetInt32(
                options,
                "max-pending-fault-packets",
                defaults.MaximumPendingFaultPackets));

        TimeSpan? stepTimeout = options.ContainsKey("step-timeout")
            ? GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60))
            : null;
        TimeSpan? scenarioTimeout = options.ContainsKey("scenario-timeout")
            ? GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(12))
            : null;
        return await new MultiplayerSoakRunner().RunAsync(
            new MultiplayerSoakOptions(
                runtime,
                artifactDirectory,
                schedule,
                budget,
                stepTimeout,
                scenarioTimeout,
                KeepData: GetFlag(options, "keep-data"),
                ContinueAfterFailure: !GetFlag(options, "fail-fast")))
            .ConfigureAwait(false);
    }

    private static string[] GetScenarioRotation(
        IReadOnlyDictionary<string, string?> options) {
        if (!options.TryGetValue("scenarios", out string? value)) {
            return MultiplayerSoakSchedule.DefaultScenarioRotation.ToArray();
        }
        if (string.IsNullOrWhiteSpace(value)) {
            throw new ArgumentException("Option '--scenarios' expects a comma-separated list.");
        }
        string[] scenarios = value.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (scenarios.Length == 0) {
            throw new ArgumentException("Option '--scenarios' contains no scenario names.");
        }
        return scenarios;
    }

    private static long ToBytesFromMebibytes(int mebibytes, string optionName) {
        if (mebibytes <= 0) {
            throw new ArgumentOutOfRangeException(
                optionName,
                "Resource budget must be a positive MiB value.");
        }
        return checked((long)mebibytes * 1024 * 1024);
    }

    private static void PrintSoakResult(MultiplayerSoakSummary summary) {
        Console.WriteLine("SOAK PASSED");
        Console.WriteLine($"iterations={summary.CompletedIterations}");
        Console.WriteLine($"scheduleSeed={summary.Schedule.Seed}");
        Console.WriteLine($"transport={summary.Schedule.Transport}");
        Console.WriteLine($"includesLoss={summary.Schedule.IncludesLoss}");
        foreach (MultiplayerSoakResourceHighWatermark highWatermark
            in summary.ResourceHighWatermarks) {
            Console.WriteLine(
                $"resource {highWatermark.Role} "
                + $"workingSetMiB={highWatermark.PeakWorkingSetBytes / 1024d / 1024d:F1} "
                + $"privateMiB={highWatermark.PeakPrivateBytes / 1024d / 1024d:F1} "
                + $"handles={highWatermark.PeakHandles} threads={highWatermark.PeakThreads}");
        }
        Console.WriteLine($"artifacts={summary.ArtifactDirectory}");
    }

    private static void PrintCoverageTrustReport(CoverageTrustReport report) {
        Console.WriteLine(report.IsStructurallyValid
            ? "COVERAGE CONTRACT VALID"
            : "COVERAGE CONTRACT INVALID");
        Console.WriteLine($"features={report.TotalFeatures}");
        Console.WriteLine($"surfaces.hooks={report.HookSurfaces}");
        Console.WriteLine($"surfaces.packets={report.PacketSurfaces}");
        Console.WriteLine("statuses=" + string.Join(
            ",",
            report.Statuses.Select(pair => $"{pair.Key}:{pair.Value}")));
        Console.WriteLine("confidence=" + string.Join(
            ",",
            report.ConfidenceLevels.Select(pair => $"{pair.Key}:{pair.Value}")));
        FeatureTrustSummary[] open = report.Features
            .Where(feature => feature.Status != "covered")
            .ToArray();
        Console.WriteLine($"openFeatures={open.Length}");
        foreach (FeatureTrustSummary feature in open) {
            Console.WriteLine(
                $"open {feature.Risk} {feature.Status} {feature.FeatureId} gaps={feature.Gaps.Length}");
        }
        foreach (string error in report.ValidationErrors) {
            Console.Error.WriteLine($"error {error}");
        }
    }

    private static Dictionary<string, string?> ParseOptions(string[] args, int startIndex) {
        Dictionary<string, string?> result = new(StringComparer.OrdinalIgnoreCase);
        for (int index = startIndex; index < args.Length; index++) {
            string token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2) {
                throw new ArgumentException($"Unexpected positional argument '{token}'.");
            }
            string key = token[2..];
            string? value = null;
            if (index + 1 < args.Length
                && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) {
                value = args[++index];
            }
            if (!result.TryAdd(key, value)) {
                throw new ArgumentException($"Option '--{key}' was specified more than once.");
            }
        }
        return result;
    }

    private static bool GetFlag(IReadOnlyDictionary<string, string?> options, string name) {
        if (!options.TryGetValue(name, out string? value)) {
            return false;
        }
        if (value == null) {
            return true;
        }
        if (bool.TryParse(value, out bool parsed)) {
            return parsed;
        }
        throw new ArgumentException($"Option '--{name}' expects true or false.");
    }

    private static int GetInt32(
        IReadOnlyDictionary<string, string?> options,
        string name,
        int defaultValue) {
        if (!options.TryGetValue(name, out string? value)) {
            return defaultValue;
        }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) {
            return parsed;
        }
        throw new ArgumentException($"Option '--{name}' expects an integer.");
    }

    private static double GetDouble(
        IReadOnlyDictionary<string, string?> options,
        string name,
        double defaultValue) {
        if (!options.TryGetValue(name, out string? value)) {
            return defaultValue;
        }
        if (double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double parsed)) {
            return parsed;
        }
        throw new ArgumentException($"Option '--{name}' expects a number.");
    }

    private static MultiplayerTransportKind GetTransport(
        IReadOnlyDictionary<string, string?> options,
        string name,
        MultiplayerTransportKind defaultValue) {
        if (!options.TryGetValue(name, out string? value)) {
            return defaultValue;
        }
        return ParseTransport(value, $"Option '--{name}'");
    }

    private static MultiplayerTransportKind ParseTransport(string? value, string source) {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value, ignoreCase: true, out MultiplayerTransportKind parsed)
            && Enum.IsDefined(parsed)) {
            return parsed;
        }
        throw new ArgumentException($"{source} expects direct or relay.");
    }

    private static TimeSpan GetTimeSpan(
        IReadOnlyDictionary<string, string?> options,
        string name,
        TimeSpan defaultValue) {
        double seconds = GetDouble(options, name, defaultValue.TotalSeconds);
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) {
            throw new ArgumentOutOfRangeException(name, "Timeout must be a positive number of seconds.");
        }
        return TimeSpan.FromSeconds(seconds);
    }

    private static string FindRepositoryRoot() {
        string[] seeds = [Environment.CurrentDirectory, AppContext.BaseDirectory];
        foreach (string seed in seeds) {
            DirectoryInfo? directory = new(Path.GetFullPath(seed));
            while (directory != null) {
                if (File.Exists(Path.Combine(directory.FullName, "SurvivalcraftApi.sln"))) {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException(
            "Could not find SurvivalcraftApi.sln from the current or application directory.");
    }

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "help";

    private static void PrintHelp() {
        Console.WriteLine("Survivalcraft multiplayer automation host");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  coverage [--output PATH]");
        Console.WriteLine("  doctor [--runtime PATH] [--static-only]");
        Console.WriteLine("  soak [--iterations N] [--transport direct|relay] [options]");
        Console.WriteLine("  soak --replay-schedule PATH [options]");
        Console.WriteLine("  run core-multiplayer [options]");
        Console.WriteLine("  run compatibility-matrix [options]");
        Console.WriteLine("  run relay-mod-channel [options]");
        Console.WriteLine("  run creative-fly-multiplayer --transport direct|relay [options]");
        Console.WriteLine("  run terrain-interaction-multiplayer --transport direct|relay [options]");
        Console.WriteLine("  run player-motion-multiplayer --transport direct|relay [options]");
        Console.WriteLine("  run terrain-streaming-multiplayer --transport direct|relay [options]");
        Console.WriteLine("  run entity-creature-multiplayer --transport direct|relay [options]");
        Console.WriteLine("  run pickable-projectile-multiplayer --transport direct|relay [options]");
        Console.WriteLine("  run inventory-health-multiplayer --transport direct|relay [options]");
        Console.WriteLine("  run environment-mechanics-multiplayer --transport direct|relay [options]");
        Console.WriteLine("  run mount-multiplayer --transport direct|relay [options]");
        Console.WriteLine("  run logic-multiplayer --transport direct|relay [options]");
        Console.WriteLine("  run session-lifecycle-multiplayer --transport direct|relay [options]");
        Console.WriteLine("  replay ARTIFACT_DIRECTORY [options]");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --runtime PATH             Built Survivalcraft.Test runtime");
        Console.WriteLine("  --artifacts PATH           Durable result bundle directory");
        Console.WriteLine("  --seed INTEGER             Deterministic scenario seed");
        Console.WriteLine("  --fault-seed INTEGER       Deterministic network-fault seed");
        Console.WriteLine("  --step-timeout SECONDS     Per-step deadline (default 60)");
        Console.WriteLine("  --scenario-timeout SECONDS Overall deadline (default 360)");
        Console.WriteLine("  --latency-ms INTEGER       Injected application-packet latency");
        Console.WriteLine("  --jitter-ms INTEGER        Injected deterministic jitter");
        Console.WriteLine("  --loss-rate NUMBER         Packet loss probability [0,1]");
        Console.WriteLine("  --duplication-rate NUMBER  Packet duplication probability [0,1]");
        Console.WriteLine("  --reorder-rate NUMBER      Packet reorder probability [0,1]");
        Console.WriteLine("  --reorder-delay-ms INTEGER Extra delay applied to reordered packets");
        Console.WriteLine("  --bandwidth-bytes INTEGER  Per-node outbound byte-rate limit");
        Console.WriteLine("  --keep-data                Keep isolated game data directories");
        Console.WriteLine();
        Console.WriteLine("Soak options:");
        Console.WriteLine("  --iterations INTEGER       Generated schedule length (default one full rotation)");
        Console.WriteLine("  --scenarios CSV            Scenario-name rotation to execute");
        Console.WriteLine("  --include-loss             Add deterministic 1-3% application-packet loss");
        Console.WriteLine("  --replay-schedule PATH     Replay an exact fault schedule JSON file");
        Console.WriteLine("  --max-working-set-mib N    Per-node peak working-set budget (default 1536)");
        Console.WriteLine("  --max-private-mib N        Per-node peak private-memory budget (default 2048)");
        Console.WriteLine("  --max-growth-mib N         Per-node working-set growth budget (default 768)");
        Console.WriteLine("  --max-handles N            Per-node peak handle budget (default 8192)");
        Console.WriteLine("  --max-threads N            Per-node peak thread budget (default 512)");
        Console.WriteLine("  --max-pending-fault-packets N  Per-node fault queue budget (default 10000)");
        Console.WriteLine("  --fail-fast                Stop after the first failed soak iteration");
        Console.WriteLine("  --transport direct|relay   Multiplayer transport for v2 gameplay scenarios");
    }
}
