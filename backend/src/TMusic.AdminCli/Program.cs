using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;

namespace TMusic.AdminCli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        if (command is not ("create-superadmin" or "status"))
        {
            Console.Error.WriteLine($"Unknown command: {args[0]}");
            PrintUsage();
            return 2;
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? config.GetConnectionString("TMusic")
            ?? Environment.GetEnvironmentVariable("TMUSIC_SQL_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("ERROR: SQL Server connection string is required via ConnectionStrings__DefaultConnection or ConnectionStrings__TMusic.");
            return 1;
        }

        var sqlBuilder = new SqlConnectionStringBuilder(connectionString);
        if (string.Equals(sqlBuilder.InitialCatalog, "TMusicDev", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("REFUSED: Legacy database 'TMusicDev' is protected and cannot be modified by TMusic.AdminCli.");
            return 1;
        }

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning).AddConsole());
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<TMusicDb>(o => o.UseSqlServer(connectionString));
        services.AddIdentityCore<Account>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.SignIn.RequireConfirmedEmail = false;
            options.Password.RequiredLength = 12;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireDigit = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<TMusicDb>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TMusicDb>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        if (!await db.Database.CanConnectAsync())
        {
            Console.Error.WriteLine("ERROR: Unable to connect to SQL Server database.");
            return 1;
        }

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
        {
            Console.Error.WriteLine($"ERROR: Database has {pending.Count} pending migration(s). Apply EF Core migrations before running bootstrap.");
            return 1;
        }

        await EnsureRolesSeededAsync(roleManager);

        if (command == "status")
        {
            var superAdmins = await userManager.GetUsersInRoleAsync(AdminRoles.SuperAdmin);
            var activeSuperAdmins = superAdmins.Count(x => x.DeletedAt == null && !x.IsSuspended);
            Console.WriteLine($"Database: {sqlBuilder.InitialCatalog}");
            Console.WriteLine($"Roles seeded: {string.Join(", ", AdminRoles.All)}");
            Console.WriteLine($"SuperAdmin accounts: {superAdmins.Count} (active: {activeSuperAdmins})");
            return 0;
        }

        return await CreateSuperAdminAsync(args.Skip(1).ToArray(), db, userManager);
    }

    private static async Task<int> CreateSuperAdminAsync(string[] args, TMusicDb db, UserManager<Account> userManager)
    {
        var existingSuperAdmins = await userManager.GetUsersInRoleAsync(AdminRoles.SuperAdmin);
        if (existingSuperAdmins.Any(x => x.DeletedAt == null))
        {
            Console.WriteLine("INFO: A SuperAdmin account already exists. Bootstrap is idempotent and will not create another account or reset passwords.");
            return 0;
        }

        string? email = GetArg(args, "--email") ?? Environment.GetEnvironmentVariable("TMUSIC_BOOTSTRAP_EMAIL");
        string? displayName = GetArg(args, "--display-name") ?? Environment.GetEnvironmentVariable("TMUSIC_BOOTSTRAP_DISPLAY_NAME");
        var passwordFromStdin = args.Contains("--password-stdin", StringComparer.OrdinalIgnoreCase);
        var requireEmailVerify = args.Contains("--unconfirmed-email", StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(email))
        {
            if (Console.IsInputRedirected)
            {
                Console.Error.WriteLine("ERROR: Provide --email or TMUSIC_BOOTSTRAP_EMAIL in non-interactive mode.");
                return 2;
            }
            Console.Write("SuperAdmin Email: ");
            email = Console.ReadLine()?.Trim();
        }
        else
        {
            email = email.Trim();
        }

        if (!IsValidEmail(email))
        {
            Console.Error.WriteLine("ERROR: Invalid email format.");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            if (!Console.IsInputRedirected)
            {
                Console.Write("Display Name (optional, default 'TMusic SuperAdmin'): ");
                displayName = Console.ReadLine()?.Trim();
            }
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = "TMusic SuperAdmin";
        }
        displayName = displayName.Trim();
        if (displayName.Length > 100)
        {
            Console.Error.WriteLine("ERROR: Display name must not exceed 100 characters.");
            return 2;
        }

        string? password;
        if (passwordFromStdin)
        {
            password = (await Console.In.ReadLineAsync())?.TrimEnd('\r', '\n');
        }
        else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TMUSIC_BOOTSTRAP_PASSWORD")))
        {
            password = Environment.GetEnvironmentVariable("TMUSIC_BOOTSTRAP_PASSWORD");
        }
        else
        {
            if (Console.IsInputRedirected)
            {
                Console.Error.WriteLine("ERROR: Provide password via --password-stdin or TMUSIC_BOOTSTRAP_PASSWORD (never pass password as a CLI argument).");
                return 2;
            }
            password = ReadMaskedPassword("Strong Password (12+ chars, upper/lower/digit/special): ");
            var confirm = ReadMaskedPassword("Confirm Password: ");
            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("ERROR: Password confirmation does not match.");
                return 2;
            }
        }

        if (!IsValidPassword(password))
        {
            Console.Error.WriteLine("ERROR: Password must be 12-128 characters and include uppercase, lowercase, digit, and special character.");
            return 2;
        }

        if (await userManager.FindByEmailAsync(email!) is not null)
        {
            Console.Error.WriteLine("ERROR: An account with this email already exists. Bootstrap refuses to overwrite or auto-elevate existing accounts.");
            return 1;
        }

        var now = DateTimeOffset.UtcNow;
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            DisplayName = displayName,
            EmailConfirmed = !requireEmailVerify,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var tx = await db.Database.BeginTransactionAsync();
        var created = await userManager.CreateAsync(account, password!);
        if (!created.Succeeded)
        {
            Console.Error.WriteLine("ERROR: Failed to create SuperAdmin account: " + string.Join("; ", created.Errors.Select(e => e.Code)));
            return 1;
        }

        var roleAdded = await userManager.AddToRoleAsync(account, AdminRoles.SuperAdmin);
        if (!roleAdded.Succeeded)
        {
            Console.Error.WriteLine("ERROR: Failed to assign SuperAdmin role.");
            return 1;
        }

        db.AuditLogs.Add(new AuditLog
        {
            ActorId = account.Id,
            ActorEmail = account.Email,
            ActorRole = AdminRoles.SuperAdmin,
            Action = "SUPERADMIN_BOOTSTRAPPED",
            TargetType = "ACCOUNT",
            TargetId = account.Id.ToString("D"),
            Result = "SUCCESS",
            Reason = "Initial SuperAdmin created via AdminCli bootstrap.",
            MetadataJson = $"{{\"emailConfirmed\":{(account.EmailConfirmed ? "true" : "false")}}}",
            CorrelationId = $"cli-{Guid.NewGuid():N}",
            CreatedAt = now
        });

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        Console.WriteLine($"SUCCESS: SuperAdmin account created (Id: {account.Id:D}, EmailConfirmed: {account.EmailConfirmed}).");
        Console.WriteLine("NEXT STEP: Sign in at TMusic Admin Web to complete mandatory TOTP MFA setup before accessing admin features.");
        return 0;
    }

    private static async Task EnsureRolesSeededAsync(RoleManager<IdentityRole<Guid>> roleManager)
    {
        foreach (var roleName in AdminRoles.All)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new IdentityRole<Guid>(roleName);
                var result = await roleManager.CreateAsync(role);
                if (!result.Succeeded)
                    throw new InvalidOperationException($"Failed to seed role {roleName}.");
            }

            if (AdminPermissions.DefaultRolePermissions.TryGetValue(roleName, out var defaults))
            {
                var existingClaims = (await roleManager.GetClaimsAsync(role))
                    .Where(c => c.Type == "permission")
                    .Select(c => c.Value)
                    .ToHashSet(StringComparer.Ordinal);
                if (existingClaims.Count == 0 && defaults.Length > 0)
                {
                    foreach (var permission in defaults)
                        await roleManager.AddClaimAsync(role, new Claim("permission", permission));
                }
            }
        }
    }

    private static string? GetArg(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static string ReadMaskedPassword(string prompt)
    {
        Console.Write(prompt);
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return sb.ToString();
            }
            if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
            {
                sb.Length--;
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }

    private static bool IsValidEmail(string? email) =>
        email?.Trim() is { Length: > 0 and <= 254 } text &&
        MailAddress.TryCreate(text, out var addr) &&
        addr.Address == text;

    private static bool IsValidPassword(string? value) =>
        value is { Length: >= 12 and <= 128 } &&
        value.Any(char.IsUpper) &&
        value.Any(char.IsLower) &&
        value.Any(char.IsDigit) &&
        value.Any(c => !char.IsLetterOrDigit(c));

    private static void PrintUsage()
    {
        Console.WriteLine("TMusic Admin Bootstrap CLI");
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project backend/src/TMusic.AdminCli -- create-superadmin [--email <email>] [--display-name <name>] [--password-stdin]");
        Console.WriteLine("  dotnet run --project backend/src/TMusic.AdminCli -- status");
        Console.WriteLine("Environment:");
        Console.WriteLine("  ConnectionStrings__DefaultConnection (or ConnectionStrings__TMusic)");
        Console.WriteLine("  TMUSIC_BOOTSTRAP_EMAIL, TMUSIC_BOOTSTRAP_PASSWORD, TMUSIC_BOOTSTRAP_DISPLAY_NAME (optional non-interactive)");
    }
}
