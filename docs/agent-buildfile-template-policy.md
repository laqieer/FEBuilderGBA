# Base-ROM / Template Policy for Agent-Generated Content

**Purpose.** When FEBuilderGBA is used as an **agent / LLM backend** to *generate* buildfile-style
mods (see the [emit-recipe path, #1935](https://github.com/laqieer/FEBuilderGBA/issues/1935) and
[CLI-as-backend, #1937](https://github.com/laqieer/FEBuilderGBA/issues/1937)), the generated output
has to start from *some* base project. This document sets the policy for choosing that base and for
incorporating engine features — so generated content is **neutral, redistributable, and correctly
attributed**.

Tracks [#1938](https://github.com/laqieer/FEBuilderGBA/issues/1938); spun out of discussion
[#1930](https://github.com/laqieer/FEBuilderGBA/discussions/1930).

> **This is a policy, not an implementation.** It does not build a generator, ship a scaffold, or
> curate an ASM feature set. It states constraints that the emit-recipe (#1935) and CLI-backend
> (#1937) work must honor; the *how* lives in those issues.

## The choice: neutral base vs. fan-mod base

[fe-infinity](https://github.com/i-am-neon/fe-infinity) (an LLM FE8 generator) builds on
[**Legends of Avenir**](https://github.com/Snakey11/Legends-of-Avenir) — a specific, complete fan
mod. That's *batteries-included* (it inherits LoA's large `ASM/` engine library — SkillSystem,
HybridClasses, CharacterCreator, ScriptedBattles, plus an `Avenir/`-specific folder), but it carries
real costs for a **general-purpose generator**:

- **Attribution / consent** — building on someone's actual creative fan project (its ASM *and* its
  identity) is thornier than starting neutral.
- **Coupling & baggage** — tied to LoA's structure/offsets/custom content; hard to retarget to
  vanilla FE8; ships content the generator doesn't need.
- **Clean-room / redistributability** — generated output ideally stands on a neutral base so it's
  clean and shareable.

Contrast [**EasyBuildfile**](https://github.com/MysticOCE/EasyBuildfile) — a bare FE8U template
(clean-ROM + minimal make variants → UPS). Minimal, **neutral** starting point.

> The same FEB-vs-buildfile tradeoff, one level up: LoA-as-base is the "batteries-included" choice;
> EasyBuildfile is the "clean minimal slate" choice.

## Legal posture (read this first)

**Neutrality is not the same as a license grant.** A base being "neutral" (no bundled fan-mod
identity/content) says nothing about whether you're *legally permitted* to copy it.

At the time of writing, **none of the three projects above declares an open-source license** —
`MysticOCE/EasyBuildfile`, `Snakey11/Legends-of-Avenir`, and `i-am-neon/fe-infinity` each report **no
`LICENSE` file / no detected license**. Under default copyright (Berne Convention; GitHub's own Terms
of Service for repos without a license), **"no license" means all rights reserved** — you may view
the repo, but you do **not** automatically have permission to copy, modify, or redistribute its
contents.

Therefore this policy applies **uniformly**:

- **Assume all-rights-reserved** for any third-party base/asset/engine feature **absent an explicit
  license or written permission**. EasyBuildfile gets the **same** "reference, attribute, seek
  explicit permission before vendoring" treatment as Legends of Avenir — it is **not** inherently
  "safer" to copy just because it's neutral.
- Community norms are informal, usually per-asset **F2U / F2E** ("free to use / free to edit",
  credit-required, frequently non-commercial). **F2U/F2E is not a blanket grant** and is not a
  substitute for a stated license — honor the specific terms the creator stated, and when in doubt,
  ask.
- **Prefer genuinely permissive licenses when they actually exist** (MIT / BSD / Unlicense / CC0);
  otherwise fall back to the assume-ARR posture above.
- **Never imply ROM redistribution.** Buildfiles apply onto a **user-supplied, legally-obtained clean
  ROM** — the base ROM is never bundled or distributed by FEBuilderGBA or its generated output.

## Policy

1. **Default to a neutral base template** (EasyBuildfile-style: a clean, minimal FE8U buildfile that
   applies onto a user-supplied clean ROM) rather than a fan mod — for its **neutrality** (no
   inherited creative identity/content), *with* the legal posture above still applied to the template
   itself.
2. **Pull engine features à la carte, with attribution.** Add engine features (SkillSystem, class
   expansion, etc.) only as **opt-in**, **referenced** (not vendored) unless permission is explicit,
   and each recorded per the attribution schema below.
3. **Generated output should stand on a neutral base** so it is clean-room and shareable — do not
   inherit a fan mod's identity/content wholesale.
4. **Emit-recipe (#1935) targets a neutral, user-supplied clean ROM**, not a mod-specific base.

## If FEBuilderGBA ships a scaffold / templates

*(Conditional — FEBuilderGBA does not ship one today; this governs the case if it ever does.)*

- Ship **only neutral templates** — never derived from a fan mod.
- Every shipped or opt-in asset carries **source + license/permission metadata** (the attribution
  schema below).
- Optional engine-feature scaffolds are **à-la-carte, attributed, and permission-verified** —
  **referenced, not vendored**, unless the upstream permission is explicit and recorded.
- Do not bundle a base ROM; scaffolds operate on a user-supplied, legally-obtained clean ROM.

## Attribution schema

For each opt-in base/feature the generator (or a shipped scaffold) references, record **at least**
these fields. *This is a schema/template — not a curated list of recommended ASM features (that
curation is explicitly out of scope for this policy).*

| Field | Meaning |
|---|---|
| Feature / asset | What is being used (e.g. "SkillSystem"). |
| Upstream source | Repo/author/URL it comes from. |
| License / permission status | Declared license, or "no license (assume ARR)", or the specific F2U/F2E terms + a link to where the creator stated them. |
| Referenced vs. vendored | Whether it's pulled at build time (referenced) or copied into the tree (vendored — allowed **only** with explicit permission). |
| Attribution text | The credit line the generated output must carry. |

## Relationship

- Discussion [#1930](https://github.com/laqieer/FEBuilderGBA/discussions/1930); emit-recipe
  [#1935](https://github.com/laqieer/FEBuilderGBA/issues/1935); CLI-backend
  [#1937](https://github.com/laqieer/FEBuilderGBA/issues/1937).
- [#1941](https://github.com/laqieer/FEBuilderGBA/issues/1941) discusses LoA's `ASM/SkillSystem` as an
  architectural *pattern* ("friendlier engine data structures"). **Note:** citing a project's design
  as prior art does **not** imply permission to vendor its code — the attribution/permission rules
  above still apply.

## Out of scope

- Building the generator or a scaffold.
- Curating a specific ASM/engine feature set (only the *policy/schema* for attribution is here).
- Low-level recipe formats / CLI verbs (owned by #1935 / #1937 / #1939).
