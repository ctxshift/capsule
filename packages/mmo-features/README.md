# MMO feature contracts

Engine-neutral chat and player-trading contracts. This package depends on Capsule feature primitives and RPG item schemas. A game supplies providers using its own networking or local test harness.

Consumers require @mmo/chat or @mmo/player-trade independently. Importing chat does not require a trading or inventory provider. Trading reuses the RPG ItemKind schema; this does not imply that the same item key has the same meaning in different games. Game adapters resolve their catalogs.

Chat State is an ordered batch of at most four messages, not a four-message history limit. A provider publishes further batches; the consumer owns presentation history. Send is bounded to 512 UTF-8 bytes with an 80-byte recipient key. The provider enforces channel, recipient and gameplay policies. Trading carries at most 24 items per side in this contract version and integer primary-currency amounts. These explicit bounds are validated by generated codecs and are not transport fragmentation limits.

Sequence and revision identify reviewed state. Request acceptance is dispatch acknowledgement, not successful delivery, settlement or a locally authorized balance mutation. Publish authoritative results separately. Mocks require explicit state publication and reject stale requests.

Run bun run generate:features (or append --check), bun run typecheck and bun run test. Generated native interfaces are in `com.capsule.features.mmo/Runtime` under `Mmo.Features`. `native/Mmo.Features.csproj` compiles this canonical source against Capsule.Core.
