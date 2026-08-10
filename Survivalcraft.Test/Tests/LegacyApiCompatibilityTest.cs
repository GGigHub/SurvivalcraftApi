using System.Reflection;
using Engine;
using Game;
using GameEntitySystem;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Survivalcraft.Test;

public sealed class LegacyApiCompatibilityTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void Api145AdditiveMembersKeepTheirHistoricalBinaryShapes() {
        FieldInfo explosionField = typeof(Block).GetField(
            "IsExplosionTransparent",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingFieldException(typeof(Block).FullName, "IsExplosionTransparent");
        Assert.Equal(typeof(bool), explosionField.FieldType);

        AirBlock block = new();
        explosionField.SetValue(block, true);
        Assert.True(block.KeepsPickablesOnExplosion);
        explosionField.SetValue(block, false);
        block.ExplosionKeepsPickables = true;
        Assert.True(block.KeepsPickablesOnExplosion);

        MethodInfo decipherMethod = typeof(ModsManageContentScreen).GetMethod(
            "GetDecipherStream",
            BindingFlags.Static | BindingFlags.Public,
            null,
            [typeof(Stream)],
            null)
            ?? throw new MissingMethodException(
                typeof(ModsManageContentScreen).FullName,
                "GetDecipherStream(Stream)");
        Assert.Equal(typeof(Stream), decipherMethod.ReturnType);
        byte[] payload = [1, 3, 3, 7];
        using Stream decoded = Assert.IsAssignableFrom<Stream>(
            decipherMethod.Invoke(null, [new MemoryStream(payload)]));
        using MemoryStream decodedCopy = new();
        decoded.CopyTo(decodedCopy);
        Assert.Equal(payload, decodedCopy.ToArray());

        MethodInfo legacyRecipes = typeof(CraftingRecipesManager).GetMethod(
            "GetRecipes1",
            BindingFlags.Static | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null)
            ?? throw new MissingMethodException(
                typeof(CraftingRecipesManager).FullName,
                "GetRecipes1()");
        Assert.Equal(
            typeof(ReadOnlyList<CraftingRecipe>),
            legacyRecipes.ReturnType);

        MethodInfo addParticleSystem = typeof(SubsystemParticles).GetMethod(
            "AddLegacyParticleSystem",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            [typeof(ParticleSystemBase)],
            null)
            ?? throw new MissingMethodException(
                typeof(SubsystemParticles).FullName,
                "AddLegacyParticleSystem(ParticleSystemBase)");
        MethodInfo removeParticleSystem = typeof(SubsystemParticles).GetMethod(
            "RemoveLegacyParticleSystem",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            [typeof(ParticleSystemBase)],
            null)
            ?? throw new MissingMethodException(
                typeof(SubsystemParticles).FullName,
                "RemoveLegacyParticleSystem(ParticleSystemBase)");
        Assert.Equal(typeof(void), addParticleSystem.ReturnType);
        Assert.Equal(typeof(void), removeParticleSystem.ReturnType);
        Assert.Single(typeof(SubsystemParticles).GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "AddParticleSystem"));
        Assert.Single(typeof(SubsystemParticles).GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "RemoveParticleSystem"));

        MethodInfo setOnFire = typeof(ComponentOnFire).GetMethod(
            "SetOnFire",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            [typeof(ComponentCreature), typeof(float)],
            null)
            ?? throw new MissingMethodException(
                typeof(ComponentOnFire).FullName,
                "SetOnFire(ComponentCreature, Single)");
        MethodInfo extinguish = typeof(ComponentOnFire).GetMethod(
            "Extinguish",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null)
            ?? throw new MissingMethodException(
                typeof(ComponentOnFire).FullName,
                "Extinguish()");
        Assert.Equal(typeof(void), setOnFire.ReturnType);
        Assert.True(setOnFire.IsVirtual);
        Assert.Equal(typeof(void), extinguish.ReturnType);
        Assert.True(extinguish.IsVirtual);

        MethodInfo injure = RequirePublicInstanceMethod(
            typeof(ComponentHealth),
            "Injure",
            typeof(float),
            typeof(ComponentCreature),
            typeof(bool),
            typeof(string));
        MethodInfo startMounting = RequirePublicInstanceMethod(
            typeof(ComponentRider),
            "StartMounting",
            typeof(ComponentMount));
        MethodInfo startDismounting = RequirePublicInstanceMethod(
            typeof(ComponentRider),
            "StartDismounting");
        MethodInfo nauseaEffect = RequirePublicInstanceMethod(
            typeof(ComponentSickness),
            "NauseaEffect");
        MethodInfo sleep = RequirePublicInstanceMethod(
            typeof(ComponentSleep),
            "Sleep",
            typeof(bool));
        Type[] legacyProjectileParameters = [
            typeof(int),
            typeof(Vector3),
            typeof(Vector3),
            typeof(Vector3),
            typeof(ComponentCreature)
        ];
        MethodInfo addProjectile = RequirePublicInstanceMethod(
            typeof(SubsystemProjectiles),
            "AddProjectile",
            legacyProjectileParameters);
        MethodInfo fireProjectile = RequirePublicInstanceMethod(
            typeof(SubsystemProjectiles),
            "FireProjectile",
            legacyProjectileParameters);
        MethodInfo changeCell = RequirePublicInstanceMethod(
            typeof(SubsystemTerrain),
            "ChangeCell",
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(bool));
        MethodInfo movingBlocksRaycast = RequirePublicInstanceMethod(
            typeof(SubsystemMovingBlocks),
            "Raycast",
            typeof(Vector3),
            typeof(Vector3),
            typeof(bool));
        MethodInfo minerRaycast = RequirePublicInstanceMethod(
            typeof(ComponentMiner),
            "Raycast",
            typeof(Ray3),
            typeof(RaycastMode),
            typeof(bool),
            typeof(bool),
            typeof(bool));
        MethodInfo destroyCell = RequirePublicInstanceMethod(
            typeof(SubsystemTerrain),
            "DestroyCell",
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(bool),
            typeof(bool));
        MethodInfo setBoneTransform = RequirePublicInstanceMethod(
            typeof(ModelWidget),
            "SetBoneTransform",
            typeof(int),
            typeof(Matrix?));
        MethodInfo damageItem = typeof(BlocksManager).GetMethod(
            "DamageItem",
            BindingFlags.Static | BindingFlags.Public,
            null,
            [typeof(int), typeof(int)],
            null)
            ?? throw new MissingMethodException(
                typeof(BlocksManager).FullName,
                "DamageItem(Int32, Int32)");

        Assert.All(
            [injure, startMounting, startDismounting, nauseaEffect, sleep,
                addProjectile, fireProjectile, changeCell, destroyCell],
            method => Assert.True(method.IsVirtual));
        Assert.Equal(typeof(Projectile), addProjectile.ReturnType);
        Assert.Equal(typeof(Projectile), fireProjectile.ReturnType);
        Assert.Equal(typeof(MovingBlocksRaycastResult?), movingBlocksRaycast.ReturnType);
        Assert.Equal(typeof(object), minerRaycast.ReturnType);
        Assert.Equal(typeof(void), setBoneTransform.ReturnType);
        Assert.Equal(typeof(int), damageItem.ReturnType);

        Assert.Equal(typeof(string), typeof(ModsManager).GetField("path")?.FieldType);
        foreach (string fieldName in new[] {
                     "WaterColorsMap",
                     "GrassColorsMap",
                     "OakLeavesColorsMap",
                     "BirchLeavesColorsMap",
                     "MimosaLeavesColorsMap",
                     "SpruceLeavesColorsMap",
                     "TallSpruceLeavesColorsMap",
                     "IvyColorsMap"
                 }) {
            Assert.Equal(
                typeof(BlockColorsMap),
                typeof(BlockColorsMap).GetField(fieldName)?.FieldType);
        }

        Assert.Equal(
            typeof(Vector3),
            typeof(PlayerInput).GetField("SneakMove")?.FieldType);
        Assert.Equal(
            typeof(bool),
            typeof(PlayerInput).GetField("ToggleSneak")?.FieldType);
        Assert.Equal(
            typeof(float),
            typeof(ComponentInput).GetField("m_lastLeftTrigger")?.FieldType);
        Assert.Equal(
            typeof(float),
            typeof(ComponentInput).GetField("m_lastRightTrigger")?.FieldType);

        Assert.Equal(
            typeof(DynamicArray<ClothingData>),
            typeof(ClothingBlock).GetField("m_legacyClothingData")?.FieldType);
        Assert.Equal(
            typeof(ComponentCreature),
            typeof(Projectile).GetField("LegacyOwner")?.FieldType);
        Assert.Equal(
            typeof(bool),
            typeof(Projectile).GetField("LegacyIsInWater")?.FieldType);
        Assert.Equal(
            typeof(Action<PlayerData>),
            typeof(SubsystemPlayers).GetProperty("LegacyPlayerAdded")?.PropertyType);
        Assert.Equal(
            typeof(Action<PlayerData>),
            typeof(SubsystemPlayers).GetProperty("LegacyPlayerRemoved")?.PropertyType);
        Assert.NotNull(typeof(SubsystemPlayers).GetEvent("PlayerAdded"));
        Assert.NotNull(typeof(SubsystemPlayers).GetEvent("PlayerRemoved"));

        MethodInfo playIdleSound = RequirePublicInstanceMethod(
            typeof(ComponentCreatureSounds),
            "PlayIdleSound",
            typeof(bool));
        MethodInfo playPainSound = RequirePublicInstanceMethod(
            typeof(ComponentCreatureSounds),
            "PlayPainSound");
        MethodInfo playMoanSound = RequirePublicInstanceMethod(
            typeof(ComponentCreatureSounds),
            "PlayMoanSound");
        MethodInfo playSneezeSound = RequirePublicInstanceMethod(
            typeof(ComponentCreatureSounds),
            "PlaySneezeSound");
        MethodInfo playCoughSound = RequirePublicInstanceMethod(
            typeof(ComponentCreatureSounds),
            "PlayCoughSound");
        MethodInfo playPukeSound = RequirePublicInstanceMethod(
            typeof(ComponentCreatureSounds),
            "PlayPukeSound");
        MethodInfo playAttackSound = RequirePublicInstanceMethod(
            typeof(ComponentCreatureSounds),
            "PlayAttackSound");
        Assert.All(
            [playIdleSound, playPainSound, playMoanSound, playSneezeSound,
                playPukeSound, playAttackSound],
            method => Assert.True(method.IsVirtual));
        Assert.False(playCoughSound.IsVirtual);
        Assert.All(
            [playIdleSound, playPainSound, playMoanSound, playSneezeSound,
                playCoughSound, playPukeSound, playAttackSound],
            method => Assert.Equal(typeof(void), method.ReturnType));

        Assert.Equal(
            typeof(bool),
            typeof(SubsystemPlayers).GetField("PlayerStartedPlaying")?.FieldType);
        Assert.Equal(
            typeof(double?),
            typeof(PlayerData).GetField("m_playerDeathTime")?.FieldType);

        MethodInfo startFlu = RequirePublicInstanceMethod(
            typeof(ComponentFlu),
            "StartFlu");
        MethodInfo startSickness = RequirePublicInstanceMethod(
            typeof(ComponentSickness),
            "StartSickness");
        MethodInfo legacyInjury = RequirePublicInstanceMethod(
            typeof(ComponentHealth),
            "Injure",
            typeof(Injury));
        MethodInfo legacyMinerHit = RequirePublicInstanceMethod(
            typeof(ComponentMiner),
            "Hit",
            typeof(ComponentBody),
            typeof(Vector3),
            typeof(Vector3));
        MethodInfo legacyLightningStrike = RequirePublicInstanceMethod(
            typeof(SubsystemSky),
            "MakeLightningStrike",
            typeof(Vector3),
            typeof(bool));
        MethodInfo legacyMovingBlockChangeCell = RequirePublicInstanceMethod(
            typeof(SubsystemTerrain),
            "ChangeCell",
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(bool),
            typeof(MovingBlock));
        ConstructorInfo legacyContentInfoConstructor =
            typeof(ContentInfo).GetConstructor([typeof(string)])
            ?? throw new MissingMethodException(
                typeof(ContentInfo).FullName,
                ".ctor(String)");
        Assert.All(
            [startFlu, startSickness, legacyInjury, legacyLightningStrike,
                legacyMovingBlockChangeCell],
            method => Assert.True(method.IsVirtual));
        Assert.False(legacyMinerHit.IsVirtual);
        Assert.All(
            [startFlu, startSickness, legacyInjury, legacyMinerHit,
                legacyLightningStrike, legacyMovingBlockChangeCell],
            method => Assert.Equal(typeof(void), method.ReturnType));
        ContentInfo legacyContentInfo = Assert.IsType<ContentInfo>(
            legacyContentInfoConstructor.Invoke(["Textures/Legacy.png"]));
        Assert.Equal(string.Empty, legacyContentInfo.SourcePackageName);

        Projectile projectile = new();
        typeof(Projectile).GetProperty("IsInWater")!.SetValue(projectile, true);
        Assert.True(projectile.IsInFluid);
        Assert.True(projectile.LegacyIsInWater);
        ComponentCreature owner = new() { m_entity = new Entity() };
        projectile.Owner = owner;
        Assert.Same(owner, projectile.LegacyOwner);
        Assert.Same(owner.Entity, projectile.OwnerEntity);

        MethodInfo temperatureAdjustment = typeof(SubsystemWeather).GetMethod(
            "GetLegacyTemperatureAdjustmentAtHeight",
            BindingFlags.Static | BindingFlags.Public,
            null,
            [typeof(int)],
            null)
            ?? throw new MissingMethodException(
                typeof(SubsystemWeather).FullName,
                "GetLegacyTemperatureAdjustmentAtHeight(Int32)");
        Assert.Equal(typeof(int), temperatureAdjustment.ReturnType);
        Assert.Equal(
            SubsystemWeather.GetTemperatureAdjustmentAtHeight(80),
            (int)temperatureAdjustment.Invoke(null, [80])!);

        PropertyInfo viewAngleMode = typeof(SettingsManager).GetProperty(
            "ViewAngleMode",
            BindingFlags.Static | BindingFlags.Public)
            ?? throw new MissingMemberException(
                typeof(SettingsManager).FullName,
                "ViewAngleMode");
        Assert.Equal(typeof(ViewAngleMode), viewAngleMode.PropertyType);
        Assert.Equal(0, (int)ViewAngleMode.Normal);
        Assert.Equal(1, (int)ViewAngleMode.Narrow);
        Assert.Equal(2, (int)ViewAngleMode.Wide);
        float originalViewAngle = SettingsManager.ViewAngle;
        try {
            SettingsManager.ViewAngleMode = ViewAngleMode.Narrow;
            Assert.Equal(0.9f, SettingsManager.ViewAngle);
            SettingsManager.ViewAngleMode = ViewAngleMode.Wide;
            Assert.Equal(1.125f, SettingsManager.ViewAngle);
        }
        finally {
            SettingsManager.ViewAngle = originalViewAngle;
        }
    }

    [Theory]
    [InlineData("1.45", true)]
    [InlineData("1.53", true)]
    [InlineData("1.7", true)]
    [InlineData("1.8", true)]
    [InlineData("1.80", true)]
    [InlineData("1.8.0.3", true)]
    [InlineData("1.81", false)]
    [InlineData("1.8.1", false)]
    [InlineData("1.9", false)]
    [InlineData(null, false)]
    [Trait("Layer", "L0")]
    public void RecipeGetterRewriteIsLimitedToPre181ApiVersions(
        string? apiVersion,
        bool expected) {
        Assert.Equal(
            expected,
            LegacyModAssemblyCompatibility.IsPre181ApiVersion(apiVersion!));
    }

    static MethodInfo RequirePublicInstanceMethod(
        Type declaringType,
        string name,
        params Type[] parameterTypes) =>
        declaringType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(method =>
                method.Name == name
                && !method.IsGenericMethod
                && method.GetParameters()
                    .Select(parameter => parameter.ParameterType)
                    .SequenceEqual(parameterTypes))
        ?? throw new MissingMethodException(
            declaringType.FullName,
            $"{name}({string.Join(", ", parameterTypes.Select(type => type.Name))})");

    [Fact]
    [Trait("Layer", "L0")]
    public void OldReadOnlyRecipeGetterReferenceIsRewrittenWithoutChangingItsReturnType() {
        byte[] original = CreateLegacyRecipesProbeAssembly();
        ModInfo legacyMod = new() {
            Name = "Legacy probe",
            ApiVersion = "1.45"
        };

        byte[] patched = LegacyModAssemblyCompatibility.Apply(
            legacyMod,
            "LegacyProbe.dll",
            original,
            out string[] appliedBreakIds);

        Assert.Equal(
            LegacyModAssemblyCompatibility.CraftingRecipesReadOnlyGetterBreakId,
            Assert.Single(appliedBreakIds));
        Assert.NotEqual(original, patched);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            new MemoryStream(patched, false));
        MethodReference reference = Assert.Single(
            assembly.MainModule.Types
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions)
                .Select(instruction => instruction.Operand)
                .OfType<MethodReference>()
                .Where(method => method.DeclaringType.FullName
                    == "Game.CraftingRecipesManager"));
        Assert.Equal("GetRecipes1", reference.Name);
        Assert.Equal(
            "Engine.ReadOnlyList`1<Game.CraftingRecipe>",
            reference.ReturnType.FullName);

        ModInfo modernMod = new() { ApiVersion = "1.9" };
        byte[] modernResult = LegacyModAssemblyCompatibility.Apply(
            modernMod,
            "ModernProbe.dll",
            original,
            out string[] modernBreakIds);
        Assert.Same(original, modernResult);
        Assert.Empty(modernBreakIds);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LegacyParticleCallsAreRenamedWithoutCreatingHarmonyAmbiguousOverloads() {
        byte[] original = CreateLegacyParticleSystemProbeAssembly();
        ModInfo legacyMod = new() {
            Name = "Legacy particle probe",
            ApiVersion = "1.45"
        };

        byte[] patched = LegacyModAssemblyCompatibility.Apply(
            legacyMod,
            "LegacyParticleProbe.dll",
            original,
            out string[] appliedBreakIds);

        Assert.Equal(
            LegacyModAssemblyCompatibility.LegacyParticleSystemOneArgumentBreakId,
            Assert.Single(appliedBreakIds));
        Assert.NotEqual(original, patched);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            new MemoryStream(patched, false));
        string[] names = assembly.MainModule.GetMemberReferences()
            .OfType<MethodReference>()
            .Where(reference => reference.DeclaringType.FullName
                == "Game.SubsystemParticles")
            .Select(reference => reference.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[] { "AddLegacyParticleSystem", "RemoveLegacyParticleSystem" },
            names);

        ModInfo modernMod = new() { ApiVersion = "1.9" };
        byte[] modernResult = LegacyModAssemblyCompatibility.Apply(
            modernMod,
            "ModernParticleProbe.dll",
            original,
            out string[] modernBreakIds);
        Assert.Same(original, modernResult);
        Assert.Empty(modernBreakIds);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void FirearmsSiyahAkrepBootstrapCallIsReplacedByEqualLengthNops() {
        byte[] original = CreateFirearmsBootstrapProbeAssembly();
        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
                   new MemoryStream(original, false))) {
            MethodDefinition method = assembly.MainModule.Types
                .Single(type => type.FullName == "Game.GunsModLoader")
                .Methods.Single(candidate => candidate.Name == "BlocksInitalized");
            Instruction call = Assert.Single(method.Body.Instructions.Where(instruction =>
                instruction.OpCode == OpCodes.Call));
            Assert.Equal(0x0F, call.Offset);
            Assert.Equal("Game.SAFunctions::InitializeFunctions",
                ((MethodReference)call.Operand).FullName.Split(' ')[1].TrimEnd('(', ')'));
        }

        byte[] patched = LegacyModAssemblyCompatibility.RewriteStaticVoidCallToNops(
            original,
            "FirearmsProbe.dll",
            "Game.GunsModLoader",
            "BlocksInitalized",
            0x0F,
            "Game.SAFunctions",
            "InitializeFunctions");

        Assert.NotEqual(original, patched);
        using AssemblyDefinition patchedAssembly = AssemblyDefinition.ReadAssembly(
            new MemoryStream(patched, false));
        MethodDefinition patchedMethod = patchedAssembly.MainModule.Types
            .Single(type => type.FullName == "Game.GunsModLoader")
            .Methods.Single(candidate => candidate.Name == "BlocksInitalized");
        Assert.DoesNotContain(patchedMethod.Body.Instructions,
            instruction => instruction.OpCode == OpCodes.Call);
        Instruction[] replacement = patchedMethod.Body.Instructions
            .Where(instruction => instruction.Offset is >= 0x0F and < 0x14)
            .ToArray();
        Assert.Equal(5, replacement.Length);
        Assert.All(replacement, instruction => Assert.Equal(OpCodes.Nop, instruction.OpCode));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void FirearmsPackageAdapterRequiresAuditedPackageAndDllHashesAndIdentity() {
        byte[] rawPackageBytes = System.Text.Encoding.UTF8.GetBytes("read-only-package-probe");
        using Stream decodedPackage = ModsManager.GetDecipherStreamAndHash(
            new MemoryStream(rawPackageBytes, writable: false),
            out string rawPackageSha256);
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rawPackageBytes)),
            rawPackageSha256);
        Assert.Equal(rawPackageBytes, ((MemoryStream)decodedPackage).ToArray());

        byte[] untrustedBytes = CreateFirearmsBootstrapProbeAssembly();
        ModInfo matchingMetadata = new() {
            PackageName = "gunsmod.ysf.scmod",
            Version = "2.8",
            ApiVersion = "1.45"
        };

        Assert.False(LegacyModAssemblyCompatibility.IsExactFirearms28Assembly(
            matchingMetadata,
            "#Guns.dll",
            untrustedBytes,
            LegacyModAssemblyCompatibility.Firearms28PackageSha256));
        Assert.True(LegacyModAssemblyCompatibility.IsExactFirearms28Package(
            matchingMetadata,
            LegacyModAssemblyCompatibility.Firearms28PackageSha256));
        Assert.False(LegacyModAssemblyCompatibility.IsExactFirearms28Package(
            matchingMetadata,
            new string('0', 64)));
        Assert.Matches("^[0-9a-f]{64}$",
            LegacyModAssemblyCompatibility.Firearms28AssemblySha256);
        Assert.Matches("^[0-9a-f]{64}$",
            LegacyModAssemblyCompatibility.Firearms28PackageSha256);

        matchingMetadata.PackageName = "lookalike.gunsmod.ysf.scmod";
        Assert.False(LegacyModAssemblyCompatibility.IsExactFirearms28Package(
            matchingMetadata,
            LegacyModAssemblyCompatibility.Firearms28PackageSha256));
        Assert.False(LegacyModAssemblyCompatibility.IsExactFirearms28Assembly(
            matchingMetadata,
            "#Guns.dll",
            untrustedBytes,
            LegacyModAssemblyCompatibility.Firearms28PackageSha256));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LegacyGameWidgetTypeReferenceCanBeRemappedToTheCurrentGlobalType() {
        byte[] original = CreateLegacyGameWidgetTypeProbeAssembly();

        byte[] patched = LegacyModAssemblyCompatibility.RewriteTypeReferenceNamespace(
            original,
            "LegacyGameWidgetProbe.dll",
            "Survivalcraft",
            "Game",
            "GameWidget",
            string.Empty);

        Assert.NotEqual(original, patched);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            new MemoryStream(patched, false));
        TypeReference gameWidget = Assert.Single(
            assembly.MainModule.GetTypeReferences().Where(reference =>
                reference.Scope?.Name == "Survivalcraft"
                && reference.Name == "GameWidget"));
        Assert.Equal(string.Empty, gameWidget.Namespace);
        Assert.Equal("GameWidget", gameWidget.FullName);
        Assert.Equal(
            "GameWidget",
            assembly.MainModule.Types
                .Single(type => type.FullName == "Legacy.GameWidgetProbe")
                .Methods.Single(method => method.Name == "GetGameWidget")
                .ReturnType.FullName);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LegacyTemperatureAdjustmentCallCanBeRemappedToTheCompatibilityMethod() {
        byte[] original = CreateLegacyTemperatureAdjustmentProbeAssembly();

        byte[] patched = LegacyModAssemblyCompatibility.RewriteMethodReferenceName(
            original,
            "LegacyWeatherProbe.dll",
            "Survivalcraft",
            "Game",
            "SubsystemWeather",
            "GetTemperatureAdjustmentAtHeight",
            "GetLegacyTemperatureAdjustmentAtHeight",
            "System.Int32",
            ["System.Int32"]);

        Assert.NotEqual(original, patched);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            new MemoryStream(patched, false));
        MethodReference call = Assert.IsType<MethodReference>(
            assembly.MainModule.Types
                .Single(type => type.FullName == "Legacy.WeatherProbe")
                .Methods.Single(method => method.Name == "GetAdjustment")
                .Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call)
                .Operand);
        Assert.Equal("GetLegacyTemperatureAdjustmentAtHeight", call.Name);
        Assert.Equal("Game.SubsystemWeather", call.DeclaringType.FullName);
        Assert.Equal("System.Int32", call.ReturnType.FullName);
        Assert.Equal("System.Int32", Assert.Single(call.Parameters).ParameterType.FullName);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LegacyFieldReferenceCanBeRemappedWithoutChangingItsBinaryType() {
        byte[] original = CreateLegacyClothingFieldProbeAssembly();

        byte[] patched = LegacyModAssemblyCompatibility.RewriteFieldReferenceName(
            original,
            "LegacyClothingProbe.dll",
            "Survivalcraft",
            "Game",
            "ClothingBlock",
            "m_clothingData",
            "m_legacyClothingData",
            "Engine.DynamicArray`1<Game.ClothingData>");

        Assert.NotEqual(original, patched);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            new MemoryStream(patched, false));
        FieldReference field = Assert.Single(
            assembly.MainModule.GetMemberReferences()
                .OfType<FieldReference>()
                .Where(reference => reference.DeclaringType.FullName == "Game.ClothingBlock"));
        Assert.Equal("m_legacyClothingData", field.Name);
        Assert.Equal("Engine.DynamicArray`1<Game.ClothingData>", field.FieldType.FullName);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LegacyInstancePropertyAccessorCanBeRemappedToCompatibilityProperty() {
        byte[] original = CreateLegacyPlayerAddedAccessorProbeAssembly();

        byte[] patched = LegacyModAssemblyCompatibility.RewriteMethodReferenceName(
            original,
            "LegacyPlayerCallbacksProbe.dll",
            "Survivalcraft",
            "Game",
            "SubsystemPlayers",
            "get_PlayerAdded",
            "get_LegacyPlayerAdded",
            "System.Action`1<Game.PlayerData>",
            [],
            hasThis: true);

        Assert.NotEqual(original, patched);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            new MemoryStream(patched, false));
        MethodReference call = Assert.IsType<MethodReference>(
            assembly.MainModule.Types
                .Single(type => type.FullName == "Legacy.PlayerCallbacksProbe")
                .Methods.Single(method => method.Name == "GetPlayerAdded")
                .Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Callvirt)
                .Operand);
        Assert.Equal("get_LegacyPlayerAdded", call.Name);
        Assert.True(call.HasThis);
        Assert.Equal("System.Action`1<Game.PlayerData>", call.ReturnType.FullName);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LegacyProjectileUpdateBodyCanBeReplacedByMinimalReturn() {
        byte[] original = CreateLegacyProjectileUpdateProbeAssembly();
        int originalCodeSize;
        using (AssemblyDefinition originalAssembly = AssemblyDefinition.ReadAssembly(
                   new MemoryStream(original, false))) {
            originalCodeSize = originalAssembly.MainModule.Types
                .Single(type => type.FullName == "Game.SubsystemNewProjectiles")
                .Methods.Single(method => method.Name == "Update")
                .Body.CodeSize;
        }

        byte[] patched = LegacyModAssemblyCompatibility.RewriteInstanceVoidMethodToReturn(
            original,
            "FirearmsProjectileProbe.dll",
            "Game.SubsystemNewProjectiles",
            "Update",
            ["Single"],
            originalCodeSize);

        Assert.NotEqual(original, patched);
        using AssemblyDefinition patchedAssembly = AssemblyDefinition.ReadAssembly(
            new MemoryStream(patched, false));
        MethodDefinition patchedMethod = patchedAssembly.MainModule.Types
            .Single(type => type.FullName == "Game.SubsystemNewProjectiles")
            .Methods.Single(method => method.Name == "Update");
        Assert.Equal(Mono.Cecil.TypeAttributes.Public,
            patchedMethod.DeclaringType.Attributes & Mono.Cecil.TypeAttributes.VisibilityMask);
        Assert.Equal(Mono.Cecil.MethodAttributes.Virtual,
            patchedMethod.Attributes & Mono.Cecil.MethodAttributes.Virtual);
        Assert.Equal("System.Single", Assert.Single(patchedMethod.Parameters).ParameterType.FullName);
        Assert.Empty(patchedMethod.Body.ExceptionHandlers);
        Instruction instruction = Assert.Single(patchedMethod.Body.Instructions);
        Assert.Equal(OpCodes.Ret, instruction.OpCode);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LegacyTwoArgumentStaticCallCanBeReplacedByStackPops() {
        byte[] original = CreateLegacyTwoArgumentCallProbeAssembly();
        int callOffset;
        using (AssemblyDefinition originalAssembly = AssemblyDefinition.ReadAssembly(
                   new MemoryStream(original, false))) {
            MethodDefinition caller = originalAssembly.MainModule.Types
                .Single(type => type.FullName == "Game.GunsModLoader")
                .Methods.Single(method => method.Name == "OnTerrainContentsGenerated");
            callOffset = caller.Body.Instructions
                .Single(instruction => instruction.OpCode == OpCodes.Call)
                .Offset;
        }

        byte[] patched = LegacyModAssemblyCompatibility.RewriteStaticVoidCallToPops(
            original,
            "FirearmsStructuresProbe.dll",
            "Game.GunsModLoader",
            "OnTerrainContentsGenerated",
            ["Game.TerrainChunk"],
            callOffset,
            "Game.SAGenerateManager",
            "HandleStructuresAddition",
            ["Game.SubsystemGMItemsBlockBehavior", "Game.TerrainChunk"]);

        using AssemblyDefinition patchedAssembly = AssemblyDefinition.ReadAssembly(
            new MemoryStream(patched, false));
        MethodDefinition patchedMethod = patchedAssembly.MainModule.Types
            .Single(type => type.FullName == "Game.GunsModLoader")
            .Methods.Single(method => method.Name == "OnTerrainContentsGenerated");
        Assert.DoesNotContain(patchedMethod.Body.Instructions,
            instruction => instruction.OpCode == OpCodes.Call);
        Instruction[] replacement = patchedMethod.Body.Instructions
            .Where(instruction => instruction.Offset is >= 2 and < 7)
            .ToArray();
        Assert.Equal(5, replacement.Length);
        Assert.Equal(OpCodes.Pop, replacement[0].OpCode);
        Assert.Equal(OpCodes.Pop, replacement[1].OpCode);
        Assert.All(replacement.Skip(2),
            instruction => Assert.Equal(OpCodes.Nop, instruction.OpCode));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LegacyStaticDictionaryCanBeResetBeforeRepeatedInitialization() {
        LegacyDictionaryProbe.Colors.Clear();
        LegacyDictionaryProbe.Colors.Add("blue", Color.Blue);
        LegacyDictionaryProbe.Colors.Add("green", Color.Green);

        int firstResetCount = LegacyModRuntimeCompatibility.ClearStaticDictionary(
            typeof(LegacyDictionaryProbe).Assembly,
            typeof(LegacyDictionaryProbe).FullName!,
            nameof(LegacyDictionaryProbe.Colors));
        int secondResetCount = LegacyModRuntimeCompatibility.ClearStaticDictionary(
            typeof(LegacyDictionaryProbe).Assembly,
            typeof(LegacyDictionaryProbe).FullName!,
            nameof(LegacyDictionaryProbe.Colors));

        Assert.Equal(2, firstResetCount);
        Assert.Equal(0, secondResetCount);
        Assert.Empty(LegacyDictionaryProbe.Colors);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LegacyBlockValueRemapPreservesLightAndData() {
        int original = Terrain.MakeBlockValue(603, 11, 12345);
        Dictionary<int, int> blockIndexMap = new() { [603] = 391 };

        int remapped = LegacyModRuntimeCompatibility.RemapLegacyBlockValue(
            original,
            blockIndexMap);

        Assert.Equal(391, Terrain.ExtractContents(remapped));
        Assert.Equal(11, Terrain.ExtractLight(remapped));
        Assert.Equal(12345, Terrain.ExtractData(remapped));
        Assert.Equal(
            Terrain.MakeBlockValue(5, 7, 99),
            LegacyModRuntimeCompatibility.RemapLegacyBlockValue(
                Terrain.MakeBlockValue(5, 7, 99),
                blockIndexMap));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LegacyBlockIndexResolutionRequiresAnAuditedMapping() {
        Dictionary<int, int> blockIndexMap = new() { [611] = 405 };

        Assert.Equal(
            405,
            LegacyModRuntimeCompatibility.RequireLegacyBlockIndex(
                blockIndexMap,
                611,
                "test gun"));
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            LegacyModRuntimeCompatibility.RequireLegacyBlockIndex(
                blockIndexMap,
                612,
                "test gun"));
        Assert.Contains("612", exception.Message, StringComparison.Ordinal);
        Assert.Contains("test gun", exception.Message, StringComparison.Ordinal);
    }

    sealed class LegacyDictionaryProbe {
        public static readonly Dictionary<string, Color> Colors = [];
    }

    static byte[] CreateLegacyParticleSystemProbeAssembly() {
        using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("LegacyParticleProbe", new Version(1, 0, 0, 0)),
            "LegacyParticleProbe",
            ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;
        AssemblyNameReference survivalcraft = new(
            "Survivalcraft",
            new Version(2, 3, 0, 0));
        module.AssemblyReferences.Add(survivalcraft);
        TypeReference subsystemParticles = new(
            "Game",
            "SubsystemParticles",
            module,
            survivalcraft);
        TypeReference particleSystemBase = new(
            "Game",
            "ParticleSystemBase",
            module,
            survivalcraft);
        MethodReference addParticleSystem = new(
            "AddParticleSystem",
            module.TypeSystem.Void,
            subsystemParticles) {
            HasThis = true
        };
        addParticleSystem.Parameters.Add(new ParameterDefinition(particleSystemBase));
        MethodReference removeParticleSystem = new(
            "RemoveParticleSystem",
            module.TypeSystem.Void,
            subsystemParticles) {
            HasThis = true
        };
        removeParticleSystem.Parameters.Add(new ParameterDefinition(particleSystemBase));

        TypeDefinition probeType = new(
            "Legacy",
            "ParticleProbe",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(probeType);
        MethodDefinition exercise = new(
            "Exercise",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
            module.TypeSystem.Void);
        exercise.Parameters.Add(new ParameterDefinition(subsystemParticles));
        exercise.Parameters.Add(new ParameterDefinition(particleSystemBase));
        probeType.Methods.Add(exercise);
        ILProcessor processor = exercise.Body.GetILProcessor();
        processor.Append(processor.Create(OpCodes.Ldarg_0));
        processor.Append(processor.Create(OpCodes.Ldarg_1));
        processor.Append(processor.Create(OpCodes.Callvirt, addParticleSystem));
        processor.Append(processor.Create(OpCodes.Ldarg_0));
        processor.Append(processor.Create(OpCodes.Ldarg_1));
        processor.Append(processor.Create(OpCodes.Callvirt, removeParticleSystem));
        processor.Append(processor.Create(OpCodes.Ret));

        using MemoryStream output = new();
        assembly.Write(output);
        return output.ToArray();
    }

    static byte[] CreateLegacyGameWidgetTypeProbeAssembly() {
        using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("LegacyGameWidgetProbe", new Version(1, 0, 0, 0)),
            "LegacyGameWidgetProbe",
            ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;
        AssemblyNameReference survivalcraft = new(
            "Survivalcraft",
            new Version(2, 3, 0, 0));
        module.AssemblyReferences.Add(survivalcraft);
        TypeReference legacyGameWidget = new(
            "Game",
            "GameWidget",
            module,
            survivalcraft);
        TypeDefinition probeType = new(
            "Legacy",
            "GameWidgetProbe",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(probeType);
        MethodDefinition getGameWidget = new(
            "GetGameWidget",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
            legacyGameWidget);
        probeType.Methods.Add(getGameWidget);
        ILProcessor processor = getGameWidget.Body.GetILProcessor();
        processor.Append(processor.Create(OpCodes.Ldnull));
        processor.Append(processor.Create(OpCodes.Ret));

        using MemoryStream output = new();
        assembly.Write(output);
        return output.ToArray();
    }

    static byte[] CreateLegacyTemperatureAdjustmentProbeAssembly() {
        using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("LegacyWeatherProbe", new Version(1, 0, 0, 0)),
            "LegacyWeatherProbe",
            ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;
        AssemblyNameReference survivalcraft = new(
            "Survivalcraft",
            new Version(2, 3, 0, 0));
        module.AssemblyReferences.Add(survivalcraft);
        TypeReference subsystemWeather = new(
            "Game",
            "SubsystemWeather",
            module,
            survivalcraft);
        MethodReference legacyMethod = new(
            "GetTemperatureAdjustmentAtHeight",
            module.TypeSystem.Int32,
            subsystemWeather) {
            HasThis = false
        };
        legacyMethod.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));

        TypeDefinition probeType = new(
            "Legacy",
            "WeatherProbe",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(probeType);
        MethodDefinition getAdjustment = new(
            "GetAdjustment",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
            module.TypeSystem.Int32);
        getAdjustment.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        probeType.Methods.Add(getAdjustment);
        ILProcessor processor = getAdjustment.Body.GetILProcessor();
        processor.Append(processor.Create(OpCodes.Ldarg_0));
        processor.Append(processor.Create(OpCodes.Call, legacyMethod));
        processor.Append(processor.Create(OpCodes.Ret));

        using MemoryStream output = new();
        assembly.Write(output);
        return output.ToArray();
    }

    static byte[] CreateLegacyClothingFieldProbeAssembly() {
        using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("LegacyClothingProbe", new Version(1, 0, 0, 0)),
            "LegacyClothingProbe",
            ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;
        AssemblyNameReference survivalcraft = new(
            "Survivalcraft",
            new Version(2, 3, 0, 0));
        AssemblyNameReference engine = new(
            "Engine",
            new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(survivalcraft);
        module.AssemblyReferences.Add(engine);
        TypeReference clothingBlock = new(
            "Game",
            "ClothingBlock",
            module,
            survivalcraft);
        TypeReference clothingData = new(
            "Game",
            "ClothingData",
            module,
            survivalcraft);
        TypeReference dynamicArray = new(
            "Engine",
            "DynamicArray`1",
            module,
            engine,
            true);
        GenericInstanceType clothingArray = new(dynamicArray);
        clothingArray.GenericArguments.Add(clothingData);
        FieldReference legacyField = new(
            "m_clothingData",
            clothingArray,
            clothingBlock);

        TypeDefinition probeType = new(
            "Legacy",
            "ClothingProbe",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(probeType);
        MethodDefinition getData = new(
            "GetData",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
            clothingArray);
        getData.Parameters.Add(new ParameterDefinition(clothingBlock));
        probeType.Methods.Add(getData);
        ILProcessor processor = getData.Body.GetILProcessor();
        processor.Append(processor.Create(OpCodes.Ldarg_0));
        processor.Append(processor.Create(OpCodes.Ldfld, legacyField));
        processor.Append(processor.Create(OpCodes.Ret));

        using MemoryStream output = new();
        assembly.Write(output);
        return output.ToArray();
    }

    static byte[] CreateLegacyPlayerAddedAccessorProbeAssembly() {
        using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("LegacyPlayerCallbacksProbe", new Version(1, 0, 0, 0)),
            "LegacyPlayerCallbacksProbe",
            ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;
        AssemblyNameReference survivalcraft = new(
            "Survivalcraft",
            new Version(2, 3, 0, 0));
        module.AssemblyReferences.Add(survivalcraft);
        TypeReference subsystemPlayers = new(
            "Game",
            "SubsystemPlayers",
            module,
            survivalcraft);
        TypeReference playerData = new(
            "Game",
            "PlayerData",
            module,
            survivalcraft);
        GenericInstanceType playerAction = new(module.ImportReference(typeof(Action<>)));
        playerAction.GenericArguments.Add(playerData);
        MethodReference legacyGetter = new(
            "get_PlayerAdded",
            playerAction,
            subsystemPlayers) {
            HasThis = true
        };

        TypeDefinition probeType = new(
            "Legacy",
            "PlayerCallbacksProbe",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(probeType);
        MethodDefinition getPlayerAdded = new(
            "GetPlayerAdded",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
            playerAction);
        getPlayerAdded.Parameters.Add(new ParameterDefinition(subsystemPlayers));
        probeType.Methods.Add(getPlayerAdded);
        ILProcessor processor = getPlayerAdded.Body.GetILProcessor();
        processor.Append(processor.Create(OpCodes.Ldarg_0));
        processor.Append(processor.Create(OpCodes.Callvirt, legacyGetter));
        processor.Append(processor.Create(OpCodes.Ret));

        using MemoryStream output = new();
        assembly.Write(output);
        return output.ToArray();
    }

    static byte[] CreateLegacyRecipesProbeAssembly() {
        using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("LegacyProbe", new Version(1, 0, 0, 0)),
            "LegacyProbe",
            ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;
        AssemblyNameReference survivalcraft = new(
            "Survivalcraft",
            new Version(2, 4, 0, 0));
        AssemblyNameReference engine = new(
            "Engine",
            new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(survivalcraft);
        module.AssemblyReferences.Add(engine);

        TypeDefinition caller = new(
            "CompatibilityProbe",
            "Caller",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(caller);
        MethodDefinition probe = new(
            "Probe",
            Mono.Cecil.MethodAttributes.Public
                | Mono.Cecil.MethodAttributes.Static,
            module.TypeSystem.Void);
        caller.Methods.Add(probe);

        TypeReference craftingRecipe = new(
            "Game",
            "CraftingRecipe",
            module,
            survivalcraft);
        TypeReference readOnlyList = new(
            "Engine",
            "ReadOnlyList`1",
            module,
            engine,
            true);
        GenericInstanceType returnType = new(readOnlyList);
        returnType.GenericArguments.Add(craftingRecipe);
        TypeReference craftingRecipesManager = new(
            "Game",
            "CraftingRecipesManager",
            module,
            survivalcraft);
        MethodReference oldGetter = new(
            "get_Recipes",
            returnType,
            craftingRecipesManager) {
            HasThis = false
        };

        ILProcessor processor = probe.Body.GetILProcessor();
        processor.Append(processor.Create(OpCodes.Call, oldGetter));
        processor.Append(processor.Create(OpCodes.Pop));
        processor.Append(processor.Create(OpCodes.Ret));

        using MemoryStream output = new();
        assembly.Write(output);
        return output.ToArray();
    }

    static byte[] CreateFirearmsBootstrapProbeAssembly() {
        using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("FirearmsProbe", new Version(1, 0, 0, 0)),
            "FirearmsProbe",
            ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;

        TypeDefinition saFunctions = new(
            "Game",
            "SAFunctions",
            Mono.Cecil.TypeAttributes.Public
                | Mono.Cecil.TypeAttributes.Abstract
                | Mono.Cecil.TypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(saFunctions);
        MethodDefinition initializeFunctions = new(
            "InitializeFunctions",
            Mono.Cecil.MethodAttributes.Public
                | Mono.Cecil.MethodAttributes.Static,
            module.TypeSystem.Void);
        saFunctions.Methods.Add(initializeFunctions);
        initializeFunctions.Body.GetILProcessor().Append(
            initializeFunctions.Body.GetILProcessor().Create(OpCodes.Ret));

        TypeDefinition gunsModLoader = new(
            "Game",
            "GunsModLoader",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(gunsModLoader);
        MethodDefinition blocksInitialized = new(
            "BlocksInitalized",
            Mono.Cecil.MethodAttributes.Public,
            module.TypeSystem.Void);
        gunsModLoader.Methods.Add(blocksInitialized);
        ILProcessor processor = blocksInitialized.Body.GetILProcessor();
        for (int index = 0; index < 0x0F; index++) {
            processor.Append(processor.Create(OpCodes.Nop));
        }
        processor.Append(processor.Create(OpCodes.Call, initializeFunctions));
        processor.Append(processor.Create(OpCodes.Ret));

        using MemoryStream output = new();
        assembly.Write(output);
        return output.ToArray();
    }

    static byte[] CreateLegacyProjectileUpdateProbeAssembly() {
        using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("FirearmsProjectileProbe", new Version(1, 0, 0, 0)),
            "FirearmsProjectileProbe",
            ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;
        TypeDefinition projectiles = new(
            "Game",
            "SubsystemNewProjectiles",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(projectiles);
        MethodDefinition update = new(
            "Update",
            Mono.Cecil.MethodAttributes.Public
                | Mono.Cecil.MethodAttributes.Virtual
                | Mono.Cecil.MethodAttributes.Final
                | Mono.Cecil.MethodAttributes.NewSlot,
            module.TypeSystem.Void);
        update.Parameters.Add(new ParameterDefinition("dt", Mono.Cecil.ParameterAttributes.None,
            module.TypeSystem.Single));
        projectiles.Methods.Add(update);

        ILProcessor processor = update.Body.GetILProcessor();
        Instruction tryStart = processor.Create(OpCodes.Nop);
        Instruction handlerStart = processor.Create(OpCodes.Nop);
        Instruction methodReturn = processor.Create(OpCodes.Ret);
        processor.Append(tryStart);
        for (int index = 0; index < 32; index++) {
            processor.Append(processor.Create(OpCodes.Nop));
        }
        processor.Append(processor.Create(OpCodes.Leave_S, methodReturn));
        processor.Append(handlerStart);
        processor.Append(processor.Create(OpCodes.Endfinally));
        processor.Append(methodReturn);
        update.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally) {
            TryStart = tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = methodReturn
        });

        using MemoryStream output = new();
        assembly.Write(output);
        return output.ToArray();
    }

    static byte[] CreateLegacyTwoArgumentCallProbeAssembly() {
        using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("FirearmsStructuresProbe", new Version(1, 0, 0, 0)),
            "FirearmsStructuresProbe",
            ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;
        TypeDefinition terrainChunk = new(
            "Game",
            "TerrainChunk",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
            module.TypeSystem.Object);
        TypeDefinition behavior = new(
            "Game",
            "SubsystemGMItemsBlockBehavior",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
            module.TypeSystem.Object);
        TypeDefinition generateManager = new(
            "Game",
            "SAGenerateManager",
            Mono.Cecil.TypeAttributes.Public
                | Mono.Cecil.TypeAttributes.Abstract
                | Mono.Cecil.TypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(terrainChunk);
        module.Types.Add(behavior);
        module.Types.Add(generateManager);
        MethodDefinition handleStructures = new(
            "HandleStructuresAddition",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
            module.TypeSystem.Void);
        handleStructures.Parameters.Add(new ParameterDefinition(
            "behavior",
            Mono.Cecil.ParameterAttributes.None,
            behavior));
        handleStructures.Parameters.Add(new ParameterDefinition(
            "chunk",
            Mono.Cecil.ParameterAttributes.None,
            terrainChunk));
        handleStructures.Body.GetILProcessor().Append(
            handleStructures.Body.GetILProcessor().Create(OpCodes.Ret));
        generateManager.Methods.Add(handleStructures);

        TypeDefinition loader = new(
            "Game",
            "GunsModLoader",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(loader);
        MethodDefinition terrainGenerated = new(
            "OnTerrainContentsGenerated",
            Mono.Cecil.MethodAttributes.Public,
            module.TypeSystem.Void);
        terrainGenerated.Parameters.Add(new ParameterDefinition(
            "chunk",
            Mono.Cecil.ParameterAttributes.None,
            terrainChunk));
        loader.Methods.Add(terrainGenerated);
        ILProcessor processor = terrainGenerated.Body.GetILProcessor();
        processor.Append(processor.Create(OpCodes.Ldnull));
        processor.Append(processor.Create(OpCodes.Ldarg_1));
        processor.Append(processor.Create(OpCodes.Call, handleStructures));
        processor.Append(processor.Create(OpCodes.Ret));

        using MemoryStream output = new();
        assembly.Write(output);
        return output.ToArray();
    }
}
