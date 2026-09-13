using Topaz.Links;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<ILinkStore, InMemoryLinkStore>();
builder.Services.AddSingleton<LinkService>();
var app = builder.Build();
app.UseStaticFiles();
app.MapControllers();
app.Run();
