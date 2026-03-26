# Tiresias

> *The blind prophet who sees everything.*

A lightweight Unity Editor plugin that exposes your scene state, assets, and compiler status as a local REST API — built for AI code assistant integration (Claude Code, Cursor, etc.).

---

## What It Does

Tiresias runs an HTTP server on `localhost:7890` inside the Unity Editor. Any tool that can make HTTP requests can now ask Unity questions:

- What GameObjects are in the scene and what components do they have?
- Are there any compiler errors right now?
- What scripts and prefabs exist in the project?

This gives Claude Code the context it needs to write correct code the first time instead of guessing.

---

## Installation

### VCC (recommended)

1. In VRChat Creator Companion, go to **Settings → Packages → Add Repository**
2. Paste: `https://daeronryuujin.github.io/tiresias/index.json`
3. Add **Tiresias** to your project from the package list

CLAUDE.md will be automatically copied to your project root on first compile.

### Manual

1. Create `Assets/Editor/Tiresias/` in your Unity project
2. Copy all `.cs` files from this repo into that folder
3. Copy `CLAUDE.md` to your project root
4. Unity will auto-compile and the server will start

---

## Usage

The server starts automatically when Unity opens. You can manage it via **Tools → Tiresias → Open Panel**.

Base URL: `http://localhost:7890`

### Endpoints

| Endpoint | Description |
|---|---|
| `GET /status` | Server health, Unity version, play/compile state |
| `GET /scene` | Active scene info |
| `GET /scene/hierarchy?depth=N` | Full scene tree (default depth 3) |
| `GET /scene/object?name=<name>` | Component detail for one GameObject |
| `GET /scene/selected` | Currently selected objects |
| `GET /assets/scripts` | All .cs files under Assets/ |
| `GET /assets/prefabs` | All prefabs under Assets/ |
| `GET /compiler/status` | isCompiling, isUpdating |
| `GET /compiler/errors` | Current compilation errors with file/line |

---

## Claude Code Integration

Copy `CLAUDE.md` from this repo into the root of your Unity project. Claude Code reads this automatically and will:

1. Query Tiresias before writing code to understand the scene
2. Check compiler errors before and after making changes
3. Poll for compile completion before declaring success

---

## Architecture

- `TiresiasServer.cs` — `[InitializeOnLoad]` entry point, manages the `HttpListener` on a background thread
- `TiresiasRouter.cs` — URL path → handler dispatch
- `TiresiasHandlers.cs` — All endpoint implementations (Unity Editor API calls)
- `ResponseHelper.cs` — UTF-8 JSON response writer
- `Json.cs` — Minimal JSON serializer (no external dependencies)
- `TiresiasWindow.cs` — Editor window under Tools menu

---

## Requirements

- Unity 2019.4+ (LTS recommended)
- No external packages required
- Works alongside VRChat SDK, UdonSharp, Coplay MCP, and UnityMCP-VRC

---

## License

MIT
