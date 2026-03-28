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

        // ── PUT /api/scene/{name}/components/{type}/fields/{field} ──────────

        public static void SetFieldReference(HttpListenerRequest req, HttpListenerResponse res,
            string gameObjectName, string componentType, string fieldName)
        {
            // Phase 1: Parse and validate request body (worker thread — no Unity API calls)
            string body;
            try { body = Json.ReadBody(req); }
            catch
            {
                ResponseHelper.Send(res, 400, "{\"error\":\"Could not read request body\"}");
                return;
            }

            var fields = Json.ParseFlat(body);
            if (fields.Count == 0)
            {
                ResponseHelper.Send(res, 400, "{\"error\":\"Invalid or empty request body\"}");
                return;
            }

            fields.TryGetValue("referenceType", out var referenceType);
            fields.TryGetValue("targetGameObjectName", out var targetGoName);
            fields.TryGetValue("targetComponentType", out var targetCompType);

            if (referenceType != "gameObject" && referenceType != "component")
            {
                ResponseHelper.Send(res, 400, "{\"error\":\"referenceType must be 'gameObject' or 'component'\"}");
                return;
            }
            if (string.IsNullOrEmpty(targetGoName))
            {
                ResponseHelper.Send(res, 400, "{\"error\":\"targetGameObjectName is required\"}");
                return;
            }
            if (referenceType == "component" && string.IsNullOrEmpty(targetCompType))
            {
                ResponseHelper.Send(res, 400, "{\"error\":\"targetComponentType is required when referenceType is 'component'\"}");
                return;
            }

            // Phase 2: Execute on main thread (SerializedObject requires it)
            try
            {
                var result = MainThreadDispatcher.Execute(() =>
                    SetFieldReferenceOnMainThread(gameObjectName, componentType, fieldName,
                        referenceType, targetGoName, targetCompType));

                ResponseHelper.Send(res, result.StatusCode, result.Json);
            }
            catch (TimeoutException)
            {
                ResponseHelper.Send(res, 504, "{\"error\":\"Main thread operation timed out\"}");
            }
            catch (Exception ex)
            {
                ResponseHelper.Send(res, 500, Json.Object(new Dictionary<string, object>
                {
                    ["error"] = $"Internal error: {ex.Message}"
                }));
            }
        }

        private struct HandlerResult
        {
            public int StatusCode;
            public string Json;
            public static HandlerResult Error(int code, string message)
                => new HandlerResult { StatusCode = code, Json = Tiresias.Json.Object(new Dictionary<string, object> { ["error"] = message }) };
            public static HandlerResult Ok(string json)
                => new HandlerResult { StatusCode = 200, Json = json };
        }

        private static HandlerResult SetFieldReferenceOnMainThread(
            string gameObjectName, string componentType, string fieldName,
            string referenceType, string targetGoName, string targetCompType)
        {
            // Find source GameObject
            var sourceGo = GameObject.Find(gameObjectName);
            if (sourceGo == null)
                return HandlerResult.Error(404, $"No GameObject found at path '{gameObjectName}'");

            // Find component on source
            var component = FindComponentByTypeName(sourceGo, componentType);
            if (component == null)
            {
                var available = sourceGo.GetComponents<Component>()
                    .Where(c => c != null)
                    .Select(c => c.GetType().Name)
                    .ToArray();
                return HandlerResult.Error(404,
                    $"No component '{componentType}' on GameObject '{gameObjectName}'. Available: [{string.Join(", ", available)}]");
            }

            // Create SerializedObject and find the property
            var so = new SerializedObject(component);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                var availableFields = ListSerializedFields(so);
                return HandlerResult.Error(400,
                    $"No field '{fieldName}' on component '{componentType}'. Available: [{string.Join(", ", availableFields)}]");
            }

            if (prop.propertyType != SerializedPropertyType.ObjectReference)
                return HandlerResult.Error(400,
                    $"Field '{fieldName}' is type '{prop.propertyType}', not an object reference");

            // Find target GameObject
            var targetGo = GameObject.Find(targetGoName);
            if (targetGo == null)
                return HandlerResult.Error(404, $"Target GameObject '{targetGoName}' not found");

            // Resolve the reference value
            UnityEngine.Object targetObj;
            string assignedTypeName;

            if (referenceType == "gameObject")
            {
                targetObj = targetGo;
                assignedTypeName = "GameObject";
            }
            else
            {
                var targetComp = FindComponentByTypeName(targetGo, targetCompType);
                if (targetComp == null)
                {
                    var available = targetGo.GetComponents<Component>()
                        .Where(c => c != null)
                        .Select(c => c.GetType().Name)
                        .ToArray();
                    return HandlerResult.Error(404,
                        $"No component '{targetCompType}' on target GameObject '{targetGoName}'. Available: [{string.Join(", ", available)}]");
                }
                targetObj = targetComp;
                assignedTypeName = targetComp.GetType().Name;
            }

            // Set the reference
            prop.objectReferenceValue = targetObj;
            so.ApplyModifiedProperties();

            // Build success response
            var assignedValue = Json.Object(new Dictionary<string, object>
            {
                ["name"] = targetGo.name,
                ["type"] = assignedTypeName
            });

            return HandlerResult.Ok(Json.Object(new Dictionary<string, object>
            {
                ["success"] = true,
                ["gameObject"] = gameObjectName,
                ["component"] = componentType,
                ["field"] = fieldName,
                ["assignedValue"] = new RawJson(assignedValue)
            }));
        }

        private static Component FindComponentByTypeName(GameObject go, string typeName)
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name == typeName || c.GetType().FullName == typeName)
                    return c;
            }
            return null;
        }

        private static string[] ListSerializedFields(SerializedObject so)
        {
            var names = new List<string>();
            var iter = so.GetIterator();
            if (iter.NextVisible(true))
            {
                do
                {
                    names.Add(iter.name);
                } while (iter.NextVisible(false));
            }
            return names.ToArray();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string Vec3(Vector3 v)
            => $"{{\"x\":{v.x:F3},\"y\":{v.y:F3},\"z\":{v.z:F3}}}";
    }
}
