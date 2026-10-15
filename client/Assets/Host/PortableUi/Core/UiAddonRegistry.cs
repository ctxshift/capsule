using System;
using System.Collections.Generic;
using System.Threading;

namespace PortableUi
{
    // Descriptive only: the explicit grant, not the trust label, authorizes imports.
    public enum UiAddonTrust { FirstParty, Community }

    public readonly struct UiObservationCapability
    {
        public readonly ushort Topic;
        public readonly ushort SchemaVersion;
        public UiObservationCapability(ushort topic, ushort schemaVersion)
        {
            if (topic == 0 || schemaVersion == 0)
                throw new ArgumentOutOfRangeException(nameof(topic), "Observation topic and schema must be nonzero.");
            Topic = topic; SchemaVersion = schemaVersion;
        }
        internal uint Key => (uint)Topic << 16 | SchemaVersion;
    }

    // Constructed by trusted host registration. An addon manifest is never read here.
    public sealed class UiCapabilityGrant
    {
        const int MaxCapabilitiesPerKind = 64;
        readonly HashSet<uint> observations;
        readonly HashSet<ushort> actions, inputs;
        readonly HashSet<ushort> readOnlyActions;

        public UiCapabilityGrant(IEnumerable<UiObservationCapability> observationTopics,
            IEnumerable<ushort> actionCapabilities, IEnumerable<ushort> inputCapabilities,
            bool unrestrictedCapacity = false, IEnumerable<ushort> readOnlyActionCapabilities = null)
        {
            observations = new HashSet<uint>();
            actions = new HashSet<ushort>();
            inputs = new HashSet<ushort>();
            if (observationTopics != null)
                foreach (var item in observationTopics)
                {
                    if (item.Topic == 0 || item.SchemaVersion == 0 ||
                        !observations.Add(item.Key) || (!unrestrictedCapacity && observations.Count > MaxCapabilitiesPerKind))
                        throw new ArgumentException("Invalid or duplicate observation grant.");
                }
            CopyIds(actionCapabilities, actions, "action", unrestrictedCapacity);
            CopyIds(inputCapabilities, inputs, "input", unrestrictedCapacity);
            readOnlyActions = new HashSet<ushort>();
            CopyIds(readOnlyActionCapabilities, readOnlyActions, "read-only action", unrestrictedCapacity);
            if (!readOnlyActions.IsSubsetOf(actions)) throw new ArgumentException("Read-only actions require an action grant.");
        }

        static void CopyIds(IEnumerable<ushort> source, HashSet<ushort> target, string name, bool unrestrictedCapacity)
        {
            if (source == null) return;
            foreach (ushort id in source)
                if (id == 0 || !target.Add(id) || (!unrestrictedCapacity && target.Count > MaxCapabilitiesPerKind))
                    throw new ArgumentException("Invalid or duplicate " + name + " grant.");
        }

        public bool AllowsObservation(ushort topic, ushort schemaVersion) =>
            observations.Contains((uint)topic << 16 | schemaVersion);
        public bool AllowsAction(ushort capability) => actions.Contains(capability);
        public bool AllowsReadOnlyAction(ushort capability) => readOnlyActions.Contains(capability);
        public bool AllowsInput(ushort capability) => inputs.Contains(capability);
        public bool HasInput => inputs.Count != 0;
        internal UiCapabilityGrant WithFeatureTransport()
        {
            var topics = new List<UiObservationCapability>();
            foreach (uint key in observations) topics.Add(new UiObservationCapability((ushort)(key >> 16), (ushort)key));
            foreach (ushort topic in new ushort[] { 65000, 65001, 65002 })
                if (!AllowsObservation(topic, 1)) topics.Add(new UiObservationCapability(topic, 1));
            var extended = new HashSet<ushort>(actions) { 65000, 65001 };
            var reads = new HashSet<ushort>(readOnlyActions) { 65001 };
            return new UiCapabilityGrant(topics, extended, inputs, true, reads);
        }
        public bool IsSubsetOf(UiCapabilityGrant ceiling) => ceiling != null &&
            observations.IsSubsetOf(ceiling.observations) && actions.IsSubsetOf(ceiling.actions) &&
            inputs.IsSubsetOf(ceiling.inputs) && readOnlyActions.IsSubsetOf(ceiling.readOnlyActions);
    }

