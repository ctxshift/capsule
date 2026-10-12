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

        public UiCapabilityGrant(IEnumerable<UiObservationCapability> observationTopics,
            IEnumerable<ushort> actionCapabilities, IEnumerable<ushort> inputCapabilities)
        {
            observations = new HashSet<uint>();
            actions = new HashSet<ushort>();
            inputs = new HashSet<ushort>();
            if (observationTopics != null)
                foreach (var item in observationTopics)
                {
                    if (item.Topic == 0 || item.SchemaVersion == 0 ||
                        !observations.Add(item.Key) || observations.Count > MaxCapabilitiesPerKind)
                        throw new ArgumentException("Invalid or duplicate observation grant.");
                }
            CopyIds(actionCapabilities, actions, "action");
            CopyIds(inputCapabilities, inputs, "input");
        }

        static void CopyIds(IEnumerable<ushort> source, HashSet<ushort> target, string name)
        {
            if (source == null) return;
            foreach (ushort id in source)
                if (id == 0 || !target.Add(id) || target.Count > MaxCapabilitiesPerKind)
                    throw new ArgumentException("Invalid or duplicate " + name + " grant.");
        }

        public bool AllowsObservation(ushort topic, ushort schemaVersion) =>
            observations.Contains((uint)topic << 16 | schemaVersion);
        public bool AllowsAction(ushort capability) => actions.Contains(capability);
        public bool AllowsInput(ushort capability) => inputs.Contains(capability);
        public bool HasInput => inputs.Count != 0;
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
        readonly uint ownerNonce;
        int consumed;
        internal UiAddonActivation(uint owner, UiCapabilityGrant grant)
        { ownerNonce = owner; Grant = grant; }
        public uint OwnerNonce => ownerNonce;
        public uint ConsumeOwner()
        {
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
            new Dictionary<string, UiAddonRegistration>(StringComparer.Ordinal);

        public UiAddonRegistration Register(string id, UiAddonTrust trust, UiCapabilityGrant grant)
        {
            if (grant == null) throw new ArgumentNullException(nameof(grant));
            if (string.IsNullOrEmpty(id) || id.Length > 96 || !ValidId(id))
                throw new ArgumentException("Addon registration ID is invalid.", nameof(id));
            if (!Enum.IsDefined(typeof(UiAddonTrust), trust))
                throw new ArgumentOutOfRangeException(nameof(trust));
            lock (registrations)
            {
                if (registrations.ContainsKey(id))
                    throw new ArgumentException("Addon already registered: " + id);
                var registration = new UiAddonRegistration(this, id, trust, grant);
                registrations.Add(id, registration);
                return registration;
            }
        }

        public UiAddonActivation Activate(UiAddonRegistration registration)
        {
            if (registration == null) throw new ArgumentNullException(nameof(registration));
            lock (registrations)
            {
                if (!ReferenceEquals(registration.Registry, this) ||
                    !registrations.TryGetValue(registration.Id, out var owned) ||
                    !ReferenceEquals(owned, registration))
                    throw new ArgumentException("Registration belongs to another host.", nameof(registration));
            }
            return new UiAddonActivation(AllocateOwner(ref nextOwner), registration.Grant);
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
