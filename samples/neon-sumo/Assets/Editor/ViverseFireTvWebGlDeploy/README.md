# Fire TV WebGL deploy (Viverse)

This folder was copied from the **Viverse_Firestick_App** repo (`tools/unity-firetv-webgl-deploy/Editor/`).

## Requirements

- Unity **WebGL** module; **Development** build for profiling.
- **adb** on `PATH` or enter full path in the window (EditorPrefs).
- Fire TV / Cube with **wireless debugging** (`adb connect <ip>:5555`).
- Debug Viverse APK installed: `com.viverse.firetv` (build `installDebug` from the Android project).

## Menu

**Viverse → Fire TV → Deploy WebGL build to Fire TV…**

1. **Browse** to your WebGL output folder (contains `index.html`), e.g. `Builds` after a WebGL build.
2. Set **device serial**, **PC LAN IP**, **websockify listen port** (must match Unity’s “Proxying from :PORT” line, often **35020** in deploy prefs).
3. **Patch index.html, push to device, launch profiler host** — patches `--player-connection-ip=127.0.0.1:19000`, pushes to the device, starts `LocalhostRelaySpikeActivity`.

### Standalone Profiler vs Editor (important)

Unity’s **websockify** forwards WebSocket traffic to a **TCP port on your PC**. That **target** must be the **standalone Profiler** player-connection port, **not** the main Editor’s port.

Evidence from `Secondary-profiler.log` (same machine):

- Standalone Profiler advertises: `[Port] 55000` and `[Id] Profiler-WindowsEditor(...)`.
- Websockify was: `Proxying from :35020 to localhost:34999` — **34999 ≠ 55000**, so the device relay hit the **wrong** listener.
- The active session then showed `Platform: WindowsEditor` at `192.168.1.212:55001`, i.e. the **Editor**, not the standalone Profiler process.

**Fix:** Unity does not expose that TCP target in the UI; use the repo script **`Tools/RunWebsockifyStandaloneProfiler.bat`** (or `.ps1`) so WebSocket **`ListenPort`** (default **35020**) forwards to **`localhost:<Profiler [Port]>`** (default **55000** — check `Secondary-profiler.log`). Stop Unity’s own `[websockify wrapper]` first if it already binds **35020**. Match **Websockify port** in this deploy window to **ListenPort**.

## Full docs

In the Viverse Android repo: `docs/fire-tv-webgl-profiler.md`.
