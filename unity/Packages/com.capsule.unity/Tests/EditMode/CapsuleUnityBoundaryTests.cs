using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Capsule.Unity;
using UnityEngine;

namespace Capsule.Tests
{
    public sealed class CapsuleUnityBoundaryTests
    {
        [Serializable] sealed class AssemblyDefinition { public string name; public string[] references; }

        [Test]
        public void PresentationAssemblyDependsOnlyOnCapsuleAndUnityPresentation()
        {
            var assembly = typeof(CapsuleNativeTarget).Assembly;
            Assert.That(assembly.GetName().Name, Is.EqualTo("Capsule.Unity"));
            Assert.That(typeof(UnityUiImageDecoder).Assembly, Is.SameAs(assembly));
            Assert.That(typeof(UnityUiDecodedImage).Assembly, Is.SameAs(assembly));
            var allowed = new[] { "Capsule.Core", "Capsule.Yoga", "Capsule.Package", "Unity.ugui", "Unity.TextMeshPro", "Unity.InputSystem" };
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                string name = reference.Name;
                bool framework = name == "mscorlib" || name == "netstandard" || name == "System" || name.StartsWith("System.", StringComparison.Ordinal);
                bool engine = name == "UnityEngine" || name.StartsWith("UnityEngine.", StringComparison.Ordinal);
                Assert.That(framework || engine || allowed.Contains(name), Is.True, "Unexpected assembly dependency: " + reference);
            }
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/com.capsule.unity/Runtime/Capsule.Unity.asmdef"));
            var definition = JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(path));
            Assert.That(definition.name, Is.EqualTo("Capsule.Unity"));
            Assert.That(definition.references, Is.EquivalentTo(allowed), "Declared assembly dependencies must remain explicit.");
        }
    }
}
