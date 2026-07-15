# GUI Strategy

FEBuilderGBA ships **two** graphical front-ends over the same shared engine
(`FEBuilderGBA.Core` + `FEBuilderGBA.CLI` + `FEBuilderGBA.SkiaSharp`). They are
held to **different standards** on purpose.

## Policy

| GUI | Project | Status | What it accepts |
|-----|---------|--------|-----------------|
| **WinForms GUI** | `FEBuilderGBA` (`net10.0-windows`) | **Stable** — used widely for years | **Bug fixes only. No new features.** |
| **Avalonia GUI** | `FEBuilderGBA.Avalonia` (`net10.0`, cross-platform) | **Preview** — not yet widely relied upon | **All new GUI features ship here.** Bug fixes too. |

## Why

- **WinForms is the production workhorse.** A huge existing user base depends on
  it every day, and its behaviour is deeply name-driven (designer control names
  wire editor logic via `InputFormRef.MakeLinkEvent`), which makes changes
  disproportionately regression-prone. Its goal is therefore **stability**: we
  change it only to fix bugs, never to add features.
- **Avalonia is still in preview.** It is cross-platform (Windows/Linux/macOS,
  plus an Android head) and not yet the primary tool for most users, so a
  regression there has a **limited blast radius**. That makes it the right place
  to build and iterate on **new features**, including cross-platform conveniences
  such as Help → Check for Updates (a safe release check that opens the downloads
  page, rather than WinForms' Windows-only self-replacing updater).

## What this means in practice

- **A new feature request → the Avalonia GUI.** Implement it in
  `FEBuilderGBA.Avalonia` (backed by `FEBuilderGBA.Core` where logic is shared).
  Do **not** add it to the WinForms GUI.
- **A WinForms issue is actioned only when it is a bug/regression** — restoring
  or correcting existing behaviour. Feature requests targeting WinForms are
  declined (redirect the requester to the Avalonia GUI and/or
  [Ideas Discussions](https://github.com/laqieer/FEBuilderGBA/discussions/categories/ideas)).
- **Core / CLI are not restricted by this policy.** `FEBuilderGBA.Core`,
  `FEBuilderGBA.CLI`, and `FEBuilderGBA.SkiaSharp` are shared, cross-platform,
  and back *both* GUIs — they continue to accept new capabilities. This policy
  is specifically about **GUI feature work**, i.e. new editors/screens/UI
  behaviour, which belongs in Avalonia.

## Narrow carve-out — completing an in-flight parity rollout

The policy governs **new** feature work. A rollout that was already **in
flight before the freeze** may be *completed* in WinForms when the maintainer
explicitly asks for it, because finishing an existing, tested pattern across the
last few editors is lower-risk than leaving it half-applied.

- **FE-Repo browse buttons.** The FE-Repo Resource Browser buttons were rolled
  out across the graphics editors in #1380/#1394 (2026-06-24), *before* the
  freeze (#1800, 2026-07-04). Completing that parity on a remaining graphics
  editor (e.g. the Battle Animations editor, #1807 — reopened by the maintainer
  for parity) is treated as **finishing the in-flight rollout**, not as opening
  WinForms to new feature work. Such a carve-out must be (a) explicitly
  requested by the maintainer and (b) recorded here.

- **Content repository setup wizard.** Issue #1814 was explicitly maintainer-requested to ship to **both** WinForms and Avalonia despite the WinForms feature freeze, because first-run setup of `patch2`, FE-Repo, and FE-Repo-Music is a cross-GUI onboarding and supportability requirement. The WinForms surface for this carve-out is limited to the content-repo setup wizard entry points; it does not reopen WinForms for unrelated feature work.

## For reviewers & AI agents

When triaging an issue or scoping a PR:

1. **Is it a new GUI feature?** → target `FEBuilderGBA.Avalonia`. Reject/redirect
   if it proposes adding the feature to `FEBuilderGBA` (WinForms).
2. **Is it a WinForms bug/regression?** → fixing it is in scope.
3. **Is it Core/CLI/SkiaSharp?** → not covered by this policy; scope normally.

This policy is also recorded across the repository's contributor docs:
[`README.md`](../README.md), [`CONTRIBUTING.md`](../CONTRIBUTING.md),
[`DEVELOPMENT-WORKFLOW.md`](../DEVELOPMENT-WORKFLOW.md),
[`.github/SUPPORT.md`](../.github/SUPPORT.md), `CLAUDE.md`, and
`.github/copilot-instructions.md`.
