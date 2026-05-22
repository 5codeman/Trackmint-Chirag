using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PersonalFinanceTracker.Application.Abstractions;
using PersonalFinanceTracker.Infrastructure.Background;
using PersonalFinanceTracker.Infrastructure.Messaging;
using PersonalFinanceTracker.Infrastructure.Persistence;
using PersonalFinanceTracker.Infrastructure.Security;
using PersonalFinanceTracker.Infrastructure.Seed;
using PersonalFinanceTracker.Infrastructure.Services;

namespace PersonalFinanceTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddHttpContextAccessor();

        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAccountAccessService, AccountAccessService>();
        services.AddScoped<IAccountMembershipService, AccountMembershipService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<IGoalService, GoalService>();
        services.AddScoped<IRecurringTransactionService, RecurringTransactionService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IForecastService, ForecastService>();
        services.AddScoped<IInsightsService, InsightsService>();
        services.AddScoped<IRuleService, RuleService>();
        services.AddScoped<IBalanceService, BalanceService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IDefaultCategorySeeder, DefaultCategorySeeder>();
        services.AddSingleton<IIntegrationEventPublisher, NoOpIntegrationEventPublisher>();

        if (configuration.GetValue("Features:EnableRecurringWorker", true))
        {
            services.AddHostedService<RecurringTransactionWorker>();
        }

        return services;
    }

    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ApplicationDbContext>>();

        const int maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await dbContext.Database.EnsureCreatedAsync();
                await EnsureV2SchemaAsync(dbContext);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex, "Database initialization failed on attempt {Attempt}. Retrying...", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        await dbContext.Database.EnsureCreatedAsync();
        await EnsureV2SchemaAsync(dbContext);
    }

    private static async Task EnsureV2SchemaAsync(ApplicationDbContext dbContext)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return;
        }

        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "AccountMembers" (
                "Id" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                "AccountId" uuid NOT NULL,
                "UserId" uuid NOT NULL,
                "Role" text NOT NULL,
                CONSTRAINT "PK_AccountMembers" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_AccountMembers_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_AccountMembers_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
            );
            """);

        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AccountMembers_AccountId_UserId"
            ON "AccountMembers" ("AccountId", "UserId");
            """);

        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_AccountMembers_UserId"
            ON "AccountMembers" ("UserId");
            """);

        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Rules" (
                "Id" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                "UserId" uuid NOT NULL,
                "Name" character varying(120) NOT NULL,
                "ConditionField" text NOT NULL,
                "ConditionOperator" text NOT NULL,
                "ConditionValue" character varying(255) NOT NULL,
                "ActionType" text NOT NULL,
                "ActionValue" character varying(255) NOT NULL,
                "IsActive" boolean NOT NULL,
                "Priority" integer NOT NULL,
                CONSTRAINT "PK_Rules" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_Rules_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
            );
            """);

        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_Rules_UserId_Priority"
            ON "Rules" ("UserId", "Priority");
            """);
    }
}
