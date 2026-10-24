# Reusable game feature packages

Capsule supplies schemas, codecs, feature admission and UI execution. Feature
libraries define engine-independent observations and reviewed requests above that
platform. Games implement providers and retain authority over outcomes.

| Package | Responsibility | Runtime dependencies |
| --- | --- | --- |
| `@capsule-sdk/rpg-features` | HUD units, nameplates, dialogue, bag/equipment, loot, merchants, quests, status effects, combat | Capsule features |
| `@capsule-sdk/mmo-features` | Chat and trading | Capsule features and RPG item schemas |
| `@capsule-sdk/skill-system-features` | Skills/perks, progression and training | Capsule features |
| `@capsule-sdk/example-inventory` | Neutral inventory contracts, mock and sample view | Capsule features, React API and React peer |

The inventory package is an example, not a platform runtime dependency. MMO
imports RPG item schemas without requiring a phantom item-catalog provider. RPG
does not depend on MMO; skills do not depend on either. Catalog identities,
artwork, authoritative simulation and game/transport providers stay with the host.
The shared quest evaluator supports mocks and conformance; it does not make UI
execution authoritative over progression.

Public names, versions, descriptors, fingerprints and generated C# content are
preserved. The [baseline](provenance/game-features.json) retains contract hashes,
feature fingerprints and native GUIDs. Generation may move files but does not
change a wire contract as part of repository extraction.

## Canonical native contracts

Three real UPM packages own generated C# under `Runtime/`:

- `com.capsule.features.rpg`: `Rpg.Features`
- `com.capsule.features.mmo`: `Mmo.Features`
- `com.capsule.features.skill-system`: `SkillSystem.Features`

They declare Capsule Core dependencies and engine-independent assemblies. The
standalone `.NET Standard 2.1` projects under each TypeScript package's `native/`
compile those same UPM-owned files. There is no second generated C# copy under
the reusable package's `generated/` directory. That directory retains descriptor
JSON and shared schema metadata. The inventory example keeps its own generated
interfaces under its package; it is not installed as a production UPM dependency.
The import verifier checks descriptor hashes, native content and GUID metadata
against the reviewed baseline. Intentional contract upgrades must review and
update that baseline as well as generated files.

For Unity, install Core and whichever feature packages the host needs, together
with the runtime/adapter packages in [Unity setup](unity-setup.md). A generator
updates only repository-owned outputs. Do not compile host-owned copies of these
contracts alongside the package assemblies. Host composition and provider wiring
remain explicit integration work.

## Development and validation

From the repository root after [tool setup](../CONTRIBUTING.md):

```powershell
mise run generate
mise run check-features
mise run check
```

Generation checks reject descriptor/native drift. Generated JSON is excluded
from formatting because the generator owns its exact bytes. TypeScript tests
exercise contract mocks, bounds, prerequisite/catalog logic and request behavior.
The standalone provider fixture covers independent item catalogs, MMO chat,
skills/training, serialization, observations and reviewed requests. The inventory
fixture checks TypeScript/C# wire agreement in both directions.

A separate automated consumer copies only the six required source packages into
a temporary workspace, installs declared dependencies, imports public exports,
generates native interfaces and admits a mock chat provider. This verifies source
workspace consumption without a game checkout or root aliases. Release tarball
installation, registry feeds and engine-driven acceptance are separate gates.

These tests do not prove authoritative multiplayer gameplay, Unity import/build
or rendered in-game live reload. The [UI development workflow](ui-development.md)
still requires a running game and React server with demonstrated same-process
updates, last-good recovery and working controls after replacement.
