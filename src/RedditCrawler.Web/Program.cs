using RedditCrawler;
using RedditCrawler.Configuration;
using RedditCrawler.Services;
using RedditCrawler.Web.Components;
using RedditCrawler.Web.Services;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.SignalR", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting RedditCrawler Web");

    var builder = WebApplication.CreateBuilder(args);

    // Load user-overridden settings persisted by SettingsPersistenceService (optional, created on first save).
    // Written to the persist directory (mounted volume) so it survives image rebuilds without polluting /app.
    var persistDir = Environment.GetEnvironmentVariable("SETTINGS_PERSIST_DIR")
        ?? Path.Combine(builder.Environment.ContentRootPath, "persist");
    Directory.CreateDirectory(persistDir);
    builder.Configuration.AddJsonFile(Path.Combine(persistDir, "appsettings.user.json"), optional: true, reloadOnChange: false);

    builder.Host.UseSerilog((ctx, services, config) => config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore.SignalR", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    var crawlerConfig = builder.Configuration.GetSection("Crawler").Get<CrawlerConfig>() ?? new CrawlerConfig();
    var ollamaConfig = builder.Configuration.GetSection("Ollama").Get<OllamaConfig>() ?? new OllamaConfig();
    var vectorConfig = builder.Configuration.GetSection("Vector").Get<VectorConfig>() ?? new VectorConfig();
    var ragConfig = builder.Configuration.GetSection("Rag").Get<RagConfig>() ?? new RagConfig();
    var openRouterConfig = builder.Configuration.GetSection("OpenRouter").Get<OpenRouterConfig>() ?? new OpenRouterConfig();

    Log.Information("Config — Subreddits: {Subs}, Ollama: {OllamaUrl} (enabled={OllamaEnabled}), Vector DB: enabled={VectorEnabled}, ChatProvider: {Provider}",
        string.Join(", ", crawlerConfig.Subreddits),
        ollamaConfig.BaseUrl, ollamaConfig.Enabled,
        vectorConfig.Enabled,
        ragConfig.ChatProvider);

    builder.Services.AddRedditCrawlerCore(crawlerConfig, ollamaConfig, vectorConfig, ragConfig, openRouterConfig);
    builder.Services.AddSingleton<CrawlOrchestrator>();
    builder.Services.AddSingleton<ConnectionHealthService>();
    builder.Services.AddSingleton<RateLimiterService>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<SettingsPersistenceService>(sp =>
        new SettingsPersistenceService(
            persistDir,
            sp.GetRequiredService<ILogger<SettingsPersistenceService>>(),
            ollamaConfig, vectorConfig, ragConfig, openRouterConfig));

    var app = builder.Build();

    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0}ms";
        opts.GetLevel = (ctx, elapsed, ex) =>
            ex != null || ctx.Response.StatusCode >= 500
                ? LogEventLevel.Error
                : ctx.Request.Path.StartsWithSegments("/_blazor") || ctx.Request.Path.StartsWithSegments("/_framework")
                    ? LogEventLevel.Debug
                    : LogEventLevel.Information;
    });

    if (!app.Environment.IsDevelopment())
        app.UseExceptionHandler("/Error");

    app.UseStaticFiles();
    app.UseAntiforgery();

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
