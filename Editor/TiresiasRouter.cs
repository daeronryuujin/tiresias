using UnityEditor;
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

            // CORS headers so browser-based tools can also hit this
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
                        TiresiasHandlers.Status(req, res);
                        break;

                    // ── Scene ─────────────────────────────────────────────────
                    case "/scene":
                        TiresiasHandlers.SceneInfo(req, res);
                        break;

                    case "/scene/hierarchy":
                        TiresiasHandlers.Hierarchy(req, res);
                        break;

                    case "/scene/object":
                        TiresiasHandlers.ObjectDetail(req, res);
                        break;

                    case "/scene/selected":
                        TiresiasHandlers.Selected(req, res);
                        break;

                    // ── Assets ────────────────────────────────────────────────
                    case "/assets/scripts":
                        TiresiasHandlers.ListScripts(req, res);
                        break;

                    case "/assets/prefabs":
                        TiresiasHandlers.ListPrefabs(req, res);
                        break;

                    // ── Compiler ──────────────────────────────────────────────
                    case "/compiler/status":
                        TiresiasHandlers.CompilerStatus(req, res);
                        break;

                    case "/compiler/errors":
                        TiresiasHandlers.CompilerErrors(req, res);
                        break;

                    // ── Console ───────────────────────────────────────────────
                    case "/console/errors":
                        TiresiasHandlers.ConsoleErrors(req, res);
                        break;

                    default:
                        if (!TryHandleParameterizedRoute(req, res, path))
                            ResponseHelper.Send(res, 404, $"{{\"error\":\"Unknown route: {path}\"}}");
                        break;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Tiresias] Handler exception: {ex}");
                ResponseHelper.Send(res, 500, $"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        /// <summary>
        /// Handles parameterized routes that can't be matched by exact switch cases.
        /// Returns true if the route was handled, false if it should 404.
        /// </summary>
        private static bool TryHandleParameterizedRoute(HttpListenerRequest req, HttpListenerResponse res, string path)
        {
            var method = req.HttpMethod;

            // POST /api/scene/objects
            if (method == "POST" && path == "/api/scene/objects")
            {
                TiresiasHandlers.CreateGameObject(req, res);
                return true;
            }

            if (!path.StartsWith("/api/"))
                return false;

            var segments = path.Split('/');
            // segments[0] = "", [1] = "api"

            // /api/scene/...
            if (segments.Length >= 3 && segments[1] == "api" && segments[2] == "scene" && segments.Length >= 4)
            {
                var name = Uri.UnescapeDataString(segments[3]);

                // /api/scene/{name}  (4 segments)
                if (segments.Length == 4)
                {
                    // DELETE /api/scene/{name}
                    if (method == "DELETE")
                    {
                        TiresiasHandlers.DeleteGameObject(req, res, name);
                        return true;
                    }
                }

                // /api/scene/{name}/{action}  (5 segments)
                if (segments.Length == 5)
                {
                    var action = segments[4];

                    // POST /api/scene/{name}/components
                    if (method == "POST" && action == "components")
                    {
                        TiresiasHandlers.AddComponent(req, res, name);
                        return true;
                    }

                    // PUT /api/scene/{name}/transform
                    if (method == "PUT" && action == "transform")
                    {
                        TiresiasHandlers.SetTransform(req, res, name);
                        return true;
                    }

                    // PUT /api/scene/{name}/active
                    if (method == "PUT" && action == "active")
                    {
                        TiresiasHandlers.SetActive(req, res, name);
                        return true;
                    }

                    // PUT /api/scene/{name}/parent
                    if (method == "PUT" && action == "parent")
                    {
                        TiresiasHandlers.SetParent(req, res, name);
                        return true;
                    }
                }

                // /api/scene/{name}/components/{type}  (6 segments)
                if (segments.Length == 6 && segments[4] == "components")
                {
                    var compType = Uri.UnescapeDataString(segments[5]);

                    // DELETE /api/scene/{name}/components/{type}
                    if (method == "DELETE")
                    {
                        TiresiasHandlers.RemoveComponent(req, res, name, compType);
                        return true;
                    }
                }

                // /api/scene/{name}/components/{type}/fields/{field}  (8 segments)
                if (segments.Length == 8
                    && segments[4] == "components"
                    && segments[6] == "fields")
                {
                    var compType  = Uri.UnescapeDataString(segments[5]);
                    var fieldName = Uri.UnescapeDataString(segments[7]);

                    // PUT /api/scene/{name}/components/{type}/fields/{field}
                    if (method == "PUT")
                    {
                        TiresiasHandlers.SetField(req, res, name, compType, fieldName);
                        return true;
                    }
                }
            }

            // /api/assets/prefabs/{path}  (4+ segments)
            if (segments.Length >= 5 && segments[1] == "api" && segments[2] == "assets" && segments[3] == "prefabs")
            {
                // Rejoin remaining segments to reconstruct the prefab path (may contain slashes)
                var prefabPath = Uri.UnescapeDataString(string.Join("/", segments, 4, segments.Length - 4));

                // POST /api/assets/prefabs/{path}
                if (method == "POST")
                {
                    TiresiasHandlers.InstantiatePrefab(req, res, prefabPath);
                    return true;
                }
            }

            return false;
        }
    }
}
