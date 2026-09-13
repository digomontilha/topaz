using System.Security.Cryptography;
namespace Topaz.Links;

public enum CreationStatus { Created, Invalid, Conflict }
public sealed record CreationResult(CreationStatus Status, string? Code = null, string? Error = null);

public sealed class LinkService(ILinkStore store) : IDisposable
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private readonly SemaphoreSlim generationGate = new(1, 1);

    public async Task<CreationResult> CreateAsync(string? originalUrl, string? alias, CancellationToken cancellationToken = default)
    {
        await generationGate.WaitAsync(cancellationToken);
        try
        {
            originalUrl = originalUrl?.Trim();
            if (originalUrl is null || originalUrl.Length > 2048 ||
                !Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
                return new(CreationStatus.Invalid, Error: "Informe uma URL HTTP ou HTTPS válida, sem credenciais e com até 2048 caracteres.");

            alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
            if (alias is not null && (alias.Length > 32 || !alias.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
                return new(CreationStatus.Invalid, Error: "O alias deve ter de 1 a 32 letras sem acentos, números, hífen ou sublinhado.");

            if (alias is not null)
                return store.TryAdd(alias, originalUrl)
                    ? new(CreationStatus.Created, alias)
                    : new(CreationStatus.Conflict, Error: "Este alias já está em uso.");

            // A reserva atômica trata colisões com outros códigos e aliases.
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var code = RandomNumberGenerator.GetString(Alphabet, 7);
                if (store.TryAdd(code, originalUrl)) return new(CreationStatus.Created, code);
            }
            return new(CreationStatus.Conflict, Error: "Não foi possível reservar um código. Tente novamente.");
        }
        finally { generationGate.Release(); }
    }

    public string? Find(string code) => store.Find(code);
    public void Dispose() => generationGate.Dispose();
}
