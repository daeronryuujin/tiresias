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
        // ── /api/assets/refresh ───────────────────────────────────────────────

        public static void AssetRefresh(HttpListenerRequest req, HttpListenerResponse res)
        {
            MainThreadDispatcher.Execute(() => { AssetDatabase.Refresh(); return 0; });
            ResponseHelper.Send(res, 200, "{\"status\":\"ok\"}");
        }

        // ── /status ───────────────────────────────────────────────────────────

        public static void Status(HttpListenerRequest req, HttpListenerResponse res)
        {
            var json = MainThreadDispatcher.Execute(() => Json.Object(new Dictionary<string, object>
            {
                ["status"]       = "ok",
                ["version"]      = "1.7.0",
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

            var (code, json) = MainThreadDispatcher.Execute(() =>
            {
                var go = GameObject.Find(name);
                if (go == null) return (404, $"{{\"error\":\"No GameObject named '{EscJson(name)}'\"}}");

                var t = go.transform;
                var components = go.GetComponents<Component>()
                    .Where(c => c != null)
                    .Select(c => Json.Object(new Dictionary<string, object>
                    {
                        ["type"]    = c.GetType().Name,
                        ["enabled"] = (c is Behaviour b) ? (object)b.enabled : (object)true,
                    }));

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
            var json = MainThreadDispatcher.Execute(() => Json.Object(new Dictionary<string, object>
            {
                ["isCompiling"]    = EditorApplication.isCompiling,
                ["isUpdating"]     = EditorApplication.isUpdating,
                ["lastCompileAt"]  = _lastCompileTime,
                ["errorCount"]     = _compilerErrors.Count,
                ["warningCount"]   = _lastWarningCount,
            }));
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
            CompilationPipeline.compilationStarted += _ =>
            {
                _compilerErrors.Clear();
                _lastWarningCount = 0;
            };
            CompilationPipeline.assemblyCompilationFinished += (path, messages) =>
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
            return "[" + string.Join(",", _compilerErrors.Select(e => Json.Object(e))) + "]";
        }

        // ── /console/errors ───────────────────────────────────────────────────

        public static void ConsoleErrors(HttpListenerRequest req, HttpListenerResponse res)
        {
            ResponseHelper.Send(res, 200,
                "{\"note\":\"Unity has no public API for reading past console logs. Use /compiler/errors for compilation issues.\",\"entries\":[]}");
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
            f.TryGetValue("componentType", out var componentType);
            if (string.IsNullOrEmpty(componentType))
            { ResponseHelper.Send(res, 400, "{\"error\":\"'componentType' is required\"}"); return; }

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

                if (comp == null) comp = go.AddComponent(type);
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
                var scene = SceneManager.GetActiveScene();
                UnityEngine.Object.DestroyImmediate(go);
                EditorSceneManager.MarkSceneDirty(scene);
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

                UnityEngine.Object.DestroyImmediate(comp);
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

            var so   = new SerializedObject(comp);
            var prop = so.FindProperty(field);
            if (prop == null)
                return HR.Error(400, $"No field '{field}' on '{compType}'. Available: [{string.Join(", ", ListSerializedFields(so))}]");
            if (prop.propertyType != SerializedPropertyType.ObjectReference)
                return HR.Error(400, $"Field '{field}' is '{prop.propertyType}' — use 'valueType' for primitives");

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

            prop.objectReferenceValue = targetObj;
            so.ApplyModifiedProperties();

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
                    default:
                        return HR.Error(400,
                            $"Unknown valueType '{valType}'. Supported: float, int, bool, string, vector3, color");
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
                var t = asm.GetType(typeName);
                if (t != null) return t;
                t = Array.Find(asm.GetTypes(), x => x.Name == typeName);
                if (t != null) return t;
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
    internal class ResponseCapture
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
    internal class BatchRequest
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
