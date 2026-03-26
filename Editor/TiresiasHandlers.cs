using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Tiresias
{
    public static class TiresiasHandlers
    {
        // ── /status ───────────────────────────────────────────────────────────

        public static void Status(HttpListenerRequest req, HttpListenerResponse res)
        {
            var json = Json.Object(new Dictionary<string, object>
            {
                ["status"]          = "ok",
                ["version"]         = "1.0.0",
                ["unityVersion"]    = Application.unityVersion,
                ["projectPath"]     = System.IO.Path.GetFileName(Application.dataPath.Replace("/Assets", "")),
                ["isPlaying"]       = EditorApplication.isPlaying,
                ["isCompiling"]     = EditorApplication.isCompiling,
            });
            ResponseHelper.Send(res, 200, json);
        }

        // ── /scene ────────────────────────────────────────────────────────────

        public static void SceneInfo(HttpListenerRequest req, HttpListenerResponse res)
        {
            var scene = SceneManager.GetActiveScene();
            var json = Json.Object(new Dictionary<string, object>
            {
                ["name"]            = scene.name,
                ["path"]            = scene.path,
                ["isDirty"]         = scene.isDirty,
                ["isLoaded"]        = scene.isLoaded,
                ["rootCount"]       = scene.rootCount,
            });
            ResponseHelper.Send(res, 200, json);
        }

        // ── /scene/hierarchy ─────────────────────────────────────────────────

        public static void Hierarchy(HttpListenerRequest req, HttpListenerResponse res)
        {
            // Optional query param: ?depth=N (default 3, max 10)
            int maxDepth = 3;
            var depthParam = req.QueryString["depth"];
            if (depthParam != null) int.TryParse(depthParam, out maxDepth);
            maxDepth = Mathf.Clamp(maxDepth, 1, 10);

            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();

            var rootNodes = roots.Select(go => SerializeGameObject(go, maxDepth, 0)).ToList();

            ResponseHelper.Send(res, 200, Json.Array(rootNodes));
        }

        private static string SerializeGameObject(GameObject go, int maxDepth, int currentDepth)
        {
            var components = go.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => Json.Quote(c.GetType().Name))
                .ToList();

            var fields = new Dictionary<string, object>
            {
                ["name"]          = go.name,
                ["active"]        = go.activeSelf,
                ["tag"]           = go.tag,
                ["layer"]         = LayerMask.LayerToName(go.layer),
                ["components"]    = "[" + string.Join(",", components) + "]",
                ["childCount"]    = go.transform.childCount,
            };

            if (currentDepth < maxDepth && go.transform.childCount > 0)
            {
                var children = new List<string>();
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    children.Add(SerializeGameObject(go.transform.GetChild(i).gameObject, maxDepth, currentDepth + 1));
                }
                fields["children"] = "[" + string.Join(",", children) + "]";
            }

            return Json.Object(fields);
        }

        // ── /scene/object ─────────────────────────────────────────────────────

        public static void ObjectDetail(HttpListenerRequest req, HttpListenerResponse res)
        {
            var name = req.QueryString["name"];
            if (string.IsNullOrEmpty(name))
            {
                ResponseHelper.Send(res, 400, "{\"error\":\"Missing ?name= parameter\"}");
                return;
            }

            var go = GameObject.Find(name);
            if (go == null)
            {
                ResponseHelper.Send(res, 404, $"{{\"error\":\"No GameObject named '{name}'\"}}");
                return;
            }

            var t = go.transform;
            var components = go.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c =>
                {
                    var fields = new Dictionary<string, object>
                    {
                        ["type"]    = c.GetType().Name,
                        ["enabled"] = (c is Behaviour b) ? (object)b.enabled : (object)true,
                    };
                    return Json.Object(fields);
                });

            var detail = new Dictionary<string, object>
            {
                ["name"]       = go.name,
                ["active"]     = go.activeSelf,
                ["tag"]        = go.tag,
                ["layer"]      = LayerMask.LayerToName(go.layer),
                ["position"]   = Vec3(t.position),
                ["rotation"]   = Vec3(t.eulerAngles),
                ["scale"]      = Vec3(t.localScale),
                ["parent"]     = t.parent != null ? t.parent.name : null,
                ["components"] = "[" + string.Join(",", components) + "]",
            };

            ResponseHelper.Send(res, 200, Json.Object(detail));
        }

        // ── /scene/selected ───────────────────────────────────────────────────

        public static void Selected(HttpListenerRequest req, HttpListenerResponse res)
        {
            var selected = Selection.gameObjects;
            if (selected.Length == 0)
            {
                ResponseHelper.Send(res, 200, "{\"selected\":[]}");
                return;
            }

            var names = selected.Select(go => Json.Quote(go.name)).ToList();
            var json = "{\"selected\":[" + string.Join(",", names) + "]}";
            ResponseHelper.Send(res, 200, json);
        }

        // ── /assets/scripts ───────────────────────────────────────────────────

        public static void ListScripts(HttpListenerRequest req, HttpListenerResponse res)
        {
            var guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
            var paths = guids
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Where(p => p.EndsWith(".cs"))
                .Select(p => Json.Quote(p))
                .ToList();

            ResponseHelper.Send(res, 200, "[" + string.Join(",", paths) + "]");
        }

        // ── /assets/prefabs ───────────────────────────────────────────────────

        public static void ListPrefabs(HttpListenerRequest req, HttpListenerResponse res)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            var paths = guids
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Select(p => Json.Quote(p))
                .ToList();

            ResponseHelper.Send(res, 200, "[" + string.Join(",", paths) + "]");
        }

        // ── /compiler/status ─────────────────────────────────────────────────

        public static void CompilerStatus(HttpListenerRequest req, HttpListenerResponse res)
        {
            var json = Json.Object(new Dictionary<string, object>
            {
                ["isCompiling"] = EditorApplication.isCompiling,
                ["isUpdating"]  = EditorApplication.isUpdating,
            });
            ResponseHelper.Send(res, 200, json);
        }

        // ── /compiler/errors ─────────────────────────────────────────────────

        private static readonly List<Dictionary<string, object>> _compilerErrors = new List<Dictionary<string, object>>();
        private static bool _hooked;

        [InitializeOnLoadMethod]
        private static void HookCompilationEvents()
        {
            if (_hooked) return;
            _hooked = true;
            CompilationPipeline.compilationStarted += _ => { _compilerErrors.Clear(); };
            CompilationPipeline.assemblyCompilationFinished += (path, messages) =>
            {
                foreach (var m in messages)
                {
                    if (m.type != CompilerMessageType.Error) continue;
                    _compilerErrors.Add(new Dictionary<string, object>
                    {
                        ["file"]    = m.file,
                        ["line"]    = m.line,
                        ["message"] = m.message,
                    });
                }
            };
        }

        public static void CompilerErrors(HttpListenerRequest req, HttpListenerResponse res)
        {
            var entries = _compilerErrors
                .Select(e => Json.Object(e))
                .ToList();
            ResponseHelper.Send(res, 200, "[" + string.Join(",", entries) + "]");
        }

        // ── /console/errors ───────────────────────────────────────────────────

        public static void ConsoleErrors(HttpListenerRequest req, HttpListenerResponse res)
        {
            // Note: Unity doesn't expose a public log API for reading past entries.
            // We return a polite note and rely on the compiler errors endpoint for real data.
            ResponseHelper.Send(res, 200,
                "{\"note\":\"Unity has no public API for reading past console logs. Use /compiler/errors for compilation issues.\",\"entries\":[]}");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string Vec3(Vector3 v)
            => $"{{\"x\":{v.x:F3},\"y\":{v.y:F3},\"z\":{v.z:F3}}}";
    }
}
