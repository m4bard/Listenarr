# Frontend Test Layout

Vitest specs live in `test/` folders next to the code they exercise:

- Components: `src/components/<area>/test/*.spec.ts`
- Views: `src/views/<area>/test/*.spec.ts`
- Stores, services, composables, and utilities: `src/<area>/test/*.spec.ts`

Use `*.spec.ts` for frontend tests; Vitest only discovers that extension under
`src/**/test/`. Specs run in jsdom by default. Use `*.node.spec.ts` only for
tests that intentionally run without browser globals. Test infrastructure in
`src/test` is opt-in only: factories, explicit mocks, local stubs, and mount
helpers. Shared specs under `src/test` are limited to app-shell, framework, and
smoke coverage. Vitest does not load a global setup file.

Rules:

- Do not add `setupFiles` to Vitest config.
- Do not add global app-service mocks.
- Do not add global browser/API monkeypatches.
- Put API, SignalR, toast, storage, and component stubs directly in the spec
  that needs them, or import explicit helpers from `src/test`.
- Keep test data in factories when the same shape appears in multiple specs.
- Run `npm run type-check:test` before changing shared helpers or fixture
  factories.
- Run `npm run verify` before submitting frontend test infrastructure changes.
