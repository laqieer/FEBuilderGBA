#!/usr/bin/env python3
# /// script
# dependencies = ["mss>=9.0.0", "pyautogui>=0.9.54", "Pillow>=10.0.0", "pywin32>=306"]
# requires-python = ">=3.10"
# ///
"""MCP server for computer use — gives Claude Code screenshot + mouse/keyboard control.

Uses a minimal JSON-RPC implementation (no heavy mcp SDK) so startup takes < 1 s.
Heavy deps (pyautogui, mss, Pillow, win32gui) are lazy-imported on first tool call.
"""

import base64
import io
import json
import sys
import time
from typing import Any

# ---------------------------------------------------------------------------
# Lazy-loaded modules
# ---------------------------------------------------------------------------
_mss = None
_pyautogui = None
_Image = None
_win32gui = None
_win32con = None
_HAS_WIN32: bool | None = None
_dpi_set = False


def _ensure_deps():
    """Import heavy deps on first call."""
    global _mss, _pyautogui, _Image, _win32gui, _win32con, _HAS_WIN32, _dpi_set
    if _pyautogui is not None:
        return

    if not _dpi_set and sys.platform == "win32":
        import ctypes
        try:
            ctypes.windll.shcore.SetProcessDpiAwareness(1)
        except Exception:
            try:
                ctypes.windll.user32.SetProcessDPIAware()
            except Exception:
                pass
        _dpi_set = True

    import mss as _mss_mod
    import mss.tools  # noqa: F401
    import pyautogui as _pag
    from PIL import Image as _Img

    _mss = _mss_mod
    _pyautogui = _pag
    _Image = _Img

    import os
    _pyautogui.FAILSAFE = os.environ.get("MCP_FAILSAFE", "0") != "0"
    _pyautogui.PAUSE = 0.05

    try:
        import win32gui as _wg
        import win32con as _wc
        _win32gui = _wg
        _win32con = _wc
        _HAS_WIN32 = True
    except ImportError:
        _HAS_WIN32 = False


def _has_win32() -> bool:
    global _HAS_WIN32
    if _HAS_WIN32 is None:
        try:
            import win32gui  # noqa: F401
            import win32con  # noqa: F401
            _HAS_WIN32 = True
        except ImportError:
            _HAS_WIN32 = False
    return _HAS_WIN32


# ---------------------------------------------------------------------------
# Tool helpers
# ---------------------------------------------------------------------------

def _take_screenshot(
    region: dict | None = None,
    max_width: int = 1280,
    max_height: int = 800,
) -> tuple[str, int, int]:
    with _mss.mss() as sct:
        if region:
            monitor = {
                "left": region["x"], "top": region["y"],
                "width": region["width"], "height": region["height"],
            }
        else:
            monitor = sct.monitors[1]

        raw = sct.grab(monitor)
        img = _Image.frombytes("RGB", raw.size, raw.rgb)

        w, h = img.size
        scale = min(max_width / w, max_height / h, 1.0)
        if scale < 1.0:
            img = img.resize((int(w * scale), int(h * scale)), _Image.LANCZOS)

        buf = io.BytesIO()
        img.save(buf, format="PNG", optimize=True)
        b64 = base64.standard_b64encode(buf.getvalue()).decode("ascii")
        return b64, img.size[0], img.size[1]


def _type_text(text: str, interval: float = 0.02):
    if text.isascii():
        _pyautogui.typewrite(text, interval=interval)
    else:
        import subprocess
        process = subprocess.Popen(
            ["cmd", "/c", "clip"], stdin=subprocess.PIPE
        )
        process.communicate(text.encode("utf-16le"))
        if process.returncode != 0:
            raise RuntimeError(f"clip failed with exit code {process.returncode}")
        _pyautogui.hotkey("ctrl", "v")
        time.sleep(0.1)


# ---------------------------------------------------------------------------
# Tool definitions (plain dicts — no SDK needed)
# ---------------------------------------------------------------------------

