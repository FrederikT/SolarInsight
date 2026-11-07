import tseslint from 'typescript-eslint'
export default [
  { ignores: ['dist/**', 'test-results/**', 'playwright-report/**'] },
  ...tseslint.configs.recommended,
]

