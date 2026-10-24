using System;
using System.Threading;
using System.Threading.Tasks;
using Capsule.InventoryFeature;

// Compilation itself verifies implementation of the generated engine provider interface.
sealed class Provider : IInventoryProvider
{
    public event Action<TInventoryState> StateChanged;
    public TInventoryState GetState() => new TInventoryState(true, 7, 9,
        Array.AsReadOnly(new[] { new TInventoryEntry("sword-é", "bag", 2, true) }));
    public Task<TTransferResult> Transfer(TTransfer request, CancellationToken cancellationToken)
        => Task.FromResult(new TTransferResult(request.requestId, 1, "fixture"));
    public void Publish() => StateChanged?.Invoke(GetState());
}
static class Program
{
    static void Main(string[] args)
    {
        byte[] input = Convert.FromBase64String(args[0]);
        var decoded = FeatureCodec.DecodeInventoryState(input);
        if (decoded.entries[0].item != "sword-é" || decoded.epoch != 7 || decoded.revision != 9)
            throw new Exception("TS to C# fixture mismatch");
        byte[] output = FeatureCodec.Encode(new Provider().GetState());
        if (Convert.ToBase64String(output) != args[0]) throw new Exception("C# to TS fixture mismatch");
        var provider = new Provider();
        using var binding = InventoryContract.Bind(provider);
        if (binding.Definition.Dependencies.Count != 1 || binding.StatePermission("State") != "inventory.read" || binding.RequestPermission("Transfer") != "inventory.transfer") throw new Exception("Generated binding metadata mismatch");
        byte[] first = null, second = null; int publications = 0;
        using var one = binding.Subscribe("State", bytes => { first = bytes; publications++; });
        using var two = binding.Subscribe("State", bytes => second = bytes);
        if (!ReferenceEquals(first, second) || Convert.ToBase64String(first) != args[0]) throw new Exception("Binding did not share initial encoding");
        provider.Publish();
        if (publications != 2 || !ReferenceEquals(first, second)) throw new Exception("Binding publication fanout mismatch");
        var response = binding.Request("Transfer", FeatureCodec.Encode(new TTransfer(3, 7, 9, "sword-é", "mainHand")), CancellationToken.None).GetAwaiter().GetResult();
        if (FeatureCodec.DecodeTransferResult(response).requestId != 3) throw new Exception("Generated request dispatch mismatch");
        one.Dispose(); provider.Publish();
        if (publications != 2) throw new Exception("Retired subscription invoked");
        binding.Dispose(); provider.Publish();
        bool stopped = false; try { binding.Subscribe("State", _ => { }); } catch (ObjectDisposedException) { stopped = true; }
        if (!stopped) throw new Exception("Disposed binding accepted subscription");
        for (int n = 0; n < input.Length; n++)
        {
            var truncated = new byte[n]; Array.Copy(input, truncated, n);
            bool rejected = false;
            try { FeatureCodec.DecodeInventoryState(truncated); } catch (FormatException) { rejected = true; }
            if (!rejected) throw new Exception("Accepted truncated payload");
        }
        Console.WriteLine("Native contract and cross-language fixture passed");
    }
}
