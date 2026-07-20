// Shared case table for deriveSidecar()/getViewportArtifactPaths() (viewport-artifacts.mjs),
// imported by BOTH:
//   - smoke.mjs's fast, dependency-free self-check (runArtifactContractSelfCheck), run before any
//     server/browser starts — the ONLY coverage `.github/workflows/pages.yml` actually exercises,
//     since it invokes this script directly and never `node --test` (#1998 follow-up, review
//     PRRT_kwDOH0Mc1M6SVA6P: the standalone viewport-artifacts.test.mjs suite alone provides ZERO
//     CI protection — a startup-cleanup regression could merge while every required check stays
//     green).
//   - viewport-artifacts.test.mjs's `node:test` suite.
// so the two coverage paths can never silently diverge, matching the established pattern used by
// layout-metrics-validation.cases.mjs and viewport-override.cases.mjs.

import path from 'node:path';

export const CASES = [
  {
    name: 'deriveSidecar inserts the suffix before the extension, preserving the directory',
    fn: 'deriveSidecar',
    args: [path.join('out', 'web-boot-smoke.png'), 'compact'],
    expected: path.join('out', 'web-boot-smoke.compact.png'),
  },
  {
    name: 'deriveSidecar works with no directory component',
    fn: 'deriveSidecar',
    args: ['web-boot-smoke.png', 'before'],
    expected: 'web-boot-smoke.before.png',
  },
  {
    name: 'deriveSidecar defaults to a .png extension when the base path has none',
    fn: 'deriveSidecar',
    args: ['artifact', 'before'],
    expected: 'artifact.before.png',
  },
  {
    name: 'getViewportArtifactPaths returns exactly the 4 artifacts a single viewport run may produce, mainPath first',
    fn: 'getViewportArtifactPaths',
    args: [path.join('out', 'web-boot-smoke.png')],
    expected: [
      path.join('out', 'web-boot-smoke.png'),
      path.join('out', 'web-boot-smoke.before.png'),
      path.join('out', 'web-boot-smoke.movecost.before.png'),
      path.join('out', 'web-boot-smoke.movecost.png'),
    ],
  },
  {
    name: 'getViewportArtifactPaths derives from an already-sidecar-suffixed mainPath (e.g. the compact viewport)',
    fn: 'getViewportArtifactPaths',
    args: [path.join('scratch', 'derived.compact.png')],
    expected: [
      path.join('scratch', 'derived.compact.png'),
      path.join('scratch', 'derived.compact.before.png'),
      path.join('scratch', 'derived.compact.movecost.before.png'),
      path.join('scratch', 'derived.compact.movecost.png'),
    ],
  },
];
