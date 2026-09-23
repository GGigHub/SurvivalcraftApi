using System.Text;
using System.Text.RegularExpressions;

namespace Game {
    public static class PackageNameRules {
        public const string StudioPackageName = "com.scspm.modstudio";
        public static readonly string[] ProtectedNames = ["survivalcraft", "fastdebug", StudioPackageName];

        static readonly Regex s_packageRegex = new(@"^[a-z][a-z0-9]*(\.[a-z0-9]+)+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsProtected(string packageName) {
            if (string.IsNullOrEmpty(packageName)) {
                return false;
            }
            for (int i = 0; i < ProtectedNames.Length; i++) {
                if (string.Equals(packageName, ProtectedNames[i], StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }

        public static bool TryValidate(string packageName, out string error) {
            error = null;
            if (string.IsNullOrWhiteSpace(packageName)) {
                error = "Package name is empty.";
                return false;
            }
            string trimmed = packageName.Trim();
            if (trimmed.Length < 3 || trimmed.Length > 80) {
                error = "Package name length must be 3-80.";
                return false;
            }
            if (!s_packageRegex.IsMatch(trimmed)) {
                error = "Package name must be a lowercase reverse-domain like com.studio.hello.";
                return false;
            }
            if (IsProtected(trimmed)) {
                error = "This package name is reserved.";
                return false;
            }
            return true;
        }

        public static string SuggestPackageName(string displayName) {
            string slug = Slug(displayName);
            if (string.IsNullOrEmpty(slug)) {
                slug = "mod";
            }
            return "com.studio." + slug;
        }

        public static string ToAssemblyName(string displayName) {
            if (string.IsNullOrWhiteSpace(displayName)) {
                return "StudioMod";
            }
            StringBuilder builder = new();
            bool newWord = true;
            foreach (char c in displayName) {
                if (char.IsLetterOrDigit(c)) {
                    if (newWord) {
                        builder.Append(char.ToUpperInvariant(c));
                        newWord = false;
                    }
                    else {
                        builder.Append(c);
                    }
                }
                else {
                    newWord = true;
                }
            }
            string name = builder.ToString();
            if (name.Length == 0) {
                return "StudioMod";
            }
            if (char.IsDigit(name[0])) {
                return "Mod" + name;
            }
            return name;
        }

        public static string Slug(string displayName) {
            if (string.IsNullOrWhiteSpace(displayName)) {
                return "";
            }
            StringBuilder builder = new();
            foreach (char c in displayName) {
                if (char.IsLetterOrDigit(c)) {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }
            return builder.ToString();
        }
    }
}
