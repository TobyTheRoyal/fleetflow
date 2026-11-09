using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

namespace FleetFlow.Api.Security;

public static class DeploymentSecurity
{
    public static void Configure(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsProduction()) return;
        var secrets = new DeploymentSecrets(builder.Configuration);
        builder.Services.AddSingleton(_ => secrets);
        builder.Services.AddHostedService<ProductionValidation>();
        builder.Services.AddDataProtection().SetApplicationName("FleetFlow.Portfolio.Production")
            .ProtectKeysWithCertificate(secrets.Certificate).UnprotectKeysWithAnyCertificate(secrets.Certificates);
        builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, config) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear(); options.KnownProxies.Clear();
            foreach (var address in config.GetSection("Deployment:TrustedProxies").Get<string[]>() ?? [])
            {
                var parsed = IPAddress.Parse(address);
                if (parsed.Equals(IPAddress.Any) || parsed.Equals(IPAddress.IPv6Any)) throw new InvalidOperationException("Wildcard proxy trust is forbidden.");
                options.KnownProxies.Add(parsed);
            }
            foreach (var network in config.GetSection("Deployment:TrustedNetworks").Get<string[]>() ?? [])
            {
                var parsed = System.Net.IPNetwork.Parse(network);
                if (parsed.PrefixLength == 0) throw new InvalidOperationException("Wildcard proxy trust is forbidden.");
                options.KnownIPNetworks.Add(parsed);
            }
            // With no configured proxy this explicit sentinel trusts nobody, never all networks.
            if (options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0) options.KnownProxies.Add(IPAddress.None);
        });
        builder.Services.AddOptions<Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionOptions>().Configure<IConfiguration>((options, config) => options.HttpsPort = new Uri(config["Deployment:PublicOrigin"]!).Port);
        builder.Services.AddHsts(options => { options.MaxAge = TimeSpan.FromDays(30); options.IncludeSubDomains = false; options.ExcludedHosts.Clear(); });
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
    }
    internal static Task RenderFreeEdgeAsync(HttpContext context, RequestDelegate next, string hostname)
    {
        var request = context.Request;
        if (!string.Equals(request.Host.Value, hostname, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Host.Value, hostname + ":443", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        }

        request.Headers.Remove("Forwarded");
        request.Headers.Remove("X-Forwarded-For");
        request.Headers.Remove("X-Forwarded-Host");
        request.Headers.Remove("X-Forwarded-Proto");
        request.Headers.Remove("X-Forwarded-Prefix");
        // Render Free exposes only the managed public edge. It redirects public HTTP
        // before forwarding HTTPS traffic internally over HTTP. Keep the transport peer for rate limits.
        request.Scheme = Uri.UriSchemeHttps;
        return next(context);
    }

    public static async Task HeadersAsync(HttpContext context, RequestDelegate next)
    {
        var production = context.RequestServices.GetRequiredService<IHostEnvironment>().IsProduction();
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            var asset = context.Request.Path.StartsWithSegments("/assets") && (context.Request.Path.Value?.Contains('-') ?? false);
            context.Response.Headers.CacheControl = asset ? "public,max-age=31536000,immutable" : "no-store";
            if (production)
            {
                var publicOrigin = context.RequestServices.GetRequiredService<IConfiguration>()["Deployment:PublicOrigin"]!;
                var websocketOrigin = publicOrigin.Replace("https://", "wss://", StringComparison.Ordinal);
                context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' " + websocketOrigin + "; frame-ancestors 'none'; object-src 'none'; base-uri 'self'; form-action 'self'";
                context.Response.Headers.XFrameOptions = "DENY";
                context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            }
            return Task.CompletedTask;
        });
        await next(context);
    }
}

public sealed class DeploymentSecrets : IDisposable
{
    public X509Certificate2 Certificate { get; }
    public X509Certificate2[] Certificates { get; }
    internal string? RenderFreeHostname { get; }

