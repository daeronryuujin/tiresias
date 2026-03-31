# Tiresias — Improvement Roadmap & Claude Code Spec

> **Purpose**: This document is the Claude Code task spec for improving Tiresias, the Unity Editor REST API plugin that gives Claude Code live visibility into Unity scene state. `/add` this file when working on the Tiresias repo.

---

## Current State (v1.4.0)

**Architecture:**
```
Assets/Editor/Tiresias/
├── TiresiasServer.cs       — [InitializeOnLoad] entry, HttpListener on background thread, port 7890
├── TiresiasRouter.cs       — URL path → handler dispatch
├── TiresiasHandlers.cs     — All endpoint implementations (Unity Editor API calls on main thread)
├── ResponseHelper.cs       — UTF-8 JSON response writer
├── Json.cs                 — Minimal JSON serializer (no external deps), includes RawJson wrapper
├── TiresiasWindow.cs       — Editor window under Tools > Tiresias
└── TiresiasInstaller.cs    — Auto-copies CLAUDE.md on first load
```

**Working endpoints:**
- `GET /scene/hierarchy` — full scene tree with names, instance IDs, active state
- `GET /scene/gameobject?path=X` or `?instanceId=N` — component list + properties for a single object
- `GET /compiler/errors` — event-based via CompilationPipeline hooks (compilationStarted clears, assemblyCompilationFinished appends)
- `GET /assets/scripts` — lists all .cs files in Assets/
- `GET /assets/prefabs` — lists all .prefab files
- `GET /vrchat/world-descriptor` — VRC_SceneDescriptor config
- `GET /vrchat/udon-programs` — all UdonBehaviour instances with program sources and public variable states (THE killer feature)

**Distribution:**
- VPM package via VCC (GitHub Pages hosts `index.json`)
- GitHub Actions: push to main → auto-release → vpm-release workflow builds zip, uploads to release, updates `index.json`
- Repo: `daeronryuujin/tiresias`

**Known limitations:**
- Cannot click Unity buttons or trigger menu actions
- Cannot add/remove VCC packages
- Cannot see visual scene layout (no screenshots/render output)
- SerializedProperty access works but can be fragile with nested/complex types
- No WebSocket — polling only

---

## Improvement Roadmap

### Priority 1: Reliability & Robustness

#### Task 1.1: Error Handling Hardening

**Problem**: Several handlers can throw if Unity objects are destroyed mid-request, if a component has null references, or if SerializedObject access fails on certain component types.

**File**: `TiresiasHandlers.cs`

**Changes:**
- Wrap ALL handler bodies in try-catch at the handler level (not just individual operations)
- Return structured error JSON: `{"error": true, "message": "...", "endpoint": "/scene/gameobject", "details": "..."}`
- Specifically handle: `MissingReferenceException`, `NullReferenceException`, `ArgumentException` from SerializedProperty access
- Log warnings to Unity console but never crash the server

**Acceptance criteria:**
- Requesting a destroyed GameObject returns `{"error": true, "message": "GameObject not found or destroyed"}` with HTTP 404
- Requesting a component with broken serialized references returns partial data + warnings, not a crash
- Server continues running after any single request error

#### Task 1.2: Thread Safety Audit

**Problem**: HttpListener runs on a background thread but Unity API calls must happen on the main thread. Current implementation uses `EditorApplication.delayCall` to marshal to main thread, but edge cases exist.

**File**: `TiresiasServer.cs`, `TiresiasHandlers.cs`

**Changes:**
- Audit every handler for thread safety
- Ensure ALL Unity API calls (including `SerializedObject` construction) happen inside the main-thread callback
- Add timeout: if main-thread callback doesn't fire within 5 seconds (e.g., Unity is in a heavy import), return HTTP 503 with retry-after header
- Document thread model in code comments

**Acceptance criteria:**
- No `UnityException: ... can only be called from the main thread` errors under any load
- Heavy asset imports don't hang the HTTP server permanently
- Stress test: 10 rapid sequential requests don't crash or deadlock

#### Task 1.3: Startup Robustness

