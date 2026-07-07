#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""
Build the embedded CJK fallback font for the WebAssembly (Browser) head (#1890).

The wasm app has NO system fonts and ships only Inter (no CJK glyphs), so Japanese
game text and the ja/zh UI translations render as tofu. Desktop/Android/iOS fall back
to OS CJK fonts; wasm cannot. This subsets a full Noto Sans CJK SC into a compact
fallback that covers all realistic Japanese + Simplified-Chinese text used by the app,
keeping the committed binary small.

Coverage kept:
  * Basic Latin + Latin-1 (mixed-script safety)
  * CJK Symbols & Punctuation, Hiragana, Katakana (+ phonetic ext), Halfwidth/Fullwidth Forms
  * Every codepoint encodable in Shift-JIS (⊇ JIS X 0208 — all standard Japanese)
  * Every codepoint encodable in GB2312 (standard Simplified Chinese)
  * Every character actually present in config/translate and config/data (UI + game names)

Base font (OFL): Noto Sans CJK SC Regular (Source Han Sans SC), from notofonts/noto-cjk.
Reproduce:
  pip install fonttools
  # download the base OTF (≈15.7 MB) to _fontwork/NotoSansCJKsc-Regular.otf, then:
  python scripts/build-cjk-font-subset.py \
      --base _fontwork/NotoSansCJKsc-Regular.otf \
      --out  FEBuilderGBA.Browser/Assets/Fonts/NotoSansCJKsc-Subset.otf
"""
import argparse
import glob
import os
import sys


def build_charset(repo_root: str) -> set[str]:
    chars: set[str] = set()

    def add_range(a: int, b: int) -> None:
        for cp in range(a, b + 1):
            chars.add(chr(cp))

    # Explicit ranges (kana, CJK punctuation, fullwidth forms, Latin).
    add_range(0x0020, 0x007E)  # Basic Latin
    add_range(0x00A0, 0x00FF)  # Latin-1 Supplement
    add_range(0x2010, 0x2027)  # general punctuation (dashes, quotes)
    add_range(0x2030, 0x205E)  # general punctuation
    add_range(0x3000, 0x303F)  # CJK Symbols and Punctuation
    add_range(0x3040, 0x309F)  # Hiragana
    add_range(0x30A0, 0x30FF)  # Katakana
    add_range(0x31F0, 0x31FF)  # Katakana Phonetic Extensions
    add_range(0xFF00, 0xFFEF)  # Halfwidth and Fullwidth Forms

    # All Shift-JIS-encodable codepoints (⊇ JIS X 0208 — covers standard Japanese).
    for cp in range(0x10000):
        try:
            chr(cp).encode("shift_jis")
            chars.add(chr(cp))
        except UnicodeEncodeError:
            pass

    # All GB2312-encodable codepoints (standard Simplified Chinese).
    for cp in range(0x10000):
        try:
            chr(cp).encode("gb2312")
            chars.add(chr(cp))
        except UnicodeEncodeError:
            pass

    # Every character actually used by the app's translations and game-name data.
    patterns = [
        os.path.join(repo_root, "config", "translate", "**", "*.txt"),
        os.path.join(repo_root, "config", "data", "**", "*.txt"),
    ]
    for pat in patterns:
        for path in glob.glob(pat, recursive=True):
            try:
                with open(path, encoding="utf-8", errors="ignore") as fh:
                    for line in fh:
                        for ch in line:
                            if ch not in ("\r", "\n", "\t"):
                                chars.add(ch)
            except OSError:
                pass

    return chars


def main() -> int:
    ap = argparse.ArgumentParser(description="Subset a Noto Sans CJK SC into the wasm CJK fallback.")
    ap.add_argument("--base", required=True, help="path to the full Noto Sans CJK SC OTF")
    ap.add_argument("--out", required=True, help="output subset OTF path")
    args = ap.parse_args()

    from fontTools.subset import Options, Subsetter
    from fontTools.ttLib import TTFont

    repo_root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    chars = build_charset(repo_root)

    opts = Options()
    opts.desubroutinize = True
    opts.name_IDs = ["*"]        # keep the name table so the family name is preserved
    opts.name_legacy = True
    opts.recalc_bounds = True
    opts.drop_tables = []        # keep default table set (CFF/cmap/etc.)
    opts.notdef_outline = True
    opts.glyph_names = False     # numeric glyph names → smaller
    # This is a horizontal-text FALLBACK font: drop vertical-writing, width, ruby and
    # regional-alternate (locl) features so the SC default glyph is used and the glyph
    # count (and file size) stays small. The fullwidth/halfwidth Unicode blocks are kept
    # directly in the charset, so the fwid/hwid GSUB features are not needed.
    opts.layout_features = ["ccmp", "mark", "mkmk", "kern"]

    ss = Subsetter(options=opts)
    ss.populate(text="".join(chars))

    font = TTFont(args.base)
    ss.subset(font)

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    font.save(args.out)

    out_font = TTFont(args.out)
    family = ""
    for rec in out_font["name"].names:
        if rec.nameID == 1:
            family = rec.toUnicode()
            break
    print(f"requested chars : {len(chars)}")
    print(f"glyphs in subset: {out_font['maxp'].numGlyphs}")
    print(f"output          : {args.out} ({os.path.getsize(args.out):,} bytes)")
    print(f"family name (id1): {family!r}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
