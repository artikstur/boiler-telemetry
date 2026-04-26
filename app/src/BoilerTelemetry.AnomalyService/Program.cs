using BoilerTelemetry.AnomalyService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AnomalyServiceSettings>(
    builder.Configuration.GetSection("AnomalyService"));

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("CrudApi", client =>
    client.BaseAddress = new Uri(builder.Configuration["CrudApiBaseUrl"] ?? "http://localhost:8080"));

builder.Services.AddHostedService<AnomalyDetectionWorker>();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
