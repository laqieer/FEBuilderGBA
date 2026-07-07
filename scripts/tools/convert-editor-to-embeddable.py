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
    ("Closed event", re.compile(r"\bClosed\s*\+=")),
    ("OnClosed override", re.compile(r"\boverride\s+void\s+OnClosed\s*\(")),
    ("Close with result", re.compile(r"(?<![\.\w])Close\s*\(\s*[^)\s]|\bthis\s*\.\s*Close\s*\(\s*[^)\s]")),
)

ROOT_RE = re.compile(r"(?P<root><Window\b(?P<attrs>.*?)>)", re.DOTALL)
ATTR_RE = re.compile(r"(?P<name>[\w:.]+)\s*=\s*\"(?P<value>[^\"]*)\"")
OPENED_RE = re.compile(
    r"^(?P<indent>[ \t]*)Opened[ \t]*\+=[ \t]*\([^;\n]*\)[ \t]*=>[ \t]*(?P<stmt>[^;\n]+;)[ \t]*\n",
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
    can_resize: bool
    startup_location: str


def parse_size_to_content(value: str | None) -> bool:
    """Map Avalonia Window SizeToContent to EditorDescriptor auto-size flag."""
    return value in {"Width", "Height", "WidthAndHeight"}


def parse_can_resize(value: str | None) -> bool:
    """Map Avalonia Window CanResize to EditorDescriptor resize flag."""
    return not (value is not None and value.strip().lower() == "false")


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
        can_resize=parse_can_resize(attrs.get("CanResize")),
        startup_location=attrs.get("WindowStartupLocation", "CenterOwner"),
    )
    return match.group("root"), attrs, descriptor


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


def add_using(cs: str, using_line: str) -> str:
    if using_line in cs:
        return cs
    first_using = re.search(r"^using\s+", cs, re.MULTILINE)
    if first_using:
        return cs[: first_using.start()] + using_line + "\n" + cs[first_using.start() :]

    lines = cs.splitlines(keepends=True)
    index = 0
    in_block_comment = False
    while index < len(lines):
        stripped = lines[index].strip()
        if in_block_comment:
            index += 1
            if "*/" in stripped:
                in_block_comment = False
            continue
        if not stripped or stripped.startswith("//"):
            index += 1
            continue
        if stripped.startswith("/*"):
            index += 1
            if "*/" not in stripped:
                in_block_comment = True
            continue
        break
    return "".join(lines[:index]) + using_line + "\n" + "".join(lines[index:])


def ensure_avalonia_using(cs: str) -> str:
    return add_using(cs, "using global::Avalonia;")


def ensure_system_using(cs: str) -> str:
    if "EventHandler" not in cs and "EventArgs" not in cs:
        return cs
    if "using System;" in cs or "using global::System;" in cs:
        return cs
    return add_using(cs, "using System;")


def ensure_avalonia_controls_using(cs: str) -> str:
    if "TopLevel.GetTopLevel(this)" not in cs and "WindowStartupLocation." not in cs:
        return cs
    return add_using(cs, "using global::Avalonia.Controls;")


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


def convert_owner_bound_pick_from_editor(cs: str) -> str:
    """Reroute Window owner arguments through the hosting TopLevel after conversion to UserControl."""
    return re.sub(
        r"(PickFromEditor<[^>]+>\s*\(\s*[^;,\n\)](?:[^;]*?)),\s*this\s*\)",
        r"\1, TopLevel.GetTopLevel(this) as Window)",
        cs,
        flags=re.DOTALL,
    )


