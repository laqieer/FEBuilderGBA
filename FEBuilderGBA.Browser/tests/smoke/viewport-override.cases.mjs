// Shared case table for parseViewportOverride() (viewport-override.mjs), imported by BOTH:
//   - smoke.mjs's fast, dependency-free self-check (runViewportParserSelfCheck), run before any
//     server/browser starts — the only coverage `.github/workflows/pages.yml` actually exercises,
//     since it invokes this script directly and never `node --test`.
//   - viewport-override.test.mjs's `node:test` suite.
// so the two coverage paths can never silently diverge.

export const CASES = [
  {
    name: 'both env vars unset -> default compact+acceptance matrix',
    wRaw: undefined,
    hRaw: undefined,
    expect: 'accept',
    expectedPlan: [
      { width: 600, height: 500, tag: 'compact', owning: false },
      { width: 1920, height: 852, tag: 'acceptance', owning: true },
    ],
  },
  {
    name: 'valid explicit pair (600x500) -> single explicit viewport',
    wRaw: '600',
    hRaw: '500',
    expect: 'accept',
    expectedPlan: [{ width: 600, height: 500, tag: 'explicit', owning: true }],
  },
  {
    name: 'valid explicit pair (1920x852) -> single explicit viewport',
    wRaw: '1920',
    hRaw: '852',
    expect: 'accept',
    expectedPlan: [{ width: 1920, height: 852, tag: 'explicit', owning: true }],
  },
  {
    name: 'fractional width (600.5x500) rejects with a positive-integers message',
    wRaw: '600.5',
    hRaw: '500',
    expect: 'reject',
    errorIncludes: 'positive integers',
  },
  {
    name: 'fractional height (600x500.5) rejects with a positive-integers message',
    wRaw: '600',
    hRaw: '500.5',
    expect: 'reject',
    errorIncludes: 'positive integers',
  },
  {
    name: 'zero width rejects with a positive-integers message',
    wRaw: '0',
    hRaw: '500',
    expect: 'reject',
    errorIncludes: 'positive integers',
  },
  {
    name: 'zero height rejects with a positive-integers message',
    wRaw: '600',
    hRaw: '0',
    expect: 'reject',
    errorIncludes: 'positive integers',
  },
  {
    name: 'negative width rejects with a positive-integers message',
    wRaw: '-600',
    hRaw: '500',
    expect: 'reject',
    errorIncludes: 'positive integers',
  },
  {
    name: 'negative height rejects with a positive-integers message',
    wRaw: '600',
    hRaw: '-500',
    expect: 'reject',
    errorIncludes: 'positive integers',
  },
  {
    name: 'non-finite width (non-numeric string) rejects with a positive-integers message',
    wRaw: 'not-a-number',
    hRaw: '500',
    expect: 'reject',
    errorIncludes: 'positive integers',
  },
  {
    name: 'non-finite height (Infinity) rejects with a positive-integers message',
    wRaw: '600',
    hRaw: 'Infinity',
    expect: 'reject',
    errorIncludes: 'positive integers',
  },
  {
    name: 'incomplete pair (width only set) rejects with a both-set-together message',
    wRaw: '600',
    hRaw: undefined,
    expect: 'reject',
    errorIncludes: 'must both be set together',
  },
  {
    name: 'incomplete pair (height only set) rejects with a both-set-together message',
    wRaw: undefined,
    hRaw: '500',
    expect: 'reject',
    errorIncludes: 'must both be set together',
  },
];
