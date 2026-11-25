using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using StackExchange.Redis;
using Toobit.Platform.Api.Health;
using Toobit.Platform.Api.Observability;
using Toobit.Platform.Api.Startup;
using Toobit.Platform.Api.Workers;
using Toobit.Platform.Application;
using Toobit.Platform.Execution;
using Toobit.Platform.Infrastructure.Config;
using Toobit.Platform.Infrastructure.Persistence;
using Toobit.Platform.Infrastructure.Resilience;
using Toobit.Platform.Infrastructure.Security;
using Toobit.Platform.Infrastructure.Toobit;
using Toobit.Platform.MarketScanner;
using Toobit.Platform.Risk;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext().Enrich.With<SecretMaskingEnricher>()
    .WriteTo.Console(new RenderedCompactJsonFormatter())     // structured JSON logs
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext().Enrich.With<SecretMaskingEnricher>()
        .WriteTo.Console(new RenderedCompactJsonFormatter()));

    // ---- Secrets + fail-fast config validation (env in dev; swap ISecretProvider for Vault/ASM in prod) ----------
    ISecretProvider secrets = new EnvSecretProvider();
    SecretValidation.ValidateOrThrow(secrets);
    builder.Services.AddSingleton(secrets);

    builder.Services.Configure<ToobitOptions>(builder.Configuration.GetSection("Toobit"));
    builder.Services.AddOptions<StrategyOptions>().Bind(builder.Configuration.GetSection("Strategy"))
        .Validate(StrategyOptions.IsValid, "Strategy risk limits and SL/TP must be set and > 0").ValidateOnStart();
    builder.Services.AddOptions<ScannerOptions>().Bind(builder.Configuration.GetSection("Scanner"));
    builder.Services.AddSingleton(builder.Configuration.GetSection("Executor").Get<ExecutorOptions>() ?? new ExecutorOptions());
    builder.Services.AddSingleton(builder.Configuration.GetSection("Reconciliation").Get<ReconciliationOptions>() ?? new ReconciliationOptions());

    // ---- Persistence / cache ----------------------------------------------------------------------------------
    builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(secrets.GetRequired("POSTGRES_CONNECTION")));
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        ConnectionMultiplexer.Connect(new ConfigurationOptions { AbortOnConnectFail = false, EndPoints = { secrets.GetRequired("REDIS_CONNECTION") } }));

    builder.Services.AddSingleton<IClock, SystemClock>();
    builder.Services.AddSingleton<IOrderRepository, EfOrderRepository>();
    builder.Services.AddSingleton<ITrendingRepository, EfTrendingRepository>();
    builder.Services.AddSingleton<ISignalRepository, EfSignalRepository>();
    builder.Services.AddSingleton<IRiskEventRepository, EfRiskEventRepository>();
    builder.Services.AddSingleton<IReconciliationRepository, EfReconciliationRepository>();
    builder.Services.AddSingleton<IAuditLog, EfAuditLog>();
    builder.Services.AddSingleton<ISymbolPauseStore, DbSymbolPauseStore>();
    builder.Services.AddSingleton<IFeatureFlags, DbFeatureFlags>();
    builder.Services.AddSingleton<IRiskContextProvider, DbRiskContextProvider>();
    builder.Services.AddSingleton<IStrategyConfigProvider, OptionsStrategyConfigProvider>();
    builder.Services.AddSingleton<IKillSwitch>(sp => new FailClosedKillSwitch(
        new RedisKillSwitchStore(sp.GetRequiredService<IConnectionMultiplexer>()),
        new DbKillSwitchStore(sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<IClock>()),
        sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ILogger<FailClosedKillSwitch>>()));

    // ---- Exchange (ONLY here) + isolated resilience pipelines -------------------------------------------------
    builder.Services.AddToobitClients();
    builder.Services.AddSingleton<IScannerExchange, ToobitScannerExchange>();
    builder.Services.AddSingleton<ITradingExchange, ToobitTradingExchange>();
    builder.Services.AddHttpClient("analytics", c => c.BaseAddress = new Uri(builder.Configuration["Analytics:BaseUrl"] ?? "http://analytics:8000"));

    // ---- Domain services --------------------------------------------------------------------------------------
    builder.Services.AddSingleton<RiskEngine>();
    builder.Services.AddSingleton<ScannerState>();
    builder.Services.AddSingleton(sp => new OrderQueue(builder.Configuration.GetValue("Pipeline:QueueCapacity", 256), sp.GetRequiredService<ILogger<OrderQueue>>()));
    builder.Services.AddSingleton<OrderExecutor>();
    builder.Services.AddSingleton<SignalService>();
    builder.Services.AddSingleton<PriceBook>();
    builder.Services.AddSingleton(new CandleBuffer(capacity: 500));
    builder.Services.Configure<WsOptions>(builder.Configuration.GetSection("Ws"));
    builder.Services.Configure<AnalysisOptions>(builder.Configuration.GetSection("Analysis"));
    builder.Services.AddHostedService(sp => new ToobitWebSocketFeed(sp.GetRequiredService<PriceBook>(), sp.GetRequiredService<CandleBuffer>(),
        () => sp.GetRequiredService<ScannerState>().Top.Select(t => t.Symbol).ToList(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WsOptions>>(), sp.GetRequiredService<ILogger<ToobitWebSocketFeed>>()));
    builder.Services.AddHostedService<AnalysisWorker>();
    builder.Services.AddHostedService<ScannerWorker>();
    builder.Services.AddHostedService<OrderWorker>();
    builder.Services.Configure<RetentionOptions>(builder.Configuration.GetSection("Retention"));
    builder.Services.AddHostedService<RetentionService>();
    builder.Services.AddHostedService<ReconciliationService>();

    var internalKey = new InternalKey(secrets.GetRequired("INTERNAL_HMAC_KEY_ID"), Encoding.UTF8.GetBytes(secrets.GetRequired("INTERNAL_HMAC_KEY")),
        new HashSet<string> { "signals:write" });
    builder.Services.AddSingleton(new InternalHmacVerifier(new Dictionary<string, InternalKey> { [internalKey.KeyId] = internalKey },
        TimeSpan.FromSeconds(builder.Configuration.GetValue("Internal:MaxSkewSeconds", 60))));

    // ---- AuthN/AuthZ: short-lived JWT (<=15 min) + RBAC roles Admin/Trader/Viewer ------------------------------
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "toobit-platform",
            ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"] ?? "toobit-dashboard",
            ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secrets.GetRequired("JWT_SIGNING_KEY"))),
        };
    });
    builder.Services.AddAuthorization();
    var jwtSettings = new JwtSettings(builder.Configuration["Jwt:Issuer"] ?? "toobit-platform", builder.Configuration["Jwt:Audience"] ?? "toobit-dashboard",
        Encoding.UTF8.GetBytes(secrets.GetRequired("JWT_SIGNING_KEY")), TimeSpan.FromMinutes(builder.Configuration.GetValue("Jwt:AccessMinutes", 15))).Validate();
    builder.Services.AddSingleton(new TokenService(jwtSettings));
    builder.Services.AddSingleton(PiiProtector.FromBase64(secrets.GetRequired("PII_ENCRYPTION_KEY")));
    builder.Services.AddHostedService<AdminSeeder>();
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins((builder.Configuration["Cors:Origins"] ?? "http://localhost:5173").Split(','))
        .AllowAnyHeader().AllowAnyMethod()));

    // ---- Health -------------------------------------------------------------------------------------------------
    builder.Services.AddHealthChecks()
        .AddCheck<PostgresHealth>("postgres", tags: new[] { "ready" })
        .AddCheck<RedisHealth>("redis", tags: new[] { "ready" })
        .AddCheck<ToobitHealth>("toobit", tags: new[] { "ready" })
        .AddCheck<PythonHealth>("analytics", tags: new[] { "ready" });

    // ---- OpenTelemetry: traces + metrics (Prometheus scrape at /metrics) ----------------------------------------
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("toobit-api"))
        .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation()
            .AddOtlpExporter())     // endpoint via OTEL_EXPORTER_OTLP_ENDPOINT (jaeger in the observability profile)
        .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation()
            .AddMeter(PlatformMetrics.MeterName).AddPrometheusExporter());

    var app = builder.Build();
    app.UseSerilogRequestLogging();
    app.UseMiddleware<CorrelationMiddleware>();
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();
    if (!app.Environment.IsProduction()) { app.UseSwagger(); app.UseSwaggerUI(); }   // OpenAPI spec -> contracts/openapi.json
    app.MapControllers();
    app.MapPrometheusScrapingEndpoint("/metrics");
    app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new()
    {
        Predicate = r => r.Tags.Contains("ready"),
        // Degraded (Redis/Toobit/Python down) still reports ready=200: the system degrades gracefully; Postgres is the hard dependency.
        ResultStatusCodes = { [HealthStatus.Degraded] = StatusCodes.Status200OK },
    });

    // DEV/COMPOSE ONLY (Db:EnsureCreated=true). Staging/prod apply EF migrations in the CD job: `dotnet ef database update`.
    if (builder.Configuration.GetValue<bool>("Db:EnsureCreated"))
    {
        using var boot = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        boot.Database.EnsureCreated();
    }
    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host terminated unexpectedly");   // message never contains secret values
    throw;
}
finally { Log.CloseAndFlush(); }

public partial class Program { }   // for WebApplicationFactory in integration tests
