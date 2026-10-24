# Capsule installation and release plan

Install the engine plugin first. Its setup flow should create or connect an
external React UI project, validate the runtime and connect a development server.
Run the game and UI server together; TSX, theme and asset edits should live-reload
in the same game process. Invalid edits must preserve the last-good UI.

The setup window and unified CLI are planned capabilities. Current candidates
support explicit Unity package installation and the existing workshop commands.
Browser preview and portable runtime checks do not establish in-game live reload.

## Distribution

| Channel | Deliverables |
| --- | --- |
| npm | JS/TS contracts, React authoring SDK, compiler/workshop, reusable feature packages; unified CLI when implemented |
| GitHub Releases | Unity plugin bundle, portable managed/native runtime bundle, compatibility manifest and checksums |
| Later marketplaces | Unity Asset Store, Fab Unity/Unreal and Godot ecosystem listings for implemented adapters |

No Capsule NuGet packages are needed or generated. External .NET dependencies
still restore through their existing build-time package references. Engine users
receive the required libraries inside their bundle. Godot and Unreal plugins are
future work; a runtime ZIP is not a substitute for an adapter.

The CLI should support project initialization, starting the development server
and scaffolding addons/components/features. Editor setup should reuse these
implementations. Installing the CLI is not a prerequisite for adding an engine
plugin. Local Capsule source development remains a separate attach/detach workflow;
do not require a release to iterate on shared code.

## Candidate creation

Use Windows x64, PowerShell 7, Git with `archive --mtime` support, tar, the pinned
Node/Bun/.NET SDKs and their matching runtime for the native test host. From a committed
checkout at the repository root:

```powershell
bun install --frozen-lockfile
bun run test:release
bun run release:build --output .local/candidate-a
bun run release:verify --artifacts .local/candidate-a
bun run release:reproduce --output .local/reproduction
```

Output directories must be fresh. The builder exports one immutable Git revision
into an external temporary directory, installs with the source lockfile and fresh
Bun cache, stages explicit npm file lists, builds managed DLLs with locked restores
and creates deterministic archives. Source runtime dependencies must be exact;
workspace references become exact released versions in npm manifests.

`npm/` contains npm-ready tarballs. `upm/` contains individual UPM archives for
verification and advanced installs. `engines/capsule-unity-<version>.zip` contains
all eight embedded UPM packages and setup instructions. Copy its `Packages/`
contents into the host's `Packages/`, preserving unrelated packages. This resolves
internal dependencies without a registry. Keep GUIDs and importer settings.
Unity's declared external package dependencies still require normal UPM access.

`engines/capsule-runtime-win-x64-<version>.zip` provides direct DLLs for standalone
hosts; see [native bundle](release-native.md). Git package URLs in
`unity-manifest.json` are an optional source installation alternative; they only
work after the source commit is available remotely.

The compatibility manifest records source revision, exact package versions,
integrities, tool/runtime/ABI versions, feature baseline and native/carrier/SDK
hashes. Verification checks complete archive inventories, UPM GUID uniqueness,
bundle contents and resource agreement. Its external npm consumer compiles a
neutral addon, checks invalid-edit recovery and rebuilds the SDK; a direct-DLL
consumer then runs the real native lifecycle against that SDK/addon.

The consumer package uses local tarball overrides solely to test unpublished
candidates. Those paths are relative inside the verification set; they are not
published npm dependencies. The retained consumer lockfile freezes resolved
third-party dependencies. Dependency notices are inventoried with hashes. Platform
esbuild/Rollup packages inherit the matching parent distribution notice when their
archive omits it; Yoga 3.2.1 uses the retained upstream Yoga notice for that version.

## Verification and promotion

Build twice from the same source and compare every output file, including locks,
notices, manifests and archives. Run the frozen packed consumer and direct DLL
checks. `release:reproduce` performs those steps in two external snapshots with
fresh Bun and managed dependency caches, then records a relative-path inventory
in `reproducibility.json`. Run the [fresh Unity consumer gate](release-upm.md) through `agent-unity`;
portable verification alone does not approve an engine release. The current helper
discovery mismatch blocks that gate and must be resolved before promotion.

Candidate CI retains artifacts and logs only. It does not publish packages, create
release tags, upload GitHub release assets or submit marketplaces. Before a real
release, establish npm scope ownership/publication access and verify package names,
complete engine acceptance, then explicitly promote the exact tested bytes. Put
engine ZIPs, manifest, checksums and notices on the GitHub release; publish matching
JS/TS tarballs to npm. Preserve package identities and coordinate compatible
versions. No publish step should rebuild different bytes.

Upgrade the complete compatible set together and retain the prior manifest and
host lockfiles. Roll back by restoring the previous plugin bundle and matching npm
versions/lockfile. Do not combine a newer SDK with an older carrier or native DLL.
