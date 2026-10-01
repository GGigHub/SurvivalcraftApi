using System.Globalization;

namespace Game {
    enum ModpackDecisionKind {
        InvalidResource,
        Busy,
        OtherModpackActive,
        FileInfoLoading,
        NoManagedMods,
        MissingEmbeddedItems,
        MissingDownloadItems,
        ReadyToActivate
    }

    sealed class ModpackDecisionInput {
        public bool IsModpack { get; init; }
        public bool IsBusy { get; init; }
        public bool HasOtherActiveModpack { get; init; }
        public bool IsFileInfoLoading { get; init; }
        public bool HasManagedMods { get; init; }
        public IReadOnlyList<ModpackItem> MissingEmbeddedItems { get; init; } = [];
        public IReadOnlyList<ModpackItem> MissingDownloadItems { get; init; } = [];
    }

    sealed class ModpackDecision {
        public ModpackDecisionKind Kind { get; init; }
        public IReadOnlyList<ModpackItem> MissingEmbeddedItems { get; init; }
        public IReadOnlyList<ModpackItem> MissingDownloadItems { get; init; }

        public bool RequiresDownload => Kind == ModpackDecisionKind.MissingDownloadItems;

        public bool CanActivate => Kind == ModpackDecisionKind.ReadyToActivate;

        public ModpackDecision(ModpackDecisionKind kind,
            IReadOnlyList<ModpackItem> missingEmbeddedItems = null,
            IReadOnlyList<ModpackItem> missingDownloadItems = null) {
            Kind = kind;
            MissingEmbeddedItems = missingEmbeddedItems ?? [];
            MissingDownloadItems = missingDownloadItems ?? [];
        }
    }

    enum ModpackOperationKind {
        Applied,
        NotActive,
        PersistFailed,
        RestoreFailed,
        StateSaveFailed
    }

    enum ModpackDownloadCompletionKind {
        ReadyToActivate,
        Incomplete,
        Failed
    }

    sealed class ModpackOperationResult {
        public ModpackOperationKind Kind { get; init; }
        public LocalModpackActivationState State { get; init; }
        public string Error { get; init; }
        public bool Succeeded => Kind == ModpackOperationKind.Applied;
    }

    sealed class ModpackActivationRequest {
        public LocalModpackRecord Record { get; init; }
        public string RecordPath { get; init; }
        public string DisplayName { get; init; }
        public IEnumerable<LocalModpackModStateEntry> CurrentModStates { get; init; } = [];
        public IEnumerable<LocalModpackFileStateEntry> BackupFiles { get; init; } = [];
    }

    sealed class ModpackDeactivationRequest {
        public LocalModpackRecord Record { get; init; }
        public LocalModpackActivationState ActiveState { get; init; }
        public IEnumerable<LocalModpackModStateEntry> CurrentModStates { get; init; } = [];
    }

    /// <summary>
    /// 整合包启用入口的无 UI 决策器。输入是 Screen 收集的事实，输出只描述下一步业务状态。
    /// 下载执行仍由 DownloadFlowManager 负责。
    /// </summary>
    sealed class ModpackManager {
        readonly Func<LocalModpackActivationState, Exception> m_saveState;
        readonly Func<IEnumerable<LocalModpackFileStateEntry>, string, (bool Success, string Error)> m_persistChanges;
        readonly Func<IEnumerable<LocalModpackFileStateEntry>, (bool Success, string Error)> m_restoreBackups;

        public ModpackManager(Func<LocalModpackActivationState, Exception> saveState = null,
            Func<IEnumerable<LocalModpackFileStateEntry>, string, (bool Success, string Error)> persistChanges = null,
            Func<IEnumerable<LocalModpackFileStateEntry>, (bool Success, string Error)> restoreBackups = null) {
            m_saveState = saveState ?? ModpackActivationState.Save;
            m_persistChanges = persistChanges
                ?? ((backups, key) => {
                    bool success = ModpackDataFiles.TryPersistChanges(backups, key, out string error);
                    return (success, error);
                });
            m_restoreBackups = restoreBackups
                ?? (backups => {
                    bool success = ModpackDataFiles.TryRestoreBackups(backups, out string error);
                    return (success, error);
                });
        }

        public ModpackDownloadCompletionKind EvaluateDownloadCompletion(ModpackDownloadProgress progress, bool hasMissingItems) {
            if (progress == null
                || progress.FailedCount > 0
                || !string.IsNullOrEmpty(progress.Error)
                || progress.Interrupted) {
                return ModpackDownloadCompletionKind.Failed;
            }
            return hasMissingItems ? ModpackDownloadCompletionKind.Incomplete : ModpackDownloadCompletionKind.ReadyToActivate;
        }

