import js from '@eslint/js';
import { defineConfig } from 'eslint/config';
import prettier from 'eslint-config-prettier';
import hooks from 'eslint-plugin-react-hooks';
import globals from 'globals';
import ts from 'typescript-eslint';

export default defineConfig([
  {
    ignores: [
      '**/node_modules/**',
      '**/bin/**',
      '**/obj/**',
      '**/dist/**',
      '.local/**',
    ],
  },
  {
    files: [
      'tools/**/*.{js,mjs,ts,tsx}',
      'addons/**/*.{js,mjs,ts,tsx}',
      'packages/**/*.{js,mjs,ts,tsx}',
      'examples/**/*.{js,mjs,ts,tsx}',
      'eslint.config.mjs',
    ],
    extends: [js.configs.recommended],
    languageOptions: { globals: globals.node },
    rules: { eqeqeq: ['error', 'always'], curly: ['error', 'all'] },
  },
  {
    files: [
      'tools/**/*.{ts,tsx}',
      'addons/**/*.{ts,tsx}',
      'packages/**/*.{ts,tsx}',
      'examples/**/*.{ts,tsx}',
    ],
    extends: [ts.configs.recommended],
    rules: {
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
      ],
    },
  },
  {
    // Private host ABI/reconciler boundaries predate the typed addon API.
    // Keep this exception out of components, screens and public declarations.
    files: [
      'tools/capsule-react/src/host.ts',
      'tools/capsule-react/src/scheduler.ts',
      'tools/capsule-react/benchmarks/hello.ts',
      'tools/capsule-react/benchmarks/*-adapter.ts',
    ],
    rules: { '@typescript-eslint/no-explicit-any': 'off' },
  },
  {
    // Benchmark prefixes are concatenated with host.ts by build-*.mjs;
    // their bindings are consumed by that appended code.
    files: ['tools/capsule-react/benchmarks/*-adapter.ts'],
    rules: { '@typescript-eslint/no-unused-vars': 'off' },
  },
  {
    files: [
      'tools/**/*.{ts,tsx}',
      'addons/**/*.{ts,tsx}',
      'packages/**/*.{ts,tsx}',
      'examples/**/*.{ts,tsx}',
    ],
    plugins: { 'react-hooks': hooks },
    rules: {
      'react-hooks/rules-of-hooks': 'error',
      'react-hooks/exhaustive-deps': 'error',
    },
  },
  prettier,
]);
