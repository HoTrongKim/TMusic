using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using TMusic.Api.Admin;
using TMusic.Api.Downloads;
using TMusic.Api.Identity;
using TMusic.Api.Library;
using TMusic.Api.Providers;
using TMusic.Api.Imports;
using TMusic.Application.Identity;
using TMusic.Infrastructure.Downloads;
using TMusic.Infrastructure.Imports;
using TMusic.Infrastructure.Identity;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Providers;
using TMusic.Infrastructure.Storage;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options=>options.Limits.MaxRequestBodySize=128*1024);
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var railwayPort) && railwayPort is > 0 and <= 65535)
    builder.WebHost.UseUrls($"http://0.0.0.0:{railwayPort}");
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options=> {
    options.MultipartBodyLengthLimit=CoverImageStorage.MaxFileBytes+65_536;
});
builder.Services.AddProblemDetails(options=>options.CustomizeProblemDetails=context=>context.ProblemDetails.Extensions["traceId"]=context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<TMusicDb>((services,options)=> {
    var c=services.GetRequiredService<IConfiguration>();
    options.UseSqlServer(c.GetConnectionString("DefaultConnection") ?? c.GetConnectionString("TMusic") ?? throw new InvalidOperationException("SQL Server connection string is required."));
});
builder.Services.AddIdentityCore<Account>(options=> {
    options.User.RequireUniqueEmail=true; options.SignIn.RequireConfirmedEmail=false;
    options.Password.RequiredLength=12; options.Password.RequireUppercase=true; options.Password.RequireLowercase=true;
    options.Password.RequireDigit=true; options.Password.RequireNonAlphanumeric=true;
    options.Lockout.MaxFailedAccessAttempts=5; options.Lockout.DefaultLockoutTimeSpan=TimeSpan.FromMinutes(15);
}).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<TMusicDb>().AddSignInManager().AddDefaultTokenProviders();
builder.Services.Configure<DataProtectionTokenProviderOptions>(options=>options.TokenLifespan=TimeSpan.FromHours(1));
var protection=builder.Services.AddDataProtection().SetApplicationName("TMusic.V2");
if (builder.Configuration["DataProtection:KeyPath"] is { Length: >0 } keyPath) protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.Services.AddScoped<IAccountMailSender,SmtpAccountMailSender>();
builder.Services.AddSingleton<ICoverImageStorage,CoverImageStorage>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddSingleton<IExternalTokenVerifier,ExternalTokenVerifier>();
builder.Services.AddScoped<AppleTokens>();
builder.Services.AddHttpClient("apple",http=>http.Timeout=TimeSpan.FromSeconds(12)).RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(()=>new SocketsHttpHandler { AllowAutoRedirect=false,UseCookies=false,MaxResponseHeadersLength=16 });
builder.Services.AddProviders(builder.Configuration);
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<ApiDownloadBackgroundRunner>();
}
builder.Services.AddHealthChecks().AddCheck<SqlReady>("sql",tags:["ready"]);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IConfiguration>((options,config)=> {
    options.MapInboundClaims=false; options.IncludeErrorDetails=false; options.RequireHttpsMetadata=true;
    options.TokenValidationParameters=new TokenValidationParameters {
        ValidateIssuerSigningKey=true,ValidateAudience=true,ValidateIssuer=true,ValidateLifetime=true,RequireExpirationTime=true,
        ValidIssuer=SessionService.Issuer(config),ValidAudience=SessionService.Audience(config),ClockSkew=TimeSpan.FromSeconds(15),
        ValidAlgorithms=[SecurityAlgorithms.HmacSha256],IssuerSigningKeyResolver=(_,_,_,_)=>SessionService.SigningKey(config) is { } key?[key]:[]
    };
    options.Events=new JwtBearerEvents {
        OnMessageReceived=context=> {
            if (string.IsNullOrEmpty(context.Token) &&
                string.IsNullOrEmpty(context.Request.Headers.Authorization) &&
                context.Request.Path.StartsWithSegments("/api/admin") &&
                context.Request.Cookies.TryGetValue(AdminAuthConstants.AccessCookieName, out var cookieToken) &&
                !string.IsNullOrWhiteSpace(cookieToken))
            {
                context.Token=cookieToken;
                context.HttpContext.Items[AdminAuthConstants.AuthViaCookieItemKey]=true;
            }
            return Task.CompletedTask;
        },
        OnTokenValidated=async context=> {
            if (!Guid.TryParse(context.Principal?.FindFirstValue("sub"),out var user) || !Guid.TryParse(context.Principal.FindFirstValue("sid"),out var family)) { context.Fail("Invalid session."); return; }
            var db=context.HttpContext.RequestServices.GetRequiredService<TMusicDb>();
            var time=context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
            var ct=context.HttpContext.RequestAborted;
            if (!await db.UserSessions.AnyAsync(s=>s.UserId==user && s.FamilyId==family && s.RevokedAt==null && s.RotatedAt==null && s.ExpiresAt>time,ct)) { context.Fail("Session unavailable."); return; }
            var activeAccount=await db.Users.AsNoTracking().AnyAsync(u=>u.Id==user && u.DeletedAt==null && (!u.IsSuspended || (u.SuspendedUntil!=null && u.SuspendedUntil<=time)),ct);
            if (!activeAccount) context.Fail("Account unavailable.");
        }
    };
});
builder.Services.AddAdminAuthorization();
var adminCorsOrigins=(builder.Configuration["Cors:AdminOrigins"]??"")
    .Split([',',';',' '],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
    .Concat(builder.Configuration.GetSection("Cors:AdminOrigins").Get<string[]>()??[])
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
builder.Services.AddCors(options=> {
    options.AddPolicy("AdminWeb",policy=> {
        if (adminCorsOrigins.Length>0)
            policy.WithOrigins(adminCorsOrigins).AllowCredentials().WithHeaders("Content-Type","Authorization",AdminAuthConstants.CsrfHeaderName,"If-Match").WithMethods("GET","POST","PUT","PATCH","DELETE","OPTIONS");
        else if (builder.Environment.IsDevelopment())
            policy.WithOrigins("http://localhost:5173","http://127.0.0.1:5173","http://localhost:4173").AllowCredentials().WithHeaders("Content-Type","Authorization",AdminAuthConstants.CsrfHeaderName,"If-Match").WithMethods("GET","POST","PUT","PATCH","DELETE","OPTIONS");
    });
});
builder.Services.AddRateLimiter(options=> {
    options.RejectionStatusCode=429;
    options.OnRejected=async (context,ct)=>await context.HttpContext.Response.WriteAsJsonAsync(new {type="about:blank",title="RATE_LIMITED",status=429,code="RATE_LIMITED"},ct);
    options.AddPolicy("auth",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",
        _=>new FixedWindowRateLimiterOptions { PermitLimit=builder.Configuration.GetValue("RateLimits:AuthPermitLimit",30),Window=TimeSpan.FromMinutes(1),QueueLimit=0 }));
    options.AddPolicy("admin-auth",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",
        _=>new FixedWindowRateLimiterOptions { PermitLimit=builder.Configuration.GetValue("RateLimits:AdminAuthPermitLimit",15),Window=TimeSpan.FromMinutes(1),QueueLimit=0 }));
    options.AddPolicy("admin-api",context=>RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue("sub")??context.Connection.RemoteIpAddress?.ToString()??"unknown",
        _=>new FixedWindowRateLimiterOptions { PermitLimit=builder.Configuration.GetValue("RateLimits:AdminApiPermitLimit",180),Window=TimeSpan.FromMinutes(1),QueueLimit=0 }));
    options.AddPolicy("provider",context=>RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue("sub")??context.Connection.RemoteIpAddress?.ToString()??"unknown",
        _=>new FixedWindowRateLimiterOptions { PermitLimit=builder.Configuration.GetValue("RateLimits:ProviderPermitLimit",30),Window=TimeSpan.FromMinutes(1),QueueLimit=0 }));
});
var app=builder.Build();
app.UseExceptionHandler(); app.UseStatusCodePages();
app.Use(async (context,next)=> {
    context.Response.Headers["X-Correlation-ID"]=context.TraceIdentifier;
    context.Response.Headers["X-Content-Type-Options"]="nosniff";
    context.Response.Headers["X-Frame-Options"]="DENY";
    context.Response.Headers["Referrer-Policy"]="no-referrer";
    context.Response.Headers["Permissions-Policy"]="camera=(), microphone=(), geolocation=()";
    context.Response.Headers.CacheControl="no-store";
    await next();
});
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseCors();
app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")) app.MapOpenApi();
app.MapHealthChecks("/health",new HealthCheckOptions {Predicate=_=>false});
app.MapHealthChecks("/health/ready",new HealthCheckOptions {Predicate=x=>x.Tags.Contains("ready")});
app.MapGet("/api/health/media",async (MediaDependencyHealthService mediaHealth,CancellationToken ct)=> {
    var report=await mediaHealth.CheckAsync(ct);
    return Results.Json(new {
        status=report.Status,
        ytDlp=report.YtDlp,
        ffmpeg=report.Ffmpeg,
        ffprobe=report.Ffprobe,
        javascriptRuntime=report.JavascriptRuntime,
        ejs=report.Ejs,
        externalProvider=new {
            provider=report.ExternalProvider.ProviderName,
            enabled=report.ExternalProvider.Enabled,
            configured=report.ExternalProvider.Configured,
            status=report.ExternalProvider.Status
        },
        checkedAtUtc=report.CheckedAtUtc,
        note=report.Note
    },statusCode:report.Status=="healthy"?StatusCodes.Status200OK:StatusCodes.Status503ServiceUnavailable);
});
var api=app.MapGroup("/api/v1");
api.MapGet("/system/capabilities",(IExternalTokenVerifier verifier,IAccountMailSender mail,SessionService sessions,IAudioExtractor extractor,IConfiguration c)=>Results.Ok(new {
    emailAuthentication=sessions.IsConfigured,passwordResetEmail=mail.IsConfigured,googleSignIn=verifier.Available("google"),appleSignIn=verifier.Available("apple"),
    youtubeMetadata=!string.IsNullOrWhiteSpace(c["YouTube:ApiKey"]),tikTokLinks=c.GetValue("Features:EnableTikTokVideoImport",true),
    soundCloudLinks=c.GetValue("Features:EnableSoundCloudImport",true),albumSharing=true,guestLocalMode=true,
    multiPlaylistMerge=false,crossProviderMixedCollections=true,licensedAudioDownload=extractor.IsAvailable,
    supportedAudioBitrates=AudioBitrate.Supported
}));
api.MapAccountEndpoints(); api.MapExternalAuthEndpoints(); api.MapAlbumEndpoints(); api.MapSongEndpoints();
api.MapAudioDownloadEndpoints(); api.MapProviderEndpoints(); api.MapImportEndpoints();
var admin=app.MapGroup("/api/admin").RequireCors("AdminWeb").RequireRateLimiting("admin-api");
admin.MapAdminAuthEndpoints();
admin.MapAdminDashboardEndpoints();
admin.MapAdminUsersEndpoints();
admin.MapAdminContentEndpoints();
admin.MapAdminOperationsEndpoints();
admin.MapAdminSystemEndpoints();
app.Run();
public partial class Program { }
public sealed class ApiDownloadBackgroundRunner(IServiceScopeFactory scopes,IConfiguration config,IAudioExtractor extractor,ILogger<ApiDownloadBackgroundRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (config.GetValue("Features:EnableAudioDownloads",false))
        {
            try
            {
                if (await extractor.ValidateToolsAsync(stoppingToken)) logger.LogInformation("Audio download tools passed startup validation (yt-dlp and FFmpeg).");
                else logger.LogWarning("Audio downloads are enabled but yt-dlp or FFmpeg failed startup validation.");
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Audio tools startup validation failed with {ErrorType}.", error.GetType().Name);
            }
        }
        AudioTempWorkspace.CleanupStaleDirectories(TimeSpan.FromHours(2));
        var nextCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var runDownloads = config.GetValue("AudioDownload:RunInApiProcess",true);
                var runImports = config.GetValue("Imports:RunInApiProcess",false);
                if (!runDownloads && !runImports) { await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken); continue; }
                await using var scope=scopes.CreateAsyncScope();
                var imports = runImports ? scope.ServiceProvider.GetRequiredService<ImportProcessor>() : null;
                var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
                var now = clock.GetUtcNow();
                if (now >= nextCleanup)
                {
                    if (imports is not null) await imports.PurgeExpiredAsync(stoppingToken);
                    AudioTempWorkspace.CleanupStaleDirectories(TimeSpan.FromHours(2), clock);
                    nextCleanup = now.AddMinutes(5);
                }
                if (runDownloads && await scope.ServiceProvider.GetRequiredService<AudioDownloadQueueRunner>().ProcessAvailableAsync(stoppingToken)) continue;
                if (imports is not null && await imports.ProcessOneAsync(stoppingToken)) continue;
                await Task.Delay(TimeSpan.FromSeconds(1),stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogWarning("Background job runner failed with {ErrorType}.",ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(3),stoppingToken);
            }
        }
    }
}
public sealed class SqlReady(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,CancellationToken cancellationToken=default)
    {
        await using var scope=scopes.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<TMusicDb>();
        return await db.Database.CanConnectAsync(cancellationToken)?HealthCheckResult.Healthy():HealthCheckResult.Unhealthy("SQL Server unavailable.");
    }
}
public sealed class ApiExceptionHandler(IProblemDetailsService details,ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context,Exception error,CancellationToken ct)
    {
        var (status,code)=error switch {
            DbUpdateConcurrencyException=>(409,"CONCURRENT_MODIFICATION"),
            DbUpdateException e when e.InnerException is Microsoft.Data.SqlClient.SqlException { Number:2601 or 2627 }=>(409,"CONCURRENT_MODIFICATION"),
            DbUpdateException e when e.InnerException is Microsoft.Data.SqlClient.SqlException { Number:1205 }=>(409,"RETRY_CONCURRENT_REQUEST"),
            Microsoft.Data.SqlClient.SqlException { Number:1205 }=>(409,"RETRY_CONCURRENT_REQUEST"),
            BadHttpRequestException=>(400,"INVALID_REQUEST"),
            OperationCanceledException=>(408,"REQUEST_CANCELLED"),
            _=>(500,"INTERNAL_ERROR")
        };
        logger.LogWarning("Request failed with {ErrorType}; correlation {Correlation}",error.GetType().Name,context.TraceIdentifier);
        if (status >= 500)
        {
            try
            {
                var audit = context.RequestServices.GetService<AdminAuditService>();
                if (audit is not null)
                {
                    var evType = error is Microsoft.Data.SqlClient.SqlException or DbUpdateException ? "DB_ERROR" : "API_ERROR";
                    await audit.RecordSystemEventAsync("ERROR", evType, code, $"Lỗi xử lý yêu cầu ({error.GetType().Name}).", null, new { path = context.Request.Path.Value }, CancellationToken.None);
                }
            }
            catch { /* Best-effort telemetry; never mask primary error response */ }
        }
        context.Response.StatusCode=status;
        return await details.TryWriteAsync(new ProblemDetailsContext {HttpContext=context,ProblemDetails=new() {
            Status=status,Title=code,Extensions={["code"]=code,["traceId"]=context.TraceIdentifier}
        }});
    }
}





