# Skill system features

Engine- and game-independent TypeScript contracts for skill/perk progress and
training offers. Import from `@capsule-sdk/skill-system-features/progression` or
`@capsule-sdk/skill-system-features/training`. Each feature is independently admitted;
a game can publish progression without implementing trainers.

Games own stable skill, perk, resource, requirement and offer IDs. They supply
labels, icon keys and observed progress. `kind` distinguishes game-defined
categories such as skills and perks. Rank zero is untrained; a zero experience
threshold means no next experience threshold is supplied. Resource `value` is
available quantity, `spent` is consumed allocation and `limit` is its capacity;
zero limit means no capacity is supplied.

Requirements and affordability are provider observations for display. They are
not executable rules. Providers must revalidate ownership, freshness, offer
identity and eligibility against authoritative state before accepting a request.
An accepted request is not an unlock confirmation: the next state publication
confirms progress. Closing uses `close: true`; the offer ID is ignored.

Epoch, owner generation and sequence identify the observed context; stale or
duplicate interactions must not execute twice. Browser mocks demonstrate this
intent/observation separation and never award progress themselves.

Snapshots hold at most 256 skills, 32 resources, 128 training offers, 32
requirements per entry and 16 costs per offer. IDs are bounded to 128 UTF-8 bytes.
These are explicit view bounds, not a total game-catalog limit. Providers must
reject unsupported views rather than silently truncate; larger or paged views
require an explicit contract revision.

Run `bun run generate:features`, `bun run typecheck` and `bun run test` from this
package. Generated C# interfaces compile via `native/SkillSystem.Features.csproj`
without an engine or networking dependency. Other engine-language generators
are not supplied by this package yet.
