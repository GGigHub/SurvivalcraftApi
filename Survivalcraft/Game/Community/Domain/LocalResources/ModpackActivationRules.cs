namespace Game {
    static class ModpackActivationRules {
        public static List<ModpackModStateChange> BuildEnableChanges(
            IEnumerable<LocalModpackModStateEntry> currentStates,
            ManagedModSet managedMods) {
            List<ModpackModStateChange> changes = [];
            foreach (LocalModpackModStateEntry state in currentStates ?? []) {
                if (state == null) {
                    continue;
                }
                changes.Add(
                    new ModpackModStateChange {
                        PackageName = state.PackageName,
                        Version = state.Version,
                        IsDisabled = !ModpackModMatcher.IsManaged(managedMods, state.PackageName, state.Version, state.FilePath)
                    }
                );
            }
            return changes;
        }

        public static List<ModpackModStateChange> BuildDisableChanges(IEnumerable<LocalModpackModStateEntry> currentStates,
            ManagedModSet managedMods,
            IEnumerable<LocalModpackModStateEntry> backupStates) {
            List<ModpackModStateChange> changes = [];
            foreach (LocalModpackModStateEntry state in currentStates ?? []) {
                if (state != null
                    && ModpackModMatcher.IsManaged(managedMods, state.PackageName, state.Version, state.FilePath)) {
                    changes.Add(new ModpackModStateChange { PackageName = state.PackageName, Version = state.Version, IsDisabled = true });
                }
            }
            foreach (LocalModpackModStateEntry state in backupStates ?? []) {
                if (state != null) {
                    changes.Add(
                        new ModpackModStateChange { PackageName = state.PackageName, Version = state.Version, IsDisabled = state.IsDisabled }
                    );
                }
            }
            return changes;
        }
    }

    sealed class ModpackModStateChange {
        public string PackageName { get; set; }
        public string Version { get; set; }
        public bool IsDisabled { get; set; }
    }
}