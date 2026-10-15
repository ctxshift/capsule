using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PortableUi
{
    public static class UiFeatureIdentity
    {
        public static bool Name(string value) => value != null && Regex.IsMatch(value, @"^@[a-z0-9][a-z0-9._-]*/[a-z0-9][a-z0-9._-]*$");
        public static bool Version(string value) => value != null && Regex.IsMatch(value, @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$");
        public static bool Range(string value) => Version(value) || value != null && Regex.IsMatch(value, @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.\*$");
        public static bool Fingerprint(string value) => value != null && Regex.IsMatch(value, "^[a-f0-9]{64}$");
        public static bool Permission(string value) => value != null && Regex.IsMatch(value, "^[a-zA-Z][a-zA-Z0-9._-]*$");
        public static bool Matches(string version, string range) => Version(version) && Range(range) &&
            (range.EndsWith(".*", StringComparison.Ordinal) ? version.StartsWith(range.Substring(0, range.Length - 1), StringComparison.Ordinal) : version == range);
    }
    public sealed class UiFeatureDependency
    {
        public string Name { get; }
        public string Range { get; }
        public UiFeatureDependency(string name, string range)
        {
            if (!UiFeatureIdentity.Name(name) || !UiFeatureIdentity.Range(range)) throw new ArgumentException("Invalid feature dependency");
            Name = name; Range = range;
        }
    }
    public sealed class UiFeatureRequirement
    {
        public string Name { get; }
        public string Range { get; }
        public string ContractVersion { get; }
        public string Fingerprint { get; }
        public bool Optional { get; }
        public IReadOnlyList<string> Permissions { get; }
        public UiFeatureRequirement(string name, string range, string contractVersion, string fingerprint, bool optional, IEnumerable<string> permissions)
        {
            if (!UiFeatureIdentity.Name(name) || !UiFeatureIdentity.Matches(contractVersion, range) || !UiFeatureIdentity.Fingerprint(fingerprint))
                throw new ArgumentException("Invalid feature requirement identity");
            var list = (permissions ?? throw new ArgumentNullException(nameof(permissions))).ToArray();
            if (list.Any(p => !UiFeatureIdentity.Permission(p)) || list.Distinct(StringComparer.Ordinal).Count() != list.Length)
                throw new ArgumentException("Invalid feature permissions");
            Name = name; Range = range; ContractVersion = contractVersion; Fingerprint = fingerprint; Optional = optional;
            Permissions = Array.AsReadOnly(list);
        }
    }
    public sealed class UiFeatureDefinition
    {
        public string Name { get; }
        public string Version { get; }
        public string Fingerprint { get; }
        public IReadOnlyList<UiFeatureDependency> Dependencies { get; }
        public IReadOnlyList<string> Permissions { get; }
        public UiFeatureDefinition(string name, string version, string fingerprint, IEnumerable<UiFeatureDependency> dependencies, IEnumerable<string> permissions)
        {
            if (!UiFeatureIdentity.Name(name) || !UiFeatureIdentity.Version(version) || !UiFeatureIdentity.Fingerprint(fingerprint)) throw new ArgumentException("Invalid feature definition");
            var deps = (dependencies ?? throw new ArgumentNullException(nameof(dependencies))).ToArray();
            var grants = (permissions ?? throw new ArgumentNullException(nameof(permissions))).ToArray();
            if (deps.Any(d => d == null) || deps.Select(d => d.Name).Distinct(StringComparer.Ordinal).Count() != deps.Length ||
                grants.Any(p => !UiFeatureIdentity.Permission(p)) || grants.Distinct(StringComparer.Ordinal).Count() != grants.Length) throw new ArgumentException("Invalid feature definition members");
            Name = name; Version = version; Fingerprint = fingerprint;
            Dependencies = Array.AsReadOnly(deps); Permissions = Array.AsReadOnly(grants);
        }
    }
    // Selected only by host composition; manifests request permissions, never grant them.
    public sealed class UiFeatureGrant
    {
        public string Name { get; }
        internal readonly HashSet<string> Permissions;
        public UiFeatureGrant(string name, params string[] permissions)
        {
            if (!UiFeatureIdentity.Name(name) || permissions == null || permissions.Any(p => !UiFeatureIdentity.Permission(p))) throw new ArgumentException("Invalid feature grant");
            Name = name; Permissions = new HashSet<string>(permissions, StringComparer.Ordinal);
        }
    }
    public sealed class UiFeatureRegistration : IDisposable
    {
        internal readonly UiFeatureRegistry Registry;
        public UiFeatureDefinition Definition { get; }
        public object Provider { get; }
        public long Generation { get; }
        internal UiFeatureRegistration(UiFeatureRegistry registry, UiFeatureDefinition definition, object provider, long generation)
        { Registry = registry; Definition = definition; Provider = provider; Generation = generation; }
        public void Dispose() => Registry.Remove(this);
    }
    public sealed class UiFeatureSelection
    {
        readonly UiFeatureRegistry registry;
        readonly UiFeatureRegistration[] dependencies;
        readonly IReadOnlyDictionary<string, UiFeatureRegistration> providers;
        readonly Dictionary<string, HashSet<string>> permissions;
        public IReadOnlyDictionary<string, UiFeatureRegistration> Providers => providers;
        internal UiFeatureSelection(UiFeatureRegistry registry, Dictionary<string, UiFeatureRegistration> providers,
            IEnumerable<UiFeatureRegistration> dependencies, Dictionary<string, HashSet<string>> permissions)
        { this.registry = registry; this.providers = new System.Collections.ObjectModel.ReadOnlyDictionary<string, UiFeatureRegistration>(providers); this.dependencies = dependencies.Distinct().ToArray(); this.permissions = permissions; }
        public bool IsCurrent => dependencies.All(registry.Contains);
        public bool Allows(string feature, string permission) => IsCurrent && permissions.TryGetValue(feature, out var granted) && granted.Contains(permission);
    }
    // Host-thread composition. No provider calls or addon evaluation happen during resolution.
    public sealed class UiFeatureRegistry
    {
        readonly Dictionary<string, UiFeatureRegistration> providers = new Dictionary<string, UiFeatureRegistration>(StringComparer.Ordinal);
        readonly Dictionary<string, string> identities = new Dictionary<string, string>(StringComparer.Ordinal);
        long sequence;
        public UiFeatureRegistration Register(UiFeatureDefinition definition, object provider)
        {
            lock (providers)
            {
            if (definition == null || provider == null) throw new ArgumentNullException();
            string identity = definition.Name + "@" + definition.Version;
            if (identities.TryGetValue(identity, out var fingerprint) && fingerprint != definition.Fingerprint) throw new InvalidOperationException("Feature definition changed without a version change");
            if (providers.ContainsKey(definition.Name)) throw new InvalidOperationException("Feature provider already selected");
            var entry = new UiFeatureRegistration(this, definition, provider, checked(++sequence));
            identities[identity] = definition.Fingerprint; providers.Add(definition.Name, entry); return entry;
            }
        }
        internal void Remove(UiFeatureRegistration entry) { lock (providers) { if (Contains(entry)) providers.Remove(entry.Definition.Name); } }
        internal bool Contains(UiFeatureRegistration entry) { lock (providers) { return providers.TryGetValue(entry.Definition.Name, out var current) && ReferenceEquals(current, entry); } }
        UiFeatureRegistration Resolve(string name, string range, HashSet<string> visiting, List<UiFeatureRegistration> closure)
        {
            if (!visiting.Add(name)) throw new InvalidOperationException("Cyclic feature dependency: " + name);
            if (!providers.TryGetValue(name, out var entry) || !UiFeatureIdentity.Matches(entry.Definition.Version, range)) throw new InvalidOperationException("Missing compatible feature: " + name + "@" + range);
            foreach (var dependency in entry.Definition.Dependencies) Resolve(dependency.Name, dependency.Range, visiting, closure);
            visiting.Remove(name); closure.Add(entry); return entry;
        }
        public UiFeatureSelection Select(IEnumerable<UiFeatureRequirement> requirements, IEnumerable<UiFeatureGrant> grants)
        {
            lock (providers)
            {
            var allowed = (grants ?? Array.Empty<UiFeatureGrant>()).ToDictionary(g => g.Name, StringComparer.Ordinal);
            var selected = new Dictionary<string, UiFeatureRegistration>(StringComparer.Ordinal);
            var permissions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var closure = new List<UiFeatureRegistration>(); var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var requirement in requirements ?? Array.Empty<UiFeatureRequirement>())
            {
                if (requirement == null || !names.Add(requirement.Name)) throw new ArgumentException("Duplicate/null feature requirement");
                try
                {
                    var deps = new List<UiFeatureRegistration>();
                    var entry = Resolve(requirement.Name, requirement.Range, new HashSet<string>(StringComparer.Ordinal), deps);
                    if (entry.Definition.Version == requirement.ContractVersion && entry.Definition.Fingerprint != requirement.Fingerprint) throw new InvalidOperationException("Feature schema fingerprint mismatch: " + requirement.Name);
                    if (requirement.Permissions.Any(p => !entry.Definition.Permissions.Contains(p) || !allowed.TryGetValue(requirement.Name, out var grant) || !grant.Permissions.Contains(p))) throw new InvalidOperationException("Feature permission denied: " + requirement.Name);
                    selected.Add(requirement.Name, entry); permissions.Add(requirement.Name, new HashSet<string>(requirement.Permissions, StringComparer.Ordinal)); closure.AddRange(deps);
                }
                catch (InvalidOperationException) when (requirement.Optional) { }
            }
            return new UiFeatureSelection(this, selected, closure, permissions);
            }
        }
    }
}
