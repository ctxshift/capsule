import { expect, it } from 'vitest';
import {
  compileQuestPrerequisites,
  type QuestConditionValue,
  type QuestPrerequisitesValue,
} from '../src/quest-prerequisites';

const node = (
  id: string,
  kind: QuestConditionValue['kind'],
  subject = '',
  children: string[] = [],
): QuestConditionValue => ({ id, kind, subject, children });
const refs = {
  quests: new Set(['arrival', 'hunt', 'survey']),
  facts: new Set(['trained']),
};
const state = (
  completed: string[] = [],
  active: string[] = [],
  trained = false,
) => ({
  completed: new Set(completed),
  active: new Set(active),
  facts: new Map([['trained', trained]]),
});
const expression = (
  ...nodes: QuestConditionValue[]
): QuestPrerequisitesValue => ({ version: 1, root: nodes[0].id, nodes });

it('combines completion, active attempts and facts without treating ready as completed', () => {
  const rule = compileQuestPrerequisites(
    expression(
      node('root', 'all', '', ['choice', 'trained', 'not-active']),
      node('choice', 'any', '', ['hunt', 'survey']),
      node('hunt', 'quest-completed', 'hunt'),
      node('survey', 'quest-completed', 'survey'),
      node('trained', 'fact', 'trained'),
      node('not-active', 'not', '', ['active']),
      node('active', 'quest-active', 'arrival'),
    ),
    refs,
  );
  expect(rule.evaluate(state([], ['hunt'], true))).toBe(false);
  expect(rule.evaluate(state(['hunt'], [], false))).toBe(false);
  expect(rule.evaluate(state(['hunt'], ['arrival'], true))).toBe(false);
  expect(rule.evaluate(state(['hunt'], [], true))).toBe(true);
  expect(rule.evaluate(state(['survey'], [], true))).toBe(true);
});

it('accepts shared subexpressions and immutable admitted definitions', () => {
  const source = expression(
    node('root', 'all', '', ['a', 'b']),
    node('a', 'not', '', ['leaf']),
    node('b', 'not', '', ['leaf']),
    node('leaf', 'fact', 'trained'),
  );
  const rule = compileQuestPrerequisites(source, refs);
  expect(rule.evaluate(state())).toBe(true);
  expect(Object.isFrozen(rule.definition.nodes[0].children)).toBe(true);
  expect(rule.definition).not.toBe(source);
});

it('requires explicit facts even beneath negation or a short-circuited branch', () => {
  const rule = compileQuestPrerequisites(
    expression(
      node('root', 'any', '', ['yes', 'not']),
      node('yes', 'always'),
      node('not', 'not', '', ['fact']),
      node('fact', 'fact', 'trained'),
    ),
    refs,
  );
  expect(() => rule.evaluate({ ...state(), facts: new Map() })).toThrow(
    'Missing boolean fact',
  );
});

it('reuses the same vocabulary with another game catalog', () => {
  const rule = compileQuestPrerequisites(
    expression(
      node('unlock', 'all', '', ['rescue', 'permit']),
      node('rescue', 'quest-completed', 'example.quest'),
      node('permit', 'fact', 'pilot.licensed'),
    ),
    {
      quests: new Set(['example.quest']),
      facts: new Set(['pilot.licensed']),
    },
  );
  expect(
    rule.evaluate({
      completed: new Set(['example.quest']),
      active: new Set(),
      facts: new Map([['pilot.licensed', true]]),
    }),
  ).toBe(true);
});

it.each([
  [
    'duplicate',
    expression(node('root', 'always'), node('root', 'always')),
    'Duplicate',
  ],
  [
    'missing child',
    expression(node('root', 'not', '', ['missing'])),
    'Missing condition',
  ],
  ['cycle', expression(node('root', 'not', '', ['root'])), 'Condition cycle'],
  [
    'unreachable',
    expression(node('root', 'always'), node('other', 'always')),
    'Unreachable',
  ],
  ['empty all', expression(node('root', 'all')), 'arity'],
  ['empty any', expression(node('root', 'any')), 'arity'],
  ['double not', expression(node('root', 'not', '', ['a', 'b'])), 'arity'],
  ['repeated child', expression(node('root', 'all', '', ['a', 'a'])), 'arity'],
  [
    'operator subject',
    expression(node('root', 'not', 'ignored', ['a'])),
    'arity',
  ],
  ['leaf children', expression(node('root', 'fact', 'trained', ['a'])), 'Leaf'],
  [
    'unknown quest',
    expression(node('root', 'quest-completed', 'missing')),
    'Unknown quest',
  ],
  ['unknown fact', expression(node('root', 'fact', 'missing')), 'Unknown fact'],
  ['always subject', expression(node('root', 'always', 'ignored')), 'subject'],
])('rejects malformed authoring: %s', (_, value, message) => {
  expect(() => compileQuestPrerequisites(value, refs)).toThrow(message);
});

it('rejects unsupported versions, vocabulary and wire bounds', () => {
  const valid = expression(node('root', 'always'));
  expect(() =>
    compileQuestPrerequisites({ ...valid, version: 2 }, refs),
  ).toThrow('version');
  expect(() => compileQuestPrerequisites({ ...valid, root: '' }, refs)).toThrow(
    'Missing condition',
  );
  expect(() =>
    compileQuestPrerequisites(
      { ...valid, nodes: [{ ...valid.nodes[0], kind: 'script' }] },
      refs,
    ),
  ).toThrow('Unsupported condition');
  expect(() =>
    compileQuestPrerequisites(
      { ...valid, nodes: [{ ...valid.nodes[0], id: 'x'.repeat(129) }] },
      refs,
    ),
  ).toThrow();
});
