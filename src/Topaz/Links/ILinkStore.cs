namespace Topaz.Links;

public interface ILinkStore
{
    bool TryAdd(string code, string originalUrl);
    string? Find(string code);
}
