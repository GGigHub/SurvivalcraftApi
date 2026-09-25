using Engine;
using System.Security.Cryptography;
using System.Text.Json;

namespace Game;

public sealed class Compat23ModLoader : ModLoader {
    private readonly HashSet<string> _adaptedPackages = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions s_jsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    public override void OnPreloadStart(IReadOnlyList<ModEntity> mods) {
        _adaptedPackages.Clear();
        if (Entity == null
            || !Entity.TryGetModFileBytes("PatchData/catalog.json", out byte[] catalogBytes)) {
            return;
        }

        Compat23PatchCatalog catalog;
        try {
            catalog = JsonSerializer.Deserialize<Compat23PatchCatalog>(catalogBytes, s_jsonOptions);
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.Compat23] Failed to read patch catalog: {e}");
            return;
        }
        if (catalog?.FormatVersion != 1 || catalog.Rules == null) {
            Log.Error("[Survivalcraft.Compat23] Unsupported or empty patch catalog.");
            return;
        }
        if (!string.Equals(catalog.TargetScVersion, ModsManager.GameVersion, StringComparison.Ordinal)) {
            Log.Warning(
                $"[Survivalcraft.Compat23] Catalog targets Survivalcraft {catalog.TargetScVersion}, current game version is {ModsManager.GameVersion}; patches skipped.");
            return;
        }

