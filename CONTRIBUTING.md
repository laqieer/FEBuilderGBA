# Contributing to FEBuilderGBA

## Documentation Guidelines

Documentation is a first-class deliverable. Every code change should include corresponding doc updates.

### What to Update

| Change Type | Required Doc Updates |
|------------|---------------------|
| New CLI command | CLAUDE.md (Command-Line Tools section), --help text |
| New Avalonia feature | CLAUDE.md (Architecture Overview if structural) |
| New Core API | CLAUDE.md (Core Data Access Pattern) |
| Bug fix | None required unless it changes behavior |
| New config key | CLAUDE.md (Configuration Files section) |
| Build/CI change | CLAUDE.md (Build & Development Commands) |

### Where Documentation Lives

- **CLAUDE.md** — Primary project reference (architecture, commands, patterns)
- **DEVELOPMENT-WORKFLOW.md** — Development process and PR workflow
- **README.md** — User-facing overview and quick start
- **docs/** — Detailed specs and reports

### Doc Review in PRs

Every PR reviewer (human or automated) should verify:
1. New features have corresponding CLAUDE.md updates
2. Changed CLI commands have updated --help text
3. README reflects any user-facing changes
4. No stale documentation references removed features

### How to Propose Doc Changes

1. File an issue with the `documentation` label
2. Or include doc updates in your feature PR
3. For large doc restructuring, open a dedicated docs PR

## Code Contribution Guidelines

See [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) for the mandatory development workflow including plan review, implementation, and PR review gates.

## Commit Identity

When using Claude Code automation, commit as `laqieer <laqieer@126.com>`. Human contributors use their own identity.
