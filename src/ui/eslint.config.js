import antfu from '@antfu/eslint-config'

export default antfu({
  vue: true,
  typescript: true,
  formatters: false,
  stylistic: {
    indent: 2,
    quotes: 'single',
    semi: false,
  },
  ignores: [
    '**/dist/**',
    '**/node_modules/**',
    '**/generated/**',
    '**/.output/**',
    '**/src-tauri/target/**',
    '**/capacitor-plugins/**/ios/**',
    '**/capacitor-plugins/**/android/**',
    'packages/seren-sdk/src/types/generated/**',
  ],
  rules: {
    'no-console': ['warn', { allow: ['warn', 'error', 'info'] }],
    'ts/consistent-type-definitions': ['error', 'interface'],
    'vue/multi-word-component-names': 'off',
    'unused-imports/no-unused-vars': [
      'error',
      { argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
    ],
  },
})
