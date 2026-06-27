// Conventional-commit rules for FEBuilderGBA.
//
// Used by the "Lint PR / Commits" GitHub Actions workflow
// (.github/workflows/pr-title-lint.yml, job `commits`) to validate every commit
// in a pull request, so the future tag-triggered changelog generator (#1632)
// consumes a clean, conventional-commit corpus. See issue #1647 and
// docs/DEPLOYMENT.md ("Commit & PR Title Convention").
//
// This config intentionally relaxes the stock @commitlint/config-conventional
// rules to match the project's long-standing style:
//   - Scopes are free-form (avalonia, core, gap-sweep, ...) and optional.
//   - Subject case / full-stop are NOT enforced.
//   - Commit-body and footer line-length limits are disabled (the project
//     records long prompts and tool footers in commit bodies).
//
// The allowed `type-enum` list MUST stay in sync with the `types:` input of the
// `pr-title` job in .github/workflows/pr-title-lint.yml.

export default {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'type-enum': [
      2,
      'always',
      [
        'build',
        'chore',
        'ci',
        'docs',
        'feat',
        'fix',
        'perf',
        'refactor',
        'revert',
        'style',
        'test',
      ],
    ],
    // Scopes are free-form and optional in this repo.
    'scope-enum': [0],
    'scope-empty': [0],
    // Do not enforce subject case; matches existing commit style.
    'subject-case': [0],
    // Long bodies/footers (original prompts, tool footers) are expected.
    'body-max-line-length': [0],
    'footer-max-line-length': [0],
  },
};
