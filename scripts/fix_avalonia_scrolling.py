#!/usr/bin/env python3
"""Batch-fix Avalonia .axaml views to prevent content clipping.

For every Window-based view:
  1. Add SizeToContent="WidthAndHeight" if missing
For views without ScrollViewer:
  2. Wrap the root content element in <ScrollViewer>
"""

import os
import re
import sys

VIEWS_DIR = os.path.join(os.path.dirname(__file__), "..", "FEBuilderGBA.Avalonia", "Views")

# Views that should NOT get a ScrollViewer wrapper (e.g., they manage their own scrolling)
EXCLUDE_SCROLLVIEWER = {
    "NotifyPleaseWaitView.axaml",  # Simple progress indicator, no need to scroll
}

def add_size_to_content(content: str) -> str:
    """Add SizeToContent='WidthAndHeight' to Window element if not present."""
    if 'SizeToContent=' in content:
        return content
    # Insert before the closing > of the <Window ...> tag
    # Handle both single-line and multi-line Window tags
    pattern = r'(<Window\b[^>]*?)(>)'
    def replacer(m):
        tag = m.group(1)
        close = m.group(2)
        # Don't add if already present
        if 'SizeToContent' in tag:
            return m.group(0)
        return f'{tag}\n        SizeToContent="WidthAndHeight"{close}'
    return re.sub(pattern, replacer, content, count=1, flags=re.DOTALL)


def wrap_with_scrollviewer(content: str) -> str:
    """Wrap the first child element of <Window> in a ScrollViewer."""
    if 'ScrollViewer' in content:
        return content

    # Find the end of the <Window ...> opening tag
    window_match = re.search(r'<Window\b[^>]*>', content, re.DOTALL)
    if not window_match:
        return content

    window_end = window_match.end()

    # Find </Window> closing tag
    close_match = re.search(r'</Window>\s*$', content)
    if not close_match:
        return content

    close_start = close_match.start()

    # Get the content between <Window> and </Window>
    inner = content[window_end:close_start]

    # Wrap inner content in ScrollViewer
    # Use indentation matching the original content
    new_inner = (
        '\n  <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto">'
        + inner
        + '  </ScrollViewer>\n'
    )

    return content[:window_end] + new_inner + content[close_start:]


def process_file(filepath: str) -> list:
    """Process a single .axaml file. Returns list of changes made."""
    with open(filepath, 'r', encoding='utf-8') as f:
        original = f.read()

    if '<Window' not in original:
        return []

    changes = []
    content = original

    # Step 1: Add SizeToContent
    new_content = add_size_to_content(content)
    if new_content != content:
        changes.append("added SizeToContent")
        content = new_content

    # Step 2: Wrap with ScrollViewer (only for files without one)
    basename = os.path.basename(filepath)
    if basename not in EXCLUDE_SCROLLVIEWER and 'ScrollViewer' not in original:
        new_content = wrap_with_scrollviewer(content)
        if new_content != content:
            changes.append("added ScrollViewer wrapper")
            content = new_content

    if content != original:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)

    return changes


def main():
    views_dir = os.path.normpath(VIEWS_DIR)
    if not os.path.isdir(views_dir):
        print(f"Error: Views directory not found: {views_dir}", file=sys.stderr)
        sys.exit(1)

    total_files = 0
    modified_files = 0
    sv_added = 0
    stc_added = 0

    for filename in sorted(os.listdir(views_dir)):
        if not filename.endswith('.axaml'):
            continue
        filepath = os.path.join(views_dir, filename)
        total_files += 1

        changes = process_file(filepath)
        if changes:
            modified_files += 1
            change_str = ", ".join(changes)
            print(f"  Modified: {filename} ({change_str})")
            if "added ScrollViewer" in change_str:
                sv_added += 1
            if "added SizeToContent" in change_str:
                stc_added += 1

    print(f"\nSummary:")
    print(f"  Total .axaml files scanned: {total_files}")
    print(f"  Files modified: {modified_files}")
    print(f"  SizeToContent added: {stc_added}")
    print(f"  ScrollViewer wrappers added: {sv_added}")


if __name__ == "__main__":
    main()
