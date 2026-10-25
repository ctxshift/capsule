# Unity bundle installation verification

Use a Windows host with Unity 6000.6.3f1, official Unity CLI/Pipeline and
`agent-unity`. From the repository root, pass the complete candidate directory:

```powershell
pwsh -NoProfile -File tools/release/verify-upm.ps1 -Artifacts <absolute-release-directory>
```

The verifier validates the release manifest, archive hashes, UPM closure and Unity
ZIP payload equivalence before creating a new external temporary project. It
copies the ZIP's eight embedded `Packages/com.capsule.*` directories into the
consumer, with no source links, `Library` or existing project settings. The project
declares pinned Unity uGUI, Input System, Newtonsoft, Test Framework and Pipeline
dependencies. Registry access is needed for these external dependencies.

An Editor test assembly checks all eight public assemblies and their embedded
package paths/versions, imported carrier/SDK resources, Windows x64 plugin bytes
and importer settings, and preserved runtime asset GUIDs. The current candidate
executes 97 cases: one assembly check, eight packages, five resources, three native
plugins and 80 GUIDs. Expected counts are derived from the verified candidate;
every case must execute and pass, with zero failures, skips or inconclusive cases.

The supported helper runs closed-project headless tests and retains NUnit XML,
CLI output and Editor diagnostics under `.local/release-upm/`. `consumer.json` and
`verification.json` identify the exact source revision, bundle/manifest hashes,
temporary project and observed counts. Keep these local logs for diagnosis; only
portable summaries belong in tracked verification records.

Some CLI versions infer a running instance when `pipeline list` executes inside
a Unity project directory. Use a helper that runs unscoped discovery from a neutral
directory, while preserving its explicit project match and Unity lockfile guards.
An inventory error must not be bypassed by directly launching a second Editor.

The verifier never targets an existing project or closes a user's Editor. Passing
these tests establishes fresh package import, compilation, embedded dependency
resolution, resource integrity and asset identity. It does not establish rendered
appearance, Player builds, native runtime execution inside Unity, input setup,
TextMeshPro Essentials installation or in-game UI live reload. Those require their
own focused engine/plugin and development-workflow acceptance.

The [recorded candidate proof](verification/unity-release.json) pins the tested
source commit, ZIP and manifest hashes, helper revision, reproducibility inventory
and observed counts. It is a point-in-time attestation for that candidate; rerun
the verifier for changed bundle payloads before promoting a different set.