**Problem**: Port 7890 might already be in use (e.g., previous Unity instance didn't shut down cleanly).

**File**: `TiresiasServer.cs`

**Changes:**
- On startup, attempt to bind port 7890
- If bind fails, try ports 7891-7899 sequentially
- Display the actual bound port in the Tiresias editor window
- Write the active port to a file: `Library/Tiresias.port` (so Claude Code can discover it)
- Update CLAUDE.md template to check `Library/Tiresias.port` if default port fails

**Acceptance criteria:**
- Two Unity instances can run simultaneously with Tiresias on different ports
- Claude Code can always discover the correct port
- Editor window shows the currently bound port

---

### Priority 2: New Endpoints

#### Task 2.1: Scene Modification Endpoints (Write API)

**Goal**: Let Claude Code make scene changes directly, not just read state.

**New endpoints:**
```
POST /scene/gameobject/create
  Body: {"name": "NewObject", "parent": "Environment/Lighting", "components": ["MeshRenderer", "BoxCollider"]}
  Returns: {"instanceId": 12345, "path": "Environment/Lighting/NewObject"}

POST /scene/gameobject/delete
  Body: {"path": "Environment/OldObject"} or {"instanceId": 12345}
  Returns: {"deleted": true}

POST /scene/component/add
  Body: {"gameObjectPath": "Stage/Screen", "componentType": "MeshRenderer"}
  Returns: component details

POST /scene/component/set-property
  Body: {"gameObjectPath": "Stage/Screen", "componentType": "MeshRenderer", "property": "material", "value": "Assets/Materials/ScreenMat.mat"}
  Returns: updated property value

POST /scene/gameobject/set-active
  Body: {"path": "UI/Scoreboard", "active": false}

POST /scene/gameobject/reparent
  Body: {"path": "OldParent/MyObject", "newParent": "NewParent"}
```

**Safety:**
- All write operations go through `Undo.RegisterCompleteObjectUndo()` so they can be undone in Unity
- Mark scene dirty after modifications (`EditorSceneManager.MarkSceneDirty`)
- Return the full updated state of the modified object in the response

**Acceptance criteria:**
- Claude Code can create a GameObject, add components, set properties, and verify the result — all without human interaction
- All changes are undo-able via Ctrl+Z in Unity
- Scene is marked dirty (triggers save prompt)

#### Task 2.2: Asset Database Endpoints

**New endpoints:**
```
GET /assets/search?query=AudioLink&type=Material
  Returns: list of matching assets with paths and types

GET /assets/import-status
  Returns: {"isImporting": false, "progress": 1.0}

POST /assets/refresh
  Triggers: AssetDatabase.Refresh()

GET /assets/dependencies?path=Assets/Prefabs/MyPrefab.prefab
  Returns: list of all assets this prefab depends on
```

**Acceptance criteria:**
- Claude Code can search for assets by name/type before referencing them in scripts
- Claude Code can trigger an asset refresh after writing new files and wait for import to complete
- Dependency queries work for prefabs, materials, and scripts

#### Task 2.3: Build & Validation Endpoint

**New endpoints:**
```
GET /build/validate
  Returns: VRChat SDK validation results (if SDK is present)
  Checks: missing references, performance stats, banned components

GET /build/stats
  Returns: {"triangles": 150000, "materials": 23, "textures": 15, "meshRenderers": 42}
```

**Acceptance criteria:**
- Claude Code can check if the world will pass VRChat's upload validation before asking the user to build
- Performance stats help Claude Code make optimization decisions

---

### Priority 3: Developer Experience

#### Task 3.1: Live Reload Notification

**Goal**: When scripts recompile successfully, Tiresias emits a notification that Claude Code can poll for.

**New endpoint:**
```
GET /compiler/status
  Returns: {"compiling": false, "lastCompileTime": "2026-03-30T12:34:56", "errors": 0, "warnings": 3}
```

**Claude Code workflow:**
1. Write/modify a script
2. Poll `/compiler/status` until `compiling` is false
3. Check `/compiler/errors` for any issues
4. Proceed or fix

#### Task 3.2: Batch Operations

**New endpoint:**
```
POST /batch
  Body: [
    {"method": "GET", "path": "/scene/hierarchy"},
    {"method": "GET", "path": "/compiler/errors"},
    {"method": "GET", "path": "/assets/scripts"}
  ]
  Returns: array of responses
```

**Purpose**: Reduce round-trips. Claude Code often needs hierarchy + compiler status + script list in one go.

#### Task 3.3: CLAUDE.md Auto-Generation

**Goal**: Tiresias generates a project-specific CLAUDE.md snippet based on what's actually in the scene.

**New endpoint:**
```
GET /meta/claude-md-snippet
  Returns: Markdown text block documenting:
    - All current Tiresias endpoints
    - Current scene hierarchy summary
    - Installed packages (UdonSharp, AudioLink, ProTV, etc.)
    - Common project-specific patterns detected
```

---

### Priority 4: Future / Exploration

#### Task 4.1: WebSocket Event Stream

Replace polling with push notifications for: compilation events, scene hierarchy changes, play mode enter/exit.

#### Task 4.2: Screenshot Endpoint

```
GET /scene/screenshot?width=800&height=600&camera=SceneView
```
Returns a PNG of the current scene view or game view. Would let Claude Code "see" the visual layout.

#### Task 4.3: Prefab Editing

```
POST /assets/prefab/modify
  Body: {"path": "Assets/Prefabs/VoteButton.prefab", "operations": [...]}
```
Edit prefab contents without opening them in the scene.

---

## Repo & Release Workflow

**Branch strategy**: `main` only (single developer)
**Release flow**: Bump `version` in `package.json` → push to `main` → `auto-release.yml` creates GitHub release → `vpm-release.yml` builds zip, uploads, updates `index.json`
**Testing**: Manual testing in Unity. Consider adding a basic test scene that exercises all endpoints.

## DO NOT

- Add external NuGet/npm dependencies — Tiresias must be zero-dependency
- Use `async/await` in Unity Editor code — use coroutine-style patterns or `EditorApplication.delayCall`
- Break backward compatibility on existing endpoints without version bumping
- Add authentication — Tiresias is localhost-only by design
- Use `JsonUtility` for serialization — it can't handle dictionaries. Use the custom `Json.cs`
