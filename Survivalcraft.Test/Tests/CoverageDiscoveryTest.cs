using System.Reflection;
using Survivalcraft.Testing.Coverage;
using Xunit;

namespace Survivalcraft.Test;

public sealed class CoverageDiscoveryTest {
    [Fact]
    [Trait("Layer", "L1")]
    public async Task ManifestTestReferencesResolveToDiscoverableMethods() {
        string path = Path.Combine(AppContext.BaseDirectory, "Coverage", "core-multiplayer.json");
        CoverageManifest manifest = await CoverageManifestValidator.LoadAsync(path);

        string[] references = manifest.Features
            .SelectMany(feature => new[] { feature.DirectTest, feature.RelayTest })
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(references);
        foreach (string reference in references) {
            AssertTestReferenceExists(reference);
        }
    }

    private static void AssertTestReferenceExists(string reference) {
        int caseStart = reference.IndexOf('(');
        string methodReference = caseStart >= 0 ? reference[..caseStart] : reference;
        int separator = methodReference.LastIndexOf('.');
        Assert.True(separator > 0, $"Invalid test reference '{reference}'.");
        string typeName = methodReference[..separator];
        string methodName = methodReference[(separator + 1)..];
        Assembly assembly = typeof(CoverageDiscoveryTest).Assembly;
        Type? type = assembly.GetTypes().SingleOrDefault(candidate =>
            string.Equals(candidate.Name, typeName, StringComparison.Ordinal));
        Assert.NotNull(type);
        MethodInfo? method = type.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        Assert.True(
            method.GetCustomAttributes<FactAttribute>(inherit: true).Any(),
            $"Referenced method '{methodReference}' is not an xUnit test.");
        if (caseStart >= 0) {
            string caseName = reference[(caseStart + 1)..].TrimEnd(')');
            bool caseExists = method.GetCustomAttributes<InlineDataAttribute>(inherit: true)
                .SelectMany(attribute => attribute.GetData(method))
                .Any(data => data.Length > 0
                    && string.Equals(data[0]?.ToString(), caseName, StringComparison.OrdinalIgnoreCase));
            Assert.True(
                caseExists,
                $"Referenced theory case '{reference}' was not found.");
        }
    }
}
