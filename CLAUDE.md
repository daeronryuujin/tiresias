# Tiresias — Claude Code Integration Reference

## What Tiresias Is

Tiresias is a lightweight Unity Editor plugin that runs a local HTTP REST API on **`http://localhost:7890`**.
It gives you live read/write access to the Unity scene, asset list, and compiler state — use it to orient yourself before writing or modifying code, and to directly mutate the scene without touching `.unity` files.

## When to Use It

- Before modifying any script: check `/compiler/errors` to make sure the project is clean first.
- Before placing or referencing a GameObject: query `/scene/hierarchy` to confirm it exists and get its exact name.
- Before writing a script that references components: query `/scene/object?name=<NAME>` to see what components are already present.
- If something breaks after you make a change: check `/compiler/errors` again for new errors.
- To add GameObjects, components, set fields, or wire up references: use the write endpoints below instead of editing `.unity` files by hand.

## Endpoint Reference

### Read (v1.0+)

| Endpoint | Method | Description |
|---|---|---|
| `/status` | GET | Server status, Unity version, isPlaying, isCompiling, port |
| `/scene` | GET | Active scene name, path, dirty state |
| `/scene/hierarchy` | GET | Full scene tree. Optional `?depth=N` (default 3) |
| `/scene/object` | GET | Component detail. `?name=<Name>`. Add `?detail=full` for all serialized field values |
| `/scene/selected` | GET | Names of currently selected GameObjects |
| `/scenes` | GET | List all .unity scene files in the project |
| `/assets/scripts` | GET | All .cs file paths under Assets/ |
| `/assets/prefabs` | GET | All prefab paths under Assets/ |
| `/assets/search` | GET | Search assets. `?query=AudioLink&type=Material&folder=Assets` |
| `/assets/dependencies` | GET | Asset dependency graph. `?path=Assets/Prefabs/X.prefab` |
| `/assets/import-status` | GET | Check if an asset imported successfully. `?path=Assets/Models/bar_stool.glb` |
| `/compiler/status` | GET | isCompiling, isUpdating, lastCompileAt, errorCount, warningCount |
| `/compiler/errors` | GET | Array of {file, line, message} for compile errors |
| `/console/logs` | GET | Console log buffer (last 200). `?type=Error&since=<ISO timestamp>&clear=true` |
| `/build/stats` | GET | Scene performance stats: triangles, vertices, materials, textures, lights |
| `/batch` | POST | Execute multiple requests in one round-trip (max 10) |

### Port Discovery (v1.7+)

Tiresias tries ports 7890-7899. The actual port is written to `Library/Tiresias.port`.

```bash
# Auto-discover port
PORT=$(cat Library/Tiresias.port 2>/dev/null || echo 7890)
curl http://localhost:$PORT/status
```

### Write (v1.6+)

| Endpoint | Method | Body | Description |
|---|---|---|---|
| `/api/scene/objects` | POST | `{"name":"Foo","parent":"ParentName"}` | Create a new empty GameObject. `parent` is optional. |
| `/api/scene/{name}` | DELETE | — | Delete a GameObject by name. |
| `/api/scene/{name}/active` | PUT | `{"active":true}` | Set active/inactive. |
| `/api/scene/{name}/parent` | PUT | `{"parent":"NewParent"}` | Reparent (omit or null `parent` to unparent). |
| `/api/scene/{name}/transform` | PUT | `{"px":0,"py":1,"pz":0,"rx":0,"ry":0,"rz":0,"sx":1,"sy":1,"sz":1}` | Set local position/rotation/scale. All fields optional. |
| `/api/scene/{name}/components` | POST | `{"type":"MyScript"}` | Add a component by type name. Handles UdonSharp behaviours automatically. |
| `/api/scene/{name}/components/{type}` | DELETE | — | Remove a component by type name. |
| `/api/scene/{name}/components/{type}/fields/{field}` | PUT | see below | Set a serialized field (reference or value). |
| `/api/assets/prefabs/{path}` | POST | `{"parent":"ParentName","name":"Override"}` | Instantiate a prefab from Assets/. `parent` and `name` are optional. |
| `/api/assets/refresh` | POST | — | Trigger `AssetDatabase.Refresh()` — picks up new files without Ctrl+R. |