    public sealed class UiAddonRegistration
    {
        internal readonly UiAddonRegistry Registry;
        internal readonly UiCapabilityGrant Grant;
        public string Id { get; }
        public UiAddonTrust Trust { get; }
        internal UiAddonRegistration(UiAddonRegistry registry, string id, UiAddonTrust trust,
            UiCapabilityGrant grant)
        { Registry = registry; Id = id; Trust = trust; Grant = grant; }
    }

    // A reload gets a new activation/owner. Reusing one activation is rejected.
    public sealed class UiAddonActivation
    {
        public UiCapabilityGrant Grant { get; }
        public UiFeatureSelection Features { get; }
        readonly uint ownerNonce;
        int consumed;
        internal UiAddonActivation(uint owner, UiCapabilityGrant grant, UiFeatureSelection features)
        { ownerNonce = owner; Grant = grant; Features = features; }
        public uint OwnerNonce => ownerNonce;
        public uint ConsumeOwner()
        {
            if (Features != null && !Features.IsCurrent) throw new InvalidOperationException("Feature providers changed before activation");
            if (Interlocked.Exchange(ref consumed, 1) != 0)
                throw new InvalidOperationException("Addon activation already used.");
            return ownerNonce;
        }
    }

    public sealed class UiAddonRegistry
    {
        static long nextOwner;

        internal static uint AllocateOwner(ref long counter)
        {
            while (true)
            {
                long previous = Interlocked.Read(ref counter);
                if (previous >= uint.MaxValue)
                    throw new InvalidOperationException("Addon owner nonce exhausted.");
                if (Interlocked.CompareExchange(ref counter, previous + 1, previous) == previous)
                    return (uint)(previous + 1);
            }
        }
        readonly Dictionary<string, UiAddonRegistration> registrations =
            new Dictionary<string, UiAddonRegistration>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> reserved;
        readonly UiCapabilityGrant communityCeiling;

        // Legacy diagnostic hosts may opt out of admission policy. Game hosts
        // supply their reserved identities and an explicit community ceiling.
        public UiAddonRegistry() { reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        public UiAddonRegistry(IEnumerable<string> reservedCoreIds, UiCapabilityGrant allowedCommunity)
        {
            communityCeiling = allowedCommunity ?? throw new ArgumentNullException(nameof(allowedCommunity));
            reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (reservedCoreIds == null) throw new ArgumentNullException(nameof(reservedCoreIds));
            foreach (string id in reservedCoreIds)
                if (string.IsNullOrEmpty(id) || id.Length > 96 || !ValidId(id) || !reserved.Add(id))
                    throw new ArgumentException("Invalid or duplicate reserved identity.");
        }

        public UiAddonRegistration Register(string id, UiAddonTrust trust, UiCapabilityGrant grant)
        {
            if (grant == null) throw new ArgumentNullException(nameof(grant));
            if (string.IsNullOrEmpty(id) || id.Length > 96 || !ValidId(id))
                throw new ArgumentException("Addon registration ID is invalid.", nameof(id));
            if (!Enum.IsDefined(typeof(UiAddonTrust), trust))
                throw new ArgumentOutOfRangeException(nameof(trust));
            if (trust == UiAddonTrust.Community &&
                (reserved.Contains(id) || communityCeiling != null && !grant.IsSubsetOf(communityCeiling)))
                throw new ArgumentException("Community registration requests reserved identity or capabilities.");
            lock (registrations)
            {
                if (registrations.ContainsKey(id))
                    throw new ArgumentException("Addon already registered: " + id);
                var registration = new UiAddonRegistration(this, id, trust, grant);
                registrations.Add(id, registration);
                return registration;
            }
        }

        public UiAddonActivation Activate(UiAddonRegistration registration, UiFeatureSelection features = null)
        {
            if (registration == null) throw new ArgumentNullException(nameof(registration));
            lock (registrations)
            {
                if (!ReferenceEquals(registration.Registry, this) ||
                    !registrations.TryGetValue(registration.Id, out var owned) ||
                    !ReferenceEquals(owned, registration))
                    throw new ArgumentException("Registration belongs to another host.", nameof(registration));
            }
            if (features != null && !features.IsCurrent) throw new InvalidOperationException("Feature providers changed before activation");
            return new UiAddonActivation(AllocateOwner(ref nextOwner), features != null && features.Providers.Count != 0 ? registration.Grant.WithFeatureTransport() : registration.Grant, features);
        }

        static bool ValidId(string id)
        {
            foreach (char value in id)
                if (!(value >= 'a' && value <= 'z') && !(value >= 'A' && value <= 'Z') &&
                    !(value >= '0' && value <= '9') && value != '-' && value != '_' && value != '.')
                    return false;
            return true;
        }
    }
}