        public bool IsActive(LocalModpackRecord record, string resourcePath, LocalModpackActivationState activeState) {
            if (record == null
                || activeState == null) {
                return false;
            }
            string normalizedResourcePath = NormalizePath(resourcePath);
            string normalizedActivePath = NormalizePath(activeState.RecordPath);
            if (!string.IsNullOrWhiteSpace(normalizedResourcePath)
                && !string.IsNullOrWhiteSpace(normalizedActivePath)
                && string.Equals(normalizedResourcePath, normalizedActivePath, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
            return ModpackActivationState.MatchesRecord(record, activeState);
        }

        public bool HasOtherActiveModpack(LocalModpackRecord record, string resourcePath, LocalModpackActivationState activeState) =>
            activeState != null && !IsActive(record, resourcePath, activeState);

        public bool IsCurrentResource(string currentResourcePath, string requestResourcePath) {
            string currentPath = NormalizePath(currentResourcePath);
            string requestPath = NormalizePath(requestResourcePath);
            return !string.IsNullOrWhiteSpace(currentPath)
                && !string.IsNullOrWhiteSpace(requestPath)
                && string.Equals(currentPath, requestPath, StringComparison.OrdinalIgnoreCase);
        }

        public ActivationStateLoad LoadActivationState() => ModpackActivationState.LoadResolved();

        public IReadOnlyList<LocalModpackModStateEntry> CaptureModStateSnapshot(IEnumerable<LocalResourceItem> resources,
            Func<LocalResourceItem, string> getFilePath,
            Func<LocalResourceItem, bool> getIsDisabled) {
            if (getFilePath == null
                || getIsDisabled == null) {
                throw new ArgumentNullException(getFilePath == null ? nameof(getFilePath) : nameof(getIsDisabled));
            }
            Dictionary<string, LocalModpackModStateEntry> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (LocalResourceItem item in resources ?? []) {
                if (item?.Type != ExternalContentType.Mod) {
                    continue;
                }
                string filePath = getFilePath(item)?.Trim();
                string packageName = (item.PackageName ?? item.ModEntity?.modInfo?.PackageName)?.Trim();
                string version = (item.Version ?? item.ModEntity?.modInfo?.Version)?.Trim();
                if (string.IsNullOrWhiteSpace(filePath)
                    || string.IsNullOrWhiteSpace(packageName)
                    || string.IsNullOrWhiteSpace(version)) {
                    continue;
                }
                string key = NormalizePath(filePath);
                if (string.IsNullOrWhiteSpace(key)
                    || result.ContainsKey(key)) {
                    continue;
                }
                result[key] = new LocalModpackModStateEntry {
                    FilePath = filePath,
                    DisplayName = item.DisplayName ?? item.Name ?? packageName,
                    PackageName = packageName,
                    Version = version,
                    IsDisabled = getIsDisabled(item)
                };
            }
            return [.. result.Values.OrderBy(item => item.DisplayName, StringComparer.InvariantCultureIgnoreCase)
                .ThenBy(item => item.PackageName, StringComparer.OrdinalIgnoreCase)];
        }

        public ModpackOperationResult Activate(ModpackActivationRequest request,
            Action clearPendingActions,
            Action saveSettings,
            Func<string, string, bool, bool> setDisabledState) {
            if (request?.Record == null) {
                return new ModpackOperationResult { Kind = ModpackOperationKind.NotActive };
            }
            if (setDisabledState == null) {
                throw new ArgumentNullException(nameof(setDisabledState));
            }
            IReadOnlyList<LocalModpackModStateEntry> backupStates = [.. request.CurrentModStates ?? []];
            ManagedModSet managedMods = ModpackModMatcher.BuildManagedSet(request.Record);
            clearPendingActions?.Invoke();
            bool changed = false;
            foreach (ModpackModStateChange change in ModpackActivationRules.BuildEnableChanges(backupStates, managedMods)) {
                changed |= setDisabledState(change.PackageName, change.Version, change.IsDisabled);
            }
            if (changed) {
                saveSettings?.Invoke();
            }
            LocalModpackActivationState state = new() {
                RecordPath = request.RecordPath,
                PackageName = request.Record.PackageName,
                Version = request.Record.Version,
                DisplayName = request.DisplayName ?? request.Record.PackageName ?? "Modpack",
                UpdatedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                BackupModStates = backupStates,
                BackupFiles = [.. request.BackupFiles ?? []]
            };
            Exception error = m_saveState(state);
            return new ModpackOperationResult {
                Kind = error == null ? ModpackOperationKind.Applied : ModpackOperationKind.StateSaveFailed, State = state, Error = error?.Message
            };
        }

        public ModpackOperationResult Deactivate(ModpackDeactivationRequest request,
            Action clearPendingActions,
            Action saveSettings,
            Func<string, string, bool, bool> setDisabledState) {
            if (request?.ActiveState == null) {
                return new ModpackOperationResult { Kind = ModpackOperationKind.NotActive };
            }
            if (setDisabledState == null) {
                throw new ArgumentNullException(nameof(setDisabledState));
            }
            string persistenceKey = request.ActiveState.PackageName ?? request.ActiveState.RecordPath;
            (bool Success, string Error) persistResult = m_persistChanges(request.ActiveState.BackupFiles, persistenceKey);
            if (!persistResult.Success) {
                return new ModpackOperationResult { Kind = ModpackOperationKind.PersistFailed, Error = persistResult.Error };
            }
            (bool Success, string Error) restoreResult = m_restoreBackups(request.ActiveState.BackupFiles);
            if (!restoreResult.Success) {
                return new ModpackOperationResult { Kind = ModpackOperationKind.RestoreFailed, Error = restoreResult.Error };
            }
            ManagedModSet managedMods = ModpackModMatcher.BuildManagedSet(request.Record);
            clearPendingActions?.Invoke();
            bool changed = false;
            foreach (ModpackModStateChange change in ModpackActivationRules.BuildDisableChanges(
                    request.CurrentModStates,
                    managedMods,
                    request.ActiveState.BackupModStates
                )) {
                changed |= setDisabledState(change.PackageName, change.Version, change.IsDisabled);
            }
            if (changed) {
                saveSettings?.Invoke();
            }
            Exception error = m_saveState(null);
            return new ModpackOperationResult {
                Kind = error == null ? ModpackOperationKind.Applied : ModpackOperationKind.StateSaveFailed, Error = error?.Message
            };
        }

        public bool DisableDownloadedMods(ModpackDownloadProgress progress,
            IEnumerable<LocalResourceItem> resources,
            Func<LocalResourceItem, string> getFilePath,
            Func<string, string, bool, bool> setDisabledState,
            Action saveSettings) {
            IReadOnlyList<ModpackItem> downloadedModItems = [.. (progress?.Results ?? [])
                .Where(result => result?.Success == true
                    && result.Item != null
                    && ModpackRules.ResolveContentType(result.Item) == ExternalContentType.Mod
                )
                .Select(result => result.Item)];
            if (downloadedModItems.Count == 0) {
                return false;
            }
            ManagedModSet downloadedMods = ModpackModMatcher.BuildManagedSet(downloadedModItems);
            bool changed = false;
            foreach (LocalResourceItem item in resources ?? []) {
                if (item?.Type != ExternalContentType.Mod) {
                    continue;
                }
                string packageName = (item.PackageName ?? item.ModEntity?.modInfo?.PackageName)?.Trim();
                string version = (item.Version ?? item.ModEntity?.modInfo?.Version)?.Trim();
                string filePath = getFilePath(item)?.Trim();
                if (ModpackModMatcher.IsManaged(downloadedMods, packageName, version, filePath)) {
                    changed |= setDisabledState(packageName, version, true);
                }
            }
            if (changed) {
                saveSettings?.Invoke();
            }
            return changed;
        }

        public IReadOnlyList<ModpackItem> GetMissingEmbeddedItems(LocalModpackRecord record, Func<ModpackItem, bool> isEmbeddedMissing) {
            if (isEmbeddedMissing == null) {
                throw new ArgumentNullException(nameof(isEmbeddedMissing));
            }
            return [.. (record?.Manifest?.Items ?? []).Where(item => item != null && item.IsEnabled && isEmbeddedMissing(item))];
        }

        public IReadOnlyList<ModpackItem> GetMissingDownloadItems(LocalModpackRecord record,
            Func<ModpackItem, bool> isEmbeddedMissing,
            Func<ModpackItem, bool> isDownloaded) {
            if (isEmbeddedMissing == null) {
                throw new ArgumentNullException(nameof(isEmbeddedMissing));
            }
            if (isDownloaded == null) {
                throw new ArgumentNullException(nameof(isDownloaded));
            }
            return [.. (record?.Manifest?.Items ?? [])
                .Where(item => item != null && item.IsEnabled)
                .Where(item => !isEmbeddedMissing(item))
                .Where(item => !isDownloaded(item))];
        }

        public ModpackDecision EvaluateEnable(ModpackDecisionInput input) {
            if (input is null or { IsModpack: false }) {
                return new ModpackDecision(ModpackDecisionKind.InvalidResource);
            }
            if (input.IsBusy) {
                return new ModpackDecision(ModpackDecisionKind.Busy);
            }
            if (input.HasOtherActiveModpack) {
                return new ModpackDecision(ModpackDecisionKind.OtherModpackActive);
            }
            if (input.IsFileInfoLoading) {
                return new ModpackDecision(ModpackDecisionKind.FileInfoLoading);
            }
            if (!input.HasManagedMods) {
                return new ModpackDecision(ModpackDecisionKind.NoManagedMods);
            }
            if (input.MissingEmbeddedItems is { Count: > 0 }) {
                return new ModpackDecision(ModpackDecisionKind.MissingEmbeddedItems, input.MissingEmbeddedItems, input.MissingDownloadItems);
            }
            return input.MissingDownloadItems is { Count: > 0 }
                ? new ModpackDecision(ModpackDecisionKind.MissingDownloadItems, input.MissingEmbeddedItems, input.MissingDownloadItems)
                : new ModpackDecision(ModpackDecisionKind.ReadyToActivate);
        }

        static string NormalizePath(string path) => path?.Trim().Replace('\\', '/');
    }
}