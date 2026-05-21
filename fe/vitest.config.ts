import { fileURLToPath } from 'node:url'
import { mergeConfig, defineConfig, configDefaults } from 'vitest/config'
import viteConfig from './vite.config'
import { testProjects, testRoot } from './vitest.projects'

export default defineConfig((configEnv) =>
  mergeConfig(
    typeof viteConfig === 'function' ? viteConfig(configEnv) : viteConfig,
    {
      resolve: {
        alias: {
          '@': fileURLToPath(new URL('./src', import.meta.url)),
        },
      },
      test: {
        execArgv: ['--no-warnings'],
        projects: testProjects,
        // Increase global test timeout to reduce flaky timeouts in CI/local runs
        testTimeout: 10000,
        // Exclude e2e and cypress test files from unit test runs
        exclude: [...configDefaults.exclude, 'e2e/**', 'cypress/**'],
        root: testRoot,
        coverage: {
          provider: 'v8',
          reportsDirectory: 'coverage/unit',
          reporter: ['text', 'html', 'lcov'],
          include: ['src/**/*.{ts,vue}'],
          exclude: [
            ...configDefaults.coverage.exclude,
            'src/**/test/**',
            'src/test/**',
            'src/**/*.d.ts',
            'src/env.d.ts',
            'src/main.ts',
          ],
          thresholds: {
            branches: 30,
            functions: 30,
            lines: 40,
            statements: 40,
          },
        },
      },
    },
  ),
)
