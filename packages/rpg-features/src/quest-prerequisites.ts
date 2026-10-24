import { s, type Value } from '@capsule/features';
import { codec } from '@capsule/features/codec';

// A flat expression graph supports shared subexpressions and native codegen
// without recursive wire types. These are per-expression bounds, not catalog limits.
export const QuestConditionNode = s.record('QuestConditionNode', {
  id: s.text(128),
  kind: s.text(32),
  subject: s.text(128),
  children: s.list(s.text(128), 32),
});
export const QuestPrerequisites = s.record('QuestPrerequisites', {
  version: s.u32,
  root: s.text(128),
  nodes: s.list(QuestConditionNode, 256),
});
export type QuestPrerequisitesValue = Value<typeof QuestPrerequisites>;
export type QuestConditionKind =
  | 'always'
  | 'all'
  | 'any'
  | 'not'
  | 'quest-completed'
  | 'quest-active'
  | 'fact';
export type QuestConditionValue = Omit<
  Value<typeof QuestConditionNode>,
  'kind'
> & {
  readonly kind: QuestConditionKind;
};
export interface QuestPrerequisiteReferences {
  readonly quests: ReadonlySet<string>;
  readonly facts: ReadonlySet<string>;
}
export interface QuestPrerequisiteState {
  readonly completed: ReadonlySet<string>;
  // Active includes ready-to-report attempts; completion is separate history.
  readonly active: ReadonlySet<string>;
  readonly facts: ReadonlyMap<string, boolean>;
}

/** Authoring validation and a pure reference evaluator for mocks/conformance.
 * Production authority must evaluate supplied server state independently.
 */
export function compileQuestPrerequisites(
  source: QuestPrerequisitesValue,
  references: QuestPrerequisiteReferences,
) {
  // Validate wire values and take one immutable copy at catalog admission.
  const wire = codec(QuestPrerequisites);
  const definition = wire.decode(wire.encode(source));
  if (definition.version !== 1) throw Error('Unsupported prerequisite version');
  const nodes = new Map<string, Value<typeof QuestConditionNode>>();
  const facts = new Set<string>();
  for (const node of definition.nodes) {
    if (!node.id || nodes.has(node.id))
      throw Error('Duplicate or empty condition ID');
    nodes.set(node.id, node);
    const branch = ['all', 'any', 'not'].includes(node.kind);
    if (branch) {
      if (
        node.subject ||
        !node.children.length ||
        (node.kind === 'not' && node.children.length !== 1) ||
        new Set(node.children).size !== node.children.length
      )
        throw Error('Invalid condition arity: ' + node.id);
    } else {
      if (node.children.length)
        throw Error('Leaf condition has children: ' + node.id);
      switch (node.kind) {
        case 'always':
          if (node.subject) throw Error('Always condition has a subject');
          break;
        case 'quest-completed':
        case 'quest-active':
          if (!node.subject || !references.quests.has(node.subject))
            throw Error('Unknown quest reference: ' + node.subject);
          break;
        case 'fact':
          if (!node.subject || !references.facts.has(node.subject))
            throw Error('Unknown fact reference: ' + node.subject);
          facts.add(node.subject);
          break;
        default:
          throw Error('Unsupported condition kind: ' + node.kind);
      }
    }
  }
  const visiting = new Set<string>(),
    visited = new Set<string>();
  function visit(id: string): void {
    const node = nodes.get(id);
    if (!node) throw Error('Missing condition: ' + id);
    if (visiting.has(id)) throw Error('Condition cycle: ' + id);
    if (visited.has(id)) return;
    visiting.add(id);
    node.children.forEach(visit);
    visiting.delete(id);
    visited.add(id);
  }
  visit(definition.root);
  if (visited.size !== nodes.size) throw Error('Unreachable condition');
  return Object.freeze({
    definition,
    evaluate(state: QuestPrerequisiteState): boolean {
      // Missing facts are not false: negating an unavailable fact must not unlock.
      for (const fact of facts)
        if (typeof state.facts.get(fact) !== 'boolean')
          throw Error('Missing boolean fact: ' + fact);
      const results = new Map<string, boolean>();
      function evaluate(id: string): boolean {
        if (results.has(id)) return results.get(id)!;
        const node = nodes.get(id)!;
        let result: boolean;
        switch (node.kind) {
          case 'always':
            result = true;
            break;
          case 'all':
            result = node.children.every(evaluate);
            break;
          case 'any':
            result = node.children.some(evaluate);
            break;
          case 'not':
            result = !evaluate(node.children[0]);
            break;
          case 'quest-completed':
            result = state.completed.has(node.subject);
            break;
          case 'quest-active':
            result = state.active.has(node.subject);
            break;
          case 'fact':
            result = state.facts.get(node.subject)!;
            break;
          default:
            throw Error('Unvalidated condition');
        }
        results.set(id, result);
        return result;
      }
      return evaluate(definition.root);
    },
  });
}
