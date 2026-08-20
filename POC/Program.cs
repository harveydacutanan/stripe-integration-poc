using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Stripe;
using POC.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Stripe
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// Register services
builder.Services.AddScoped<POC.Services.CustomerService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<WebhookService>();
builder.Services.AddSingleton<DeclineMessageResolver>();
builder.Services.AddScoped<RepaymentService>();
builder.Services.AddSingleton<WebhookStorageService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Behind a TLS-terminating proxy (ngrok while testing wallets locally, or Azure in
// production) the request reaches Kestrel over plain HTTP. Without honouring
// X-Forwarded-Proto, UseHttpsRedirection below would bounce every tunnelled request to
// https://localhost and the tunnel would appear broken.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseHttpsRedirection();

// Without an explicit Cache-Control, browsers apply heuristic caching to static files and
// keep serving stale CSS/JS after a change. Harmless in production, but it makes the POC
// look broken while iterating, so force revalidation in Development.
var isDevelopment = app.Environment.IsDevelopment();

void DisableCachingInDevelopment(StaticFileResponseContext context)
{
    if (isDevelopment)
    {
        context.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    }
}

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = DisableCachingInDevelopment
});

// Apple Pay on the web requires the domain association file to be served verbatim from
// /.well-known/apple-developer-merchantid-domain-association. The file has no extension,
// so the default static file provider will not serve it without this opt-in.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(builder.Environment.WebRootPath ?? builder.Environment.ContentRootPath, ".well-known")),
    RequestPath = "/.well-known",
    ServeUnknownFileTypes = true,
    DefaultContentType = "text/plain",
    OnPrepareResponse = DisableCachingInDevelopment
});
app.UseAuthorization();

app.MapControllers();

// Only fallback to home.html for non-API routes
app.MapFallbackToFile("home.html").Add(endpointBuilder =>
{
    ((RouteEndpointBuilder)endpointBuilder).Order = int.MaxValue;
});

app.Run();