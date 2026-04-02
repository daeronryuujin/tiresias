using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Tiresias
{
    public static class TiresiasHandlers
    {
        // ── POST /api/editor/menu ────────────────────────────────────────────
        // Execute a Unity Editor menu item by path.
        // Body: {"menuItem":"Tools/Wire YouTube Search UI"}

        public static void ExecuteMenuItem(HttpListenerRequest req, HttpListenerResponse res)
        {
            var f = ParseBody(req, res); if (f == null) return;
            f.TryGetValue("menuItem", out var menuItem);
            if (string.IsNullOrEmpty(menuItem))
            { ResponseHelper.Send(res, 400, "{\"error\":\"Missing 'menuItem' in body\"}"); return; }

            var json = MainThreadDispatcher.Execute(() =>
            {
                bool success = EditorApplication.ExecuteMenuItem(menuItem);
                return Json.Object(new Dictionary<string, object>
                {
                    ["success"] = success,
                    ["menuItem"] = menuItem,
                });
            });
            ResponseHelper.Send(res, 200, json);
        }

        // ── /api/assets/refresh ───────────────────────────────────────────────

        public static void AssetRefresh(HttpListenerRequest req, HttpListenerResponse res)
        {
            MainThreadDispatcher.Execute(() => { AssetDatabase.Refresh(); return 0; });
            ResponseHelper.Send(res, 200, "{\"status\":\"ok\"}");
        }

        // ── POST /api/scene/save ────────────────────────────────────────────

        public static void SaveScene(HttpListenerRequest req, HttpListenerResponse res)
        {
            var json = MainThreadDispatcher.Execute(() =>
            {
                var scene = SceneManager.GetActiveScene();
                bool saved = EditorSceneManager.SaveScene(scene);
                return Json.Object(new Dictionary<string, object>
                {
                    ["saved"] = saved,
                    ["scene"] = scene.name,
                    ["path"]  = scene.path,
                });
            });
            ResponseHelper.Send(res, 200, json);
        }

        // ── POST /api/scene/open ────────────────────────────────────────────

        public static void OpenScene(HttpListenerRequest req, HttpListenerResponse res)
        {
            var f = ParseBody(req, res); if (f == null) return;
            f.TryGetValue("path", out var scenePath);
            f.TryGetValue("save", out var saveStr);
            bool save = saveStr == null || saveStr.ToLower() != "false";

            if (string.IsNullOrEmpty(scenePath))
            { ResponseHelper.Send(res, 400, "{\"error\":\"'path' is required\"}"); return; }

            Dispatch(res, () =>
            {
                if (!System.IO.File.Exists(scenePath))
                    return HR.Error(404, $"Scene file not found: {scenePath}");

                if (save) EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                return HR.Ok(Json.Object(new Dictionary<string, object>
                {
                    ["opened"]    = scene.name,
                    ["path"]      = scene.path,
                    ["rootCount"] = scene.rootCount,
                }));
            });
        }

        // ── GET /scenes ─────────────────────────────────────────────────────

        public static void ListScenes(HttpListenerRequest req, HttpListenerResponse res)
        {
            var json = MainThreadDispatcher.Execute(() =>
            {
                var guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
                var scenes = new List<string>();
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    scenes.Add(Json.Object(new Dictionary<string, object>
                    {
                        ["path"] = path,
                        ["name"] = System.IO.Path.GetFileNameWithoutExtension(path),
                    }));
                }
                return Json.Object(new Dictionary<string, object>
                {
                    ["count"]  = scenes.Count,
                    ["scenes"] = new RawJson("[" + string.Join(",", scenes) + "]"),
                });
            });
            ResponseHelper.Send(res, 200, json);
        }

        // ── GET /api/editor/screenshot ───────────────────────────────────────

        public static void Screenshot(HttpListenerRequest req, HttpListenerResponse res)
        {
            var widthStr  = req.QueryString["width"];
            var heightStr = req.QueryString["height"];
            var source    = req.QueryString["source"] ?? "scene"; // "scene" or "game"

            int width  = 960;
            int height = 540;
            if (widthStr != null)  int.TryParse(widthStr, out width);
            if (heightStr != null) int.TryParse(heightStr, out height);
            width  = Mathf.Clamp(width, 64, 3840);
            height = Mathf.Clamp(height, 64, 2160);

            try
            {
                var png = MainThreadDispatcher.Execute(() =>
                {
                    Camera cam = null;

                    if (source == "game")
                    {
                        cam = Camera.main;
                        if (cam == null)
                        {
                            // Find any enabled camera
                            var cams = UnityEngine.Object.FindObjectsOfType<Camera>();
                            if (cams.Length > 0) cam = cams[0];
                        }
                    }
                    else
                    {
                        // Scene view camera
                        var sceneView = SceneView.lastActiveSceneView;
                        if (sceneView != null) cam = sceneView.camera;
                    }

                    if (cam == null) return null;

                    var rt = new RenderTexture(width, height, 24);
                    var prev = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = prev;

                    RenderTexture.active = rt;
                    var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;

                    var bytes = tex.EncodeToPNG();

                    UnityEngine.Object.DestroyImmediate(tex);
                    UnityEngine.Object.DestroyImmediate(rt);

                    return bytes;
                }, 15000); // longer timeout for render

                if (png == null)
                {
                    ResponseHelper.Send(res, 404, "{\"error\":\"No camera available for source '" + source + "'\"}");
                    return;
                }

                ResponseHelper.SendPng(res, png);
            }
            catch (TimeoutException)
            {
                ResponseHelper.Send(res, 504, "{\"error\":\"Screenshot render timed out\"}");
            }
            catch (Exception ex)
            {
                ResponseHelper.Send(res, 500, Json.Object(new Dictionary<string, object> { ["error"] = $"Screenshot failed: {ex.Message}" }));
            }
        }

        // ── POST /api/editor/play ───────────────────────────────────────────

        public static void EditorPlay(HttpListenerRequest req, HttpListenerResponse res)
        {
            MainThreadDispatcher.Execute(() => { EditorApplication.isPlaying = true; return 0; });
            ResponseHelper.Send(res, 200, "{\"status\":\"play_requested\"}");
        }

        // ── POST /api/editor/stop ───────────────────────────────────────────

        public static void EditorStop(HttpListenerRequest req, HttpListenerResponse res)
        {
            MainThreadDispatcher.Execute(() => { EditorApplication.isPlaying = false; return 0; });
            ResponseHelper.Send(res, 200, "{\"status\":\"stop_requested\"}");
        }

        // ── POST /api/editor/menu ────────────────────────────────────────────

        public static void ExecuteMenu(HttpListenerRequest req, HttpListenerResponse res)
        {
            var f = ParseBody(req, res); if (f == null) return;
            f.TryGetValue("item", out var menuItem);
            if (string.IsNullOrEmpty(menuItem))
            { ResponseHelper.Send(res, 400, "{\"error\":\"'item' is required\"}"); return; }

            var result = MainThreadDispatcher.Execute(() => EditorApplication.ExecuteMenuItem(menuItem));

            ResponseHelper.Send(res, result ? 200 : 404,
                result ? Json.Object(new Dictionary<string, object> { ["executed"] = menuItem })
                       : Json.Object(new Dictionary<string, object> { ["error"] = $"Menu item '{menuItem}' not found" }));
        }

        // ── POST /api/editor/undo ───────────────────────────────────────────

        public static void EditorUndo(HttpListenerRequest req, HttpListenerResponse res)
        {
            MainThreadDispatcher.Execute(() => { Undo.PerformUndo(); return 0; });
            ResponseHelper.Send(res, 200, "{\"status\":\"ok\"}");
        }

        // ── POST /api/editor/redo ───────────────────────────────────────────

        public static void EditorRedo(HttpListenerRequest req, HttpListenerResponse res)
        {
            MainThreadDispatcher.Execute(() => { Undo.PerformRedo(); return 0; });
            ResponseHelper.Send(res, 200, "{\"status\":\"ok\"}");
        }

        // ── /status ───────────────────────────────────────────────────────────

        public static void Status(HttpListenerRequest req, HttpListenerResponse res)
        {
            var json = MainThreadDispatcher.Execute(() => Json.Object(new Dictionary<string, object>
            {
                ["status"]       = "ok",
                ["version"]      = "1.11.0",
                ["unityVersion"] = Application.unityVersion,
                ["projectPath"]  = System.IO.Path.GetFileName(Application.dataPath.Replace("/Assets", "")),
                ["isPlaying"]    = EditorApplication.isPlaying,
                ["isCompiling"]  = EditorApplication.isCompiling,
                ["port"]         = TiresiasServer.BoundPort,
            }));
            ResponseHelper.Send(res, 200, json);
        }

        // ── /scene ────────────────────────────────────────────────────────────

        public static void SceneInfo(HttpListenerRequest req, HttpListenerResponse res)
        {
            var json = MainThreadDispatcher.Execute(() =>
            {
                var scene = SceneManager.GetActiveScene();
                return Json.Object(new Dictionary<string, object>
                {
                    ["name"]      = scene.name,
                    ["path"]      = scene.path,
                    ["isDirty"]   = scene.isDirty,
                    ["isLoaded"]  = scene.isLoaded,
                    ["rootCount"] = scene.rootCount,
                });
            });
            ResponseHelper.Send(res, 200, json);
        }

        // ── /scene/hierarchy ─────────────────────────────────────────────────

        public static void Hierarchy(HttpListenerRequest req, HttpListenerResponse res)
        {
            int maxDepth = 3;
            var depthParam = req.QueryString["depth"];
            if (depthParam != null) int.TryParse(depthParam, out maxDepth);
            maxDepth = Mathf.Clamp(maxDepth, 1, 10);

            var json = MainThreadDispatcher.Execute(() =>
            {
                var scene = SceneManager.GetActiveScene();
                var roots = scene.GetRootGameObjects();
                var rootNodes = roots.Select(go => SerializeGameObject(go, maxDepth, 0)).ToList();
                return Json.Array(rootNodes);
            });
            ResponseHelper.Send(res, 200, json);
        }

        private static string SerializeGameObject(GameObject go, int maxDepth, int currentDepth)
        {
            var components = go.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => Json.Quote(c.GetType().Name))
                .ToList();

            var fields = new Dictionary<string, object>
            {
                ["name"]       = go.name,
                ["active"]     = go.activeSelf,
                ["tag"]        = go.tag,
                ["layer"]      = LayerMask.LayerToName(go.layer),
                ["components"] = new RawJson("[" + string.Join(",", components) + "]"),
                ["childCount"] = go.transform.childCount,
            };

            if (currentDepth < maxDepth && go.transform.childCount > 0)
            {
                var children = new List<string>();
                for (int i = 0; i < go.transform.childCount; i++)
                    children.Add(SerializeGameObject(go.transform.GetChild(i).gameObject, maxDepth, currentDepth + 1));
                fields["children"] = new RawJson("[" + string.Join(",", children) + "]");
            }

            return Json.Object(fields);
        }

        // ── /scene/object ─────────────────────────────────────────────────────

        public static void ObjectDetail(HttpListenerRequest req, HttpListenerResponse res)
        {
            var name = req.QueryString["name"];
            if (string.IsNullOrEmpty(name)) { ResponseHelper.Send(res, 400, "{\"error\":\"Missing ?name= parameter\"}"); return; }
            bool detail = req.QueryString["detail"] == "full";

            var (code, json) = MainThreadDispatcher.Execute(() =>
            {
                var go = GameObject.Find(name);
                if (go == null) return (404, $"{{\"error\":\"No GameObject named '{EscJson(name)}'\"}}");

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

                        if (detail)
                        {
                            try
                            {
                                var so = new SerializedObject(c);
                                var propList = new List<string>();
                                var iter = so.GetIterator();
                                if (iter.NextVisible(true))
                                {
                                    do { propList.Add(SerializeProperty(iter)); }
                                    while (iter.NextVisible(false));
                                }
                                fields["fields"] = new RawJson("[" + string.Join(",", propList) + "]");
                            }
                            catch { /* skip fields if serialization fails */ }
                        }

                        return Json.Object(fields);
                    });

                return (200, Json.Object(new Dictionary<string, object>
                {
                    ["name"]       = go.name,
                    ["active"]     = go.activeSelf,
                    ["tag"]        = go.tag,
                    ["layer"]      = LayerMask.LayerToName(go.layer),
                    ["position"]   = new RawJson(Vec3(t.localPosition)),
                    ["rotation"]   = new RawJson(Vec3(t.localEulerAngles)),
                    ["scale"]      = new RawJson(Vec3(t.localScale)),
                    ["parent"]     = t.parent != null ? t.parent.name : null,
                    ["components"] = new RawJson("[" + string.Join(",", components) + "]"),
                }));
            });
            ResponseHelper.Send(res, code, json);
        }

        private static string SerializeProperty(SerializedProperty prop)
        {
            var fields = new Dictionary<string, object>
            {
                ["name"] = prop.name,
                ["type"] = prop.propertyType.ToString(),
            };

            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        fields["value"] = prop.intValue; break;
                    case SerializedPropertyType.Boolean:
                        fields["value"] = prop.boolValue; break;
                    case SerializedPropertyType.Float:
                        fields["value"] = prop.floatValue; break;
                    case SerializedPropertyType.String:
                        fields["value"] = prop.stringValue ?? ""; break;
                    case SerializedPropertyType.Enum:
                        var names = prop.enumDisplayNames;
                        var idx = prop.enumValueIndex;
                        fields["value"] = (idx >= 0 && idx < names.Length) ? names[idx] : idx.ToString();
                        fields["index"] = idx;
                        break;
                    case SerializedPropertyType.ObjectReference:
                        var obj = prop.objectReferenceValue;
                        if (obj != null)
                        {
                            fields["value"] = new RawJson(Json.Object(new Dictionary<string, object>
                            {
                                ["name"] = obj.name,
                                ["type"] = obj.GetType().Name,
                            }));
                        }
                        else
                        {
                            fields["value"] = null;
                        }
                        break;
                    case SerializedPropertyType.Vector3:
                        fields["value"] = new RawJson(Vec3(prop.vector3Value)); break;
                    case SerializedPropertyType.Vector2:
                        var v2 = prop.vector2Value;
                        fields["value"] = new RawJson(
                            "{\"x\":" + v2.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                            ",\"y\":" + v2.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "}");
                        break;
                    case SerializedPropertyType.Color:
                        var col = prop.colorValue;
                        fields["value"] = new RawJson(
                            "{\"r\":" + col.r.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                            ",\"g\":" + col.g.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                            ",\"b\":" + col.b.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                            ",\"a\":" + col.a.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "}");
                        break;
                    case SerializedPropertyType.Rect:
                        var r = prop.rectValue;
                        fields["value"] = new RawJson(
                            "{\"x\":" + r.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                            ",\"y\":" + r.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                            ",\"w\":" + r.width.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                            ",\"h\":" + r.height.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "}");
                        break;
                    case SerializedPropertyType.LayerMask:
                        fields["value"] = prop.intValue; break;
                    case SerializedPropertyType.ArraySize:
                        fields["value"] = prop.intValue; break;
                    default:
                        fields["value"] = $"<{prop.propertyType}>";
                        break;
                }
            }
            catch
            {
                fields["value"] = "<error reading value>";
            }

            return Json.Object(fields);
        }

        // ── /scene/selected ───────────────────────────────────────────────────

        public static void Selected(HttpListenerRequest req, HttpListenerResponse res)
        {
            var json = MainThreadDispatcher.Execute(() =>
            {
                var selected = Selection.gameObjects;
                if (selected.Length == 0) return "{\"selected\":[]}";
                return "{\"selected\":[" + string.Join(",", selected.Select(go => Json.Quote(go.name))) + "]}";
            });
            ResponseHelper.Send(res, 200, json);
        }

        // ── /assets/scripts ───────────────────────────────────────────────────

        public static void ListScripts(HttpListenerRequest req, HttpListenerResponse res)
        {
            var json = MainThreadDispatcher.Execute(() =>
            {
                var paths = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" })
                    .Select(g => AssetDatabase.GUIDToAssetPath(g)).Where(p => p.EndsWith(".cs")).Select(p => Json.Quote(p)).ToList();
                return "[" + string.Join(",", paths) + "]";
            });
            ResponseHelper.Send(res, 200, json);
        }

        // ── /assets/prefabs ───────────────────────────────────────────────────

        public static void ListPrefabs(HttpListenerRequest req, HttpListenerResponse res)
        {
            var json = MainThreadDispatcher.Execute(() =>
            {
                var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                    .Select(g => AssetDatabase.GUIDToAssetPath(g)).Select(p => Json.Quote(p)).ToList();
                return "[" + string.Join(",", paths) + "]";
            });
            ResponseHelper.Send(res, 200, json);
        }

        // ── /assets/search ───────────────────────────────────────────────────
        // GET /assets/search?query=AudioLink&type=Material&folder=Assets

        public static void AssetSearch(HttpListenerRequest req, HttpListenerResponse res)
        {
            var query  = req.QueryString["query"] ?? "";
            var type   = req.QueryString["type"];
            var folder = req.QueryString["folder"] ?? "Assets";

            if (string.IsNullOrEmpty(query) && string.IsNullOrEmpty(type))
            {
                ResponseHelper.Send(res, 400, "{\"error\":\"Provide ?query= and/or ?type= parameter\"}");
                return;
            }

            var json = MainThreadDispatcher.Execute(() =>
            {
                var filter = query;
                if (!string.IsNullOrEmpty(type)) filter += $" t:{type}";
                filter = filter.Trim();

                var guids = AssetDatabase.FindAssets(filter, new[] { folder });
                var results = new List<string>();
                int limit = 100;

                foreach (var guid in guids)
                {
                    if (results.Count >= limit) break;
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
                    results.Add(Json.Object(new Dictionary<string, object>
                    {
                        ["path"] = path,
                        ["type"] = assetType?.Name ?? "Unknown",
                        ["guid"] = guid,
                    }));
                }

                return Json.Object(new Dictionary<string, object>
                {
                    ["query"]   = filter,
                    ["count"]   = results.Count,
                    ["results"] = new RawJson("[" + string.Join(",", results) + "]"),
                });
            });
            ResponseHelper.Send(res, 200, json);
        }

        // ── /assets/dependencies ──────────────────────────────────────────────
        // GET /assets/dependencies?path=Assets/Prefabs/MyPrefab.prefab

        public static void AssetDependencies(HttpListenerRequest req, HttpListenerResponse res)
        {
            var assetPath = req.QueryString["path"];
            if (string.IsNullOrEmpty(assetPath))
            {
                ResponseHelper.Send(res, 400, "{\"error\":\"Missing ?path= parameter\"}");
                return;
            }

            var json = MainThreadDispatcher.Execute(() =>
            {
                var deps = AssetDatabase.GetDependencies(assetPath, true);
                var results = deps
                    .Where(d => d != assetPath) // exclude self
                    .Select(d =>
                    {
                        var t = AssetDatabase.GetMainAssetTypeAtPath(d);
                        return Json.Object(new Dictionary<string, object>
                        {
                            ["path"] = d,
                            ["type"] = t?.Name ?? "Unknown",
                        });
                    })
                    .ToList();

                return Json.Object(new Dictionary<string, object>
                {
                    ["asset"]        = assetPath,
                    ["count"]        = results.Count,
                    ["dependencies"] = new RawJson("[" + string.Join(",", results) + "]"),
                });
            });
            ResponseHelper.Send(res, 200, json);
        }

        // ── /compiler/status ─────────────────────────────────────────────────

        private static string _lastCompileTime = "";
        private static int _lastWarningCount = 0;

        public static void CompilerStatus(HttpListenerRequest req, HttpListenerResponse res)
        {
            var json = MainThreadDispatcher.Execute(() =>
            {
                int errorCount, warningCount;
                lock (_compilerLock) { errorCount = _compilerErrors.Count; warningCount = _lastWarningCount; }
                return Json.Object(new Dictionary<string, object>
                {
                    ["isCompiling"]    = EditorApplication.isCompiling,
                    ["isUpdating"]     = EditorApplication.isUpdating,
                    ["lastCompileAt"]  = _lastCompileTime,
                    ["errorCount"]     = errorCount,
                    ["warningCount"]   = warningCount,
                });
            });
            ResponseHelper.Send(res, 200, json);
        }

        // ── /compiler/errors ─────────────────────────────────────────────────

        private static readonly List<Dictionary<string, object>> _compilerErrors = new List<Dictionary<string, object>>();
        private static readonly object _compilerLock = new object();
        private static bool _hooked;

        [InitializeOnLoadMethod]
        private static void HookCompilationEvents()
        {
            if (_hooked) return;
            _hooked = true;
            CompilationPipeline.compilationStarted += _ =>
            {
                lock (_compilerLock)
                {
                    _compilerErrors.Clear();
                    _lastWarningCount = 0;
                }
            };
            CompilationPipeline.assemblyCompilationFinished += (path, messages) =>
            {
                lock (_compilerLock)
                {
                    foreach (var m in messages)
                    {
                        if (m.type == CompilerMessageType.Error)
                        {
                            _compilerErrors.Add(new Dictionary<string, object>
                            {
                                ["file"] = m.file, ["line"] = m.line, ["message"] = m.message,
                            });
                        }
                        else if (m.type == CompilerMessageType.Warning)
                        {
                            _lastWarningCount++;
                        }
                    }
                }
            };
            CompilationPipeline.compilationFinished += _ =>
            {
                _lastCompileTime = DateTime.Now.ToString("O");
            };
        }

        public static void CompilerErrors(HttpListenerRequest req, HttpListenerResponse res)
        {
            ResponseHelper.Send(res, 200, GetCompilerErrorsJson());
        }

        /// <summary>Direct access for batch endpoint.</summary>
        public static string GetCompilerErrorsJson()
        {
            lock (_compilerLock)
            {
                return "[" + string.Join(",", _compilerErrors.Select(e => Json.Object(e))) + "]";
            }
        }

        // ── /console/logs ────────────────────────────────────────────────────

        private const int LogBufferSize = 200;
        private static readonly List<Dictionary<string, object>> _logBuffer = new List<Dictionary<string, object>>();
        private static readonly object _logLock = new object();
        private static bool _logHooked;

        [InitializeOnLoadMethod]
        private static void HookLogMessages()
        {
            if (_logHooked) return;
            _logHooked = true;
            Application.logMessageReceived += (message, stackTrace, logType) =>
            {
                lock (_logLock)
                {
                    if (_logBuffer.Count >= LogBufferSize)
                        _logBuffer.RemoveAt(0);
                    _logBuffer.Add(new Dictionary<string, object>
                    {
                        ["message"]    = message,
                        ["stackTrace"] = stackTrace,
                        ["type"]       = logType.ToString(),
                        ["timestamp"]  = DateTime.Now.ToString("O"),
                    });
                }
            };
        }

        public static void ConsoleLogs(HttpListenerRequest req, HttpListenerResponse res)
        {
            var typeFilter = req.QueryString["type"];
            var since = req.QueryString["since"];
            var clearParam = req.QueryString["clear"];

            string json;
            lock (_logLock)
            {
                IEnumerable<Dictionary<string, object>> entries = _logBuffer;

                if (!string.IsNullOrEmpty(typeFilter))
                    entries = entries.Where(e => e["type"].ToString().Equals(typeFilter, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(since) && DateTime.TryParse(since, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var sinceDate))
                    entries = entries.Where(e =>
                        DateTime.TryParse((string)e["timestamp"], null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out var ts) && ts > sinceDate);

                var results = entries.Select(e => Json.Object(e)).ToList();

                if (clearParam == "true")
                    _logBuffer.Clear();

                json = Json.Object(new Dictionary<string, object>
                {
                    ["count"]   = results.Count,
                    ["entries"] = new RawJson("[" + string.Join(",", results) + "]"),
                });
            }
            ResponseHelper.Send(res, 200, json);
        }

        // ── /build/stats ──────────────────────────────────────────────────────
        // GET /build/stats — scene performance statistics

        public static void BuildStats(HttpListenerRequest req, HttpListenerResponse res)
        {
            var json = MainThreadDispatcher.Execute(() =>
            {
                var renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
                var skinnedRenderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
                var meshFilters = UnityEngine.Object.FindObjectsOfType<MeshFilter>();

                long totalTris = 0;
                long totalVerts = 0;
                var materials = new HashSet<Material>();
                var textures = new HashSet<Texture>();

                foreach (var mf in meshFilters)
                {
                    if (mf.sharedMesh != null)
                    {
                        totalTris += mf.sharedMesh.triangles.Length / 3;
                        totalVerts += mf.sharedMesh.vertexCount;
                    }
                }

                foreach (var sr in skinnedRenderers)
                {
                    if (sr.sharedMesh != null)
                    {
                        totalTris += sr.sharedMesh.triangles.Length / 3;
                        totalVerts += sr.sharedMesh.vertexCount;
                    }
                }

                foreach (var r in renderers)
                    foreach (var m in r.sharedMaterials)
                        if (m != null) materials.Add(m);

                foreach (var r in skinnedRenderers)
                    foreach (var m in r.sharedMaterials)
                        if (m != null) materials.Add(m);

                foreach (var mat in materials)
                {
                    var shader = mat.shader;
                    int propCount = ShaderUtil.GetPropertyCount(shader);
                    for (int i = 0; i < propCount; i++)
                    {
                        if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                        {
                            var propName = ShaderUtil.GetPropertyName(shader, i);
                            var tex = mat.GetTexture(propName);
                            if (tex != null) textures.Add(tex);
                        }
                    }
                }

                var lights = UnityEngine.Object.FindObjectsOfType<Light>();
                var audioSources = UnityEngine.Object.FindObjectsOfType<AudioSource>();
                var particleSystems = UnityEngine.Object.FindObjectsOfType<ParticleSystem>();

                return Json.Object(new Dictionary<string, object>
                {
                    ["triangles"]        = totalTris,
                    ["vertices"]         = totalVerts,
                    ["meshRenderers"]    = renderers.Length,
                    ["skinnedRenderers"] = skinnedRenderers.Length,
                    ["materials"]        = materials.Count,
                    ["textures"]         = textures.Count,
                    ["lights"]           = lights.Length,
                    ["audioSources"]     = audioSources.Length,
                    ["particleSystems"]  = particleSystems.Length,
                });
            });
            ResponseHelper.Send(res, 200, json);
        }

        // ── GET /build/validate ──────────────────────────────────────────────
        // Returns VRChat SDK validation results and performance stats.

        public static void BuildValidate(HttpListenerRequest req, HttpListenerResponse res)
        {
            var json = GetBuildValidateJson();
            ResponseHelper.Send(res, 200, json);
        }

        /// <summary>Direct access for batch endpoint.</summary>
        public static string GetBuildValidateJson()
        {
            return MainThreadDispatcher.Execute(() =>
            {
                var warnings = new List<string>();

                // Check for VRCSDK
                bool sdkPresent = FindTypeByFullName("VRC.SDKBase.VRC_SceneDescriptor") != null;

                if (!sdkPresent)
                {
                    return Json.Object(new Dictionary<string, object>
                    {
                        ["sdkPresent"] = false,
                        ["valid"] = false,
                        ["stats"] = new RawJson("null"),
                        ["warnings"] = new RawJson("[" + Json.Quote("VRChat SDK not detected in project") + "]"),
                    });
                }

                // Gather performance stats
                var meshFilters = UnityEngine.Object.FindObjectsOfType<MeshFilter>();
                var skinnedRenderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
                var renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();

                long totalTris = 0;
                foreach (var mf in meshFilters)
                    if (mf.sharedMesh != null) totalTris += mf.sharedMesh.triangles.Length / 3;
                foreach (var sr in skinnedRenderers)
                    if (sr.sharedMesh != null) totalTris += sr.sharedMesh.triangles.Length / 3;

                var materials = new HashSet<Material>();
                foreach (var r in renderers)
                    foreach (var m in r.sharedMaterials)
                        if (m != null) materials.Add(m);
                foreach (var r in skinnedRenderers)
                    foreach (var m in r.sharedMaterials)
                        if (m != null) materials.Add(m);

                var textures = new HashSet<Texture>();
                foreach (var mat in materials)
                {
                    try
                    {
                        var shader = mat.shader;
                        int propCount = ShaderUtil.GetPropertyCount(shader);
                        for (int i = 0; i < propCount; i++)
                        {
                            if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                            {
                                var propName = ShaderUtil.GetPropertyName(shader, i);
                                var tex = mat.GetTexture(propName);
                                if (tex != null) textures.Add(tex);
                            }
                        }
                    }
                    catch { /* skip materials with problematic shaders */ }
                }

                int meshRendererCount = renderers.Length + skinnedRenderers.Length;

                // Check for missing references on VRC components
                var sceneDescType = FindTypeByFullName("VRC.SDKBase.VRC_SceneDescriptor");
                var udonBehaviourType = FindTypeByFullName("VRC.Udon.UdonBehaviour");

                // Check scene descriptor exists
                bool hasSceneDescriptor = false;
                if (sceneDescType != null)
                {
                    var descriptors = UnityEngine.Object.FindObjectsOfType(sceneDescType);
                    if (descriptors.Length == 0)
                        warnings.Add("No VRC_SceneDescriptor found in scene — required for VRChat worlds");
                    else
                    {
                        hasSceneDescriptor = true;
                        if (descriptors.Length > 1)
                            warnings.Add($"Multiple VRC_SceneDescriptor components found ({descriptors.Length}) — only one is allowed");
                    }
                }

                // Check UdonBehaviours for missing program sources
                if (udonBehaviourType != null)
                {
                    var udons = UnityEngine.Object.FindObjectsOfType(udonBehaviourType);
                    foreach (var udon in udons)
                    {
                        try
                        {
                            var so = new SerializedObject(udon);
                            var programProp = so.FindProperty("serializedProgramAsset");
                            if (programProp != null && programProp.objectReferenceValue == null)
                            {
                                var goName = (udon as Component)?.gameObject?.name ?? "Unknown";
                                warnings.Add($"UdonBehaviour on '{goName}' has no program source assigned");
                            }
                        }
                        catch { /* skip if serialization fails */ }
                    }
                }

                // Check for common VRC issues
                foreach (var r in renderers)
                {
                    if (r.sharedMaterials.Any(m => m == null))
                        warnings.Add($"MeshRenderer on '{r.gameObject.name}' has missing material slot(s)");
                }
                foreach (var r in skinnedRenderers)
                {
                    if (r.sharedMaterials.Any(m => m == null))
                        warnings.Add($"SkinnedMeshRenderer on '{r.gameObject.name}' has missing material slot(s)");
                }

                bool valid = hasSceneDescriptor && warnings.Count == 0;

                var warningJsonList = new List<string>();
                foreach (var w in warnings) warningJsonList.Add(Json.Quote(w));

                var stats = Json.Object(new Dictionary<string, object>
                {
                    ["triangles"] = totalTris,
                    ["materials"] = materials.Count,
                    ["textures"] = textures.Count,
                    ["meshRenderers"] = meshRendererCount,
                });

                return Json.Object(new Dictionary<string, object>
                {
                    ["sdkPresent"] = true,
                    ["valid"] = valid,
                    ["stats"] = new RawJson(stats),
                    ["warnings"] = new RawJson("[" + string.Join(",", warningJsonList) + "]"),
                });
            });
        }

        /// <summary>Direct access for batch endpoint.</summary>
        public static string GetBuildStatsJson()
        {
            return MainThreadDispatcher.Execute(() =>
            {
                var renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
                var skinnedRenderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
                var meshFilters = UnityEngine.Object.FindObjectsOfType<MeshFilter>();

                long totalTris = 0;
                long totalVerts = 0;
                var materials = new HashSet<Material>();
                var textures = new HashSet<Texture>();

                foreach (var mf in meshFilters)
                {
                    if (mf.sharedMesh != null)
                    {
                        totalTris += mf.sharedMesh.triangles.Length / 3;
                        totalVerts += mf.sharedMesh.vertexCount;
                    }
                }

                foreach (var sr in skinnedRenderers)
                {
                    if (sr.sharedMesh != null)
                    {
                        totalTris += sr.sharedMesh.triangles.Length / 3;
                        totalVerts += sr.sharedMesh.vertexCount;
                    }
                }

                foreach (var r in renderers)
                    foreach (var m in r.sharedMaterials)
                        if (m != null) materials.Add(m);
                foreach (var r in skinnedRenderers)
                    foreach (var m in r.sharedMaterials)
                        if (m != null) materials.Add(m);
                foreach (var mat in materials)
                {
                    try
                    {
                        var shader = mat.shader;
                        int propCount = ShaderUtil.GetPropertyCount(shader);
                        for (int i = 0; i < propCount; i++)
                        {
                            if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                            {
                                var propName = ShaderUtil.GetPropertyName(shader, i);
                                var tex = mat.GetTexture(propName);
                                if (tex != null) textures.Add(tex);
                            }
                        }
                    }
                    catch { }
                }

                var lights = UnityEngine.Object.FindObjectsOfType<Light>();
                var audioSources = UnityEngine.Object.FindObjectsOfType<AudioSource>();
                var particleSystems = UnityEngine.Object.FindObjectsOfType<ParticleSystem>();

                return Json.Object(new Dictionary<string, object>
                {
                    ["triangles"]        = totalTris,
                    ["vertices"]         = totalVerts,
                    ["meshRenderers"]    = renderers.Length,
                    ["skinnedRenderers"] = skinnedRenderers.Length,
                    ["materials"]        = materials.Count,
                    ["textures"]         = textures.Count,
                    ["lights"]           = lights.Length,
                    ["audioSources"]     = audioSources.Length,
                    ["particleSystems"]  = particleSystems.Length,
                });
            });
        }

        // ── GET /meta/claude-md-snippet ──────────────────────────────────────
        // Returns a markdown snippet summarizing the current project state.

        public static void ClaudeMdSnippet(HttpListenerRequest req, HttpListenerResponse res)
        {
            var text = GetClaudeMdSnippetText();
            // Return as plain text/markdown
            res.StatusCode = 200;
            res.ContentType = "text/markdown; charset=utf-8";
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            res.ContentLength64 = bytes.Length;
            try { res.OutputStream.Write(bytes, 0, bytes.Length); }
            finally { res.OutputStream.Close(); }
        }

        /// <summary>Direct access for batch endpoint.</summary>
        public static string GetClaudeMdSnippetText()
        {
            return MainThreadDispatcher.Execute(() =>
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# Tiresias Project State Snapshot");
                sb.AppendLine();

                // Registered endpoints
                sb.AppendLine("## Registered Endpoints");
                sb.AppendLine();
                var endpoints = new string[]
                {
                    "GET  /status",
                    "GET  /scene",
                    "GET  /scene/hierarchy",
                    "GET  /scene/object",
                    "GET  /scene/selected",
                    "GET  /scenes",
                    "GET  /assets/scripts",
                    "GET  /assets/prefabs",
                    "GET  /assets/search",
                    "GET  /assets/dependencies",
                    "GET  /assets/import-status",
                    "GET  /compiler/status",
                    "GET  /compiler/errors",
                    "GET  /console/logs",
                    "GET  /build/stats",
                    "GET  /build/validate",
                    "GET  /meta/claude-md-snippet",
                    "GET  /api/editor/screenshot",
                    "POST /batch",
                    "POST /api/scene/objects",
                    "POST /api/scene/save",
                    "POST /api/scene/open",
                    "POST /api/assets/refresh",
                    "POST /api/assets/prefabs/save",
                    "POST /api/assets/instantiate",
                    "POST /api/assets/materials",
                    "POST /api/editor/play",
                    "POST /api/editor/stop",
                    "POST /api/editor/undo",
                    "POST /api/editor/redo",
                    "POST /api/editor/menu",
                    "PUT  /api/scene/{name}/transform",
                    "PUT  /api/scene/{name}/active",
                    "PUT  /api/scene/{name}/parent",
                    "PUT  /api/scene/{name}/materials",
                    "PUT  /api/scene/{name}/components/{type}/fields/{field}",
                    "PUT  /api/scene/{name}/components/{type}/events/{event}",
                    "POST /api/scene/{name}/components",
                    "DEL  /api/scene/{name}",
                    "DEL  /api/scene/{name}/components/{type}",
                    "POST /api/assets/prefabs/{path}",
                };
                foreach (var ep in endpoints)
                    sb.AppendLine("- `" + ep + "`");
                sb.AppendLine();

                // Current scene
                var scene = SceneManager.GetActiveScene();
                sb.AppendLine("## Active Scene");
                sb.AppendLine();
                sb.AppendLine($"- **Name**: {scene.name}");
                sb.AppendLine($"- **Path**: {scene.path}");
                sb.AppendLine($"- **Dirty**: {scene.isDirty}");
                sb.AppendLine();

                // Scene hierarchy summary (top-level objects)
                sb.AppendLine("## Top-Level Scene Objects");
                sb.AppendLine();
                var roots = scene.GetRootGameObjects();
                foreach (var go in roots)
                    sb.AppendLine($"- {go.name} ({go.transform.childCount} children)");
                sb.AppendLine();

                // Installed packages from manifest.json
                sb.AppendLine("## Installed Packages");
                sb.AppendLine();
                var manifestPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Application.dataPath), "Packages", "manifest.json");
                if (System.IO.File.Exists(manifestPath))
                {
                    try
                    {
                        var manifest = System.IO.File.ReadAllText(manifestPath);
                        // Simple extraction of "dependencies" keys
                        var depsIdx = manifest.IndexOf("\"dependencies\"");
                        if (depsIdx >= 0)
                        {
                            var braceStart = manifest.IndexOf('{', depsIdx);
                            if (braceStart >= 0)
                            {
                                int depth = 0;
                                int i = braceStart;
                                while (i < manifest.Length)
                                {
                                    if (manifest[i] == '{') depth++;
                                    else if (manifest[i] == '}') { depth--; if (depth == 0) break; }
                                    i++;
                                }
                                var depsBlock = manifest.Substring(braceStart, i - braceStart + 1);
                                var deps = Json.ParseFlat(depsBlock);
                                foreach (var kv in deps)
                                    sb.AppendLine($"- `{kv.Key}`: {kv.Value}");
                            }
                        }
                    }
                    catch { sb.AppendLine("- (could not read manifest.json)"); }
                }
                else
                {
                    sb.AppendLine("- (Packages/manifest.json not found)");
                }
                sb.AppendLine();

                // VRC component detection
                sb.AppendLine("## Detected VRC Components");
                sb.AppendLine();
                var udonType = FindTypeByFullName("VRC.Udon.UdonBehaviour");
                if (udonType != null)
                {
                    var udons = UnityEngine.Object.FindObjectsOfType(udonType);
                    sb.AppendLine($"- UdonBehaviour: {udons.Length}");
                }
                else
                {
                    sb.AppendLine("- UdonBehaviour: (type not found -- SDK not installed?)");
                }

                var sceneDescType = FindTypeByFullName("VRC.SDKBase.VRC_SceneDescriptor");
                if (sceneDescType != null)
                {
                    var descs = UnityEngine.Object.FindObjectsOfType(sceneDescType);
                    sb.AppendLine($"- VRC_SceneDescriptor: {descs.Length}");
                }

                var mirrorType = FindTypeByFullName("VRC.SDKBase.VRC_MirrorReflection")
                    ?? FindTypeByFullName("VRC.SDK3.Components.VRCMirrorReflection");
                if (mirrorType != null)
                {
                    var mirrors = UnityEngine.Object.FindObjectsOfType(mirrorType);
                    sb.AppendLine($"- VRCMirrorReflection: {mirrors.Length}");
                }

                var pickupType = FindTypeByFullName("VRC.SDKBase.VRC_Pickup")
                    ?? FindTypeByFullName("VRC.SDK3.Components.VRCPickup");
                if (pickupType != null)
                {
                    var pickups = UnityEngine.Object.FindObjectsOfType(pickupType);
                    sb.AppendLine($"- VRCPickup: {pickups.Length}");
                }

                sb.AppendLine();

                return sb.ToString();
            });
        }

        // ── POST /batch ───────────────────────────────────────────────────────
        // Body: [{"method":"GET","path":"/scene/hierarchy"},{"method":"GET","path":"/compiler/errors"}]

        public static void Batch(HttpListenerRequest req, HttpListenerResponse res)
        {
            string body;
            try { body = Json.ReadBody(req); }
            catch { ResponseHelper.Send(res, 400, "{\"error\":\"Could not read request body\"}"); return; }

            var requests = Json.ParseBatchRequests(body);
            if (requests == null || requests.Count == 0)
            {
                ResponseHelper.Send(res, 400, "{\"error\":\"Body must be a JSON array of {method, path} objects\"}");
                return;
            }

            if (requests.Count > 10)
            {
                ResponseHelper.Send(res, 400, "{\"error\":\"Maximum 10 requests per batch\"}");
                return;
            }

            var results = new List<string>();
            foreach (var r in requests)
            {
                var capture = new ResponseCapture();
                var fakeReq = new BatchRequest(r.method, r.path);
                TiresiasRouter.HandleDirect(fakeReq.Method, fakeReq.Path, fakeReq.QueryString, req, capture);
                results.Add(Json.Object(new Dictionary<string, object>
                {
                    ["path"]       = r.path,
                    ["statusCode"] = capture.StatusCode,
                    ["body"]       = new RawJson(capture.Body ?? "null"),
                }));
            }

            ResponseHelper.Send(res, 200, "[" + string.Join(",", results) + "]");
        }

        // =====================================================================
        // WRITE ENDPOINTS — all execute on Unity main thread via Dispatcher
        // =====================================================================

        // ── POST /api/scene/objects ───────────────────────────────────────────

        public static void CreateGameObject(HttpListenerRequest req, HttpListenerResponse res)
        {
            var f = ParseBody(req, res); if (f == null) return;
            f.TryGetValue("name",   out var goName);
            f.TryGetValue("parent", out var parentName);
            f.TryGetValue("px", out var pxs); f.TryGetValue("py", out var pys); f.TryGetValue("pz", out var pzs);

            if (string.IsNullOrEmpty(goName)) { ResponseHelper.Send(res, 400, "{\"error\":\"'name' is required\"}"); return; }

            Dispatch(res, () =>
            {
                var go = new GameObject(goName);
                Undo.RegisterCreatedObjectUndo(go, "Tiresias: Create " + goName);

                if (!string.IsNullOrEmpty(parentName))
                {
                    var parent = GameObject.Find(parentName);
                    if (parent == null)
                    {
                        UnityEngine.Object.DestroyImmediate(go);
                        return HR.Error(404, $"Parent '{parentName}' not found");
                    }
                    go.transform.SetParent(parent.transform, false);
                }

                var pos = go.transform.position;
                if (TryF(pxs, out float px)) pos.x = px;
                if (TryF(pys, out float py)) pos.y = py;
                if (TryF(pzs, out float pz)) pos.z = pz;
                go.transform.position = pos;

                MarkDirty(go);
                return HR.Ok(Json.Object(new Dictionary<string, object>
                {
                    ["created"]  = go.name,
                    ["parent"]   = go.transform.parent != null ? go.transform.parent.name : null,
                    ["position"] = new RawJson(Vec3(go.transform.position)),
                }));
            });
        }

        // ── POST /api/scene/{name}/components ─────────────────────────────────

        public static void AddComponent(HttpListenerRequest req, HttpListenerResponse res, string gameObjectName)
        {
            var f = ParseBody(req, res); if (f == null) return;
            f.TryGetValue("type", out var componentType);
            if (string.IsNullOrEmpty(componentType))
                f.TryGetValue("componentType", out componentType); // backward compat
            if (string.IsNullOrEmpty(componentType))
            { ResponseHelper.Send(res, 400, "{\"error\":\"'type' is required\"}"); return; }

            Dispatch(res, () =>
            {
                var go = GameObject.Find(gameObjectName);
                if (go == null) return HR.Error(404, $"GameObject '{gameObjectName}' not found");

                var type = FindTypeByName(componentType);
                if (type == null) return HR.Error(404, $"Type '{componentType}' not found in any loaded assembly");

                Component comp = null;

                var udonBase = FindTypeByFullName("UdonSharp.UdonSharpBehaviour");
                if (udonBase != null && udonBase.IsAssignableFrom(type))
                {
                    var util = FindTypeByFullName("UdonSharpEditor.UdonSharpEditorUtility");
                    if (util != null)
                    {
                        var method = util.GetMethod("AddUdonSharpComponent",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (method != null)
                            try { comp = method.Invoke(null, new object[] { go, type }) as Component; } catch { }
                    }
                }

                if (comp == null)
                {
                    comp = Undo.AddComponent(go, type);
                }
                MarkDirty(go);
                return HR.Ok(Json.Object(new Dictionary<string, object>
                {
                    ["gameObject"]     = gameObjectName,
                    ["addedComponent"] = comp.GetType().Name,
                }));
            });
        }

        // ── PUT /api/scene/{name}/transform ───────────────────────────────────

        public static void SetTransform(HttpListenerRequest req, HttpListenerResponse res, string gameObjectName)
        {
            var f = ParseBody(req, res); if (f == null) return;
            f.TryGetValue("space", out var space);
            bool local = space == "local";

            Dispatch(res, () =>
            {
                var go = GameObject.Find(gameObjectName);
                if (go == null) return HR.Error(404, $"GameObject '{gameObjectName}' not found");
                var t = go.transform;
                Undo.RecordObject(t, "Tiresias: SetTransform " + gameObjectName);

                if (f.ContainsKey("px") || f.ContainsKey("py") || f.ContainsKey("pz"))
                {
                    var pos = local ? t.localPosition : t.position;
                    f.TryGetValue("px", out var s); if (TryF(s, out float v)) pos.x = v;
                    f.TryGetValue("py", out s);     if (TryF(s, out v)) pos.y = v;
                    f.TryGetValue("pz", out s);     if (TryF(s, out v)) pos.z = v;
                    if (local) t.localPosition = pos; else t.position = pos;
                }

                if (f.ContainsKey("rx") || f.ContainsKey("ry") || f.ContainsKey("rz"))
                {
                    var rot = local ? t.localEulerAngles : t.eulerAngles;
                    f.TryGetValue("rx", out var s); if (TryF(s, out float v)) rot.x = v;
                    f.TryGetValue("ry", out s);     if (TryF(s, out v)) rot.y = v;
                    f.TryGetValue("rz", out s);     if (TryF(s, out v)) rot.z = v;
                    if (local) t.localEulerAngles = rot; else t.eulerAngles = rot;
                }

                if (f.ContainsKey("sx") || f.ContainsKey("sy") || f.ContainsKey("sz"))
                {
                    var scl = t.localScale;
                    f.TryGetValue("sx", out var s); if (TryF(s, out float v)) scl.x = v;
                    f.TryGetValue("sy", out s);     if (TryF(s, out v)) scl.y = v;
                    f.TryGetValue("sz", out s);     if (TryF(s, out v)) scl.z = v;
                    t.localScale = scl;
                }

                MarkDirty(go);
                return HR.Ok(Json.Object(new Dictionary<string, object>
                {
                    ["gameObject"] = gameObjectName,
                    ["position"]   = new RawJson(Vec3(t.localPosition)),
                    ["rotation"]   = new RawJson(Vec3(t.localEulerAngles)),
                    ["scale"]      = new RawJson(Vec3(t.localScale)),
                }));
            });
        }

        // ── PUT /api/scene/{name}/active ──────────────────────────────────────

        public static void SetActive(HttpListenerRequest req, HttpListenerResponse res, string gameObjectName)
        {
            var f = ParseBody(req, res); if (f == null) return;
            if (!f.TryGetValue("active", out var activeStr))
            { ResponseHelper.Send(res, 400, "{\"error\":\"'active' is required\"}"); return; }
            bool active = activeStr.ToLower() == "true" || activeStr == "1";

            Dispatch(res, () =>
            {
                var go = GameObject.Find(gameObjectName);
                if (go == null) return HR.Error(404, $"GameObject '{gameObjectName}' not found");
                Undo.RecordObject(go, "Tiresias: SetActive " + gameObjectName);
                go.SetActive(active);
                MarkDirty(go);
                return HR.Ok(Json.Object(new Dictionary<string, object>
                {
                    ["gameObject"] = gameObjectName,
                    ["active"]     = active,
                }));
            });
        }

        // ── DELETE /api/scene/{name} ──────────────────────────────────────────

        public static void DeleteGameObject(HttpListenerRequest req, HttpListenerResponse res, string gameObjectName)
        {
            Dispatch(res, () =>
            {
                var go = GameObject.Find(gameObjectName);
                if (go == null) return HR.Error(404, $"GameObject '{gameObjectName}' not found");
                Undo.DestroyObjectImmediate(go);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                return HR.Ok(Json.Object(new Dictionary<string, object> { ["deleted"] = gameObjectName }));
            });
        }

        // ── PUT /api/scene/{name}/parent ──────────────────────────────────────

        public static void SetParent(HttpListenerRequest req, HttpListenerResponse res, string gameObjectName)
        {
            var f = ParseBody(req, res); if (f == null) return;
            f.TryGetValue("parent", out var parentName);
            f.TryGetValue("worldPositionStays", out var wpsStr);
            bool wps = wpsStr == null || wpsStr.ToLower() != "false";

            Dispatch(res, () =>
            {
                var go = GameObject.Find(gameObjectName);
                if (go == null) return HR.Error(404, $"GameObject '{gameObjectName}' not found");

                Transform newParent = null;
                if (!string.IsNullOrEmpty(parentName))
                {
                    var parentGo = GameObject.Find(parentName);
                    if (parentGo == null) return HR.Error(404, $"Parent '{parentName}' not found");
                    newParent = parentGo.transform;
                }

                Undo.RecordObject(go.transform, "Tiresias: SetParent " + gameObjectName);
                go.transform.SetParent(newParent, wps);
                MarkDirty(go);
                return HR.Ok(Json.Object(new Dictionary<string, object>
                {
                    ["gameObject"] = gameObjectName,
                    ["parent"]     = newParent != null ? newParent.name : null,
                }));
            });
        }

        // ── POST /api/assets/prefabs/{path} ───────────────────────────────────

        public static void InstantiatePrefab(HttpListenerRequest req, HttpListenerResponse res, string prefabPath)
        {
            var f = ParseBody(req, res); if (f == null) return;
            f.TryGetValue("parent", out var parentName);
            f.TryGetValue("name",   out var overrideName);
            f.TryGetValue("px", out var pxs); f.TryGetValue("py", out var pys); f.TryGetValue("pz", out var pzs);

            Dispatch(res, () =>
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) return HR.Error(404, $"Prefab not found at '{prefabPath}'");

                Transform parent = null;
                if (!string.IsNullOrEmpty(parentName))
                {
                    var parentGo = GameObject.Find(parentName);
                    if (parentGo == null) return HR.Error(404, $"Parent '{parentName}' not found");
                    parent = parentGo.transform;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                Undo.RegisterCreatedObjectUndo(instance, "Tiresias: Instantiate " + prefab.name);
                if (!string.IsNullOrEmpty(overrideName)) instance.name = overrideName;

                var pos = instance.transform.position;
                if (TryF(pxs, out float px)) pos.x = px;
                if (TryF(pys, out float py)) pos.y = py;
                if (TryF(pzs, out float pz)) pos.z = pz;
                instance.transform.position = pos;

                MarkDirty(instance);
                return HR.Ok(Json.Object(new Dictionary<string, object>
                {
                    ["instantiated"] = instance.name,
                    ["prefab"]       = prefabPath,
                    ["parent"]       = parent != null ? parent.name : null,
                    ["position"]     = new RawJson(Vec3(instance.transform.position)),
                }));
            });
        }

        // ── DELETE /api/scene/{name}/components/{type} ────────────────────────

        public static void RemoveComponent(HttpListenerRequest req, HttpListenerResponse res,
            string gameObjectName, string componentType)
        {
            Dispatch(res, () =>
            {
                var go = GameObject.Find(gameObjectName);
                if (go == null) return HR.Error(404, $"GameObject '{gameObjectName}' not found");

                var comp = FindComponentByTypeName(go, componentType);
                if (comp == null) return HR.Error(404, $"Component '{componentType}' not found on '{gameObjectName}'");

                Undo.DestroyObjectImmediate(comp);
                MarkDirty(go);
                return HR.Ok(Json.Object(new Dictionary<string, object>
                {
                    ["gameObject"]       = gameObjectName,
                    ["removedComponent"] = componentType,
                }));
            });
        }

        // ── PUT /api/scene/{name}/components/{type}/fields/{field} ────────────

        public static void SetField(HttpListenerRequest req, HttpListenerResponse res,
            string gameObjectName, string componentType, string fieldName)
        {
            string body;
            try { body = Json.ReadBody(req); }
            catch { ResponseHelper.Send(res, 400, "{\"error\":\"Could not read request body\"}"); return; }

            var f = Json.ParseFlat(body);
            if (f.Count == 0) { ResponseHelper.Send(res, 400, "{\"error\":\"Invalid or empty request body\"}"); return; }

            f.TryGetValue("referenceType", out var refType);
            f.TryGetValue("valueType",     out var valType);

            if (refType == null && valType == null)
            { ResponseHelper.Send(res, 400, "{\"error\":\"Body must contain 'referenceType' or 'valueType'\"}"); return; }

            if (refType != null)
            {
                f.TryGetValue("targetGameObjectName", out var targetGoName);
                f.TryGetValue("targetComponentType",  out var targetCompType);

                if (refType != "gameObject" && refType != "component")
                { ResponseHelper.Send(res, 400, "{\"error\":\"referenceType must be 'gameObject' or 'component'\"}"); return; }
                if (string.IsNullOrEmpty(targetGoName))
                { ResponseHelper.Send(res, 400, "{\"error\":\"targetGameObjectName is required\"}"); return; }
                if (refType == "component" && string.IsNullOrEmpty(targetCompType))
                { ResponseHelper.Send(res, 400, "{\"error\":\"targetComponentType is required when referenceType is 'component'\"}"); return; }

                Dispatch(res, () => SetRefOnMain(gameObjectName, componentType, fieldName, refType, targetGoName, targetCompType));
            }
            else
            {
                f.TryGetValue("value", out var value);
                f.TryGetValue("x", out var vx); f.TryGetValue("y", out var vy); f.TryGetValue("z", out var vz);
                f.TryGetValue("r", out var vr); f.TryGetValue("g", out var vg);
                f.TryGetValue("b", out var vb); f.TryGetValue("a", out var va);

                Dispatch(res, () => SetValOnMain(gameObjectName, componentType, fieldName,
                    valType, value, vx, vy, vz, vr, vg, vb, va));
            }
        }

        // ── PUT /api/scene/{name}/components/{type}/events/{event} ──────────
        // Add a persistent listener to a UnityEvent (e.g. Button.onClick).
        // Body: { "targetGameObjectName": "X", "targetComponentType": "UdonBehaviour",
        //         "methodName": "SendCustomEvent", "argType": "string", "argValue": "OnSearchPressed" }

        public static void AddEventListener(HttpListenerRequest req, HttpListenerResponse res,
            string gameObjectName, string componentType, string eventName)
        {
            string body;
            try { body = Json.ReadBody(req); }
            catch { ResponseHelper.Send(res, 400, "{\"error\":\"Could not read request body\"}"); return; }

            var f = Json.ParseFlat(body);
            f.TryGetValue("targetGameObjectName", out var targetGoName);
            f.TryGetValue("targetComponentType",  out var targetCompType);
            f.TryGetValue("methodName",           out var methodName);
            f.TryGetValue("argType",              out var argType);
            f.TryGetValue("argValue",             out var argValue);

            if (string.IsNullOrEmpty(targetGoName) || string.IsNullOrEmpty(methodName))
            {
                ResponseHelper.Send(res, 400,
                    "{\"error\":\"Required: targetGameObjectName, methodName. Optional: targetComponentType, argType, argValue\"}");
                return;
            }

            Dispatch(res, () => AddEventListenerOnMain(
                gameObjectName, componentType, eventName,
                targetGoName, targetCompType, methodName, argType, argValue));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Main-thread implementations
        // ─────────────────────────────────────────────────────────────────────

        private struct HR
        {
            public int    StatusCode;
            public string Json;
            public static HR Error(int code, string msg)
                => new HR { StatusCode = code, Json = Tiresias.Json.Object(new Dictionary<string, object> { ["error"] = msg }) };
            public static HR Ok(string json) => new HR { StatusCode = 200, Json = json };
        }

        private static HR SetRefOnMain(string goName, string compType, string field,
            string refType, string targetGoName, string targetCompType)
        {
            var go = GameObject.Find(goName);
            if (go == null) return HR.Error(404, $"No GameObject '{goName}'");

            var comp = FindComponentByTypeName(go, compType);
            if (comp == null)
            {
                var avail = go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).ToArray();
                return HR.Error(404, $"No component '{compType}' on '{goName}'. Available: [{string.Join(", ", avail)}]");
            }

            // For UdonBehaviour, try to get the UdonSharp proxy which has the real serialized fields
            Component proxyComp = null;
            bool isUdonProxy = false;
            if (comp.GetType().Name == "UdonBehaviour")
            {
                proxyComp = TryGetUdonSharpProxy(comp);
                if (proxyComp != null) isUdonProxy = true;
            }

            var targetComp = isUdonProxy ? proxyComp : comp;
            var so   = new SerializedObject(targetComp);
            var prop = so.FindProperty(field);
            if (prop == null)
                return HR.Error(400, $"No field '{field}' on '{compType}'. Available: [{string.Join(", ", ListSerializedFields(so))}]");
            if (prop.propertyType != SerializedPropertyType.ObjectReference
                && prop.propertyType != SerializedPropertyType.Generic)
                return HR.Error(400, $"Field '{field}' is '{prop.propertyType}' — use 'valueType' for primitives");

            Undo.RecordObject(targetComp, $"Tiresias: SetField {field} on {goName}");

            var targetGo = GameObject.Find(targetGoName);
            if (targetGo == null) return HR.Error(404, $"Target '{targetGoName}' not found");

            UnityEngine.Object targetObj;
            string typeName;
            if (refType == "gameObject") { targetObj = targetGo; typeName = "GameObject"; }
            else
            {
                var tc = FindComponentByTypeName(targetGo, targetCompType);
                if (tc == null)
                {
                    var avail = targetGo.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).ToArray();
                    return HR.Error(404, $"No '{targetCompType}' on '{targetGoName}'. Available: [{string.Join(", ", avail)}]");
                }
                targetObj = tc; typeName = tc.GetType().Name;
            }

            // Handle array fields: if the property is an array, find first null slot or expand
            if (prop.isArray && prop.propertyType == SerializedPropertyType.Generic)
            {
                int slotIndex = -1;
                for (int i = 0; i < prop.arraySize; i++)
                {
                    var elem = prop.GetArrayElementAtIndex(i);
                    if (elem.propertyType == SerializedPropertyType.ObjectReference
                        && elem.objectReferenceValue == null)
                    {
                        slotIndex = i;
                        break;
                    }
                }
                if (slotIndex < 0)
                {
                    slotIndex = prop.arraySize;
                    prop.InsertArrayElementAtIndex(slotIndex);
                }
                var slot = prop.GetArrayElementAtIndex(slotIndex);
                slot.objectReferenceValue = targetObj;
            }
            else
            {
                prop.objectReferenceValue = targetObj;
            }

            so.ApplyModifiedProperties();

            // If we used an UdonSharp proxy, sync changes back to the UdonBehaviour
            if (isUdonProxy)
                TryCopyProxyToUdon(proxyComp);

            return HR.Ok(Json.Object(new Dictionary<string, object>
            {
                ["success"]       = true,
                ["gameObject"]    = goName,
                ["component"]     = compType,
                ["field"]         = field,
                ["assignedValue"] = new RawJson(Json.Object(new Dictionary<string, object>
                {
                    ["name"] = targetGo.name, ["type"] = typeName,
                })),
            }));
        }

        /// <summary>
        /// Given an UdonBehaviour component, try to get its UdonSharp proxy behaviour
        /// which exposes public variables as regular serialized fields.
        /// Uses reflection to avoid hard dependency on UdonSharp editor assemblies.
        /// </summary>
        private static Component TryGetUdonSharpProxy(Component udonBehaviour)
        {
            try
            {
                var utilType = FindTypeByFullName("UdonSharpEditor.UdonSharpEditorUtility");
                if (utilType == null) return null;

                var getProxy = utilType.GetMethod("GetProxyBehaviour",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (getProxy == null) return null;

                return getProxy.Invoke(null, new object[] { udonBehaviour }) as Component;
            }
            catch { return null; }
        }

        /// <summary>
        /// After modifying fields on the UdonSharp proxy, sync changes back to the UdonBehaviour.
        /// </summary>
        private static void TryCopyProxyToUdon(Component proxy)
        {
            try
            {
                var utilType = FindTypeByFullName("UdonSharpEditor.UdonSharpEditorUtility");
                if (utilType == null) return;

                var copyMethod = utilType.GetMethod("CopyProxyToUdon",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, new[] { FindTypeByFullName("UdonSharp.UdonSharpBehaviour") }, null);
                if (copyMethod == null) return;

                copyMethod.Invoke(null, new object[] { proxy });
            }
            catch { }
        }

        private static HR SetValOnMain(string goName, string compType, string field,
            string valType, string value,
            string vx, string vy, string vz,
            string vr, string vg, string vb, string va)
        {
            var go = GameObject.Find(goName);
            if (go == null) return HR.Error(404, $"No GameObject '{goName}'");

            var comp = FindComponentByTypeName(go, compType);
            if (comp == null) return HR.Error(404, $"No component '{compType}' on '{goName}'");

            var so   = new SerializedObject(comp);
            var prop = so.FindProperty(field);
            if (prop == null)
                return HR.Error(400, $"No field '{field}' on '{compType}'. Available: [{string.Join(", ", ListSerializedFields(so))}]");

            Undo.RecordObject(comp, $"Tiresias: SetField {field} on {goName}");

            try
            {
                switch (valType.ToLower())
                {
                    case "float":
                        if (!TryF(value, out float fv)) return HR.Error(400, $"Cannot parse '{value}' as float");
                        prop.floatValue = fv; break;
                    case "int":
                        if (!int.TryParse(value, out int iv)) return HR.Error(400, $"Cannot parse '{value}' as int");
                        prop.intValue = iv; break;
                    case "bool":
                        prop.boolValue = value != null && (value.ToLower() == "true" || value == "1"); break;
                    case "string":
                        prop.stringValue = value ?? ""; break;
                    case "vector3":
                        TryF(vx, out float x); TryF(vy, out float y); TryF(vz, out float z);
                        prop.vector3Value = new Vector3(x, y, z); break;
                    case "color":
                        TryF(vr, out float r); TryF(vg, out float g); TryF(vb, out float b);
                        float a = 1f; if (va != null) TryF(va, out a);
                        prop.colorValue = new Color(r, g, b, a); break;
                    case "vrcurl":
                        // Set a VRCUrl field (VRC.SDKBase.VRCUrl)
                        var vrcUrlType = System.AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(a => { try { return a.GetTypes(); } catch { return new System.Type[0]; } })
                            .FirstOrDefault(t => t.FullName == "VRC.SDKBase.VRCUrl");
                        if (vrcUrlType == null)
                            return HR.Error(500, "VRCUrl type not found — is VRChat SDK installed?");
                        var vrcUrl = System.Activator.CreateInstance(vrcUrlType, new object[] { value ?? "" });
                        // VRCUrl fields are serialized as managed references; set via reflection on the component
                        var fieldInfo = comp.GetType().GetField(field,
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (fieldInfo == null)
                            return HR.Error(400, $"Cannot find field '{field}' via reflection on '{compType}'");
                        fieldInfo.SetValue(comp, vrcUrl);
                        EditorUtility.SetDirty(comp);
                        break;
                    default:
                        return HR.Error(400,
                            $"Unknown valueType '{valType}'. Supported: float, int, bool, string, vector3, color, vrcurl");
                }
            }
            catch (Exception ex) { return HR.Error(400, $"Failed to set field: {ex.Message}"); }

            so.ApplyModifiedProperties();
            MarkDirty(go);
            return HR.Ok(Json.Object(new Dictionary<string, object>
            {
                ["success"]    = true,
                ["gameObject"] = goName,
                ["component"]  = compType,
                ["field"]      = field,
                ["valueType"]  = valType,
                ["value"]      = value ?? $"({valType})",
            }));
        }

        private static HR AddEventListenerOnMain(
            string goName, string compType, string eventName,
            string targetGoName, string targetCompType, string methodName,
            string argType, string argValue)
        {
            try
            {
                var go = GameObject.Find(goName);
                if (go == null) return HR.Error(404, $"No GameObject '{goName}'");

                var comp = FindComponentByTypeName(go, compType);
                if (comp == null) return HR.Error(404, $"No component '{compType}' on '{goName}'");

                var targetGo = GameObject.Find(targetGoName);
                if (targetGo == null) return HR.Error(404, $"Target '{targetGoName}' not found");

                // Find target component (default to first UdonBehaviour if not specified)
                Component targetComp;
                if (!string.IsNullOrEmpty(targetCompType))
                {
                    targetComp = FindComponentByTypeName(targetGo, targetCompType);
                    if (targetComp == null)
                        return HR.Error(404, $"No '{targetCompType}' on '{targetGoName}'");
                }
                else
                {
                    targetComp = targetGo.GetComponents<Component>()
                        .FirstOrDefault(c => c != null && c.GetType().Name == "UdonBehaviour");
                    if (targetComp == null)
                        targetComp = targetGo.GetComponents<Component>().FirstOrDefault(c => c != null && c is MonoBehaviour);
                    if (targetComp == null)
                        return HR.Error(404, $"No suitable target component on '{targetGoName}'");
                }

                // Find the UnityEvent field via SerializedObject
                var so = new SerializedObject(comp);
                // Common event field names: m_OnClick (Button), onValueChanged (Toggle/Slider), etc.
                // Try the provided name, then m_OnClick as fallback for Button
                var eventProp = so.FindProperty(eventName)
                    ?? so.FindProperty("m_" + eventName.Substring(0, 1).ToUpper() + eventName.Substring(1))
                    ?? so.FindProperty("m_OnClick"); // Button fallback

                if (eventProp == null)
                    return HR.Error(400, $"No event '{eventName}' on '{compType}'. Try 'onClick' or 'm_OnClick'.");

                // Navigate to the persistent calls array
                var callsProp = eventProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
                if (callsProp == null || !callsProp.isArray)
                    return HR.Error(400, $"'{eventName}' doesn't appear to be a UnityEvent (no m_PersistentCalls)");

                Undo.RecordObject(comp, $"Tiresias: AddEventListener {eventName} on {goName}");

                // Add a new element
                int idx = callsProp.arraySize;
                callsProp.InsertArrayElementAtIndex(idx);
                var call = callsProp.GetArrayElementAtIndex(idx);

                call.FindPropertyRelative("m_Target").objectReferenceValue = targetComp;
                call.FindPropertyRelative("m_MethodName").stringValue = methodName;
                call.FindPropertyRelative("m_CallState").intValue = 2; // RuntimeOnly = 2

                // Set argument mode and value
                var argsProp = call.FindPropertyRelative("m_Arguments");
                if (argType == "string" && argValue != null)
                {
                    call.FindPropertyRelative("m_Mode").intValue = 5; // PersistentListenerMode.String = 5
                    argsProp.FindPropertyRelative("m_StringArgument").stringValue = argValue;
                }
                else if (argType == "int" && argValue != null)
                {
                    call.FindPropertyRelative("m_Mode").intValue = 3; // PersistentListenerMode.Int = 3
                    if (!int.TryParse(argValue, out int intVal))
                        return HR.Error(400, $"Cannot parse '{argValue}' as int for event argument");
                    argsProp.FindPropertyRelative("m_IntArgument").intValue = intVal;
                }
                else if (argType == "float" && argValue != null)
                {
                    call.FindPropertyRelative("m_Mode").intValue = 4; // PersistentListenerMode.Float = 4
                    if (!TryF(argValue, out float floatVal))
                        return HR.Error(400, $"Cannot parse '{argValue}' as float for event argument");
                    argsProp.FindPropertyRelative("m_FloatArgument").floatValue = floatVal;
                }
                else if (argType == "bool" && argValue != null)
                {
                    call.FindPropertyRelative("m_Mode").intValue = 6; // PersistentListenerMode.Bool = 6
                    argsProp.FindPropertyRelative("m_BoolArgument").boolValue = argValue.ToLower() == "true";
                }
                else
                {
                    call.FindPropertyRelative("m_Mode").intValue = 1; // PersistentListenerMode.Void = 1
                }

                so.ApplyModifiedProperties();
                MarkDirty(go);

                return HR.Ok(Json.Object(new Dictionary<string, object>
                {
                    ["success"]     = true,
                    ["gameObject"]  = goName,
                    ["component"]   = compType,
                    ["event"]       = eventName,
                    ["target"]      = targetGoName,
                    ["method"]      = methodName,
                    ["argType"]     = argType ?? "void",
                    ["argValue"]    = argValue ?? "",
                    ["listenerIdx"] = idx,
                }));
            }
            catch (Exception ex)
            {
                return HR.Error(500, $"Failed to add event listener: {ex.Message}");
            }
        }

        // ── GET /assets/import-status ─────────────────────────────────────────

        public static void AssetImportStatus(HttpListenerRequest req, HttpListenerResponse res)
        {
            var assetPath = req.QueryString["path"];
            if (string.IsNullOrEmpty(assetPath))
            { ResponseHelper.Send(res, 400, "{\"error\":\"Missing ?path= parameter\"}"); return; }

            var (code, json) = MainThreadDispatcher.Execute(() =>
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (asset == null)
                    return (200, Json.Object(new Dictionary<string, object> { ["exists"] = false }));

                var importer = AssetImporter.GetAtPath(assetPath);
                var importerTypeName = importer != null ? importer.GetType().Name : "Unknown";

                var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                var subNames = new List<string>();
                foreach (var sub in subAssets)
                    if (sub != null && sub != asset) subNames.Add(sub.name);

                var mainType = asset.GetType().Name;
                if (importer is ModelImporter)
                    mainType = "Model";

                return (200, Json.Object(new Dictionary<string, object>
                {
                    ["exists"]    = true,
                    ["type"]      = mainType,
                    ["importer"]  = importerTypeName,
                    ["subAssets"] = new RawJson("[" + string.Join(",", subNames.Select(n => Json.Quote(n))) + "]"),
                }));
            });
            ResponseHelper.Send(res, code, json);
        }

        // ── POST /api/assets/instantiate ──────────────────────────────────────

        public static void InstantiateModel(HttpListenerRequest req, HttpListenerResponse res)
        {
            var f = ParseBody(req, res); if (f == null) return;
            f.TryGetValue("path",   out var assetPath);
            f.TryGetValue("name",   out var goName);
            f.TryGetValue("parent", out var parentName);
            f.TryGetValue("px", out var pxs); f.TryGetValue("py", out var pys); f.TryGetValue("pz", out var pzs);
            f.TryGetValue("rx", out var rxs); f.TryGetValue("ry", out var rys); f.TryGetValue("rz", out var rzs);
            f.TryGetValue("sx", out var sxs); f.TryGetValue("sy", out var sys); f.TryGetValue("sz", out var szs);

            if (string.IsNullOrEmpty(assetPath))
            { ResponseHelper.Send(res, 400, "{\"error\":\"'path' is required\"}"); return; }

            Dispatch(res, () =>
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                    return HR.Error(404, $"Asset not found at '{assetPath}'");

                Transform parent = null;
                if (!string.IsNullOrEmpty(parentName))
                {
                    var parentGo = GameObject.Find(parentName);
                    if (parentGo == null)
                    {
                        var newParent = new GameObject(parentName);
                        Undo.RegisterCreatedObjectUndo(newParent, "Tiresias: Create parent " + parentName);
                        MarkDirty(newParent);
                        parent = newParent.transform;
                    }
                    else
                    {
                        parent = parentGo.transform;
                    }
                }

                GameObject go = null;
                try { go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent); }
                catch { }
                if (go == null) go = (GameObject)UnityEngine.Object.Instantiate(prefab, parent);

                if (!string.IsNullOrEmpty(goName)) go.name = goName;
                Undo.RegisterCreatedObjectUndo(go, "Tiresias: Instantiate " + go.name);

                var t = go.transform;
                var pos = t.localPosition;
                var rot = t.localEulerAngles;
                var scl = t.localScale;

                if (TryF(pxs, out float px)) pos.x = px;
                if (TryF(pys, out float py)) pos.y = py;
                if (TryF(pzs, out float pz)) pos.z = pz;
                if (TryF(rxs, out float rx)) rot.x = rx;
                if (TryF(rys, out float ry)) rot.y = ry;
                if (TryF(rzs, out float rz)) rot.z = rz;
                if (TryF(sxs, out float sx)) scl.x = sx;
                if (TryF(sys, out float sy)) scl.y = sy;
                if (TryF(szs, out float sz)) scl.z = sz;

                t.localPosition    = pos;
                t.localEulerAngles = rot;
                t.localScale       = scl;

                MarkDirty(go);
                return HR.Ok(Json.Object(new Dictionary<string, object>
                {
                    ["name"]       = go.name,
                    ["instanceId"] = go.GetInstanceID(),
                }));
            });
        }

        // ── POST /api/assets/materials ────────────────────────────────────────

        public static void CreateMaterial(HttpListenerRequest req, HttpListenerResponse res)
        {
            string body;
            try { body = Json.ReadBody(req); }
            catch { ResponseHelper.Send(res, 400, "{\"error\":\"Could not read request body\"}"); return; }

            // Parse top-level fields from the body
            var f = Json.ParseFlat(body);
            f.TryGetValue("name",     out var matName);
            f.TryGetValue("shader",   out var shaderName);
            f.TryGetValue("savePath", out var savePath);

            if (string.IsNullOrEmpty(shaderName)) shaderName = "Standard";
            if (string.IsNullOrEmpty(savePath))
            { ResponseHelper.Send(res, 400, "{\"error\":\"'savePath' is required\"}"); return; }

            // Parse properties sub-object as raw string
            var propsJson = ExtractJsonSubObject(body, "properties");

            Dispatch(res, () =>
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                    return HR.Error(400, $"Shader '{shaderName}' not found");

                var mat = new Material(shader);
                if (!string.IsNullOrEmpty(matName)) mat.name = matName;

                // Apply properties
                if (!string.IsNullOrEmpty(propsJson))
                    ApplyMaterialProperties(mat, propsJson);

                // Ensure directory exists
                var dir = System.IO.Path.GetDirectoryName(savePath).Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                AssetDatabase.CreateAsset(mat, savePath);
                AssetDatabase.SaveAssets();

                return HR.Ok(Json.Object(new Dictionary<string, object>
                {
                    ["created"] = savePath,
                }));
            });
        }

        /// <summary>
        /// Extracts the raw JSON value of a named key in a JSON object string.
        /// Handles nested objects/arrays by counting braces.
        /// </summary>
        private static string ExtractJsonSubObject(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var searchKey = "\"" + key + "\"";
            int idx = json.IndexOf(searchKey);
            if (idx < 0) return null;

            // Find the ':' after the key
            int i = idx + searchKey.Length;
            while (i < json.Length && json[i] != ':') i++;
            i++; // skip ':'
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length) return null;

            char start = json[i];
            if (start != '{' && start != '[') return null;

            char closeChar = start == '{' ? '}' : ']';
            int depth = 0;
            int valStart = i;
            while (i < json.Length)
            {
                char c = json[i];
                if (c == start) depth++;
                else if (c == closeChar) { depth--; if (depth == 0) return json.Substring(valStart, i - valStart + 1); }
                else if (c == '"') { i++; while (i < json.Length && json[i] != '"') { if (json[i] == '\\') i++; i++; } }
                i++;
            }
            return null;
        }

        /// <summary>
        /// Applies a JSON properties object to a material.
        /// Each key is a shader property name.
        /// Values can be: float literals, string literals (texture path), or {r,g,b,a} objects.
        /// </summary>
        private static void ApplyMaterialProperties(Material mat, string propsJson)
        {
            if (string.IsNullOrEmpty(propsJson) || !propsJson.StartsWith("{")) return;
            // Strip outer braces
            var inner = propsJson.Substring(1, propsJson.Length - 2).Trim();
            int i = 0;
            while (i < inner.Length)
            {
                // Read key
                while (i < inner.Length && inner[i] != '"') i++;
                if (i >= inner.Length) break;
                i++; // skip '"'
                var keyStart = i;
                while (i < inner.Length && inner[i] != '"') i++;
                var propName = inner.Substring(keyStart, i - keyStart);
                i++; // skip closing '"'

                // Skip to ':'
                while (i < inner.Length && inner[i] != ':') i++;
                i++; // skip ':'
                while (i < inner.Length && char.IsWhiteSpace(inner[i])) i++;
                if (i >= inner.Length) break;

                char valueStart = inner[i];

                if (valueStart == '{')
                {
                    // Nested object — read until matching '}'
                    int depth = 0, objStart = i;
                    while (i < inner.Length)
                    {
                        if (inner[i] == '{') depth++;
                        else if (inner[i] == '}') { depth--; if (depth == 0) { i++; break; } }
                        else if (inner[i] == '"') { i++; while (i < inner.Length && inner[i] != '"') { if (inner[i] == '\\') i++; i++; } }
                        i++;
                    }
                    var subObj = inner.Substring(objStart, i - objStart);
                    var subFields = Json.ParseFlat(subObj);
                    if (subFields.TryGetValue("r", out var rs) && subFields.TryGetValue("g", out var gs)
                        && subFields.TryGetValue("b", out var bs))
                    {
                        TryF(rs, out float r); TryF(gs, out float g); TryF(bs, out float b);
                        float a = 1f; if (subFields.TryGetValue("a", out var as2)) TryF(as2, out a);
                        mat.SetColor(propName, new Color(r, g, b, a));
                    }
                }
                else if (valueStart == '"')
                {
                    // String — could be texture path
                    i++; // skip opening '"'
                    var valStart = i;
                    while (i < inner.Length && inner[i] != '"') { if (inner[i] == '\\') i++; i++; }
                    var strVal = inner.Substring(valStart, i - valStart);
                    i++; // skip closing '"'
                    var tex = AssetDatabase.LoadAssetAtPath<Texture>(strVal);
                    if (tex != null) mat.SetTexture(propName, tex);
                }
                else
                {
                    // Numeric
                    var numStart = i;
                    while (i < inner.Length && inner[i] != ',' && inner[i] != '}') i++;
                    var numStr = inner.Substring(numStart, i - numStart).Trim();
                    if (TryF(numStr, out float fv)) mat.SetFloat(propName, fv);
                }

                // Skip past comma
                while (i < inner.Length && (inner[i] == ',' || char.IsWhiteSpace(inner[i]))) i++;
            }
        }

        // ── PUT /api/scene/{name}/materials ───────────────────────────────────

        public static void AssignMaterials(HttpListenerRequest req, HttpListenerResponse res, string gameObjectName)
        {
            string body;
            try { body = Json.ReadBody(req); }
            catch { ResponseHelper.Send(res, 400, "{\"error\":\"Could not read request body\"}"); return; }

            // Extract materialPaths array
            var materialPaths = ExtractStringArray(body, "materialPaths");
            if (materialPaths == null || materialPaths.Count == 0)
            { ResponseHelper.Send(res, 400, "{\"error\":\"'materialPaths' array is required\"}"); return; }

            var f = Json.ParseFlat(body);
            int rendererIndex = 0;
            if (f.TryGetValue("rendererIndex", out var riStr))
                int.TryParse(riStr, out rendererIndex);

            Dispatch(res, () =>
            {
                var go = GameObject.Find(gameObjectName);
                if (go == null) return HR.Error(404, $"GameObject '{gameObjectName}' not found");

                var renderers = go.GetComponents<Renderer>();
                if (renderers.Length == 0) return HR.Error(404, $"No Renderer on '{gameObjectName}'");
                if (rendererIndex < 0 || rendererIndex >= renderers.Length)
                    return HR.Error(400, $"rendererIndex {rendererIndex} out of range (0..{renderers.Length - 1})");

                var renderer = renderers[rendererIndex];
                var materials = new Material[materialPaths.Count];
                for (int idx = 0; idx < materialPaths.Count; idx++)
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPaths[idx]);
                    if (mat == null) return HR.Error(404, $"Material not found at '{materialPaths[idx]}'");
                    materials[idx] = mat;
                }

                var so = new SerializedObject(renderer);
                var matsProp = so.FindProperty("m_Materials");
                matsProp.arraySize = materials.Length;
                for (int idx = 0; idx < materials.Length; idx++)
                    matsProp.GetArrayElementAtIndex(idx).objectReferenceValue = materials[idx];
                so.ApplyModifiedProperties();

                MarkDirty(go);
                return HR.Ok(Json.Object(new Dictionary<string, object>
                {
                    ["assigned"] = new RawJson("[" + string.Join(",", materialPaths.Select(p => Json.Quote(p))) + "]"),
                    ["to"]       = gameObjectName,
                }));
            });
        }

        /// <summary>Extracts a JSON string array value for a named key.</summary>
        private static List<string> ExtractStringArray(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var searchKey = "\"" + key + "\"";
            int idx = json.IndexOf(searchKey);
            if (idx < 0) return null;

            int i = idx + searchKey.Length;
            while (i < json.Length && json[i] != ':') i++;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '[') return null;

            i++; // skip '['
            var result = new List<string>();
            while (i < json.Length)
            {
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i >= json.Length || json[i] == ']') break;
                if (json[i] == '"')
                {
                    i++;
                    var sb = new System.Text.StringBuilder();
                    while (i < json.Length && json[i] != '"')
                    {
                        if (json[i] == '\\') i++;
                        if (i < json.Length) sb.Append(json[i]);
                        i++;
                    }
                    result.Add(sb.ToString());
                    i++; // skip closing '"'
                }
                else i++;
                while (i < json.Length && (json[i] == ',' || char.IsWhiteSpace(json[i]))) i++;
            }
            return result;
        }

        // ── POST /api/assets/prefabs/save ─────────────────────────────────────

        public static void SaveAsPrefab(HttpListenerRequest req, HttpListenerResponse res)
        {
            var f = ParseBody(req, res); if (f == null) return;
            f.TryGetValue("gameObject", out var goName);
            f.TryGetValue("savePath",   out var savePath);

            if (string.IsNullOrEmpty(goName))
            { ResponseHelper.Send(res, 400, "{\"error\":\"'gameObject' is required\"}"); return; }
            if (string.IsNullOrEmpty(savePath))
            { ResponseHelper.Send(res, 400, "{\"error\":\"'savePath' is required\"}"); return; }

            Dispatch(res, () =>
            {
                var go = GameObject.Find(goName);
                if (go == null) return HR.Error(404, $"GameObject '{goName}' not found");

                var dir = System.IO.Path.GetDirectoryName(savePath).Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
                    go, savePath, InteractionMode.UserAction);

                if (prefab == null)
                    return HR.Error(500, $"PrefabUtility.SaveAsPrefabAssetAndConnect returned null for '{goName}'");

                return HR.Ok(Json.Object(new Dictionary<string, object>
                {
                    ["prefab"]     = savePath,
                    ["gameObject"] = goName,
                }));
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Shared helpers
        // ─────────────────────────────────────────────────────────────────────

        private static void Dispatch(HttpListenerResponse res, Func<HR> action)
        {
            try
            {
                var r = MainThreadDispatcher.Execute(action);
                ResponseHelper.Send(res, r.StatusCode, r.Json);
            }
            catch (TimeoutException) { ResponseHelper.Send(res, 504, "{\"error\":\"Main thread operation timed out\"}"); }
            catch (Exception ex)     { ResponseHelper.Send(res, 500, Json.Object(new Dictionary<string, object> { ["error"] = $"Internal error: {ex.Message}" })); }
        }

        private static Dictionary<string, string> ParseBody(HttpListenerRequest req, HttpListenerResponse res)
        {
            try   { return Json.ParseFlat(Json.ReadBody(req)); }
            catch { ResponseHelper.Send(res, 400, "{\"error\":\"Could not read request body\"}"); return null; }
        }

        private static void MarkDirty(GameObject go)
        {
            EditorUtility.SetDirty(go);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static Component FindComponentByTypeName(GameObject go, string typeName)
        {
            foreach (var c in go.GetComponents<Component>())
                if (c != null && (c.GetType().Name == typeName || c.GetType().FullName == typeName)) return c;
            return null;
        }

        private static string[] ListSerializedFields(SerializedObject so)
        {
            var names = new List<string>();
            var iter = so.GetIterator();
            if (iter.NextVisible(true)) do { names.Add(iter.name); } while (iter.NextVisible(false));
            return names.ToArray();
        }

        private static Type FindTypeByName(string typeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(typeName);
                    if (t != null) return t;
                    t = Array.Find(asm.GetTypes(), x => x.Name == typeName);
                    if (t != null) return t;
                }
                catch { continue; }
            }
            return null;
        }

        private static Type FindTypeByFullName(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        private static bool TryF(string s, out float result)
        {
            result = 0f;
            if (s == null) return false;
            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result);
        }

        private static string Vec3(Vector3 v)
        {
            return $"{{\"x\":{v.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}" +
                   $",\"y\":{v.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}" +
                   $",\"z\":{v.z.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}}}";
        }

        private static string EscJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Batch support types
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Captures response data from an internal handler call (for /batch).</summary>
    public class ResponseCapture
    {
        public int StatusCode = 200;
        public string Body = "null";

        public void Send(int code, string json)
        {
            StatusCode = code;
            Body = json;
        }
    }

    /// <summary>Minimal request representation for batch sub-requests.</summary>
    public class BatchRequest
    {
        public string Method { get; }
        public string Path { get; }
        public System.Collections.Specialized.NameValueCollection QueryString { get; }

        public BatchRequest(string method, string path)
        {
            Method = method?.ToUpper() ?? "GET";
            // Split path and query string
            var parts = path.Split(new[] { '?' }, 2);
            Path = parts[0];
            QueryString = new System.Collections.Specialized.NameValueCollection();
            if (parts.Length > 1)
            {
                foreach (var pair in parts[1].Split('&'))
                {
                    var kv = pair.Split(new[] { '=' }, 2);
                    if (kv.Length == 2)
                        QueryString[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
                }
            }
        }
    }
}
