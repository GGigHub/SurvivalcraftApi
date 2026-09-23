using System.Diagnostics;
using System.Text;
using Engine;

namespace Game {
    public sealed class CompileOutcome {
        public bool Ok;
        public int ExitCode;
        public string Log;
        public int ElapsedMs;
        public string Code;
    }

    public static class DotnetCompiler {
        public static CompileOutcome Build(string csprojPath, int timeoutMs = 180000) {
            CompileOutcome outcome = new();
            Stopwatch watch = Stopwatch.StartNew();
            if (string.IsNullOrEmpty(csprojPath) || !File.Exists(csprojPath)) {
                outcome.Ok = false;
                outcome.Code = "not_found";
                outcome.Log = "csproj not found.";
                outcome.ElapsedMs = (int)watch.ElapsedMilliseconds;
                return outcome;
            }
            ProcessStartInfo info = new() {
                FileName = "dotnet",
                Arguments = $"build \"{csprojPath}\" -c Release --nologo -v:m",
                WorkingDirectory = Path.GetDirectoryName(csprojPath) ?? "",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            try {
                using Process process = Process.Start(info);
                if (process == null) {
                    outcome.Ok = false;
                    outcome.Code = "sdk_missing";
                    outcome.Log = "Failed to start dotnet. Install .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0";
                    outcome.ElapsedMs = (int)watch.ElapsedMilliseconds;
                    return outcome;
                }
                StringBuilder log = new();
                process.OutputDataReceived += (_, e) => {
                    if (e.Data != null) {
                        log.AppendLine(e.Data);
                    }
                };
                process.ErrorDataReceived += (_, e) => {
                    if (e.Data != null) {
                        log.AppendLine(e.Data);
                    }
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(timeoutMs)) {
                    try {
                        process.Kill(true);
                    }
                    catch {
                        // ignored
                    }
                    outcome.Ok = false;
                    outcome.Code = "compile_failed";
                    outcome.Log = log + "\nCompile timed out.";
                    outcome.ExitCode = -1;
                    outcome.ElapsedMs = (int)watch.ElapsedMilliseconds;
                    return outcome;
                }
                process.WaitForExit();
                outcome.ExitCode = process.ExitCode;
                outcome.Log = log.ToString();
                outcome.Ok = process.ExitCode == 0;
                outcome.Code = outcome.Ok ? null : "compile_failed";
                outcome.ElapsedMs = (int)watch.ElapsedMilliseconds;
                return outcome;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is FileNotFoundException) {
                outcome.Ok = false;
                outcome.Code = "sdk_missing";
                outcome.Log = "dotnet was not found in PATH. Install .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0\n" + e.Message;
                outcome.ElapsedMs = (int)watch.ElapsedMilliseconds;
                return outcome;
            }
            catch (Exception e) {
                Log.Error($"[ModStudio] compile error: {e}");
                outcome.Ok = false;
                outcome.Code = "io_error";
                outcome.Log = e.ToString();
                outcome.ElapsedMs = (int)watch.ElapsedMilliseconds;
                return outcome;
            }
        }
    }
}
