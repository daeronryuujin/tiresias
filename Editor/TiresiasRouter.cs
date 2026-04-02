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
                    case "/assets/import-status":
                        TiresiasHandlers.AssetImportStatus(req, res); break;

                    // ── Compiler ──────────────────────────────────────────────
                    case "/compiler/status":
                        TiresiasHandlers.CompilerStatus(req, res); break;
                    case "/compiler/errors":
                        TiresiasHandlers.CompilerErrors(req, res); break;

                    // ── Console ───────────────────────────────────────────────
                    case "/console/logs":
                        TiresiasHandlers.ConsoleLogs(req, res); break;

                    // ── Scenes ───────────────────────────────────────────────
                    case "/scenes":
                        TiresiasHandlers.ListScenes(req, res); break;

                    // ── Build ─────────────────────────────────────────────────
                    case "/build/stats":
                        TiresiasHandlers.BuildStats(req, res); break;
                    case "/build/validate":
                        TiresiasHandlers.BuildValidate(req, res); break;

                    // ── Meta ──────────────────────────────────────────────────
                    case "/meta/claude-md-snippet":
                        TiresiasHandlers.ClaudeMdSnippet(req, res); break;

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
            try
            {
                var json = ExecuteReadEndpoint(method, path, query);
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
        /// Uses the provided query params (from the batch sub-request path).
        /// Returns null for unknown/unsupported routes.
        /// </summary>
        private static string ExecuteReadEndpoint(string method, string path,
            System.Collections.Specialized.NameValueCollection query)
        {
            if (method != "GET") return null;

            switch (path)
            {
                case "/status":
                    return MainThreadDispatcher.Execute(() => Json.Object(new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["status"] = "ok", ["version"] = "1.11.0",
                        ["unityVersion"] = UnityEngine.Application.unityVersion,
                        ["isPlaying"] = UnityEditor.EditorApplication.isPlaying,
                        ["isCompiling"] = UnityEditor.EditorApplication.isCompiling,
                        ["port"] = TiresiasServer.BoundPort,
                    }));

                case "/compiler/errors":
                    return TiresiasHandlers.GetCompilerErrorsJson();

                case "/compiler/status":
                    return MainThreadDispatcher.Execute(() => Json.Object(new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["isCompiling"] = UnityEditor.EditorApplication.isCompiling,
                        ["isUpdating"] = UnityEditor.EditorApplication.isUpdating,
                    }));

                case "/scene":
                    return MainThreadDispatcher.Execute(() =>
                    {
                        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                        return Json.Object(new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["name"] = scene.name, ["path"] = scene.path,
                            ["isDirty"] = scene.isDirty, ["isLoaded"] = scene.isLoaded,
                            ["rootCount"] = scene.rootCount,
                        });
                    });

                case "/scene/hierarchy":
                    return MainThreadDispatcher.Execute(() =>
                    {
                        int maxDepth = 3;
                        var depthParam = query != null ? query["depth"] : null;
                        if (depthParam != null) int.TryParse(depthParam, out maxDepth);
                        maxDepth = UnityEngine.Mathf.Clamp(maxDepth, 1, 10);

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

                case "/scene/object":
                    return MainThreadDispatcher.Execute(() =>
                    {
                        var name = query != null ? query["name"] : null;
                        if (string.IsNullOrEmpty(name))
                            return Json.Object(new System.Collections.Generic.Dictionary<string, object>
                                { ["error"] = "Missing ?name= parameter" });
                        var go = UnityEngine.GameObject.Find(name);
                        if (go == null)
                            return Json.Object(new System.Collections.Generic.Dictionary<string, object>
                                { ["error"] = $"No GameObject named '{name}'" });
                        var comps = go.GetComponents<UnityEngine.Component>();
                        var compNames = new System.Collections.Generic.List<string>();
                        foreach (var c in comps)
                            if (c != null) compNames.Add(Json.Quote(c.GetType().Name));
                        return Json.Object(new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["name"] = go.name,
                            ["active"] = go.activeSelf,
                            ["components"] = new RawJson("[" + string.Join(",", compNames) + "]"),
                        });
                    });

                case "/scene/selected":
                    return MainThreadDispatcher.Execute(() =>
                    {
                        var selected = UnityEditor.Selection.gameObjects;
                        var names = new System.Collections.Generic.List<string>();
                        foreach (var go in selected) names.Add(Json.Quote(go.name));
                        return "{\"selected\":[" + string.Join(",", names) + "]}";
                    });

                case "/build/stats":
                    return TiresiasHandlers.GetBuildStatsJson();

                case "/build/validate":
                    return TiresiasHandlers.GetBuildValidateJson();

                case "/meta/claude-md-snippet":
                    return TiresiasHandlers.GetClaudeMdSnippetText();

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

            // Must be matched BEFORE the /api/assets/prefabs/{path} catch-all below
            if (method == "POST" && path == "/api/assets/prefabs/save")
            {
                TiresiasHandlers.SaveAsPrefab(req, res);
                return true;
            }

            if (method == "POST" && path == "/api/assets/instantiate")
            {
                TiresiasHandlers.InstantiateModel(req, res);
                return true;
            }

            if (method == "POST" && path == "/api/assets/materials")
            {
                TiresiasHandlers.CreateMaterial(req, res);
                return true;
            }

            if (method == "POST" && path == "/api/scene/save")
            {
                TiresiasHandlers.SaveScene(req, res);
                return true;
            }

            if (method == "POST" && path == "/api/scene/open")
            {
                TiresiasHandlers.OpenScene(req, res);
                return true;
            }

            if (method == "POST" && path == "/api/scene/objects")
            {
                TiresiasHandlers.CreateGameObject(req, res);
                return true;
            }

            if (method == "GET" && path == "/api/editor/screenshot")
            {
                TiresiasHandlers.Screenshot(req, res);
                return true;
            }

            if (method == "POST" && path == "/api/editor/play")
            {
                TiresiasHandlers.EditorPlay(req, res);
                return true;
            }

            if (method == "POST" && path == "/api/editor/stop")
            {
                TiresiasHandlers.EditorStop(req, res);
                return true;
            }

            if (method == "POST" && path == "/api/editor/undo")
            {
                TiresiasHandlers.EditorUndo(req, res);
                return true;
            }

            if (method == "POST" && path == "/api/editor/menu")
            {
                TiresiasHandlers.ExecuteMenu(req, res);
                return true;
            }

            if (method == "POST" && path == "/api/editor/redo")
            {
                TiresiasHandlers.EditorRedo(req, res);
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
                    if (method == "PUT" && action == "materials")
                    { TiresiasHandlers.AssignMaterials(req, res, name); return true; }
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

                // /api/scene/{name}/components/{type}/events/{event} PUT — add persistent listener
                if (segments.Length == 8 && segments[4] == "components" && segments[6] == "events" && method == "PUT")
                {
                    TiresiasHandlers.AddEventListener(req, res, name,
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

            // /api/editor/menu POST — execute a Unity menu item
            if (segments.Length == 4 && segments[1] == "api" && segments[2] == "editor" && segments[3] == "menu" && method == "POST")
            {
                TiresiasHandlers.ExecuteMenuItem(req, res);
                return true;
            }

            return false;
        }

        private static string EscJson(string s)
            => s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") ?? "";
    }
}
