#!/usr/bin/env python3
"""Convert Avalonia Window editors to embeddable UserControl editors.

Usage:
  python scripts/tools/convert-editor-to-embeddable.py MoveCostEditorView OtherView

The transform is intentionally conservative: it only handles the mechanical
#1873 pattern and skips editors with known Window/dialog/picker dependencies.
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path

EXCLUDED_PATTERNS: tuple[tuple[str, re.Pattern[str]], ...] = (
    ("IPickableEditor", re.compile(r"\bIPickableEditor\b")),
    ("StorageProvider", re.compile(r"\bStorageProvider\b")),
    ("MessageBox", re.compile(r"\bMessageBox\b")),
    ("ShowDialog(", re.compile(r"\bShowDialog\s*\(")),
    ("GetTopLevel", re.compile(r"\bGetTopLevel\b")),
    ("Closed event", re.compile(r"\bClosed\s*\+=")),
    ("Window Clipboard", re.compile(r"\bClipboard\b")),
    ("FileDialogHelper", re.compile(r"\bFileDialogHelper\b")),
    ("NumberInputDialog", re.compile(r"\bNumberInputDialog\b")),
    ("Dialogs.*Show", re.compile(r"\bDialogs\.[A-Za-z0-9_.]*Show\b")),
    ("owner-bound PickFromEditor", re.compile(r"\bPickFromEditor\s*<[^>]+>\s*\([^;]*\bthis\b", re.DOTALL)),
    ("owner-bound ShowDialog", re.compile(r"\bShowDialog\s*<[^>]+>\s*\(\s*this\b")),
    ("owner-bound OpenModal", re.compile(r"\bOpenModal\s*<[^>]+>\s*\([^;]*\bthis\b", re.DOTALL)),
    ("owner-bound image export", re.compile(r"\bExportPng\s*\(\s*this\b")),
    ("owner-bound image import", re.compile(r"\bImageImportService\.[A-Za-z0-9_]+\s*\(\s*this\b")),
    ("self Close()", re.compile(r"(?<![\.\w])Close\s*\(")),
)

ROOT_RE = re.compile(r"^<Window\b(?P<attrs>.*?)>", re.DOTALL)
ATTR_RE = re.compile(r"(?P<name>[\w:.]+)\s*=\s*\"(?P<value>[^\"]*)\"")
OPENED_RE = re.compile(
    r"^(?P<indent>[ \t]*)Opened[ \t]*\+=[ \t]*\([^;\n]*\)[ \t]*=>[ \t]*(?P<call>[A-Za-z_][A-Za-z0-9_]*[ \t]*\([^;\n]*\))[ \t]*;[ \t]*\n",
    re.MULTILINE,
)
OPENED_COMPOUND_RE = re.compile(
    r"^(?P<indent>[ \t]*)Opened[ \t]*\+=[ \t]*\([^;\n]*\)[ \t]*=>[ \t]*(?:\r?\n[ \t]*)?\{(?P<body>.*?)^(?P=indent)\}[ \t]*;[ \t]*\n",
    re.DOTALL | re.MULTILINE,
)
OPENED_INLINE_COMPOUND_RE = re.compile(
    r"^(?P<indent>[ \t]*)Opened[ \t]*\+=[ \t]*\([^;\n]*\)[ \t]*=>[ \t]*\{[ \t]*(?P<body>.*?)[ \t]*\}[ \t]*;[ \t]*\n",
    re.MULTILINE,
)


@dataclass(frozen=True)
class Descriptor:
    title: str
    width: str
    height: str
    size_to_content: bool


def parse_size_to_content(value: str | None) -> bool:
    """Map Avalonia Window SizeToContent to EditorDescriptor auto-size flag."""
    return value in {"Width", "Height", "WidthAndHeight"}


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def write_text(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8", newline="")


def parse_window_root(axaml: str, view_name: str) -> tuple[str, dict[str, str], Descriptor]:
    match = ROOT_RE.search(axaml)
    if not match:
        raise ValueError("AXAML root is not <Window> (already converted or unsupported)")

    attrs = {m.group("name"): m.group("value") for m in ATTR_RE.finditer(match.group("attrs"))}
    missing = [k for k in ("x:Class", "Title", "Width", "Height") if k not in attrs]
    if missing:
        raise ValueError(f"Window root missing required attrs: {', '.join(missing)}")
    expected_class = f"FEBuilderGBA.Avalonia.Views.{view_name}"
    if attrs["x:Class"] != expected_class:
        raise ValueError(f"x:Class {attrs['x:Class']} does not match {expected_class}")

    descriptor = Descriptor(
        title=attrs["Title"],
        width=attrs["Width"],
        height=attrs["Height"],
        size_to_content=parse_size_to_content(attrs.get("SizeToContent")),
    )
    return match.group(0), attrs, descriptor


def convert_axaml(axaml: str, view_name: str) -> tuple[str, Descriptor]:
    old_root, attrs, descriptor = parse_window_root(axaml, view_name)
    kept_names = [name for name in attrs if name == "x:Class" or name == "xmlns" or name.startswith("xmlns:")]
    if "xmlns" in kept_names:
        kept_names.remove("xmlns")
        kept_names.insert(0, "xmlns")
    if "x:Class" in kept_names:
        kept_names.remove("x:Class")
        kept_names.append("x:Class")

    lines: list[str] = []
    for i, name in enumerate(kept_names):
        prefix = "<UserControl " if i == 0 else "             "
        suffix = ">" if i == len(kept_names) - 1 else ""
        lines.append(f'{prefix}{name}="{attrs[name]}"{suffix}')
    new_root = "\n".join(lines)
    converted = axaml.replace(old_root, new_root, 1).replace("</Window>", "</UserControl>")
    return converted, descriptor


def ensure_avalonia_using(cs: str) -> str:
    if "using global::Avalonia;" in cs:
        return cs
    if "using System;\n" in cs:
        return cs.replace("using System;\n", "using System;\nusing global::Avalonia;\n", 1)
    return "using global::Avalonia;\n" + cs


def convert_base_list(cs: str, view_name: str) -> str:
    pattern = re.compile(rf"public\s+partial\s+class\s+{re.escape(view_name)}\s*:\s*(?P<bases>[^\n{{]+)")
    match = pattern.search(cs)
    if not match:
        raise ValueError("public partial class declaration not found")
    bases = match.group("bases")
    if "TranslatedWindow" not in bases:
        raise ValueError("class does not derive from TranslatedWindow")
    if "IEditorView" not in bases:
        raise ValueError("class does not implement IEditorView")
    new_bases = bases.replace("TranslatedWindow", "TranslatedUserControl").replace("IEditorView", "IEmbeddableEditor")
    return cs[: match.start("bases")] + new_bases + cs[match.end("bases") :]


def inject_members(cs: str, descriptor: Descriptor) -> str:
    cs = re.sub(r"public\s+bool\s+IsLoaded\s*=>", "public new bool IsLoaded =>", cs, count=1)
    if "public EditorDescriptor Descriptor" not in cs:
        marker = re.search(r"^(?P<indent>[ \t]*)public\s+new\s+bool\s+IsLoaded\s*=>[^;]+;[ \t]*\n", cs, re.MULTILINE)
        if not marker:
            raise ValueError("public bool/new bool IsLoaded expression not found")
        indent = marker.group("indent")
        size_bool = "true" if descriptor.size_to_content else "false"
        injected = (
            marker.group(0)
            + f'{indent}public EditorDescriptor Descriptor => new("{descriptor.title}", {descriptor.width}, {descriptor.height}, SizeToContent: {size_bool});\n'
            + f'{indent}public event EventHandler? CloseRequested;\n'
        )
        cs = cs[: marker.start()] + injected + cs[marker.end() :]
    if "RequestClose() => CloseRequested?.Invoke" not in cs:
        marker = re.search(r"^(?P<indent>[ \t]*)public\s+ViewModelBase\?\s+DataViewModel\s*=>\s*_vm\s*;[ \t]*\n", cs, re.MULTILINE)
        if not marker:
            marker = re.search(r"^(?P<indent>[ \t]*)public\s+event\s+EventHandler\?\s+CloseRequested\s*;[ \t]*\n", cs, re.MULTILINE)
        if not marker:
            raise ValueError("member anchor not found; add RequestClose manually")
        indent = marker.group("indent")
        injected = marker.group(0) + f"{indent}public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);\n"
        cs = cs[: marker.start()] + injected + cs[marker.end() :]
    return cs


def normalize_lambda_body(body: str) -> list[str]:
    """Trim wrapper whitespace while preserving relative indentation inside a lambda body."""
    lines = [line.rstrip() for line in body.splitlines()]
    while lines and not lines[0].strip():
        lines.pop(0)
    while lines and not lines[-1].strip():
        lines.pop()
    nonblank_indents = [
        len(line) - len(line.lstrip(" "))
        for line in lines
        if line.strip()
    ]
    common_indent = min(nonblank_indents, default=0)
    if common_indent == 0 and len(nonblank_indents) > 1:
        positive_indents = [indent for indent in nonblank_indents if indent > 0]
        if positive_indents:
            common_indent = min(positive_indents)
    return [
        line[common_indent:] if line.strip() and len(line) - len(line.lstrip(" ")) >= common_indent else line
        for line in lines
    ]


def convert_opened_handler(cs: str) -> str:
    if "OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)" in cs:
        return cs
    match = OPENED_RE.search(cs)
    body_lines: list[str]
    if not match:
        compound = OPENED_COMPOUND_RE.search(cs)
        if not compound:
            compound = OPENED_INLINE_COMPOUND_RE.search(cs)
        if not compound:
            return cs
        body_lines = normalize_lambda_body(compound.group("body"))
        match = compound
    else:
        body_lines = [match.group("call") + ";"]
    cs = cs[: match.start()] + cs[match.end() :]

    field_marker = re.search(r"^(?P<indent>[ \t]*)readonly\s+UndoService\s+_undoService\s*=\s*new\(\)\s*;[ \t]*\n", cs, re.MULTILINE)
    if not field_marker:
        field_marker = re.search(r"^(?P<indent>[ \t]*)readonly\s+[^;\n]+\s*;[ \t]*\n", cs, re.MULTILINE)
    if not field_marker:
        raise ValueError("field anchor not found; cannot place one-shot load guard")
    indent = field_marker.group("indent")
    cs = cs[: field_marker.end()] + f"{indent}bool _hasLoadedList;\n" + cs[field_marker.end() :]

    ctor_match = re.search(r"^(?P<indent>[ \t]*)public\s+[A-Za-z_][A-Za-z0-9_]*\s*\(\)\s*\{", cs, re.MULTILINE)
    if not ctor_match:
        raise ValueError("constructor not found")
    pos = ctor_match.end()
    depth = 1
    while pos < len(cs) and depth:
        if cs[pos] == "{":
            depth += 1
        elif cs[pos] == "}":
            depth -= 1
        pos += 1
    if depth != 0:
        raise ValueError("constructor body did not parse")
    ctor_end = pos
    method_indent = ctor_match.group("indent")
    override = (
        "\n\n"
        f"{method_indent}protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)\n"
        f"{method_indent}{{\n"
        f"{method_indent}    base.OnAttachedToVisualTree(e);\n"
        f"{method_indent}    if (!_hasLoadedList)\n"
        f"{method_indent}    {{\n"
        f"{method_indent}        _hasLoadedList = true;\n"
        + "\n".join(f"{method_indent}        {line}" for line in body_lines)
        + "\n"
        f"{method_indent}    }}\n"
        f"{method_indent}}}"
    )
    return cs[:ctor_end] + override + cs[ctor_end:]


def convert_cs(cs: str, view_name: str, descriptor: Descriptor) -> str:
    for label, pattern in EXCLUDED_PATTERNS:
        if pattern.search(cs):
            raise RuntimeError(f"SKIP excluded pattern: {label}")
    cs = ensure_avalonia_using(cs)
    cs = convert_base_list(cs, view_name)
    cs = inject_members(cs, descriptor)
    cs = convert_opened_handler(cs)
    return cs


def convert_view(repo: Path, view_name: str) -> tuple[bool, str]:
    views = repo / "FEBuilderGBA.Avalonia" / "Views"
    axaml_path = views / f"{view_name}.axaml"
    cs_path = views / f"{view_name}.axaml.cs"
    if not axaml_path.exists() or not cs_path.exists():
        return False, f"SKIP {view_name}: missing AXAML or code-behind"

    axaml = read_text(axaml_path)
    cs = read_text(cs_path)
    if axaml.lstrip().startswith("<UserControl") and "IEmbeddableEditor" in cs:
        return False, f"SKIP {view_name}: already embeddable"

    try:
        converted_axaml, descriptor = convert_axaml(axaml, view_name)
        converted_cs = convert_cs(cs, view_name, descriptor)
    except RuntimeError as ex:
        return False, f"SKIP {view_name}: {ex}"
    except ValueError as ex:
        return False, f"SKIP {view_name}: unsupported shape: {ex}"

    write_text(axaml_path, converted_axaml)
    write_text(cs_path, converted_cs)
    return True, f"CONVERTED {view_name}: {descriptor.title} {descriptor.width}x{descriptor.height} SizeToContent={descriptor.size_to_content}"


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("views", nargs="+", help="Editor view class names, e.g. ItemEditorView")
    parser.add_argument("--repo", default=".", help="Repository root (default: current directory)")
    args = parser.parse_args(argv)

    repo = Path(args.repo).resolve()
    converted = 0
    failed = 0
    for view in args.views:
        ok, message = convert_view(repo, view)
        print(message)
        converted += int(ok)
        if not ok and "already embeddable" not in message:
            failed += 1
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
