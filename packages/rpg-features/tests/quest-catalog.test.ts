import { expect, it } from 'vitest';
import {
  validateQuestCatalog,
  type QuestCatalogValue,
} from '../src/quest-catalog';
const refs = {
  actors: new Set(['guide']),
  targets: new Set(['beast']),
  worlds: new Set(['valley']),
  rewards: new Set(['supplies']),
};
function fixture(): QuestCatalogValue {
  return {
    formatVersion: 1,
    facts: [],
    definitions: [
      {
        id: 'hunt',
        revision: 1,
        title: 'Hunt',
        description: '',
        prerequisites: {
          version: 1,
          root: 'root',
          nodes: [{ id: 'root', kind: 'always', subject: '', children: [] }],
        },
        objectives: [
          {
            id: 'defeat',
            event: 'defeat',
            target: 'beast',
            world: 'valley',
            required: 2,
            description: '',
            after: [],
          },
        ],
        completion: 'report',
        repeat: 'once',
        reward: 'supplies',
      },
    ],
    bindings: [
      { quest: 'hunt', role: 'offer', actor: 'guide' },
      { quest: 'hunt', role: 'report', actor: 'guide' },
    ],
  };
}
it('admits a neutral catalog with several independent quest roles', () => {
  const catalog = fixture();
  const second = { ...catalog.definitions[0], id: 'patrol' };
  validateQuestCatalog(
    {
      ...catalog,
      definitions: [...catalog.definitions, second],
      bindings: [
        ...catalog.bindings,
        ...catalog.bindings.map((b) => ({ ...b, quest: 'patrol' })),
      ],
    },
    refs,
  );
});
it('rejects missing references, unsupported policy and invalid counts', () => {
  const c = fixture(),
    q = c.definitions[0];
  for (const change of [
    { revision: 0 },
    { reward: 'missing' },
    { repeat: 'daily' },
    { completion: 'automatic' },
    { objectives: [] },
    { objectives: [{ ...q.objectives[0], required: 0 }] },
    { objectives: [{ ...q.objectives[0], after: ['missing'] }] },
  ])
    expect(() =>
      validateQuestCatalog({ ...c, definitions: [{ ...q, ...change }] }, refs),
    ).toThrow();
  expect(() => validateQuestCatalog({ ...c, bindings: [] }, refs)).toThrow(
    'Missing quest role',
  );
  expect(() =>
    validateQuestCatalog({ ...c, definitions: [q, q] }, refs),
  ).toThrow('Duplicate');
});
it('rejects between-quest and objective activation cycles', () => {
  const c = fixture(),
    q = c.definitions[0];
  expect(() =>
    validateQuestCatalog(
      {
        ...c,
        definitions: [
          {
            ...q,
            prerequisites: {
              version: 1,
              root: 'self',
              nodes: [
                {
                  id: 'self',
                  kind: 'quest-completed',
                  subject: 'hunt',
                  children: [],
                },
              ],
            },
          },
        ],
      },
      refs,
    ),
  ).toThrow('cycle');
  expect(() =>
    validateQuestCatalog(
      {
        ...c,
        definitions: [
          { ...q, objectives: [{ ...q.objectives[0], after: ['defeat'] }] },
        ],
      },
      refs,
    ),
  ).toThrow('cycle');
});
