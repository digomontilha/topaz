using Microsoft.AspNetCore.Mvc;
using Topaz.Links;
namespace Topaz.Controllers;

[ApiController]
public sealed class LinksController(LinkService service) : Controller
{
    [HttpGet("/")]
    public IActionResult Index() => View();

    [HttpPost("/api/links")]
    public async Task<IActionResult> Create(CreateLinkRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request.Url, request.Alias, cancellationToken);
        if (result.Status == CreationStatus.Invalid) return BadRequest(new { error = result.Error });
        if (result.Status == CreationStatus.Conflict) return Conflict(new { error = result.Error });
        var path = Url.RouteUrl("ResolveLink", new { code = result.Code })!;
        return Created(path, new { code = result.Code, shortUrl = path });
    }

    [HttpGet("/r/{code}", Name = "ResolveLink")]
    public IActionResult Resolve(string code)
    {
        var url = service.Find(code);
        return url is null ? NotFound(new { error = "Link não encontrado." }) : Redirect(url);
    }
}

public sealed record CreateLinkRequest(string? Url, string? Alias);
