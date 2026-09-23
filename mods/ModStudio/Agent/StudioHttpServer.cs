using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Engine;

namespace Game {
    public sealed class StudioHttpServer {
        public static StudioHttpServer Instance { get; } = new();

        HttpListener m_listener;
        Thread m_thread;
        volatile bool m_running;

        public bool IsRunning => m_running && m_listener is { IsListening: true };
        public string LastError { get; private set; }

        public void Start() {
            Stop();
            StudioSettings.Load();
            LastError = null;
            if (!StudioSettings.Current.AgentEnabled) {
                Log.Information("[ModStudio] Agent HTTP disabled.");
                return;
            }
            try {
                HttpListener listener = new();
                listener.Prefixes.Add(StudioSettings.Current.BaseUrl);
                listener.Start();
                m_listener = listener;
                m_running = true;
                m_thread = new Thread(Loop) {
                    IsBackground = true,
                    Name = "ModStudioHttp"
                };
                m_thread.Start();
                Log.Information($"[ModStudio] Agent API listening on {StudioSettings.Current.BaseUrl}");
            }
            catch (Exception e) {
                LastError = e.Message;
                m_running = false;
                Log.Error($"[ModStudio] Failed to start HTTP: {e}");
            }
        }

        public void Stop() {
            m_running = false;
            try {
                m_listener?.Stop();
                m_listener?.Close();
            }
            catch {
                // ignored
            }
            m_listener = null;
        }

        void Loop() {
            while (m_running && m_listener != null) {
                try {
                    HttpListenerContext context = m_listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => Handle(context));
                }
                catch (HttpListenerException) {
                    break;
                }
                catch (ObjectDisposedException) {
                    break;
                }
                catch (Exception e) {
                    if (m_running) {
                        Log.Error($"[ModStudio] HTTP accept: {e.Message}");
                    }
                }
            }
        }

        void Handle(HttpListenerContext context) {
            try {
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;
                response.ContentType = "application/json; charset=utf-8";
                string path = request.Url?.AbsolutePath ?? "/";
                if (!StudioHttpRouter.TryMatch(request.HttpMethod, path, out StudioRoute route)) {
                    Write(response, 404, StudioResult.Fail("not_found", "Unknown route.").ToJson());
                    return;
                }
                if (route.IsHealth) {
                    Write(response, 200, new JsonObject {
                        ["ok"] = true,
                        ["version"] = "1.0.0",
                        ["agent"] = IsRunning
                    });
                    return;
                }
                if (route.NeedsAuth && !IsAuthorized(request)) {
                    Write(response, 401, StudioResult.Fail("unauthorized", "Missing or invalid token.").ToJson());
                    return;
                }
                string body = ReadBody(request);
                JsonObject args = MergeArgs(request, body, route);
                if (route.ToolName == "tools.call") {
                    string toolName = StudioJson.GetString(args, "name");
                    JsonObject inner = args["arguments"] as JsonObject ?? [];
                    Write(response, 200, StudioToolService.Call(toolName, inner).ToJson());
                    return;
                }
                Write(response, 200, StudioToolService.Call(route.ToolName, args).ToJson());
            }
            catch (Exception e) {
                try {
                    Write(context.Response, 500, StudioResult.Fail("io_error", e.Message).ToJson());
                }
                catch {
                    // ignored
                }
            }
        }

        static bool IsAuthorized(HttpListenerRequest request) {
            string expected = StudioSettings.Current.Token;
            if (string.IsNullOrEmpty(expected)) {
                return false;
            }
            string header = request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
                return string.Equals(header.Substring(7).Trim(), expected, StringComparison.Ordinal);
            }
            string alt = request.Headers["X-Studio-Token"];
            return string.Equals(alt, expected, StringComparison.Ordinal);
        }

        static string ReadBody(HttpListenerRequest request) {
            if (!request.HasEntityBody) {
                return "";
            }
            using StreamReader reader = new(request.InputStream, request.ContentEncoding);
            return reader.ReadToEnd();
        }

        static JsonObject MergeArgs(HttpListenerRequest request, string body, StudioRoute route) {
            JsonObject args = string.IsNullOrWhiteSpace(body) ? [] : StudioJson.ParseObject(body);
            if (request.Url != null && !string.IsNullOrEmpty(request.Url.Query)) {
                string query = request.Url.Query.TrimStart('?');
                foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries)) {
                    int eq = pair.IndexOf('=');
                    string key = eq < 0 ? Decode(pair) : Decode(pair.Substring(0, eq));
                    string value = eq < 0 ? "true" : Decode(pair.Substring(eq + 1));
                    if (!string.IsNullOrEmpty(key) && !args.ContainsKey(key)) {
                        if (value == "true" || value == "false") {
                            args[key] = value == "true";
                        }
                        else {
                            args[key] = value;
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(route.PackageName) && !args.ContainsKey("packageName")) {
                args["packageName"] = route.PackageName;
            }
            return args;
        }

        static string Decode(string text) => Uri.UnescapeDataString((text ?? "").Replace('+', ' '));

        static void Write(HttpListenerResponse response, int status, JsonNode node) {
            byte[] bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
            response.StatusCode = status;
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Close();
        }
    }
}
