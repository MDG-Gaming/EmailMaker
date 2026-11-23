using EmailMaker.Components;
using EmailMaker.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // Designs are autosaved to browser storage; allow reading them back over SignalR.
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 2 * 1024 * 1024);

builder.Services.AddSingleton<ImageStore>();
builder.Services.AddSingleton<EmailHtmlRenderer>();
builder.Services.AddSingleton<EmailExporter>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

// Uploaded email images. Public on purpose: "hosted" exports link straight to these URLs.
app.MapGet($"{ImageStore.RequestPath}/{{id}}", (string id, ImageStore store, HttpContext http) =>
{
    if (!store.TryGet(id, out var path, out var contentType)) return Results.NotFound();
    http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    http.Response.Headers.XContentTypeOptions = "nosniff";
    return Results.File(path, contentType);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
