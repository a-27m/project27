using Project27.Server;
using Project27.Server.Auth;
using Project27.Server.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));
builder.Services.AddProject27Auth(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(ServerStoreFactory.Create(builder.Configuration));
builder.Services.AddSingleton(new LockingOptions(
    TimeSpan.FromMinutes(builder.Configuration.GetValue("Locking:StaleAfterMinutes", 30))));
builder.Services.AddSingleton<ProjectEventBroker>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/healthz", () => Results.Ok()).AllowAnonymous();

app.MapProjectApi();

await app.Services.GetRequiredService<IServerStore>().Initialize(CancellationToken.None).ConfigureAwait(false);
await app.RunAsync().ConfigureAwait(false);

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