def convert_owner_bound_dialog_calls(cs: str) -> str:
    """Reroute owner-bound dialog helpers from the editor Window to its hosting TopLevel.

    After conversion the editor instance is a UserControl, so passing ``this`` as a
    Window owner no longer compiles and also loses the desktop modal owner. The
    desktop host is the TopLevel Window; single-view TopLevels are not Windows, so
    the cast intentionally yields null for APIs that accept ``Window?``.
    """
    owner = "TopLevel.GetTopLevel(this) as Window"
    replacements: tuple[tuple[re.Pattern[str], str], ...] = (
        (
            re.compile(r"(?P<prefix>\b(?:Dialogs\.)?MessageBoxWindow\s*\.\s*Show\s*\(\s*)this\s*,"),
            rf"\g<prefix>{owner},",
        ),
        (
            re.compile(r"(?P<prefix>\b(?:Dialogs\.)?NumberInputDialog\s*\.\s*Show\s*\(\s*)this\s*,"),
            rf"\g<prefix>{owner},",
        ),
        (
            re.compile(r"(?P<prefix>\bFileDialogHelper\s*\.\s*[A-Za-z_][A-Za-z0-9_]*\s*\(\s*)this(?P<suffix>\s*[,)])"),
            rf"\g<prefix>{owner}\g<suffix>",
        ),
        (
            re.compile(r"(?P<prefix>\bFERepoPickHelper\s*\.\s*[A-Za-z_][A-Za-z0-9_]*\s*\(\s*)this(?P<suffix>\s*[,)])"),
            rf"\g<prefix>{owner}\g<suffix>",
        ),
        (
            re.compile(r"(?P<prefix>\b[A-Za-z_][A-Za-z0-9_]*\s*\.\s*ShowAsync\s*\(\s*)this\b"),
            rf"\g<prefix>{owner}",
        ),
        (
            re.compile(r"(?P<prefix>\b[A-Za-z_][A-Za-z0-9_]*\s*\.\s*Show\s*\(\s*)this\s*,"),
            rf"\g<prefix>{owner},",
        ),
        (
            re.compile(r"(?P<prefix>\.ShowDialog(?:<[^>]+>)?\s*\(\s*)this(?P<suffix>\s*\))"),
            rf"\g<prefix>{owner}\g<suffix>",
        ),
        (
            re.compile(r"(?P<prefix>\bOpenModal<[^>]+>\s*\([^;]*?),\s*this\s*\)", re.DOTALL),
            rf"\g<prefix>, {owner})",
        ),
    )
    for pattern, repl in replacements:
        cs = pattern.sub(repl, cs)
    return cs


def convert_owner_bound_image_calls(cs: str) -> str:
    """Reroute image import/export owners from the editor Window to its hosting TopLevel."""
    owner = "TopLevel.GetTopLevel(this) as Window"
    replacements: tuple[tuple[re.Pattern[str], str], ...] = (
        (
            re.compile(r"(?P<prefix>\bImageImportService\s*\.\s*[A-Za-z_][A-Za-z0-9_]*\s*\(\s*)this(?P<suffix>\s*,)"),
            rf"\g<prefix>{owner}\g<suffix>",
        ),
        (
            re.compile(r"(?P<prefix>\b[A-Za-z_][A-Za-z0-9_]*\s*\.\s*ExportPng\s*\(\s*)this(?P<suffix>\s*[,)])"),
            rf"\g<prefix>{owner}\g<suffix>",
        ),
        (
            re.compile(r"(?P<prefix>\bExportPng\s*\(\s*)this(?P<suffix>\s*[,)])"),
            rf"\g<prefix>{owner}\g<suffix>",
        ),
        (
            re.compile(r"(?P<prefix>\bImageExportService\s*\.\s*[A-Za-z_][A-Za-z0-9_]*\s*\(\s*[^;\n]*?,\s*)this(?P<suffix>\s*[,)])"),
            rf"\g<prefix>{owner}\g<suffix>",
        ),
    )
    for pattern, repl in replacements:
        cs = pattern.sub(repl, cs)
    return cs


