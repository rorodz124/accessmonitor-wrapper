using AccessMonitorWrapper.Services;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AccessMonitor Wrapper API",
        Version = "v1",
        Description = "Interface para testar a validação de acessibilidade via AccessMonitor."
    });
});

// Register AccessMonitorService with a typed HttpClient
builder.Services.AddHttpClient<AccessMonitorService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    var baseUrl = config["AccessMonitor:BaseUrl"] ?? "http://localhost:3000";
    var referer = config["AccessMonitor:Referer"] ?? "http://localhost:3000";

    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add("Referer", referer);
    client.Timeout = TimeSpan.FromSeconds(180);
});

var app = builder.Build();

// Configure the HTTP request pipeline
var defaultFileOptions = new DefaultFilesOptions();
defaultFileOptions.DefaultFileNames.Clear();
defaultFileOptions.DefaultFileNames.Add("ValidateUrl.html");
app.UseDefaultFiles(defaultFileOptions);
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "AccessMonitor Wrapper API v1");
        options.RoutePrefix = "swagger";
    });

    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();