# Contributing

Install mise, then run from the repository root:

```powershell
mise trust .mise.toml
mise install
mise run install
mise run check
mise run test
```

The repository pins Bun, Node, Zig and Bees. Native checks also require a .NET SDK
supporting the .NET 9 test runners and .NET Standard 2.1 libraries. The SDK is
provided separately from mise. Engine validation uses the explicit project and
the tooling described in [Unity setup](docs/unity-setup.md); ordinary authoring
checks do not start an Editor or game.

`mise run generate` updates feature descriptors and canonical native contracts;
`mise run check-features` checks drift, dependency direction and neutral providers.
The root `check` task includes this gate. Generated descriptor JSON is owned by
the generator and excluded from source formatting.

`mise run build-sdk` builds a local runtime artifact. `mise run dev` starts the
neutral React workshop/browser preview. See [UI development](docs/ui-development.md)
for the running-game development goal and verification boundaries. Use
`mise run js-format` and `mise run js-check` for authored JavaScript/TypeScript.

## Task tracking and commits

Use this repository's Bees tracker. Run `bees prime` for the installed workflow
and `bees ready` for available work. Tracked `.bees/issues.jsonl` is the portable
task record; the local SQLite database and runtime files stay ignored. Run
`bees init` after cloning to reconstruct the local database from the export.
Never edit the issue export or database by hand; use the Bees CLI.

Every task must have a parent-child path to an epic. Mark work in progress when
starting and close it before committing the completed implementation. Use the
actual Capsule issue identifier as a conventional commit scope when a commit
implements that issue; omit the scope when no corresponding issue exists. Do not
copy consumer trackers or their identifiers into this repository.

Keep engine/game integration outside portable contracts. Public packages use
declared dependencies and stable feature identities. Preserve native GUIDs and
third-party licenses. Do not add game artwork, private consumer identities or
machine-specific checkout configuration to tracked artifacts.
