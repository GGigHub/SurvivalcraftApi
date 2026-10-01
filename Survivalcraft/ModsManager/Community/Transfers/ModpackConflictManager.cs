namespace Game {
    sealed class ModpackConflictManager {
        readonly Lock m_sync = new();
        ModpackConflictRequest m_pendingRequest;

        public event Action<ModpackConflictRequest> RequestCreated;

        public Task<ModpackFileConflictAction> RequestAsync(string fileName) {
            ModpackConflictRequest request = new(fileName);
            lock (m_sync) {
                if (m_pendingRequest != null) {
                    throw new InvalidOperationException(LanguageControl.Get("ModpackFileConflictDialog", "ConflictAlreadyWaitingForUser"));
                }
                m_pendingRequest = request;
            }
            RequestCreated?.Invoke(request);
            return request.Completion.Task;
        }

        public bool TryMarkRequestDisplayed(ModpackConflictRequest request) {
            lock (m_sync) {
                if (!ReferenceEquals(m_pendingRequest, request)
                    || request == null
                    || request.IsDialogShown) {
                    return false;
                }
                request.IsDialogShown = true;
                return true;
            }
        }

        public void Complete(ModpackConflictRequest request, ModpackFileConflictAction action) {
            lock (m_sync) {
                if (ReferenceEquals(m_pendingRequest, request)) {
                    m_pendingRequest = null;
                }
            }
            request?.Completion.TrySetResult(action);
        }
    }

    sealed class ModpackConflictRequest {
        public ModpackConflictRequest(string fileName) {
            FileName = fileName;
            Completion = new TaskCompletionSource<ModpackFileConflictAction>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public string FileName { get; }
        public TaskCompletionSource<ModpackFileConflictAction> Completion { get; }
        public bool IsDialogShown { get; set; }
    }
}