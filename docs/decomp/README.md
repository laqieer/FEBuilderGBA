# FEBuilder decomp-project manifest templates

Drop a `febuilder.project.json` at the **root of your decomp tree** (next to the
`Makefile`) to opt the tree into FEBuilder-managed builds and source-backed edits.
Without a manifest a decomp tree is still *detected* and opens **read-only**, but
`--build-project` returns `NotOptedIn` and source-backed table writes are disabled.

## FE8J (`fireemblem8j`) — [`febuilder.project.fe8j.json`](febuilder.project.fe8j.json)

```json
{
  "schemaVersion": 1,
  "name": "Fire Emblem: The Sacred Stones (JP) decomp (fireemblem8j)",
  "builtRom": "fireemblem8.gba",
  "forceVersion": "FE8J",
  "sym": "sym_jp.txt",
  "build": {
    "command": "make",
    "compareTarget": "make compare"
  }
}
```

Copy that file into your `fireemblem8j` checkout as **`febuilder.project.json`**.
It enables:

| Field | Effect |
|-------|--------|
| `builtRom: "fireemblem8.gba"` | The ROM `make` produces (the JP `Makefile` uses `ROM := fireemblem8.gba`). |
| `forceVersion: "FE8J"` | Pins the JP variant for the reload seam so the rebuilt ROM is loaded as FE8J. |
| `sym: "sym_jp.txt"` | Points the symbol resolver at the JP `sym_jp.txt` (linker-assignment table). It is also auto-discovered, so this is belt-and-suspenders. |
| `build.command: "make"` | Presence of a `build` section flips `IsBuildEnabled` on → `--build-project --yes` returns `Ok` instead of `NotOptedIn`, and source-backed writes are enabled. |
| `build.compareTarget: "make compare"` | The declared `make compare` target for a byte-identical rebuild check. It is **parsed and exposed** (`DecompProject.CompareTarget`) for reference/tooling; `--build-project` currently runs only `command` (run the compare target yourself). |

> This template is a **FEBuilderGBA-side** artifact — you drop it into *your own*
> decomp checkout. Do **not** commit it into the decomp repo.

To disable managed builds again without deleting the manifest, set
`"build": { "enabled": false }`.
