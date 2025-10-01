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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthorization();

app.MapControllers();

// Only fallback to home.html for non-API routes
app.MapFallbackToFile("home.html").Add(endpointBuilder =>
{
    ((RouteEndpointBuilder)endpointBuilder).Order = int.MaxValue;
});

app.Run();