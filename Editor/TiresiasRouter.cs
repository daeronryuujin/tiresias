using System;
using System.Net;

namespace Tiresias
{
    public static class TiresiasRouter
    {
        public static void Handle(HttpListenerContext context)
        {
            var req = context.Request;
            var res = context.Response;

            // CORS headers
            res.Headers.Add("Access-Control-Allow-Origin", "*");
            res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            try
            {
                var path = req.Url.AbsolutePath.TrimEnd('/');

                switch (path)
                {
                    // ── Status ────────────────────────────────────────────────
                    case "/status":
                        TiresiasHandlers.Status(req, res); break;

                    // ── Scene ─────────────────────────────────────────────────
                    case "/scene":
                        TiresiasHandlers.SceneInfo(req, res); break;
                    case "/scene/hierarchy":
                        TiresiasHandlers.Hierarchy(req, res); break;
                    case "/scene/object":
                        TiresiasHandlers.ObjectDetail(req, res); break;
                    case "/scene/selected":
                        TiresiasHandlers.Selected(req, res); break;

                    // ── Assets ────────────────────────────────────────────────
                    case "/assets/scripts":
                        TiresiasHandlers.ListScripts(req, res); break;
                    case "/assets/prefabs":
                        TiresiasHandlers.ListPrefabs(req, res); break;
                    case "/assets/search":
                        TiresiasHandlers.AssetSearch(req, res); break;
                    case "/assets/dependencies":
                        TiresiasHandlers.AssetDependencies(req, res); break;

                    // ── Compiler ──────────────────────────────────────────────
                    case "/compiler/status":
                        TiresiasHandlers.CompilerStatus(req, res); break;
                    case "/compiler/errors":
                        TiresiasHandlers.CompilerErrors(req, res); break;

                    // ── Console ───────────────────────────────────────────────
                    case "/console/errors":
                        TiresiasHandlers.ConsoleErrors(req, res); break;

                    // ── Build ─────────────────────────────────────────────────
                    case "/build/stats":
                        TiresiasHandlers.BuildStats(req, res); break;

                    // ── Batch ─────────────────────────────────────────────────
                    case "/batch":
                        if (req.HttpMethod == "POST")
                            TiresiasHandlers.Batch(req, res);
                        else
                            ResponseHelper.Send(res, 405, "{\"error\":\"POST only\"}");
                        break;

                    default:
                        if (!TryHandleParameterizedRoute(req, res, path))
                            ResponseHelper.Send(res, 404, $"{{\"error\":\"Unknown route: {EscJson(path)}\"}}");
                        break;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Tiresias] Handler exception: {ex}");
                try { ResponseHelper.Send(res, 500, $"{{\"error\":\"{EscJson(ex.Message)}\"}}"); } catch { }
            }
        }

        /// <summary>
        /// Route a request internally (for /batch sub-requests).
        /// Captures the response into a ResponseCapture instead of writing to a real HTTP response.
        /// Only supports GET read endpoints.
        /// </summary>
        public static void HandleDirect(string method, string path,
            System.Collections.Specialized.NameValueCollection query,
            HttpListenerRequest realReq, ResponseCapture capture)
        {
            // For batch, we create a shim response that captures output
            // We reuse the real request object — batch only supports read endpoints
            // that use req.QueryString (which comes from the real request for now)
            // TODO: inject query params from batch path
            try
            {
                // For read endpoints, we can call the handler and capture via a wrapper
                // Since handlers write to HttpListenerResponse, we use a simple approach:
                // execute the handler logic directly and capture the result
                var json = ExecuteReadEndpoint(method, path, realReq);
                if (json != null)
                {
                    capture.Send(200, json);
                }
                else
                {
                    capture.Send(404, $"{{\"error\":\"Unknown or unsupported batch route: {EscJson(path)}\"}}");
                }
            }
            catch (Exception ex)
            {
                capture.Send(500, $"{{\"error\":\"{EscJson(ex.Message)}\"}}");
            }
        }