def convert_top_level_services(cs: str) -> str:
    """Reroute Window-only StorageProvider/Clipboard property access through TopLevel.

    The handled shapes are intentionally narrow: direct picker awaits, local
    aliases, and nullable guards. More complex flows should be skipped manually
    rather than rewritten incorrectly.
    """
    cs = re.sub(
        r"(?P<indent>^[ \t]*)if\s*\(\s*(?:this\s*\.\s*)?StorageProvider\s*==\s*null\s*\)\s*return\s*;\s*\n(?P=indent)(?P<decl>var\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*await\s+)(?:this\s*\.\s*)?StorageProvider\s*\.\s*(?P<method>(?:Open|Save)(?:File|Folder)PickerAsync\s*\()",
        r"\g<indent>var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;\n"
        r"\g<indent>if (storageProvider == null) return;\n"
        r"\g<indent>\g<decl>storageProvider.\g<method>",
        cs,
        flags=re.MULTILINE,
    )
    cs = re.sub(
        r"(?P<indent>^[ \t]*)(?P<decl>var\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*await\s+)(?:this\s*\.\s*)?StorageProvider\s*\.\s*(?P<method>(?:Open|Save)(?:File|Folder)PickerAsync\s*\()",
        r"\g<indent>var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;\n"
        r"\g<indent>if (storageProvider == null) return;\n"
        r"\g<indent>\g<decl>storageProvider.\g<method>",
        cs,
        flags=re.MULTILINE,
    )
    cs = re.sub(
        r"(?P<indent>^[ \t]*)return\s+await\s+(?:this\s*\.\s*)?StorageProvider\s*\.\s*(?P<method>(?:Open|Save)(?:File|Folder)PickerAsync\s*\()",
        r"\g<indent>var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;\n"
        r"\g<indent>if (storageProvider == null) return null;\n"
        r"\g<indent>return await storageProvider.\g<method>",
        cs,
        flags=re.MULTILINE,
    )
    cs = re.sub(
        r"(?P<decl>\bvar\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*)(?:this\s*\.\s*)?StorageProvider\s*;",
        r"\g<decl>TopLevel.GetTopLevel(this)?.StorageProvider;",
        cs,
    )
    cs = re.sub(
        r"(?P<decl>\bIClipboard\?\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*)Clipboard\s*;",
        r"\g<decl>TopLevel.GetTopLevel(this)?.Clipboard;",
        cs,
    )
    cs = re.sub(
        r"(?P<decl>\bvar\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*)Clipboard\s*;",
        r"\g<decl>TopLevel.GetTopLevel(this)?.Clipboard;",
        cs,
    )
    return cs


def convert_self_close(cs: str) -> str:
    """Convert calls that close the editor Window itself into embeddable CloseRequested requests."""
    cs = re.sub(r"\bthis\s*\.\s*Close\s*\(\s*\)\s*;", "RequestClose();", cs)
    cs = re.sub(r"(?<![\.\w])Close\s*\(\s*\)\s*;", "RequestClose();", cs)
    return cs


def inject_members(cs: str, descriptor: Descriptor) -> str:
    cs = re.sub(r"public\s+bool\s+IsLoaded\s*=>", "public new bool IsLoaded =>", cs, count=1)
    if "public EditorDescriptor Descriptor" not in cs:
        marker = re.search(r"^(?P<indent>[ \t]*)public\s+new\s+bool\s+IsLoaded\s*=>[^;]+;[ \t]*\n", cs, re.MULTILINE)
        if not marker:
            raise ValueError("public bool/new bool IsLoaded expression not found")
        indent = marker.group("indent")
        descriptor_args = [f'"{descriptor.title}"', descriptor.width, descriptor.height]
        if descriptor.size_to_content:
            descriptor_args.append("SizeToContent: true")
        if not descriptor.can_resize:
            descriptor_args.append("CanResize: false")
        if descriptor.startup_location != "CenterOwner":
            descriptor_args.append(f"StartupLocation: WindowStartupLocation.{descriptor.startup_location}")
        injected = (
            marker.group(0)
            + f'{indent}public EditorDescriptor Descriptor => new({", ".join(descriptor_args)});\n'
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
        body_lines = [match.group("stmt").strip()]
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
    cs = convert_owner_bound_pick_from_editor(cs)
    cs = convert_owner_bound_dialog_calls(cs)
    cs = convert_owner_bound_image_calls(cs)
    cs = convert_top_level_services(cs)
    cs = ensure_avalonia_controls_using(cs)
    cs = convert_self_close(cs)
    cs = inject_members(cs, descriptor)
    cs = ensure_avalonia_controls_using(cs)
    cs = ensure_system_using(cs)
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
    constructor_ref = re.compile(rf"\bnew\s+{re.escape(view_name)}\s*\(")
    for other in views.glob("*.cs"):
        if other == cs_path:
            continue
        other_cs = read_text(other)
        if constructor_ref.search(other_cs) and "ShowDialog" in other_cs:
            return False, f"SKIP {view_name}: used as external ShowDialog target in {other.name}"

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