### Blender Pipeline (v1.9+)

| Endpoint | Method | Body | Description |
|---|---|---|---|
| `/assets/import-status` | GET | — | Check if an asset (GLB/FBX) imported successfully. Returns `exists`, `type`, `importer`, `subAssets[]`. |
| `/api/assets/instantiate` | POST | `{"path":"Assets/Models/bar_stool.glb","name":"bar_stool","parent":"GeneratedProps","px":0,"py":0,"pz":0,"rx":0,"ry":0,"rz":0,"sx":1,"sy":1,"sz":1}` | Place an imported model in the scene. Creates parent if missing. |
| `/api/assets/materials` | POST | `{"name":"BarStool_Mat","shader":"Standard","savePath":"Assets/Materials/Generated/BarStool_Mat.mat","properties":{"_Color":{"r":0.8,"g":0.2,"b":0.1,"a":1.0},"_Metallic":0.3}}` | Create and save a Material asset. Properties support Color (r/g/b/a dict), float, and texture path (string). |
| `/api/scene/{name}/materials` | PUT | `{"materialPaths":["Assets/Materials/Generated/BarStool_Mat.mat"],"rendererIndex":0}` | Assign material(s) to a Renderer on a scene GameObject. |
| `/api/assets/prefabs/save` | POST | `{"gameObject":"bar_stool","savePath":"Assets/Prefabs/Generated/bar_stool.prefab"}` | Save a scene GameObject as a prefab asset and connect it. |

### Scene & Editor Control (v1.8+)

| Endpoint | Method | Body | Description |
|---|---|---|---|
| `/api/scene/save` | POST | — | Save the active scene (Ctrl+S equivalent). |
| `/api/scene/open` | POST | `{"path":"Assets/Scenes/X.unity"}` | Open a scene. Optional `"save":"false"` to skip save prompt. |
| `/api/editor/play` | POST | — | Enter play mode. Poll `/status` until `isPlaying` is true. |
| `/api/editor/stop` | POST | — | Exit play mode. Poll `/status` until `isPlaying` is false. |
| `/api/editor/undo` | POST | — | Undo the last operation (Ctrl+Z equivalent). |
| `/api/editor/redo` | POST | — | Redo the last undone operation (Ctrl+Y equivalent). |

All write operations are registered with Unity's Undo system — every scene change is undoable.

#### SetField body formats

```json
// Object reference (GameObject)
{"referenceType":"gameObject","targetGameObjectName":"VideoPlayer"}

// Object reference (Component)
{"referenceType":"component","targetGameObjectName":"VideoPlayer","targetComponentType":"MeshRenderer"}

// Value types
{"valueType":"float","value":"1.5"}
{"valueType":"int","value":"42"}
{"valueType":"bool","value":"true"}
{"valueType":"string","value":"Hello"}
{"valueType":"vector3","x":"0","y":"1","z":"0"}
{"valueType":"color","r":"1","g":"0.5","b":"0","a":"1"}
```

## Usage Pattern (bash)

