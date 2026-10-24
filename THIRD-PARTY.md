# Third-party components

The root Apache-2.0 license applies to first-party Capsule code. It does not replace
licenses for bundled upstream components or installed Unity dependencies.

| Component | Version | Retained notices |
| --- | --- | --- |
| Wasmtime | 48.0.2 | `com.capsule.wasmtime/Runtime/Wasmtime-LICENSE.txt`, including the LLVM exception |
| QuickJS-NG | 0.17.0 | `com.capsule.wasmtime/ThirdPartyNotices/QuickJS-LICENSE.txt` and build tools |
| Yoga | 3.2.1 | `com.capsule.yoga/Runtime/Plugins/x86_64/LICENSE.yoga.txt` and provenance JSON |
| React / reconciler / scheduler in SDK bundles | 18.3.1 / 0.29.2 / 0.23.2 | Three notices under `com.capsule.wasmtime/ThirdPartyNotices` |
| Newtonsoft.Json | NuGet 13.0.2 / Unity package 3.2.2 | Installed through declared package dependencies; retain its distribution notices when packing |
| Unity uGUI/TextMeshPro | 2.6.0 | Installed package's `LICENSE.md`, copied to `docs/licenses/Unity-uGUI-LICENSE.md`, uses Unity Companion License for Unity-dependent projects |
| Liberation Sans in the neutral TMP consumer | Included TMP Essentials font | `unity/Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt` |

Package paths above are beneath `unity/Packages/`. The neutral Unity consumer's
TMP Essentials are imported from that installed Unity package. Preserve all
bundled font/license/notice files if including those consumer resources in a
distribution. System fonts used dynamically by tests are not copied here.

No Host artwork or licensed game content is included. A release must validate
the actual packed dependency closure and notices; this file is not a replacement
for that release audit or a broader platform-support claim.

Authoring builds also retain React/reconciler/scheduler notices under
`tools/capsule-react/licenses/`. The frozen Bun lock records compiler, TypeScript,
Vite, esbuild, Yoga and test/quality dependencies. These are installed development
inputs, not copied game assets. Packed transitive notices remain a release gate.
