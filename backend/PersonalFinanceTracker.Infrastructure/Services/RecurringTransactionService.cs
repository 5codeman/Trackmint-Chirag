using Microsoft.EntityFrameworkCore;
using PersonalFinanceTracker.Application.Abstractions;
using PersonalFinanceTracker.Application.DTOs.Recurring;
using PersonalFinanceTracker.Application.Exceptions;
using PersonalFinanceTracker.Domain.Entities;
using PersonalFinanceTracker.Domain.Enums;
using PersonalFinanceTracker.Infrastructure.Persistence;
using TrackMint.Contracts.Events;

namespace PersonalFinanceTracker.Infrastructure.Services;

public sealed class RecurringTransactionService(
    ApplicationDbContext dbContext,
    ICurrentUserService currentUserService,
    IAccountAccessService accountAccessService,
    IRuleService ruleService,
    IBalanceService balanceService,
    IAuditService auditService,
    IIntegrationEventPublisher eventPublisher) : IRecurringTransactionService
{
    public async Task<IReadOnlyCollection<RecurringTransactionResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        var accountIds = await accountAccessService.GetAccessibleAccountIdsAsync(userId, cancellationToken);
        var items = await Query(accountIds).ToListAsync(cancellationToken);
        return items.Select(x => x.ToResponse()).ToArray();
    }

    public async Task<RecurringTransactionResponse> CreateAsync(CreateRecurringTransactionRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        await ValidateAsync(userId, request.AccountId, request.DestinationAccountId, request.CategoryId, request.Type, cancellationToken);
        ValidationGuard.AgainstBlank(request.Title, "Title");
        ValidationGuard.AgainstNonPositiveAmount(request.Amount);

        var item = new RecurringTransaction
        {
            UserId = userId,
            Title = request.Title.Trim(),
            Type = request.Type,
            Amount = request.Amount,
            CategoryId = request.Type == TransactionType.Transfer ? null : request.CategoryId,
            AccountId = request.AccountId,
            DestinationAccountId = request.Type == TransactionType.Transfer ? request.DestinationAccountId : null,
            Frequency = request.Frequency,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            NextRunDate = request.StartDate,
            AutoCreateTransaction = request.AutoCreateTransaction
        };

        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.RecurringTransactions.AddAsync(item, cancellationToken);
        var generatedTransactions = item.AutoCreateTransaction
            ? MaterializeDueTransactions(item, DateOnly.FromDateTime(DateTime.UtcNow))
            : [];
        foreach (var generatedTransaction in generatedTransactions)
        {
            await ruleService.ApplyRulesAsync(generatedTransaction, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        if (generatedTransactions.Count > 0)
        {
            await RecalculateForImpactedOwnersAsync(item.AccountId, item.DestinationAccountId, cancellationToken);
        }
        await dbTransaction.CommitAsync(cancellationToken);
        await auditService.WriteAsync(userId, "recurring_created", nameof(RecurringTransaction), item.Id, new { item.Title, item.Amount }, cancellationToken);
        await PublishGeneratedRecurringEventsAsync(item, generatedTransactions, cancellationToken);

        var accountIds = await accountAccessService.GetAccessibleAccountIdsAsync(userId, cancellationToken);
        return (await Query(accountIds).SingleAsync(x => x.Id == item.Id, cancellationToken)).ToResponse();
    }

    public async Task<RecurringTransactionResponse> UpdateAsync(Guid id, UpdateRecurringTransactionRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        var item = await dbContext.RecurringTransactions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Recurring item not found.");
        await accountAccessService.EnsureCanEditTransactionsAsync(userId, item.AccountId, cancellationToken);

        await ValidateAsync(userId, request.AccountId, request.DestinationAccountId, request.CategoryId, request.Type, cancellationToken);
        ValidationGuard.AgainstBlank(request.Title, "Title");
        ValidationGuard.AgainstNonPositiveAmount(request.Amount);

        item.Title = request.Title.Trim();
        item.Type = request.Type;
        item.Amount = request.Amount;
        item.CategoryId = request.Type == TransactionType.Transfer ? null : request.CategoryId;
        item.AccountId = request.AccountId;
        item.DestinationAccountId = request.Type == TransactionType.Transfer ? request.DestinationAccountId : null;
        item.Frequency = request.Frequency;
        item.StartDate = request.StartDate;
        item.EndDate = request.EndDate;
        item.NextRunDate = request.NextRunDate;
        item.AutoCreateTransaction = request.AutoCreateTransaction;
        item.IsPaused = request.IsPaused;

        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var generatedTransactions = item.AutoCreateTransaction && !item.IsPaused
            ? MaterializeDueTransactions(item, DateOnly.FromDateTime(DateTime.UtcNow))
            : [];
        foreach (var generatedTransaction in generatedTransactions)
        {
            await ruleService.ApplyRulesAsync(generatedTransaction, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        if (generatedTransactions.Count > 0)
        {
            await RecalculateForImpactedOwnersAsync(item.AccountId, item.DestinationAccountId, cancellationToken);
        }
        await dbTransaction.CommitAsync(cancellationToken);
        await auditService.WriteAsync(userId, "recurring_updated", nameof(RecurringTransaction), item.Id, new { item.Title, item.Amount }, cancellationToken);
        await PublishGeneratedRecurringEventsAsync(item, generatedTransactions, cancellationToken);

        var accountIds = await accountAccessService.GetAccessibleAccountIdsAsync(userId, cancellationToken);
        return (await Query(accountIds).SingleAsync(x => x.Id == item.Id, cancellationToken)).ToResponse();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        var item = await dbContext.RecurringTransactions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Recurring item not found.");
        await accountAccessService.EnsureCanEditTransactionsAsync(userId, item.AccountId, cancellationToken);

        dbContext.RecurringTransactions.Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(userId, "recurring_deleted", nameof(RecurringTransaction), item.Id, new { item.Title }, cancellationToken);
    }

    public async Task ProcessDueItemsAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dueItems = await dbContext.RecurringTransactions
            .Where(x => !x.IsPaused && x.AutoCreateTransaction && x.NextRunDate <= today && (x.EndDate == null || x.EndDate >= x.NextRunDate))
            .ToListAsync(cancellationToken);

        if (dueItems.Count == 0)
        {
            return;
        }

        var impactedUsers = new HashSet<Guid>();

        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        foreach (var item in dueItems)
        {
            var generatedTransactions = MaterializeDueTransactions(item, today);
            foreach (var generatedTransaction in generatedTransactions)
            {
                await ruleService.ApplyRulesAsync(generatedTransaction, cancellationToken);
            }

            if (generatedTransactions.Count > 0)
            {
                var ownerIds = await dbContext.Accounts
                    .Where(x => x.Id == item.AccountId || (item.DestinationAccountId.HasValue && x.Id == item.DestinationAccountId.Value))
                    .Select(x => x.UserId)
                    .Distinct()
                    .ToArrayAsync(cancellationToken);
                foreach (var ownerId in ownerIds)
                {
                    impactedUsers.Add(ownerId);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var userId in impactedUsers)
        {
            await balanceService.RecalculateForUserAsync(userId, cancellationToken);
        }

        await dbTransaction.CommitAsync(cancellationToken);

        foreach (var item in dueItems)
        {
            var generatedTransactions = await dbContext.Transactions
                .AsNoTracking()
                .Where(x => x.RecurringTransactionId == item.Id && x.CreatedAt >= DateTime.UtcNow.AddMinutes(-10))
                .ToListAsync(cancellationToken);

            await PublishGeneratedRecurringEventsAsync(item, generatedTransactions, cancellationToken);
        }
    }

    private IQueryable<RecurringTransaction> Query(IReadOnlySet<Guid> accountIds) =>
        dbContext.RecurringTransactions
            .AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.DestinationAccount)
            .Include(x => x.Category)
            .Where(x => accountIds.Contains(x.AccountId) || (x.DestinationAccountId.HasValue && accountIds.Contains(x.DestinationAccountId.Value)))
            .OrderBy(x => x.NextRunDate);

    private async Task ValidateAsync(Guid userId, Guid accountId, Guid? destinationAccountId, Guid? categoryId, TransactionType type, CancellationToken cancellationToken)
    {
        ValidationGuard.AgainstInvalidTransaction(type, categoryId, destinationAccountId, accountId);

        await accountAccessService.EnsureCanEditTransactionsAsync(userId, accountId, cancellationToken);

        if (destinationAccountId.HasValue)
        {
            await accountAccessService.EnsureCanEditTransactionsAsync(userId, destinationAccountId.Value, cancellationToken);
        }

        if (categoryId.HasValue)
        {
            var accountOwnerId = await dbContext.Accounts
                .Where(x => x.Id == accountId)
                .Select(x => x.UserId)
                .SingleOrDefaultAsync(cancellationToken);
            var categoryExists = await dbContext.Categories.AnyAsync(x => x.Id == categoryId.Value && (x.UserId == userId || x.UserId == accountOwnerId), cancellationToken);
            if (!categoryExists)
            {
                throw new ValidationException("Selected category does not exist.");
            }
        }
    }

    private static DateOnly CalculateNextRunDate(DateOnly current, RecurringFrequency frequency) =>
        frequency switch
        {
            RecurringFrequency.Daily => current.AddDays(1),
            RecurringFrequency.Weekly => current.AddDays(7),
            RecurringFrequency.Monthly => current.AddMonths(1),
            RecurringFrequency.Yearly => current.AddYears(1),
            _ => current.AddMonths(1)
        };

    private IReadOnlyCollection<Transaction> MaterializeDueTransactions(RecurringTransaction item, DateOnly today)
    {
        var generatedTransactions = new List<Transaction>();

        while (item.NextRunDate <= today && (item.EndDate == null || item.EndDate >= item.NextRunDate))
        {
            var transaction = new Transaction
            {
                UserId = item.UserId,
                AccountId = item.AccountId,
                DestinationAccountId = item.Type == TransactionType.Transfer ? item.DestinationAccountId : null,
                CategoryId = item.Type == TransactionType.Transfer ? null : item.CategoryId,
                Type = item.Type,
                Amount = item.Amount,
                TransactionDate = item.NextRunDate,
                Merchant = item.Title,
                Note = "Auto-generated recurring transaction",
                Tags = ["recurring", "auto"],
                RecurringTransactionId = item.Id
            };

            dbContext.Transactions.Add(transaction);
            item.NextRunDate = CalculateNextRunDate(item.NextRunDate, item.Frequency);
            generatedTransactions.Add(transaction);
        }

        return generatedTransactions;
    }

    private async Task RecalculateForImpactedOwnersAsync(Guid accountId, Guid? destinationAccountId, CancellationToken cancellationToken)
    {
        var impactedOwnerIds = await dbContext.Accounts
            .Where(x => x.Id == accountId || (destinationAccountId.HasValue && x.Id == destinationAccountId.Value))
            .Select(x => x.UserId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        foreach (var ownerId in impactedOwnerIds)
        {
            await balanceService.RecalculateForUserAsync(ownerId, cancellationToken);
        }
    }

    private async Task PublishGeneratedRecurringEventsAsync(
        RecurringTransaction item,
        IEnumerable<Transaction> generatedTransactions,
        CancellationToken cancellationToken)
    {
        foreach (var transaction in generatedTransactions)
        {
            await eventPublisher.PublishAsync(new RecurringTransactionGeneratedEvent
            {
                UserId = item.UserId,
                RecurringTransactionId = item.Id,
                GeneratedTransactionId = transaction.Id,
                RunDate = transaction.TransactionDate
            }, "finance.recurring.generated", cancellationToken);
        }
    }
}
