using System.Text.Json;

namespace Survivalcraft.Testing.Snapshots;

public static class SnapshotComparer {
    public static SnapshotDiff Compare(
        StateSnapshot expected,
        StateSnapshot actual,
        SnapshotComparisonOptions? options = null) {
        options ??= new SnapshotComparisonOptions();
        using JsonDocument expectedDocument = JsonDocument.Parse(
            SnapshotCanonicalizer.ToCanonicalJson(expected, options.DecimalPlaces));
        using JsonDocument actualDocument = JsonDocument.Parse(
            SnapshotCanonicalizer.ToCanonicalJson(actual, options.DecimalPlaces));

        List<SnapshotDifference> differences = [];
        CompareElements(
            expectedDocument.RootElement,
            actualDocument.RootElement,
            "$",
            options,
            differences);
        return new SnapshotDiff(
            differences.Count == 0,
            SnapshotCanonicalizer.ComputeSha256(expected, options.DecimalPlaces),
            SnapshotCanonicalizer.ComputeSha256(actual, options.DecimalPlaces),
            differences.ToArray());
    }

    private static void CompareElements(
        JsonElement expected,
        JsonElement actual,
        string path,
        SnapshotComparisonOptions options,
        List<SnapshotDifference> differences) {
        if (options.IsIgnored(path)) {
            return;
        }
        if (expected.ValueKind != actual.ValueKind) {
            differences.Add(new SnapshotDifference(
                path,
                expected.GetRawText(),
                actual.GetRawText()));
            return;
        }

        switch (expected.ValueKind) {
            case JsonValueKind.Object:
                Dictionary<string, JsonElement> actualProperties = actual
                    .EnumerateObject()
                    .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
                foreach (JsonProperty expectedProperty in expected.EnumerateObject()) {
                    string childPath = path + "." + expectedProperty.Name;
                    if (!actualProperties.Remove(expectedProperty.Name, out JsonElement actualValue)) {
                        if (!options.IsIgnored(childPath)) {
                            differences.Add(new SnapshotDifference(
                                childPath,
                                expectedProperty.Value.GetRawText(),
                                "<missing>"));
                        }
                        continue;
                    }
                    CompareElements(
                        expectedProperty.Value,
                        actualValue,
                        childPath,
                        options,
                        differences);
                }
                foreach ((string name, JsonElement value) in actualProperties) {
                    string childPath = path + "." + name;
                    if (!options.IsIgnored(childPath)) {
                        differences.Add(new SnapshotDifference(
                            childPath,
                            "<missing>",
                            value.GetRawText()));
                    }
                }
                break;

            case JsonValueKind.Array:
                JsonElement.ArrayEnumerator expectedItems = expected.EnumerateArray();
                JsonElement.ArrayEnumerator actualItems = actual.EnumerateArray();
                int index = 0;
                while (true) {
                    bool hasExpected = expectedItems.MoveNext();
                    bool hasActual = actualItems.MoveNext();
                    if (!hasExpected && !hasActual) {
                        break;
                    }
                    string childPath = $"{path}[{index}]";
                    if (!hasExpected || !hasActual) {
                        differences.Add(new SnapshotDifference(
                            childPath,
                            hasExpected ? expectedItems.Current.GetRawText() : "<missing>",
                            hasActual ? actualItems.Current.GetRawText() : "<missing>"));
                    }
                    else {
                        CompareElements(
                            expectedItems.Current,
                            actualItems.Current,
                            childPath,
                            options,
                            differences);
                    }
                    index++;
                }
                break;

            default:
                if (expected.GetRawText() != actual.GetRawText()) {
                    differences.Add(new SnapshotDifference(
                        path,
                        expected.GetRawText(),
                        actual.GetRawText()));
                }
                break;
        }
    }
}

public sealed record SnapshotComparisonOptions(
    int DecimalPlaces = 5,
    IReadOnlySet<string>? IgnoredPaths = null) {
    public bool IsIgnored(string path) =>
        IgnoredPaths?.Any(ignored =>
            string.Equals(path, ignored, StringComparison.Ordinal)
            || path.StartsWith(ignored + ".", StringComparison.Ordinal)
            || path.StartsWith(ignored + "[", StringComparison.Ordinal)) == true;
}

public sealed record SnapshotDiff(
    bool Equal,
    string ExpectedHash,
    string ActualHash,
    SnapshotDifference[] Differences);

public sealed record SnapshotDifference(
    string Path,
    string Expected,
    string Actual);
