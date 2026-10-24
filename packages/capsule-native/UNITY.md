# Capsule Unity adapter

`com.capsule.unity` owns the `Capsule.Unity` presentation assembly.
The five embedded UPM packages preserve the source asmdef names and GUIDs.
The adapter declares Core, Package, Yoga, uGUI, and Input System dependencies;
TextMeshPro is supplied by the Unity UI package in Unity 6000.6. The package
loader declares the Unity Newtonsoft package explicitly.

A host supplies a canvas `RectTransform` and `INativeUiWidgetFactory` to
`CapsuleNativeTarget`, then binds its session or asynchronous input sink.
The host owns activation, commits, and disposal. Game themes, prefabs,
observation providers, and gameplay policy belong to the host.

`UnityUiImageDecoder` decodes ordinary package images. Its decoded wrapper
owns texture and sprite lifetime and must be used on Unity's main thread.
`UnityUiDecodedImage.Borrow` instead borrows an imported sprite and texture;
the host keeps those resources alive until every wrapper is disposed.
Borrowing does not prove source provenance.

The `Resources` shaders retain the lookup names `CapsuleAlphaAware` and
`CapsuleSoftLight`. The Wasmtime and Yoga packages carry Windows x86_64
Editor/Win64 plugin import settings. Hosts must supply compatible carrier
and SDK bytes. Other platforms are not validated by this package candidate.

Neutral package EditMode and PlayMode tests should check dependencies,
input routing, resource loading, image ownership, and presentation disposal
in a Unity consumer. They do not replace host integration checks.