#!/usr/bin/env python3
"""
Add ViewTranslationHelper support to all Avalonia Window views.

For each .axaml.cs file that contains a Window subclass:
1. Add required using statements
2. Add ViewTranslationHelper _translator field
3. Wire up translation in constructor after InitializeComponent()
4. Subscribe to CoreState.LanguageChanged
5. Override OnClosed to unsubscribe
"""

import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VIEWS_DIR = os.path.join(REPO_ROOT, "FEBuilderGBA.Avalonia", "Views")

# Files to skip
SKIP_FILES = {"MainWindow.axaml.cs", "OptionsView.axaml.cs"}

count = 0
errors = 0
skipped = 0


def process_file(filepath):
    global count, errors, skipped

    basename = os.path.basename(filepath)

    if basename in SKIP_FILES:
        skipped += 1
        return

    with open(filepath, "r", encoding="utf-8") as f:
        content = f.read()

    # Skip if not a Window subclass
    if not re.search(r"class\s+\w+\s*:\s*Window", content):
        skipped += 1
        return

    # Skip if already has ViewTranslationHelper
    if "ViewTranslationHelper" in content:
        skipped += 1
        return

    original = content

    # Step 1: Ensure 'using System;' is present
    if "using System;" not in content:
        content = "using System;\n" + content

    # Step 2: Ensure 'using FEBuilderGBA.Avalonia.Services;' is present
    if "using FEBuilderGBA.Avalonia.Services;" not in content:
        # Add before namespace declaration
        content = re.sub(
            r"(namespace\s)",
            "using FEBuilderGBA.Avalonia.Services;\n\n\\1",
            content,
            count=1,
        )

    # Step 3: Add ViewTranslationHelper field after class opening brace
    # Find 'partial class ClassName : Window...' or 'partial class ClassName : Window, IEditorView...'
    class_match = re.search(r"(partial\s+class\s+\w+[^{]*\{)", content)
    if not class_match:
        print(f"WARN: Could not find class declaration in {basename}", file=sys.stderr)
        errors += 1
        return

    insert_pos = class_match.end()
    field_decl = "\n        ViewTranslationHelper _translator;\n"
    content = content[:insert_pos] + field_decl + content[insert_pos:]

    # Step 4: Add translation setup after InitializeComponent()
    init_pattern = r"([ \t]*InitializeComponent\(\);)"
    replacement = (
        r"\1\n"
        r"            // Translation support\n"
        r"            _translator = new ViewTranslationHelper(this);\n"
        r"            _translator.TranslateAll();\n"
        r"            CoreState.LanguageChanged += _translator.OnLanguageChanged;"
    )
    content = re.sub(init_pattern, replacement, content, count=1)

    # Step 5: Add OnClosed override before the class closing brace
    # Find the second-to-last '}' which closes the class (last '}' closes namespace)
    lines = content.split("\n")

    # Find closing braces from the end
    brace_positions = []
    for i in range(len(lines) - 1, -1, -1):
        if lines[i].strip() == "}":
            brace_positions.append(i)
            if len(brace_positions) == 2:
                break

    if len(brace_positions) >= 2:
        class_close_idx = brace_positions[1]
        onclosed_code = [
            "",
            "        protected override void OnClosed(EventArgs e)",
            "        {",
            "            CoreState.LanguageChanged -= _translator.OnLanguageChanged;",
            "            base.OnClosed(e);",
            "        }",
        ]
        for j, line in enumerate(onclosed_code):
            lines.insert(class_close_idx + j, line)
        content = "\n".join(lines)
    else:
        print(
            f"WARN: Could not find class closing brace in {basename}", file=sys.stderr
        )
        errors += 1
        return

    with open(filepath, "w", encoding="utf-8") as f:
        f.write(content)

    count += 1


def main():
    global count, errors, skipped

    for filename in sorted(os.listdir(VIEWS_DIR)):
        if not filename.endswith(".axaml.cs"):
            continue
        filepath = os.path.join(VIEWS_DIR, filename)
        process_file(filepath)

    print(f"Modified: {count}, Errors: {errors}, Skipped: {skipped}")


if __name__ == "__main__":
    main()
