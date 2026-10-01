namespace MathFirst.Core.Tests;

using System;
using MathFirst.Application.Telemetry;
using Xunit;

public sealed class InstallationIdProviderTests
{
    private sealed class InMemoryInstallationIdStore : IInstallationIdStore
    {
        public string? StoredValue { get; set; }
        public int GetCallCount { get; private set; }
        public int SetCallCount { get; private set; }
        public int ClearCallCount { get; private set; }
        public string? LastSetValue { get; private set; }

        public InMemoryInstallationIdStore(string? initialValue = null)
        {
            StoredValue = initialValue;
        }

        public string? Get()
        {
            GetCallCount++;
            return StoredValue;
        }

        public void Set(string value)
        {
            SetCallCount++;
            LastSetValue = value;
            StoredValue = value;
        }

        public void Clear()
        {
            ClearCallCount++;
            StoredValue = null;
        }
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenStoreIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new PreferenceInstallationIdProvider(null!));
    }

    [Fact]
    public void GetOrCreateInstallationId_WhenStoreIsEmpty_GeneratesPersistsAndReturnsGuidD()
    {
        var store = new InMemoryInstallationIdStore(null);
        var provider = new PreferenceInstallationIdProvider(store);

        var id = provider.GetOrCreateInstallationId();

        Assert.NotNull(id);
        Assert.True(Guid.TryParseExact(id, "D", out var parsedGuid));
        Assert.NotEqual(Guid.Empty, parsedGuid);
        Assert.Equal(id, store.StoredValue);
        Assert.Equal(id, store.LastSetValue);
        Assert.Equal(1, store.SetCallCount);
        Assert.Equal(1, store.GetCallCount);
    }

    [Fact]
    public void GetOrCreateInstallationId_WhenValidGuidDExists_ReturnsExistingWithoutSetting()
    {
        var existingGuidString = Guid.NewGuid().ToString("D");
        var store = new InMemoryInstallationIdStore(existingGuidString);
        var provider = new PreferenceInstallationIdProvider(store);

        var id = provider.GetOrCreateInstallationId();

        Assert.Equal(existingGuidString, id);
        Assert.Equal(0, store.SetCallCount);
        Assert.Equal(1, store.GetCallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    [InlineData("d3b07384d11345d3bc2646c07abfb29d")] // N format (no hyphens)
    [InlineData("{d3b07384-d113-45d3-bc26-46c07abfb29d}")] // B format (braces)
    [InlineData("(d3b07384-d113-45d3-bc26-46c07abfb29d)")] // P format (parentheses)
    public void GetOrCreateInstallationId_WhenStoredValueIsInvalid_ReplacesAndPersistsNewValidGuidD(string invalidStoredValue)
    {
        var store = new InMemoryInstallationIdStore(invalidStoredValue);
        var provider = new PreferenceInstallationIdProvider(store);

        var id = provider.GetOrCreateInstallationId();

        Assert.NotNull(id);
        Assert.NotEqual(invalidStoredValue, id);
        Assert.True(Guid.TryParseExact(id, "D", out var parsedGuid));
        Assert.NotEqual(Guid.Empty, parsedGuid);
        Assert.Equal(id, store.StoredValue);
        Assert.Equal(1, store.SetCallCount);
    }

    [Fact]
    public void GetOrCreateInstallationId_WhenStoredValueIsGuidEmpty_ReplacesAndPersistsNewValidGuidD()
    {
        var emptyGuidString = Guid.Empty.ToString("D"); // "00000000-0000-0000-0000-000000000000"
        var store = new InMemoryInstallationIdStore(emptyGuidString);
        var provider = new PreferenceInstallationIdProvider(store);

        var id = provider.GetOrCreateInstallationId();

        Assert.NotNull(id);
        Assert.NotEqual(emptyGuidString, id);
        Assert.True(Guid.TryParseExact(id, "D", out var parsedGuid));
        Assert.NotEqual(Guid.Empty, parsedGuid);
        Assert.Equal(id, store.StoredValue);
        Assert.Equal(1, store.SetCallCount);
    }

    [Fact]
    public void ClearInstallationId_InvokesStoreClearWithoutGeneratingNewId()
    {
        var existingGuidString = Guid.NewGuid().ToString("D");
        var store = new InMemoryInstallationIdStore(existingGuidString);
        var provider = new PreferenceInstallationIdProvider(store);

        provider.ClearInstallationId();

        Assert.Equal(1, store.ClearCallCount);
        Assert.Null(store.StoredValue);
        Assert.Equal(0, store.SetCallCount);
    }

    [Fact]
    public void ClearInstallationId_FollowedByGetOrCreate_GeneratesNewId()
    {
        var initialGuidString = Guid.NewGuid().ToString("D");
        var store = new InMemoryInstallationIdStore(initialGuidString);
        var provider = new PreferenceInstallationIdProvider(store);

        provider.ClearInstallationId();
        var newId = provider.GetOrCreateInstallationId();

        Assert.NotNull(newId);
        Assert.True(Guid.TryParseExact(newId, "D", out var parsedGuid));
        Assert.NotEqual(Guid.Empty, parsedGuid);
        Assert.NotEqual(initialGuidString, newId);
        Assert.Equal(newId, store.StoredValue);
        Assert.Equal(1, store.SetCallCount);
        Assert.Equal(1, store.ClearCallCount);
    }
}