_BASE_TOOLS = [
    {
        "name": "screenshot",
        "description": "Take a screenshot of the screen or a region. Returns the image.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "x": {"type": "integer", "description": "Left edge of capture region"},
                "y": {"type": "integer", "description": "Top edge of capture region"},
                "width": {"type": "integer", "description": "Region width"},
                "height": {"type": "integer", "description": "Region height"},
                "max_width": {"type": "integer", "default": 1280,
                              "description": "Max output width (default 1280)"},
                "max_height": {"type": "integer", "default": 800,
                               "description": "Max output height (default 800)"},
            },
        },
    },
    {
        "name": "click",
        "description": "Click at screen coordinates.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "x": {"type": "integer"}, "y": {"type": "integer"},
                "button": {"type": "string", "enum": ["left", "right", "middle"],
                           "default": "left"},
                "clicks": {"type": "integer", "default": 1,
                           "description": "1=single, 2=double"},
            },
            "required": ["x", "y"],
        },
    },
    {
        "name": "type_text",
        "description": "Type text via keyboard. Supports ASCII and Unicode.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "text": {"type": "string"},
                "interval": {"type": "number", "default": 0.02,
                             "description": "Seconds between keystrokes"},
            },
            "required": ["text"],
        },
    },
    {
        "name": "key_press",
        "description": "Press key or combo. Examples: 'enter', 'ctrl+c', 'alt+f4', 'tab'.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "keys": {"type": "string",
                         "description": "Key combo, e.g. 'ctrl+s', 'alt+tab', 'enter'"},
            },
            "required": ["keys"],
        },
    },
    {
        "name": "mouse_move",
        "description": "Move cursor to coordinates.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "x": {"type": "integer"}, "y": {"type": "integer"},
            },
            "required": ["x", "y"],
        },
    },
    {
        "name": "scroll",
        "description": "Scroll mouse wheel. Positive = up, negative = down.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "amount": {"type": "integer", "description": "Scroll clicks"},
                "x": {"type": "integer", "description": "Optional X position"},
                "y": {"type": "integer", "description": "Optional Y position"},
            },
            "required": ["amount"],
        },
    },
    {
        "name": "drag",
        "description": "Click and drag from one point to another.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "start_x": {"type": "integer"}, "start_y": {"type": "integer"},
                "end_x": {"type": "integer"}, "end_y": {"type": "integer"},
                "duration": {"type": "number", "default": 0.5},
                "button": {"type": "string", "enum": ["left", "right", "middle"],
                           "default": "left"},
            },
            "required": ["start_x", "start_y", "end_x", "end_y"],
        },
    },
    {
        "name": "get_screen_size",
        "description": "Get screen dimensions in pixels.",
        "inputSchema": {"type": "object", "properties": {}},
    },
    {
        "name": "wait",
        "description": "Wait for a specified duration (seconds).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "seconds": {"type": "number", "description": "Seconds to wait"},
            },
            "required": ["seconds"],
        },
    },
]

_WIN32_TOOLS = [
    {
        "name": "find_window",
        "description": "Find windows by title substring. Returns hwnd, title, and rect.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "title": {"type": "string",
                          "description": "Substring to match in window titles"},
            },
            "required": ["title"],
        },
    },
    {
        "name": "focus_window",
        "description": "Bring a window to the foreground.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "hwnd": {"type": "integer",
                         "description": "Window handle from find_window"},
                "title": {"type": "string",
                          "description": "Title substring (fallback if no hwnd)"},
            },
        },
    },
]


# ---------------------------------------------------------------------------
# Tool dispatch
# ---------------------------------------------------------------------------

def _handle_tool(name: str, arguments: dict) -> list[dict]:
    _ensure_deps()

    if name == "screenshot":
        region = None
        if all(k in arguments for k in ("x", "y", "width", "height")):
            region = {k: arguments[k] for k in ("x", "y", "width", "height")}
        b64, w, h = _take_screenshot(
            region,
            arguments.get("max_width", 1280),
            arguments.get("max_height", 800),
        )
        return [
            {"type": "image", "data": b64, "mimeType": "image/png"},
            {"type": "text", "text": f"Screenshot: {w}x{h}px"},
        ]

    if name == "click":
        x, y = arguments["x"], arguments["y"]
        btn = arguments.get("button", "left")
        n = arguments.get("clicks", 1)
        _pyautogui.click(x, y, clicks=n, button=btn)
        return [{"type": "text", "text": f"Clicked {btn} {n}x at ({x},{y})"}]

    if name == "type_text":
        txt = arguments["text"]
        _type_text(txt, arguments.get("interval", 0.02))
        return [{"type": "text", "text": f"Typed {len(txt)} chars"}]

    if name == "key_press":
        keys = arguments["keys"]
        parts = [k.strip() for k in keys.split("+")]
        if len(parts) == 1:
            _pyautogui.press(parts[0])
        else:
            _pyautogui.hotkey(*parts)
        return [{"type": "text", "text": f"Pressed: {keys}"}]

    if name == "mouse_move":
        _pyautogui.moveTo(arguments["x"], arguments["y"])
        return [{"type": "text", "text": f"Moved to ({arguments['x']},{arguments['y']})"}]

    if name == "scroll":
        amt = arguments["amount"]
        kw: dict[str, Any] = {}
        if "x" in arguments:
            kw["x"] = arguments["x"]
        if "y" in arguments:
            kw["y"] = arguments["y"]
        _pyautogui.scroll(amt, **kw)
        return [{"type": "text", "text": f"Scrolled {amt}"}]

    if name == "drag":
        sx, sy = arguments["start_x"], arguments["start_y"]
        ex, ey = arguments["end_x"], arguments["end_y"]
        dur = arguments.get("duration", 0.5)
        btn = arguments.get("button", "left")
        _pyautogui.moveTo(sx, sy)
        _pyautogui.drag(ex - sx, ey - sy, duration=dur, button=btn)
        return [{"type": "text", "text": f"Dragged ({sx},{sy})->({ex},{ey})"}]

    if name == "get_screen_size":
        w, h = _pyautogui.size()
        return [{"type": "text", "text": f"Screen: {w}x{h}"}]

    if name == "wait":
        sec = arguments["seconds"]
        time.sleep(sec)
        return [{"type": "text", "text": f"Waited {sec}s"}]

    if name == "find_window" and _HAS_WIN32:
        query = arguments["title"].lower()
        results: list[dict] = []
        def _enum(hwnd: int, _: Any):
            if _win32gui.IsWindowVisible(hwnd):
                title = _win32gui.GetWindowText(hwnd)
                if query in title.lower():
                    r = _win32gui.GetWindowRect(hwnd)
                    results.append({
                        "hwnd": hwnd, "title": title,
                        "rect": {"left": r[0], "top": r[1],
                                 "right": r[2], "bottom": r[3]},
                    })
            return True  # continue enumeration
        _win32gui.EnumWindows(_enum, None)
        return [{"type": "text", "text": json.dumps(results, indent=2)}]

    if name == "focus_window" and _HAS_WIN32:
        hwnd = arguments.get("hwnd")
        if not hwnd and "title" in arguments:
            q = arguments["title"].lower()
            def _find(h: int, _: Any):
                nonlocal hwnd
                if _win32gui.IsWindowVisible(h) and q in _win32gui.GetWindowText(h).lower():
                    hwnd = h
                    return False
                return True
            try:
                _win32gui.EnumWindows(_find, None)
            except Exception:
                pass
        if hwnd:
            _win32gui.ShowWindow(hwnd, _win32con.SW_RESTORE)
            _win32gui.SetForegroundWindow(hwnd)
            title = _win32gui.GetWindowText(hwnd)
            return [{"type": "text", "text": f"Focused: {title}"}]
        return [{"type": "text", "text": "Window not found"}]

    return [{"type": "text", "text": f"Unknown tool: {name}"}]