        foreach (Compat23PatchRule rule in catalog.Rules) {
            if (rule == null || string.IsNullOrWhiteSpace(rule.PackageName) || rule.Files == null) {
                Log.Warning("[Survivalcraft.Compat23] Ignored an invalid patch rule.");
                continue;
            }
            foreach (ModEntity owner in mods ?? Array.Empty<ModEntity>()) {
                if (owner == Entity
                    || owner.modInfo == null
                    || !string.Equals(owner.modInfo.PackageName, rule.PackageName, StringComparison.Ordinal)
                    || !string.Equals(owner.modInfo.Version, rule.ModVersion, StringComparison.Ordinal)) {
                    continue;
                }
                try {
                    if (ApplyRule(owner, rule)) {
                        _adaptedPackages.Add(owner.modInfo.PackageName);
                    }
                }
                catch (Exception e) {
                    Log.Error($"[Survivalcraft.Compat23] Rule {rule.RuleId} failed safely: {e}");
                }
            }
        }
    }

    public override void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) {
        const string gunPackageName = "gunsmod.ysf.scmod";
        if (!_adaptedPackages.Contains(gunPackageName)) {
            return;
        }

        foreach (ModAssemblyImage image in assemblies ?? Array.Empty<ModAssemblyImage>()) {
            if (image?.Owner?.modInfo == null
                || !string.Equals(image.Owner.modInfo.PackageName, gunPackageName, StringComparison.Ordinal)
                || !string.Equals(image.FileName, "#Guns.dll", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            try {
                if (Compat23GunModPatcher.MakeBlockInitializationRepeatable(image)) {
                    Log.Information(
                        $"[Survivalcraft.Compat23] Patched {image.FileName} in memory to support repeated world initialization.");
                }
            }
            catch (Exception e) {
                Log.Error($"[Survivalcraft.Compat23] Failed to patch {image.FileName}: {e}");
            }
        }
    }

    public override void OnClassSubstitutes(
        IReadOnlyDictionary<string, List<ModsManager.ClassSubstitute>> substitutions) {
        int selectedCount = 0;
        foreach ((string guid, List<ModsManager.ClassSubstitute> candidates) in substitutions) {
            ModsManager.ClassSubstitute[] adaptedCandidates = candidates
                .Where(candidate => _adaptedPackages.Contains(candidate.PackageName))
                .ToArray();
            if (adaptedCandidates.Length == 0) {
                continue;
            }
            if (adaptedCandidates.Length > 1) {
                Log.Warning(
                    $"[Survivalcraft.Compat23] Multiple adapted mods replace class {guid}; selected {adaptedCandidates[0].PackageName} by mod load order.");
            }
            ModsManager.SelectedClassSubstitutes[guid] = adaptedCandidates[0];
            selectedCount++;
        }
        if (selectedCount > 0) {
            Log.Information(
                $"[Survivalcraft.Compat23] Automatically selected adapted mod classes for {selectedCount} class substitution(s).");
        }
    }

    private bool ApplyRule(ModEntity owner, Compat23PatchRule rule) {
        if (owner.modInfo.Preload) {
            Log.Information($"[Survivalcraft.Compat23] Skipped {rule.RuleId}: preload mods are already loaded.");
            return false;
        }
        Dictionary<string, byte[]> staged = new(StringComparer.Ordinal);
        List<string> deletions = [];
        foreach (Compat23FilePatch filePatch in rule.Files ?? []) {
            if (!owner.TryGetModFileBytes(filePatch.Path, out byte[] sourceBytes)) {
                if (filePatch.Optional) {
                    continue;
                }
                Log.Information(
                    $"[Survivalcraft.Compat23] Skipped {rule.RuleId}: missing {filePatch.Path} in {owner.modInfo.Name}.");
                return false;
            }
            if (!Matches(sourceBytes, filePatch.SourceSize, filePatch.SourceSha256)) {
                if (filePatch.Optional) {
                    continue;
                }
                Log.Information(
                    $"[Survivalcraft.Compat23] Skipped {rule.RuleId}: source fingerprint mismatch for {filePatch.Path}.");
                return false;
            }
            if (string.Equals(filePatch.Kind, "Delete", StringComparison.Ordinal)) {
                deletions.Add(filePatch.Path);
                continue;
            }

            if (!TryReadPatch(filePatch.PatchFile, out byte[] patchBytes)
                || !Matches(patchBytes, filePatch.PatchSize, filePatch.PatchSha256)) {
                if (filePatch.Optional) {
                    continue;
                }
                Log.Error(
                    $"[Survivalcraft.Compat23] Skipped {rule.RuleId}: patch data is missing or damaged for {filePatch.Path}.");
                return false;
            }

            byte[] targetBytes;
            try {
                targetBytes = filePatch.Kind switch {
                    "BinaryDelta" => Compat23BinaryDelta.Apply(sourceBytes, patchBytes),
                    "Replace" => patchBytes,
                    _ => throw new InvalidDataException($"Unknown patch kind [{filePatch.Kind}].")
                };
            }
            catch (Exception e) {
                Log.Error(
                    $"[Survivalcraft.Compat23] Skipped {rule.RuleId}: failed to apply {filePatch.Path}: {e.Message}");
                return false;
            }
            if (!Matches(targetBytes, filePatch.TargetSize, filePatch.TargetSha256)) {
                Log.Error(
                    $"[Survivalcraft.Compat23] Skipped {rule.RuleId}: target fingerprint mismatch for {filePatch.Path}.");
                return false;
            }
            staged.Add(filePatch.Path, targetBytes);
        }

        if (staged.Count == 0 && deletions.Count == 0) {
            return false;
        }
        try {
            owner.ApplyModFileChanges(staged, deletions);
            Log.Information(
                $"[Survivalcraft.Compat23] Applied {rule.RuleId} to {owner.modInfo.Name}: {staged.Count} file(s) replaced, {deletions.Count} deleted.");
            return true;
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.Compat23] Failed to install {rule.RuleId}: {e}");
            return false;
        }
    }

    private bool TryReadPatch(string filename, out byte[] bytes) {
        bytes = null;
        if (string.IsNullOrWhiteSpace(filename)
            || filename.StartsWith("/", StringComparison.Ordinal)
            || filename.Contains("..", StringComparison.Ordinal)
            || !filename.StartsWith("PatchData/patches/", StringComparison.Ordinal)) {
            return false;
        }
        return Entity.TryGetModFileBytes(filename, out bytes);
    }

    private static bool Matches(byte[] bytes, long expectedSize, string expectedSha256) =>
        bytes != null
        && expectedSize == bytes.LongLength
        && !string.IsNullOrWhiteSpace(expectedSha256)
        && string.Equals(
            Convert.ToHexString(SHA256.HashData(bytes)),
            expectedSha256,
            StringComparison.OrdinalIgnoreCase);
}

internal sealed class Compat23PatchCatalog {
    public Compat23PatchCatalog() { }

    public int FormatVersion { get; set; }
    public string TargetScVersion { get; set; }
    public List<Compat23PatchRule> Rules { get; set; }
}

internal sealed class Compat23PatchRule {
    public Compat23PatchRule() { }

    public string RuleId { get; set; }
    public string PackageName { get; set; }
    public string ModVersion { get; set; }
    public List<Compat23FilePatch> Files { get; set; }
}

internal sealed class Compat23FilePatch {
    public Compat23FilePatch() { }

    public string Path { get; set; }
    public string Kind { get; set; }
    public string PatchFile { get; set; }
    public long SourceSize { get; set; }
    public string SourceSha256 { get; set; }
    public long PatchSize { get; set; }
    public string PatchSha256 { get; set; }
    public long TargetSize { get; set; }
    public string TargetSha256 { get; set; }
    public bool Optional { get; set; }
}
