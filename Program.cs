using AccessMonitorWrapper.Services;

var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();

// OpenAPI / Swagger
builder.Services.AddOpenApi();

// Register AccessMonitorService with a typed HttpClient
builder.Services.AddHttpClient<AccessMonitorService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    var baseUrl = config["AccessMonitor:BaseUrl"] ?? "http://localhost:3000";
    var referer = config["AccessMonitor:Referer"] ?? "http://localhost:3000";

    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add("Referer", referer);
    client.Timeout = TimeSpan.FromSeconds(120);
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
