using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Accounts;
using Xunit;

namespace MphRead.Tests;

public sealed class MacKeychainSessionStoreTests
{
    private const string Scope = "https://prime.example.test/";
    private static readonly byte[] OldValue = { 1, 2, 3, 4 };
    private static readonly byte[] NewValue = { 5, 6, 7, 8 };

    [Fact]
    public async Task ExistingValueReplacementModifiesInPlace()
    {
        var api = new FakeMacKeychainApi();
        var store = new MacKeychainSessionStore(api);

        await store.WriteAsync(Scope, OldValue);
        await store.WriteAsync(Scope, NewValue);

        Assert.Equal(NewValue, await store.ReadAsync(Scope));
        Assert.Equal(1, api.ModifyCalls);
        Assert.Equal(0, api.DeleteCalls);
        Assert.Equal(0, api.OutstandingNativeBuffers);
    }

    [Fact]
    public async Task ModifyFailurePreservesExistingValue()
    {
        var api = new FakeMacKeychainApi();
        var store = new MacKeychainSessionStore(api);
        await store.WriteAsync(Scope, OldValue);
        api.ModifyStatus = -50;

        await Assert.ThrowsAsync<IOException>(() => store.WriteAsync(Scope, NewValue).AsTask());

        Assert.Equal(OldValue, await store.ReadAsync(Scope));
        Assert.Equal(0, api.DeleteCalls);
        Assert.All(api.LastMutationBuffer!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task MissingValueIsAdded()
    {
        var api = new FakeMacKeychainApi();
        var store = new MacKeychainSessionStore(api);

        await store.WriteAsync(Scope, OldValue);

        Assert.Equal(OldValue, await store.ReadAsync(Scope));
        Assert.Equal(1, api.AddCalls);
        Assert.Equal(0, api.ModifyCalls);
        Assert.Equal(0, api.DeleteCalls);
    }

    [Fact]
    public async Task DuplicateAddRaceFindsAndModifiesTheWinner()
    {
        var api = new FakeMacKeychainApi { ReportMissingOnce = true };
        api.OnReportedMissing = static (fake, service, account)
            => fake.Seed(service, account, OldValue);
        var store = new MacKeychainSessionStore(api);

        await store.WriteAsync(Scope, NewValue);

        Assert.Equal(NewValue, await store.ReadAsync(Scope));
        Assert.Equal(1, api.AddCalls);
        Assert.Equal(1, api.ModifyCalls);
        Assert.Equal(0, api.DeleteCalls);
    }

    [Fact]
    public async Task DuplicateRecoveryModifyFailurePreservesWinner()
    {
        var api = new FakeMacKeychainApi
        {
            ReportMissingOnce = true,
            ModifyStatus = -51
        };
        api.OnReportedMissing = static (fake, service, account)
            => fake.Seed(service, account, OldValue);
        var store = new MacKeychainSessionStore(api);

        await Assert.ThrowsAsync<IOException>(() => store.WriteAsync(Scope, NewValue).AsTask());

        Assert.Equal(OldValue, await store.ReadAsync(Scope));
        Assert.Equal(0, api.DeleteCalls);
        Assert.All(api.LastMutationBuffer!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task CancellationAfterLookupPreventsSynchronousMutation()
    {
        var api = new FakeMacKeychainApi();
        var store = new MacKeychainSessionStore(api);
        await store.WriteAsync(Scope, OldValue);
        using var cancellation = new CancellationTokenSource();
        api.OnFind = (_, _, _, _) => cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.WriteAsync(Scope, NewValue, cancellation.Token).AsTask());

        api.OnFind = null;
        Assert.Equal(OldValue, await store.ReadAsync(Scope));
        Assert.Equal(0, api.ModifyCalls);
        Assert.Equal(0, api.DeleteCalls);
    }

    [Fact]
    public async Task AlreadyCanceledWritePerformsNoLookupOrMutation()
    {
        var api = new FakeMacKeychainApi();
        var store = new MacKeychainSessionStore(api);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.WriteAsync(Scope, NewValue, cancellation.Token).AsTask());

        Assert.Equal(0, api.FindCalls);
        Assert.Equal(0, api.AddCalls);
        Assert.Equal(0, api.ModifyCalls);
        Assert.Equal(0, api.DeleteCalls);
    }

    private sealed class FakeMacKeychainApi : IMacKeychainApi
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
        private readonly Dictionary<IntPtr, string> _items = new();
        private readonly Dictionary<IntPtr, IntPtr> _contentAllocations = new();
        private long _nextItem;

        public int FindCalls { get; private set; }
        public int AddCalls { get; private set; }
        public int ModifyCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int ModifyStatus { get; set; }
        public bool ReportMissingOnce { get; set; }
        public Action<FakeMacKeychainApi, byte[], byte[]>? OnReportedMissing { get; set; }
        public Action<FakeMacKeychainApi, byte[], byte[], int>? OnFind { get; set; }
        public byte[]? LastMutationBuffer { get; private set; }
        public int OutstandingNativeBuffers => _contentAllocations.Count;

        public int FindGenericPassword(byte[] service, byte[] account,
            out uint length, out IntPtr data, out IntPtr item)
        {
            FindCalls++;
            OnFind?.Invoke(this, service, account, FindCalls);
            string key = Key(service, account);
            if (ReportMissingOnce)
            {
                ReportMissingOnce = false;
                OnReportedMissing?.Invoke(this, service, account);
                length = 0;
                data = IntPtr.Zero;
                item = IntPtr.Zero;
                return MacKeychainSessionStore.ItemNotFound;
            }
            if (!_values.TryGetValue(key, out byte[]? value))
            {
                length = 0;
                data = IntPtr.Zero;
                item = IntPtr.Zero;
                return MacKeychainSessionStore.ItemNotFound;
            }

            item = new IntPtr(++_nextItem);
            _items[item] = key;
            IntPtr allocation = Marshal.AllocHGlobal(value.Length);
            Marshal.Copy(value, 0, allocation, value.Length);
            data = allocation;
            _contentAllocations[data] = allocation;
            length = (uint)value.Length;
            return MacKeychainSessionStore.Success;
        }

        public int AddGenericPassword(byte[] service, byte[] account, byte[] value)
        {
            AddCalls++;
            LastMutationBuffer = value;
            string key = Key(service, account);
            if (_values.ContainsKey(key)) return MacKeychainSessionStore.DuplicateItem;
            _values[key] = (byte[])value.Clone();
            return MacKeychainSessionStore.Success;
        }

        public int ModifyContent(IntPtr item, byte[] value)
        {
            ModifyCalls++;
            LastMutationBuffer = value;
            if (ModifyStatus != MacKeychainSessionStore.Success) return ModifyStatus;
            if (!_items.TryGetValue(item, out string? key)) return -52;
            _values[key] = (byte[])value.Clone();
            return MacKeychainSessionStore.Success;
        }

        public int DeleteItem(IntPtr item)
        {
            DeleteCalls++;
            if (!_items.TryGetValue(item, out string? key)) return -53;
            _values.Remove(key);
            return MacKeychainSessionStore.Success;
        }

        public void Release(IntPtr item) => _items.Remove(item);

        public int FreeContent(IntPtr data)
        {
            if (!_contentAllocations.Remove(data, out IntPtr allocation)) return -54;
            Marshal.FreeHGlobal(allocation);
            return MacKeychainSessionStore.Success;
        }

        public void Seed(byte[] service, byte[] account, byte[] value)
            => _values[Key(service, account)] = (byte[])value.Clone();

        private static string Key(byte[] service, byte[] account)
            => Convert.ToHexString(service) + ":" + Convert.ToHexString(account);
    }
}