```bash
# Is the project compiling cleanly?
curl http://localhost:7890/compiler/errors

# What's in the scene?
curl http://localhost:7890/scene/hierarchy

# What components does VideoPlayer have?
curl "http://localhost:7890/scene/object?name=VideoPlayer"

# Full field introspection — see all serialized values
curl "http://localhost:7890/scene/object?name=VideoPlayer&detail=full"

# List all scenes in the project
curl http://localhost:7890/scenes

# Open a different scene
curl -X POST http://localhost:7890/api/scene/open \
  -H "Content-Type: application/json" \
  -d '{"path":"Assets/Scenes/MyWorld.unity"}'

# Create a new empty GameObject under UI
curl -X POST http://localhost:7890/api/scene/objects \
  -H "Content-Type: application/json" \
  -d '{"name":"ScorePanel","parent":"UI"}'

# Add a UdonSharp component to it
curl -X POST "http://localhost:7890/api/scene/ScorePanel/components" \
  -H "Content-Type: application/json" \
  -d '{"type":"ScoreManager"}'

# Wire a field reference
curl -X PUT "http://localhost:7890/api/scene/ScorePanel/components/ScoreManager/fields/videoPlayer" \
  -H "Content-Type: application/json" \
  -d '{"referenceType":"component","targetGameObjectName":"TV","targetComponentType":"TVManager"}'

# Set a value field
curl -X PUT "http://localhost:7890/api/scene/ScorePanel/components/ScoreManager/fields/maxScore" \
  -H "Content-Type: application/json" \
  -d '{"valueType":"int","value":"100"}'

# Instantiate a prefab
curl -X POST "http://localhost:7890/api/assets/prefabs/Prefabs%2FScoreBoard" \
  -H "Content-Type: application/json" \
  -d '{"parent":"UI"}'

# Delete a GameObject
curl -X DELETE "http://localhost:7890/api/scene/OldPanel"

# Save the scene
curl -X POST http://localhost:7890/api/scene/save

# Enter play mode for testing
curl -X POST http://localhost:7890/api/editor/play

# Check console logs during play mode
curl "http://localhost:7890/console/logs?type=Error"

# Exit play mode
curl -X POST http://localhost:7890/api/editor/stop

# Undo a mistake
curl -X POST http://localhost:7890/api/editor/undo
```

## Hard Rules for This Project

**UdonSharp Constraints (do not violate):**
- All world scripts inherit from `UdonSharpBehaviour`, NOT `MonoBehaviour`
- No generics — use arrays, never `List<T>`
- No interfaces
- No file I/O
- External HTTP only via `VRCStringDownloader` + `IVRCStringDownload` callback
- Dynamic URLs only via `VRCUrlInputField` or `VRCStringDownloader` result — never construct `VRCUrl` at runtime
- Network-synced fields require `[UdonSynced]`
- Every UdonSharp `.cs` file needs a matching `.asset` program file (generated by Unity on compile)
- Use `SendCustomEvent(string)` for same-object event calls, `SendCustomNetworkEvent` for networked calls

**Project Structure:**
- Scripts → `Assets/Scripts/`
- Prefabs → `Assets/Prefabs/`
- Do not edit `.unity` scene files by hand (GUID hell)

**Workflow:**
1. Check `/compiler/errors` before starting any work
2. Write / modify scripts
3. Wait for Unity to recompile (poll `/compiler/status` until `isCompiling` is false)
4. Check `/compiler/errors` again
5. Do not proceed if there are errors

---

## Development Reference (for working ON Tiresias itself)

### Repository & Versioning

- **GitHub**: `daeronryuujin/tiresias`
- **Package ID**: `com.daeronryuujin.tiresias`
- **Current version**: 1.9.0 (see `package.json` → `version` field)
- **Default branch**: `main` (note: local repo also has `master` — always push to `main`)
- **VPM listing URL**: `https://daeronryuujin.github.io/tiresias/index.json`

### File Layout

```
tiresias/
├── Editor/                      # All C# source — Unity Editor-only
│   ├── TiresiasServer.cs        # [InitializeOnLoad] entry point, HttpListener on background thread
│   ├── TiresiasRouter.cs        # URL path → handler dispatch, CORS headers
│   ├── TiresiasHandlers.cs      # All endpoint implementations (Unity Editor API calls)
│   ├── ResponseHelper.cs        # UTF-8 JSON response writer
│   ├── Json.cs                  # Minimal JSON serializer (no external deps)
│   ├── TiresiasWindow.cs        # Editor window under Tools → Tiresias → Open Panel
│   ├── TiresiasInstaller.cs     # Auto-copies CLAUDE.md to project root on first compile
│   └── MainThreadDispatcher.cs  # [InitializeOnLoad] queue-drain for main-thread write ops
├── package.json                 # VPM/UPM package manifest (version source of truth)
├── index.json                   # VPM repository listing (auto-updated by CI)
├── README.md                    # User-facing docs
├── CLAUDE.md                    # This file — included in zip, auto-installed by TiresiasInstaller
└── .github/workflows/
    └── vpm-release.yml          # Single CI workflow: release + VPM index update
```

