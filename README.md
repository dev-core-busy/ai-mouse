# AI Mouse

Portable Windows 11 tray utility: **hold the right mouse button and drag** to select a
screen region, release, pick a prompt from the popup, and the snippet is sent to a local
AI vision endpoint (Ollama, LM Studio, llama.cpp, or anything OpenAI-compatible).

A plain right-click still behaves exactly as usual: the hook withholds the press, and if
the pointer never moved it replays a genuine right-click via `SendInput`.

## Build

Requires the .NET 8 SDK.

```powershell
.\build.ps1
```

The project also *compiles* on Linux/macOS, which is handy for CI, though of course it
cannot run there:

```bash
dotnet build src/AiMouse/AiMouse.csproj -p:EnableWindowsTargeting=true
```

Result: `publish\AiMouse.exe` — a single self-contained executable. Copy it anywhere
together with `prompts.json` and `settings.json`; no installation, no runtime needed.

> **Native AOT is not used.** WinForms does not support AOT or trimming, so the spec's
> alternative — `PublishSingleFile` + `SelfContained` — is used instead. Expect roughly
> 70–90 MB for the compressed single file.

## Where the UI lives

Windows 11 hides new notification icons behind the `^` chevron next to the clock, so the
tray icon is easy to miss. Everything is therefore reachable from **both** places:

- the **gesture popup** (after a right-click + drag) — prompts, image actions, *Settings…*,
  *Exit AI Mouse*;
- the **tray icon** — *Settings…*, *Edit prompts.json*, *Reload configuration*, *Exit*.

Drag the icon out of the overflow flyout onto the taskbar to keep it visible.

## Configuration

*Settings…* opens a dialog for the whole LLM configuration; saving writes `settings.json`
and applies immediately, without a restart. Prompts are still edited as JSON.

**Test connection** sends a small probe image through the exact path a real capture takes —
same endpoint, same model, same multimodal payload. A green result therefore means captures
will work, not merely that the port is open. Expect the first run to be slow while the
server loads the model. Failures are reported verbatim, which distinguishes the common
cases: connection refused, a 404 naming an unknown model, or a model that has no vision
support.

Both files live **next to the exe** and are optional; built-in defaults apply if absent.
After editing `prompts.json` by hand, use the tray icon → *Reload configuration*.

### `settings.json`

| Key | Meaning |
| --- | --- |
| `Endpoint` | OpenAI-compatible server URL. A base URL is completed automatically: `http://host:8095` and `http://host:8095/v1` both resolve to `…/v1/chat/completions` |
| `Model` | Vision model name as the server reports it |
| `ApiKey` | Optional bearer token (local servers usually ignore it) |
| `SystemPrompt` | Optional system message prepended to every request |
| `TimeoutSeconds` | Request timeout, default `120` |
| `MaxTokens` | Response cap, default `2048` |
| `Temperature` | Sampling temperature, default `0.2` |
| `DragThreshold` | Pixels before the gesture engages, default `8` |
| `CopyResultToClipboard` | Copy the answer as soon as it arrives |

Endpoints that are known to work:

- Ollama — `http://localhost:11434/v1/chat/completions`, model e.g. `llama3.2-vision`
- LM Studio — `http://localhost:1234/v1/chat/completions`, model as shown in the UI

### `prompts.json`

An array of `{ "Title", "Prompt" }` objects; each becomes one row in the popup menu.

## How it works

| Stage | Implementation |
| --- | --- |
| Gesture | `SetWindowsHookEx(WH_MOUSE_LL)`, tracking `WM_RBUTTONDOWN` / `WM_MOUSEMOVE` / `WM_RBUTTONUP` |
| Suppression | **Both** press and release return `1`. No application ever sees half a click, and the system context menu never appears on its own |
| Threshold | Past `DragThreshold` px the gesture keeps the click; below it the click was plain and gets replayed |
| Replay | `SendInput` injects a genuine `RIGHTDOWN` + `RIGHTUP`, tagged with a `dwExtraInfo` marker so the hook lets its own input straight through |
| Guard | Before withholding a press, the foreground window's integrity level is probed; over an elevated window the gesture stands down entirely rather than risk eating the click |
| Overlay | Frameless `WS_EX_TOPMOST` + `WS_EX_TOOLWINDOW` + `WS_EX_NOACTIVATE` + `WS_EX_TRANSPARENT` window across the whole virtual desktop, dimmed with a colour-keyed hole for the selection |
| Capture | `Graphics.CopyFromScreen` in physical pixels (process is Per-Monitor-V2 DPI aware) |
| Transport | PNG in a `MemoryStream`, Base64 data URI in an OpenAI `image_url` content part |

The hook callback itself only records state and posts to the UI thread, so it always
returns well inside the system's `LowLevelHooksTimeout`.

## Known limitations

- **Right-click arrives on button-up.** Because the press is withheld until the release
  decides gesture-or-click, applications that act on `WM_RBUTTONDOWN` react a moment
  later than usual. The click itself is complete and in the correct order.
- **The synthetic press lands at the release point.** No cursor move is injected, so the
  replayed press is up to `DragThreshold` px away from where the user actually pressed —
  below what Windows itself treats as a click.
- **Press-and-hold and right-drag are consumed.** Right-button drag & drop (e.g. dragging
  a file in Explorer with the right button) is the very gesture this tool claims, so it
  is no longer available.
- **Elevated windows.** Neither the gesture nor injection works over a foreground window
  running at higher integrity; `InjectionGuard` detects this and passes all input through
  so the plain right-click keeps working. Start `AiMouse.exe` elevated to use the gesture
  there too.
- **60 ms capture delay.** The overlay is hidden and the desktop is given a moment to
  repaint before `CopyFromScreen` runs, otherwise the dimming would end up in the image.
- Responses are non-streaming; the result window fills in once the model is done.
