export const release = {
  version: '0.1.0',
  npmScope: '@capsule-sdk',
  repository: 'https://github.com/ctxshift/capsule',
  tools: { node: '25.6.1', bun: '1.3.6', dotnet: '10.0.401' },
  runtime: {
    unity: '6000.6.3f1',
    framework: 'netstandard2.1',
    platforms: ['win-x64'],
    wasmtime: '48.0.2',
    quickjs: '0.17.0',
    yoga: '3.2.1',
    wireMagic: 'PUI1',
    wireVersions: [1, 2],
    bridgeMetricsAbi: 1,
  },
  npm: [
    {
      path: 'packages/capsule-features',
      files: ['src', 'README.md'],
      promote: ['@types/node'],
    },
    {
      path: 'packages/capsule-react',
      files: ['src', 'text-edit-vectors.json', 'README.md'],
      promote: ['@types/react'],
    },
    {
      path: 'packages/capsule-workshop',
      files: ['src', 'tsconfig.addon.json', 'README.md'],
      promote: ['@types/node', '@types/react'],
    },
    ...[
      'rpg-features',
      'mmo-features',
      'skill-system-features',
      'inventory-feature',
    ].map((name) => ({
      path: 'packages/' + name,
      files: ['src', 'generated', 'README.md'],
    })),
    {
      path: 'tools/capsule-react',
      files: [
        'src',
        'scripts/build.mjs',
        'scripts/package-paths.mjs',
        'benchmarks',
        'licenses',
        'README.md',
      ],
      promote: ['esbuild'],
      sdk: true,
    },
  ],
};