### Architecture Notes

- **TiresiasServer.cs**: `[InitializeOnLoad]` static constructor calls `Start()`. Runs `HttpListener` on port 7890 in a background thread. Stops on `EditorApplication.quitting`.
- **TiresiasRouter.cs**: Simple `switch` on `req.Url.AbsolutePath.TrimEnd('/')`. Adds CORS headers. Routes to static methods on `TiresiasHandlers`.
- **TiresiasHandlers.cs**: Each endpoint is a static method taking `(HttpListenerRequest, HttpListenerResponse)`. The `/compiler/errors` endpoint uses event hooks (`CompilationPipeline.compilationStarted` + `assemblyCompilationFinished`) with `[InitializeOnLoadMethod]` — do NOT use `CompilationPipeline.GetAssemblies()` for this (its `Assembly` objects don't have a `compilerMessages` property).
- **TiresiasInstaller.cs**: Copies `CLAUDE.md` from `Packages/com.daeronryuujin.tiresias/CLAUDE.md` to project root. Uses `SessionState` to run once per editor session. Menu item at `Tools/Tiresias/Reinstall CLAUDE.md` for manual re-copy.
- **Json.cs**: Hand-rolled serializer. `Json.Object(Dictionary<string,object>)` and `Json.Array(List<string>)` and `Json.Quote(string)`. Also has `Json.ReadBody(req)` and `Json.ParseFlat(json)` for reading flat-string-valued JSON request bodies.
- **MainThreadDispatcher.cs**: `[InitializeOnLoad]` class that hooks `EditorApplication.update`. Provides `Execute<T>(Func<T>)` to run code on Unity's main thread from background HTTP handler threads. Required for any write operation (SerializedObject, scene mutation). Uses `ConcurrentQueue` + `ManualResetEventSlim` for cross-thread signaling with a 5-second timeout.

### CI/CD: vpm-release.yml

Single workflow triggered on push to `main` (with `paths-ignore: ['index.json']` to prevent loops):

1. Reads version from `package.json`
2. Checks if GitHub release for that tag already exists (skips if so)
3. Builds a clean zip with `package.json` at root (copies `Editor/`, `package.json`, `README.md`, `CLAUDE.md`)
4. Computes SHA256 (uppercase hex)
5. Creates GitHub release + uploads zip via `gh release create`
6. Updates `index.json` with new version entry via `jq`
7. Commits and pushes `index.json` back to `main`

**Key gotcha**: `GITHUB_TOKEN`-created releases do NOT trigger other GitHub Actions workflows. That's why this is a single combined workflow instead of separate auto-release + release workflows.

### Release Process

To cut a new release:
1. Bump `version` in `package.json`
2. Commit and push to `main`
3. The workflow handles everything else automatically (release, zip, SHA, index.json update)

### Important Gotchas

- **Tiresias Editor code uses `List<T>` and LINQ freely** — UdonSharp constraints only apply to *world scripts* (scripts that inherit from `UdonSharpBehaviour`). Editor code runs in the Unity Editor, not in VRChat, so standard C# is fine.
- **`index.json`** is auto-maintained by CI. Don't manually edit version entries — they'll be overwritten or duplicated.
- **The zip must have `package.json` at its root** (not nested in a subfolder) for VPM/UPM compatibility.
- **`/console/logs`** captures logs in real time via `Application.logMessageReceived` — a ring buffer of the last 200 entries. Replaces the old `/console/errors` stub.
- **Port 7890** is hardcoded in `TiresiasServer.cs` (`PREFIX` constant).

### PR Workflow

All PRs in previous sessions were created against `main` from branch `claude/setup-github-actions-workflow-dXPIW`. User has authorized creating and merging PRs and incrementing versions for releases. Five PRs merged so far (#1-#5).
