import { s, type Value } from '@capsule-sdk/features';
import { codec } from '@capsule-sdk/features/codec';
import {
  QuestPrerequisites,
  compileQuestPrerequisites,
} from './quest-prerequisites';

export const QuestObjectiveDefinition = s.record('QuestObjectiveDefinition', {
  id: s.text(128),
  event: s.text(32),
  target: s.text(128),
  world: s.text(128),
  description: s.text(1024),
  required: s.u32,
  after: s.list(s.text(128), 32),
});
export const QuestDefinition = s.record('QuestDefinition', {
  id: s.text(128),
  revision: s.u32,
  title: s.text(512),
  description: s.text(4096),
  prerequisites: QuestPrerequisites,
  objectives: s.list(QuestObjectiveDefinition, 32),
  completion: s.text(32),
  repeat: s.text(32),
  reward: s.text(128),
});
export const QuestBinding = s.record('QuestBinding', {
  quest: s.text(128),
  role: s.text(32),
  actor: s.text(128),
});
export const QuestFact = s.record('QuestFact', {
  id: s.text(128),
  type: s.text(32),
  owner: s.text(128),
  scope: s.text(32),
  lifetime: s.text(32),
});
export interface QuestCatalogValue {
  readonly formatVersion: 1;
  readonly definitions: readonly Value<typeof QuestDefinition>[];
  readonly bindings: readonly Value<typeof QuestBinding>[];
  readonly facts: readonly Value<typeof QuestFact>[];
}
export interface QuestCatalogReferences {
  readonly actors: ReadonlySet<string>;
  readonly rewards: ReadonlySet<string>;
  readonly targets: ReadonlySet<string>;
  readonly worlds: ReadonlySet<string>;
}
export function validateQuestCatalog(
  catalog: QuestCatalogValue,
  refs: QuestCatalogReferences,
): void {
  if (catalog.formatVersion !== 1) throw Error('Unsupported catalog format');
  const unique = (values: readonly string[]) => {
    if (values.some((v) => !v) || new Set(values).size !== values.length)
      throw Error('Duplicate or empty catalog ID');
    return new Set(values);
  };
  const quests = unique(catalog.definitions.map((q) => q.id));
  const facts = unique(catalog.facts.map((f) => f.id));
  const dependencies = new Map<string, readonly string[]>();
  for (const fact of catalog.facts) {
    codec(QuestFact).encode(fact);
    if (
      fact.type !== 'boolean' ||
      !fact.owner ||
      fact.scope !== 'character' ||
      fact.lifetime !== 'persistent'
    )
      throw Error('Unsupported fact declaration');
  }
  for (const quest of catalog.definitions) {
    codec(QuestDefinition).encode(quest);
    if (
      !quest.revision ||
      !quest.title ||
      !['report', 'accept'].includes(quest.completion) ||
      quest.repeat !== 'once' ||
      !refs.rewards.has(quest.reward)
    )
      throw Error('Unsupported quest definition');
    compileQuestPrerequisites(quest.prerequisites, { quests, facts });
    dependencies.set(
      quest.id,
      quest.prerequisites.nodes
        .filter((n) => n.kind.startsWith('quest-'))
        .map((n) => n.subject),
    );
    const objectives = unique(quest.objectives.map((o) => o.id));
    if (!objectives.size) throw Error('Quest requires objectives');
    const activation = new Map<string, readonly string[]>();
    for (const objective of quest.objectives) {
      if (
        !['defeat', 'talk', 'training'].includes(objective.event) ||
        !objective.required ||
        !refs.targets.has(objective.target) ||
        !refs.worlds.has(objective.world)
      )
        throw Error('Invalid objective');
      if (
        quest.completion === 'accept' &&
        (objective.event !== 'talk' ||
          objective.required !== 1 ||
          objective.after.length)
      )
        throw Error('Invalid accept completion');
      if (
        new Set(objective.after).size !== objective.after.length ||
        objective.after.some((id) => !objectives.has(id))
      )
        throw Error('Invalid objective dependency');
      activation.set(objective.id, objective.after);
    }
    rejectCycles(activation);
  }
  rejectCycles(dependencies);
  const bindings = new Set<string>();
  for (const binding of catalog.bindings) {
    codec(QuestBinding).encode(binding);
    const key = JSON.stringify(binding);
    if (
      bindings.has(key) ||
      !quests.has(binding.quest) ||
      !['offer', 'report'].includes(binding.role) ||
      !refs.actors.has(binding.actor)
    )
      throw Error('Invalid quest binding');
    bindings.add(key);
  }
  for (const quest of catalog.definitions)
    for (const role of quest.completion === 'report'
      ? ['offer', 'report']
      : ['offer'])
      if (
        !catalog.bindings.some((b) => b.quest === quest.id && b.role === role)
      )
        throw Error('Missing quest role');
}
function rejectCycles(edges: ReadonlyMap<string, readonly string[]>): void {
  const active = new Set<string>(),
    done = new Set<string>();
  function visit(id: string) {
    if (active.has(id)) throw Error('Quest dependency cycle');
    if (done.has(id)) return;
    active.add(id);
    for (const next of edges.get(id) ?? []) visit(next);
    active.delete(id);
    done.add(id);
  }
  for (const id of edges.keys()) visit(id);
}