    public static void ValidateInitializationTarget(IConfiguration config)
    {
        var owner = new NpgsqlConnectionStringBuilder(config.GetConnectionString("FleetFlow"));
        var host = config["Portfolio:ConfirmHost"];
        if (string.IsNullOrWhiteSpace(host) || host.Contains(',') ||
            !string.Equals(owner.Host, host, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Explicit PostgreSQL host confirmation does not match the initialization connection.");
        if (!config.GetValue<bool>("Portfolio:ProvisionLogins")) return;
        var runtime = new NpgsqlConnectionStringBuilder(config.GetConnectionString("PortfolioRuntime"));
        if (runtime.SslMode != SslMode.VerifyFull || runtime.MinPoolSize != 0 || runtime.MaxPoolSize > 10 ||
            runtime.KeepAlive != 0 || runtime.Host != owner.Host || runtime.Port != owner.Port || runtime.Database != owner.Database)
            throw new InvalidOperationException("The provisioned runtime connection must target the confirmed direct database with VerifyFull and a bounded pool.");
    }

    public DeploymentSecrets(IConfiguration config)
    {
        if (config.GetValue<bool>("ASPNETCORE_FORWARDEDHEADERS_ENABLED") || config.GetValue<bool>("FORWARDEDHEADERS_ENABLED"))
            throw new InvalidOperationException("Automatic forwarded-header trust is forbidden. Configure explicit trusted proxy addresses instead.");
        if (!Uri.TryCreate(config["Deployment:PublicOrigin"], UriKind.Absolute, out var origin) || origin.Scheme != "https" || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0)
            throw new InvalidOperationException("Production requires an exact HTTPS Deployment:PublicOrigin.");
        var hosts = (config["AllowedHosts"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (hosts.Length == 0 || hosts.Any(h => h.Contains('*')) || !hosts.Contains(origin.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production requires explicit AllowedHosts matching the public origin.");
        if (config.GetValue<bool>("Deployment:RenderFreeEdgeHttps"))
        {
            var renderHost = config["RENDER_EXTERNAL_HOSTNAME"] ?? "";
            if (!config.GetValue<bool>("RENDER") || config["RENDER_SERVICE_TYPE"] != "web" || !IsRenderHostname(renderHost))
                throw new InvalidOperationException("Render Free HTTPS mode requires valid Render web service metadata.");
            var renderOrigin = $"https://{renderHost.ToLowerInvariant()}";
            if (!MatchesOrigin(config["RENDER_EXTERNAL_URL"], renderOrigin) || !MatchesOrigin(config["Deployment:PublicOrigin"], renderOrigin) ||
                hosts.Length != 1 || !string.Equals(hosts[0], renderHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Render Free HTTPS mode requires the exact Render public URL and only its assigned hostname in AllowedHosts.");
            if (config.GetSection("Deployment:TrustedProxies").Exists() || config.GetSection("Deployment:TrustedNetworks").Exists())
                throw new InvalidOperationException("Render Free HTTPS mode cannot be combined with trusted proxy or network settings.");
            RenderFreeHostname = renderHost;
        }
        var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("FleetFlow"));
        if (connection.SslMode != SslMode.VerifyFull || connection.MaxPoolSize > 10 || connection.MinPoolSize != 0 || connection.KeepAlive != 0 || string.IsNullOrWhiteSpace(connection.Host) || connection.Host.Contains(',') || connection.Host.Contains("-pooler", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production requires direct PostgreSQL VerifyFull TLS, minimum pool 0, maximum pool <=10, and no keepalive queries.");
        if (config.GetValue<bool>("Portfolio:Enabled"))
        {
            var maintenance = new NpgsqlConnectionStringBuilder(config.GetConnectionString("PortfolioMaintenance"));
            if (maintenance.SslMode != SslMode.VerifyFull || maintenance.MaxPoolSize > 2 || maintenance.MinPoolSize != 0 || maintenance.KeepAlive != 0 || maintenance.Username == connection.Username || maintenance.Database != connection.Database || maintenance.Host != connection.Host || maintenance.Port != connection.Port)
                throw new InvalidOperationException("Portfolio maintenance requires a separate restricted login, the same direct database, VerifyFull TLS and a pool of at most two connections.");
        }
        var certificateFile = config["DataProtection:CertificatePath"];
        if (string.IsNullOrWhiteSpace(certificateFile)) throw new InvalidOperationException("A persistent runtime Data Protection certificate is required in Production.");
        var storage = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
        X509Certificate2 Load(string path) => path.EndsWith(".b64", StringComparison.OrdinalIgnoreCase)
            ? X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(File.ReadAllText(path)), config["DataProtection:CertificatePassword"], storage)
            : X509CertificateLoader.LoadPkcs12FromFile(path, config["DataProtection:CertificatePassword"], storage);
        Certificate = Load(certificateFile);
        if (!Certificate.HasPrivateKey) throw new InvalidOperationException("Data Protection requires the external certificate's private key.");
        using var privateKey = Certificate.GetRSAPrivateKey();
        if (privateKey is null || privateKey.KeySize < 2048) throw new InvalidOperationException("Data Protection requires an RSA private key of at least 2048 bits.");
        var previous = config.GetSection("DataProtection:PreviousCertificates").Get<string[]>() ?? [];
        Certificates = previous.Select(Load).Prepend(Certificate).ToArray();
        if (Certificates.Any(certificate => !certificate.HasPrivateKey)) throw new InvalidOperationException("Every retained Data Protection certificate requires its private key.");
    }

    private static bool IsRenderHostname(string hostname)
    {
        const string suffix = ".onrender.com";
        if (!hostname.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
        var label = hostname[..^suffix.Length];
        return label.Length is > 0 and <= 63 && char.IsAsciiLetterOrDigit(label[0]) &&
            char.IsAsciiLetterOrDigit(label[^1]) && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }

    private static bool MatchesOrigin(string? configured, string expected) =>
        string.Equals(configured, expected, StringComparison.Ordinal) ||
        string.Equals(configured, expected + "/", StringComparison.Ordinal);

    public void Dispose() { foreach (var certificate in Certificates) certificate.Dispose(); }
}
public sealed class ProductionValidation(DeploymentSecrets secrets, IConfiguration configuration, FleetFlow.Infrastructure.Portfolio.PortfolioSettings portfolio) : IHostedService
{
    public async Task StartAsync(CancellationToken token)
    {
        _ = secrets.Certificate;
        if (!portfolio.Enabled) return;
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("FleetFlow"));
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand("""
            SELECT NOT r.rolsuper AND NOT r.rolcreatedb AND NOT r.rolcreaterole AND NOT r.rolreplication AND NOT r.rolbypassrls
              AND pg_has_role(current_user,'fleetflow_portfolio_runtime','MEMBER')
              AND NOT pg_has_role(current_user,'fleetflow_portfolio_maintenance','MEMBER')
              AND NOT EXISTS(SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE (n.nspname='public' OR n.nspname LIKE 'portfolio_%') AND pg_has_role(current_user,c.relowner,'MEMBER'))
            FROM pg_roles r WHERE r.rolname=current_user
            """, connection);
        if (await command.ExecuteScalarAsync(token) is not true) throw new InvalidOperationException("The public demo requires an unprivileged runtime role separated from migration and maintenance ownership.");
        await using var catalog = new NpgsqlCommand("SELECT \"MigrationId\"=(SELECT \"MigrationId\" FROM public.\"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1) FROM public.\"PortfolioCatalog\" WHERE \"Id\"=1", connection);
        if (await catalog.ExecuteScalarAsync(token) is not true) throw new InvalidOperationException("Portfolio schemas do not match the current migration. Apply a reviewed portfolio schema upgrade before starting the application.");
        await using var unprotectedKeys = new NpgsqlCommand("SELECT EXISTS(SELECT FROM public.\"DataProtectionKeys\" WHERE \"Xml\" LIKE '%<masterKey%')", connection);
        if (await unprotectedKeys.ExecuteScalarAsync(token) is true) throw new InvalidOperationException("Public demo startup refuses unencrypted legacy Data Protection keys. Use a fresh production key ring; preserve development backups separately.");
        await using var maintenance = new NpgsqlConnection(configuration.GetConnectionString("PortfolioMaintenance"));
        await maintenance.OpenAsync(token);
        await using var privilege = new NpgsqlCommand("SELECT NOT r.rolsuper AND NOT r.rolcreatedb AND NOT r.rolcreaterole AND NOT r.rolreplication AND NOT r.rolbypassrls AND pg_has_role(current_user,'fleetflow_portfolio_maintenance','MEMBER') AND NOT pg_has_role(current_user,'fleetflow_portfolio_runtime','MEMBER') AND NOT EXISTS(SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE (n.nspname='public' OR n.nspname LIKE 'portfolio_%') AND pg_has_role(current_user,c.relowner,'MEMBER')) FROM pg_roles r WHERE r.rolname=current_user", maintenance);
        if (await privilege.ExecuteScalarAsync(token) is not true) throw new InvalidOperationException("Portfolio maintenance must use its separate restricted role.");
    }
    public Task StopAsync(CancellationToken token) => Task.CompletedTask;
}
