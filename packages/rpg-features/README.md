# RPG feature contracts

Engine-neutral TypeScript contracts for HUD units, nameplates, dialogue, inventory, equipment, loot and merchant interactions. No game, networking library, React or engine dependency is required. Feature identities use the @rpg namespace; consumers request individual features, not the whole package.

A consuming game implements the generated provider interfaces with its own state and authority. An accepted intent acknowledges dispatch; it does not authorize the UI to invent inventory, balances or transaction results. The provider publishes confirmed state.

## Shared vocabulary and bounds

ItemKind is an opaque game-owned catalog key, bounded to 128 UTF-8 bytes. Item-bearing contracts share this schema; the consuming game supplies catalog and artwork mappings. This is schema reuse, not a runtime catalog service or a claim that two games have identical item catalogs. Epoch, generation and revision identify the currently observed state and protect against stale input; they do not require network sessions.

Version 1 exposes bounded presentation views: 24 bag items, 8 equipped items, 64 merchant offers and 64 nearby nameplates. These are contract page/view bounds, not limits on a game's total inventory or world population. Providers must stay within them; supporting larger views requires an explicit compatible extension or a new contract version, never silent truncation. Prices use an unsigned integer amount in the game's primary currency. Inventory tokens are provider-local handles. Nameplate kind and combat-independent portrait keys are interpreted by the consuming game. Equipment health and surface indicate the observed character health and whether the host presentation surface is available; neither grants engine-object access.

## Generation and validation

Run bun run generate:features, bun run typecheck and bun run test in this package. The generator supports --check. Generated C# source lives in the canonical `com.capsule.features.rpg/Runtime` package in namespace `Rpg.Features`. The standalone `native/Rpg.Features.csproj` compiles that source against Capsule.Core.

Mocks are explicit development providers and never production fallbacks. They validate reviewed state identities and require explicit publication of resulting state.

## Quest journal

`quests` publishes up to 64 observed quests per view, each with up to eight objectives and 16 rewards. IDs and reward kinds belong to the game. Status values describe locked, available, active, ready and completed quests; freshness distinguishes unavailable, loading, fresh and stale observations. An empty journal is valid. Rewards are descriptions, never permission to award items or currency. This read-only feature does not require an accept/turn-in service; games can offer those actions through their dialogue or a separate capability. Larger views require a deliberate contract revision rather than truncation.

## Quest prerequisites

`./quest-prerequisites` owns the version-1 authoring format for eligibility
expressions. It is separate from the journal observation feature and adds no
provider, permission or addon activation requirement. The generator emits native
`TQuestPrerequisites` / `TQuestConditionNode` records and codecs, plus
`generated/quest-prerequisites.json` for non-C# consumers.

Nodes have stable local IDs and child-ID references. Supported kinds are `always`,
`all`, `any`, `not`, `quest-completed`, `quest-active` and `fact`. Operators have an
empty subject; leaves have no children. `all` and `any` require at least one child;
`not` requires exactly one. Use `always` for an unconditional offer. Quest and fact
subjects are game-owned IDs validated against the supplied catalog references.
Active attempts include ready-to-report attempts; they do not imply completion.
Boolean facts must be explicitly supplied even under negation or short-circuiting.
Fact scope, ownership and server adapters are responsibilities of the consuming
catalog/runtime; the initial format does not interpret arbitrary scripts or
implicitly read inventory, training or world state.

`compileQuestPrerequisites` validates vocabulary, references, arity, duplicate IDs,
expression cycles and unreachable nodes, then retains an immutable definition and
provides a pure evaluator for mocks and conformance tests. Shared subexpressions
are allowed. This does not validate between-quest dependency cycles or implement
objective progression, rewards, persistence or the authoritative server evaluator.
Generated native codecs validate wire shape/bounds; consumers must also apply the
same semantic admission rules before evaluating content.

Version 1 bounds one expression to 256 nodes, 32 children per node and 128 UTF-8
bytes per ID. These bound traversal and wire allocation for one prerequisite,
not the number of quests in a game. Larger expressions need an explicit format
revision rather than truncation. Evaluate admitted expressions against supplied
state; do not recompile or transmit the whole catalog on each UI update.

## Quest catalog authoring

`./quest-catalog` defines quest revisions, objective activation edges, explicit
completion policy, reward references, NPC roles and fact declarations. Catalogs
have no whole-game quest count limit. Individual definitions currently support
32 objectives with 32 activation predecessors each. Version 1 admits talk and
defeat objectives, one-time quests, explicit reporting or authored talk completion
on acceptance, and persistent character-scoped boolean facts. Unsupported policies
are rejected. `validateQuestCatalog` checks game-supplied actor, reward, target and
world references, required roles, duplicates and cycles. This initial format rejects
all quest-reference cycles, including cycles through alternatives or negation;
intentional loops need a future explicit repeat/reset format.

A consuming game authors its catalog and validates its own actor, reward, target
and world references. `Rpg.Quests.Catalog` admits these shapes and semantics
without an engine, database or game rule dependency. Game reward bundles remain
outside the reusable schema.

## Status effects

`effects` publishes owner and selected-target actor lifetimes with up to 64 effects each. Game-owned effect IDs identify definitions; instance IDs distinguish applications. Each effect supplies its name, icon key, kind, stack count and remaining milliseconds. `timed: false` means permanent and ignores remaining time. Providers remove expired instances; the UI must not infer authoritative removal from a local clock.

## Combat and action slots

`combat` supplies an ordered view of up to 64 slots and 64 confirmed hit/healing events. Each slot has a game-owned action ID, display name/icon, active/available flags and an independent phase (`idle`, `casting`, `cooldown`), duration in milliseconds, timer revision and queued flag. Empty action IDs are empty slots. A timer revision identifies a new finite animation; repeated observations must not restart it. Hit revisions identify feedback instances within an epoch/actor lifetime.

Activation carries the observed epoch, slot index and action ID. Providers validate these identities and ask the game to execute intent; UI availability is advisory because active toggles or queued actions can have different rules. Accepted requests never fabricate hits, cooldowns or resource spending. The game owns authority and deduplication of confirmations.