        /// <summary>
        /// Execute a read endpoint and return its JSON result directly.
        /// Returns null for unknown/unsupported routes.
        /// </summary>
        private static string ExecuteReadEndpoint(string method, string path, HttpListenerRequest req)
        {
            if (method != "GET") return null;

            switch (path)
            {
                case "/status":
                    return MainThreadDispatcher.Execute(() => Json.Object(new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["status"] = "ok", ["version"] = "1.7.0",
                        ["unityVersion"] = UnityEngine.Application.unityVersion,
                        ["isPlaying"] = UnityEditor.EditorApplication.isPlaying,
                        ["isCompiling"] = UnityEditor.EditorApplication.isCompiling,
                        ["port"] = TiresiasServer.BoundPort,
                    }));

                case "/compiler/errors":
                    // Access via handler's public method — it returns JSON directly
                    return TiresiasHandlers.GetCompilerErrorsJson();

                case "/compiler/status":
                    return MainThreadDispatcher.Execute(() => Json.Object(new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["isCompiling"] = UnityEditor.EditorApplication.isCompiling,
                        ["isUpdating"] = UnityEditor.EditorApplication.isUpdating,
                    }));

                case "/scene/hierarchy":
                    // Simplified — uses default depth 3
                    return MainThreadDispatcher.Execute(() =>
                    {
                        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                        var roots = scene.GetRootGameObjects();
                        var nodes = new System.Collections.Generic.List<string>();
                        foreach (var go in roots)
                            nodes.Add(Json.Object(new System.Collections.Generic.Dictionary<string, object>
                            {
                                ["name"] = go.name, ["childCount"] = go.transform.childCount,
                            }));
                        return "[" + string.Join(",", nodes) + "]";
                    });

                default:
                    return null;
            }
        }

        private static bool TryHandleParameterizedRoute(HttpListenerRequest req, HttpListenerResponse res, string path)
        {
            var method = req.HttpMethod;

            if (method == "POST" && path == "/api/assets/refresh")
            {
                TiresiasHandlers.AssetRefresh(req, res);
                return true;
            }

            if (method == "POST" && path == "/api/scene/objects")
            {
                TiresiasHandlers.CreateGameObject(req, res);
                return true;
            }

            if (!path.StartsWith("/api/"))
                return false;

            var segments = path.Split('/');

            // /api/scene/...
            if (segments.Length >= 4 && segments[1] == "api" && segments[2] == "scene")
            {
                var name = Uri.UnescapeDataString(segments[3]);

                if (segments.Length == 4 && method == "DELETE")
                {
                    TiresiasHandlers.DeleteGameObject(req, res, name);
                    return true;
                }

                if (segments.Length == 5)
                {
                    var action = segments[4];
                    if (method == "POST" && action == "components")
                    { TiresiasHandlers.AddComponent(req, res, name); return true; }
                    if (method == "PUT" && action == "transform")
                    { TiresiasHandlers.SetTransform(req, res, name); return true; }
                    if (method == "PUT" && action == "active")
                    { TiresiasHandlers.SetActive(req, res, name); return true; }
                    if (method == "PUT" && action == "parent")
                    { TiresiasHandlers.SetParent(req, res, name); return true; }
                }

                if (segments.Length == 6 && segments[4] == "components" && method == "DELETE")
                {
                    TiresiasHandlers.RemoveComponent(req, res, name, Uri.UnescapeDataString(segments[5]));
                    return true;
                }

                if (segments.Length == 8 && segments[4] == "components" && segments[6] == "fields" && method == "PUT")
                {
                    TiresiasHandlers.SetField(req, res, name,
                        Uri.UnescapeDataString(segments[5]), Uri.UnescapeDataString(segments[7]));
                    return true;
                }
            }

            // /api/assets/prefabs/{path}
            if (segments.Length >= 5 && segments[1] == "api" && segments[2] == "assets" && segments[3] == "prefabs" && method == "POST")
            {
                TiresiasHandlers.InstantiatePrefab(req, res,
                    Uri.UnescapeDataString(string.Join("/", segments, 4, segments.Length - 4)));
                return true;
            }

            return false;
        }

        private static string EscJson(string s)
            => s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") ?? "";
    }
}
