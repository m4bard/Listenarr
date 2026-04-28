# Linting Standards

Listenarr uses a small set of shared checks so local hooks and CI describe the same expectations.

## Formatting

- Root whitespace and line endings are defined in `.editorconfig`.
- Git normalizes text files to LF through `.gitattributes`, with Windows script files kept as CRLF.
- C# formatting is checked with `dotnet format listenarr.slnx --no-restore --verify-no-changes`.
- Frontend formatting is defined by `fe/.editorconfig` and `fe/.prettierrc.json`.

## Frontend linting

- ESLint is configured in `fe/eslint.config.ts`.
- Vue uses the essential Vue rules.
- TypeScript uses the Vue TypeScript recommended rules.
- Vitest and Cypress files use their matching recommended plugin rules.
- Prettier owns formatting; ESLint formatting rules stay disabled through the Prettier skip config.

## Commands

- `npm run lint` runs the project lint checks that are safe to run across the current tree.
- `npm run lint:staged` runs the pre-commit staged-file checks.
- `npm run format:check` runs the strict full formatting check.
- `npm run format` applies backend and frontend formatting.
- `npm test` runs backend and frontend tests.

## Hooks

- `pre-commit` runs staged lint and format checks so changed files follow the standard.
- `pre-push` runs the full test suite.

The full formatting check is intentionally separate because the current tree still needs a dedicated normalization pass before it can be used as a required CI gate.
