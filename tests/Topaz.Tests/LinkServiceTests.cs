using Topaz.Links;
namespace Topaz.Tests;

public class LinkServiceTests
{
    [Fact]
    public async Task GeneratedLinkResolvesToOriginalUrl()
    {
        using var service = new LinkService(new InMemoryLinkStore());
        var result = await service.CreateAsync("https://example.com/a?q=1", null);
        Assert.Equal(CreationStatus.Created, result.Status);
        Assert.Equal(7, result.Code!.Length);
        Assert.Equal("https://example.com/a?q=1", service.Find(result.Code));
        Assert.Null(service.Find("missing"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("/relative", null)]
    [InlineData("https://user:pass@example.com", null)]
    [InlineData("https://example.com", "bad alias")]
    [InlineData("https://example.com", "ação")]
    public async Task InvalidInputIsRejected(string? url, string? alias)
    {
        using var service = new LinkService(new InMemoryLinkStore());
        Assert.Equal(CreationStatus.Invalid, (await service.CreateAsync(url, alias)).Status);
    }

    [Fact]
    public async Task ConcurrentRequestsWithSameAliasHaveExactlyOneWinner()
    {
        using var service = new LinkService(new InMemoryLinkStore());
        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ =>
            Task.Run(() => service.CreateAsync("https://example.com", "same"))));
        Assert.Single(results, r => r.Status == CreationStatus.Created);
        Assert.Equal(49, results.Count(r => r.Status == CreationStatus.Conflict));
    }

    [Fact]
    public async Task GenerationIsSerializedAndGateIsReleasedAfterFailure()
    {
        var store = new ObservedStore();
        using var service = new LinkService(store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("https://example.com", "fail"));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            Task.Run(() => service.CreateAsync("https://example.com", $"alias{i}"))));
        Assert.Equal(1, store.MaxConcurrency);
    }

    private sealed class ObservedStore : ILinkStore
    {
        private int active;
        public int MaxConcurrency;
        public bool TryAdd(string code, string originalUrl)
        {
            var current = Interlocked.Increment(ref active);
            try
            {
                int previous;
                do { previous = MaxConcurrency; }
                while (current > previous && Interlocked.CompareExchange(ref MaxConcurrency, current, previous) != previous);
                Thread.Sleep(5);
                if (code == "fail") throw new InvalidOperationException("Simulated storage failure");
                return true;
            }
            finally { Interlocked.Decrement(ref active); }
        }
        public string? Find(string code) => null;
    }
}
