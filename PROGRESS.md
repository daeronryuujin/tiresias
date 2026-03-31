# Tiresias v1.9.0 — Blender Pipeline Endpoints

## Summary

Added 5 new endpoints for Blender-to-Unity asset pipeline integration. All handlers in `Editor/TiresiasHandlers.cs`, routes in `Editor/TiresiasRouter.cs`.

## New Endpoints

### GET /assets/import-status
Checks whether a Unity asset imported successfully.
- Query param: `?path=Assets/Models/Generated/bar_stool.glb`
- Returns `{"exists": true, "type": "Model", "importer": "ModelImporter", "subAssets": [...]}` or `{"exists": false}`
- Dispatches to main thread; uses `AssetDatabase.LoadAssetAtPath`, `AssetImporter.GetAtPath`, `AssetDatabase.LoadAllAssetsAtPath`

### POST /api/assets/instantiate
Places an imported GLB/FBX model in the scene.
- Body: path, name, parent, position (px/py/pz), rotation (rx/ry/rz), scale (sx/sy/sz)
- Tries `PrefabUtility.InstantiatePrefab` first, falls back to `Object.Instantiate` if it throws
- Creates missing parent GameObjects automatically
- Registers undo, marks scene dirty
- Returns `{"name": "...", "instanceId": ...}`

### POST /api/assets/materials
Creates a new Material asset on disk.
- Body: name, shader, savePath, properties (nested JSON object)
- Properties support: `{"r":..,"g":..,"b":..,"a":..}` dicts (SetColor), float/number literals (SetFloat), string values (SetTexture via asset path)
- Creates directory if it doesn't exist
- Returns `{"created": "Assets/Materials/..."}`

### PUT /api/scene/{name}/materials
Assigns material(s) to a Renderer on a scene GameObject.
- Body: `materialPaths` (string array), `rendererIndex` (int, default 0)
- Uses SerializedObject for proper undo support
- Returns `{"assigned": [...], "to": "GameObjectName"}`

### POST /api/assets/prefabs/save
Saves a scene GameObject as a prefab asset and connects it.
- Body: gameObject (name), savePath
- Creates directory if needed
- Uses `PrefabUtility.SaveAsPrefabAssetAndConnect` with `InteractionMode.UserAction`
- Returns `{"prefab": "...", "gameObject": "..."}`

## Implementation Notes

- `/api/assets/prefabs/save` is matched in `TryHandleParameterizedRoute` BEFORE the `/api/assets/prefabs/{path}` catch-all to avoid the route being swallowed.
- `Json.ParseFlat` only handles flat string values, so `CreateMaterial` reads the body raw and uses two custom helpers:
  - `ExtractJsonSubObject(json, key)` — extracts a nested `{...}` or `[...]` value by key using brace counting
  - `ApplyMaterialProperties(mat, propsJson)` — iterates the properties object, detecting color dicts, float literals, and string texture paths
  - `ExtractStringArray(json, key)` — extracts a `["...", ...]` JSON array value by key (used by `AssignMaterials`)
- All 5 handlers follow the existing `Dispatch(res, () => HR.Ok/HR.Error)` pattern for main-thread execution.

## Version Bump

`package.json` version: `1.8.0` → `1.9.0`
Version string in `TiresiasHandlers.cs` (Status + batch): `1.8.0` → `1.9.0`
