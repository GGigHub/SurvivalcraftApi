namespace Game {
    public static class StudioPathGuard {
        public static bool TryResolve(string workspaceRoot, string relativePath, out string fullPath, out string errorCode) {
            fullPath = null;
            errorCode = null;
            if (string.IsNullOrWhiteSpace(workspaceRoot)) {
                errorCode = "invalid_argument";
                return false;
            }
            string root = Path.GetFullPath(workspaceRoot);
            if (string.IsNullOrEmpty(relativePath)) {
                fullPath = root;
                return true;
            }
            string trimmed = relativePath.Replace('\\', '/').Trim();
            if (trimmed.StartsWith("/") || Path.IsPathRooted(trimmed)) {
                errorCode = "path_escape";
                return false;
            }
            string combined;
            try {
                combined = Path.GetFullPath(Path.Combine(root, trimmed));
            }
            catch (Exception) {
                errorCode = "path_escape";
                return false;
            }
            string prefix = root;
            if (!prefix.EndsWith(Path.DirectorySeparatorChar)) {
                prefix += Path.DirectorySeparatorChar;
            }
            if (!combined.Equals(root, StringComparison.OrdinalIgnoreCase)
                && !combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                errorCode = "path_escape";
                return false;
            }
            fullPath = combined;
            return true;
        }
    }
}
