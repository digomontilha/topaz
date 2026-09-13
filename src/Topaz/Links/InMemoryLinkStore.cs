using System.Collections.Concurrent;
namespace Topaz.Links;

public sealed class InMemoryLinkStore : ILinkStore
{
    private readonly ConcurrentDictionary<string, string> links = new(StringComparer.Ordinal);
    public bool TryAdd(string code, string originalUrl) => links.TryAdd(code, originalUrl);
    public string? Find(string code) => links.GetValueOrDefault(code);
}