# ---------------------------------------------------------------------------
# Minimal MCP JSON-RPC server (no SDK dependency, synchronous I/O)
# ---------------------------------------------------------------------------

PROTOCOL_VERSION = "2024-11-05"
SERVER_NAME = "computer-use"
SERVER_VERSION = "1.0.0"


def _get_tools() -> list[dict]:
    tools = list(_BASE_TOOLS)
    if sys.platform == "win32" and _has_win32():
        tools.extend(_WIN32_TOOLS)
    return tools


def _make_response(id: Any, result: dict) -> str:
    return json.dumps({"jsonrpc": "2.0", "id": id, "result": result})


def _make_error(id: Any, code: int, message: str) -> str:
    return json.dumps({"jsonrpc": "2.0", "id": id, "error": {"code": code, "message": message}})


def _handle_message(msg: dict) -> str | None:
    """Handle a JSON-RPC message; return response string or None for notifications."""
    method = msg.get("method", "")
    id = msg.get("id")
    params = msg.get("params", {})

    # --- initialize ---
    if method == "initialize":
        client_version = params.get("protocolVersion", PROTOCOL_VERSION)
        # Negotiate — we support 2024-11-05 and 2025-03-26
        supported = ["2025-03-26", "2024-11-05"]
        negotiated = client_version if client_version in supported else supported[-1]
        return _make_response(id, {
            "protocolVersion": negotiated,
            "capabilities": {
                "tools": {"listChanged": False},
            },
            "serverInfo": {"name": SERVER_NAME, "version": SERVER_VERSION},
        })

    # --- notifications (no response) ---
    if method in ("notifications/initialized", "notifications/cancelled"):
        return None

    # --- ping ---
    if method == "ping":
        return _make_response(id, {})

    # --- tools/list ---
    if method == "tools/list":
        return _make_response(id, {"tools": _get_tools()})

    # --- tools/call ---
    if method == "tools/call":
        tool_name = params.get("name", "")
        arguments = params.get("arguments", {})
        try:
            content = _handle_tool(tool_name, arguments)
            return _make_response(id, {"content": content})
        except Exception as exc:
            return _make_response(id, {
                "content": [{"type": "text", "text": f"Error: {exc}"}],
                "isError": True,
            })

    # Unknown method
    if id is not None:
        return _make_error(id, -32601, f"Method not found: {method}")
    return None


def main():
    """Run MCP server over stdin/stdout using newline-delimited JSON-RPC."""
    # Use binary stdin for reliable cross-platform line reading, text stdout for output
    stdin = sys.stdin.buffer
    stdout = sys.stdout

    # Redirect stderr to avoid polluting stdout (MCP protocol channel)
    sys.stderr = open(sys.stderr.fileno(), "w", encoding="utf-8", closefd=False)

    for raw_line in stdin:
        line = raw_line.decode("utf-8", errors="replace").strip()
        if not line:
            continue
        try:
            msg = json.loads(line)
        except json.JSONDecodeError:
            continue

        response = _handle_message(msg)
        if response is not None:
            stdout.write(response + "\n")
            stdout.flush()


if __name__ == "__main__":
    main()
