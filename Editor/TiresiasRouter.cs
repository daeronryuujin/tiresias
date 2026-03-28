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
            res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, OPTIONS");
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
            // PUT /api/scene/{gameObjectName}/components/{componentType}/fields/{fieldName}
            if (req.HttpMethod == "PUT" && path.StartsWith("/api/scene/"))
            {
                var segments = path.Split('/');
                // Expected: ["", "api", "scene", "{name}", "components", "{type}", "fields", "{field}"]
                if (segments.Length == 8
                    && segments[1] == "api" && segments[2] == "scene"
                    && segments[4] == "components" && segments[6] == "fields")
                {
                    var goName = Uri.UnescapeDataString(segments[3]);
                    var compType = Uri.UnescapeDataString(segments[5]);
                    var fieldName = Uri.UnescapeDataString(segments[7]);
                    TiresiasHandlers.SetFieldReference(req, res, goName, compType, fieldName);
                    return true;
                }
            }

            return false;
        }
    }
}
