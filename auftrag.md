# TASK: Portable Windows 11 Selection & Local AI Vision Tool

## Project Context & Goal
Build a lightweight, portable Windows 11 utility in C# (.NET 8+) that captures a screen snippet when the user performs a **Right-Click + Drag** gesture. Upon releasing the right mouse button, a dynamic context menu must pop up at the cursor location allowing the user to select predefined LLM prompts. The selected prompt and captured screenshot are then sent to a local AI Vision endpoint (e.g., Ollama or OpenAI-compatible server like LM Studio).

The final build MUST be a single, fully self-contained, portable `.exe` file using Native AOT or single-file publishing (no installation required on target systems).

---

## Technical Specifications & Architecture

### 1. Global Mouse Hook (`user32.dll`)
* Implement a low-level mouse hook using `SetWindowsHookEx` with `WH_MOUSE_LL` (`14`).
* Track `WM_RBUTTONDOWN`, `WM_MOUSEMOVE`, and `WM_RBUTTONUP`.
* **Drag Threshold Logic:**
  * Define a movement threshold (e.g., `8` pixels).
  * On `WM_RBUTTONDOWN`, capture initial coordinates `(startX, startY)`.
  * On `WM_MOUSEMOVE` with Right Button down: If $\Delta X > \text{Threshold}$ or $\Delta Y > \text{Threshold}$, mark `isDragging = true`.
  * If `isDragging == true`, swallow the `WM_RBUTTONUP` event (`return (IntPtr)1`) to prevent the native Windows context menu from appearing.
  * If `isDragging == false`, let all mouse events pass through to preserve native right-click functionality.

### 2. Transparent Selection Overlay
* When `isDragging` switches to `true`, instantly open a full-screen, top-most, frameless transparent window across all monitors (`WS_EX_TOPMOST`, `WS_EX_TOOLWINDOW`).
* Draw a visual selection bounding box (or lasso line) dynamically following the mouse path.
* On `WM_RBUTTONUP`, immediately close/hide the overlay and return the bounding coordinates `(minX, minY, width, height)`.

### 3. Screen Capture & Base64 Encoding
* Capture the screen area defined by the bounding box using `Graphics.CopyFromScreen` (supporting High-DPI scaling).
* Convert the captured image to PNG format in memory (`MemoryStream`).
* Encode the byte array to a Base64 string for API transmission.

### 4. Dynamic Context Menu
* Load prompt configurations from a local `prompts.json` file placed in the same directory as the `.exe`.
* Fallback to default prompts if `prompts.json` does not exist:
  ```json
  [
    {
      "Title": "Extract Text (OCR)",
      "Prompt": "Act as an OCR system. Extract all visible text from this image precisely without added commentary."
    },
    {
      "Title": "Describe Image",
      "Prompt": "Describe the contents and key visual elements of this image in detail."
    },
    {
      "Title": "Analyze Error / Code",
      "Prompt": "Analyze the error message or code shown in this snippet and propose a solution."
    }
  ]