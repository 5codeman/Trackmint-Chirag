using Microsoft.EntityFrameworkCore;

namespace TrackMint.AuthService.Persistence;

public static class DatabaseInitializer
{
    public static async Task InitializeAuthDatabaseAsync(this IServiceProvider services)
    {
        const int maxAttempts = 10;

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AuthDbContext>>();

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await dbContext.Database.EnsureCreatedAsync();
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex, "Auth database initialization failed on attempt {Attempt}. Retrying...", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        await dbContext.Database.EnsureCreatedAsync();
    }
}
