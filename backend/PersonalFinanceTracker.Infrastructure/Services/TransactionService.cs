using Microsoft.EntityFrameworkCore;
using PersonalFinanceTracker.Application.Abstractions;
using PersonalFinanceTracker.Application.DTOs;
using PersonalFinanceTracker.Application.DTOs.Transactions;
using PersonalFinanceTracker.Application.Exceptions;
using PersonalFinanceTracker.Domain.Entities;
using PersonalFinanceTracker.Domain.Enums;
using PersonalFinanceTracker.Infrastructure.Persistence;
using TrackMint.Contracts.Events;

namespace PersonalFinanceTracker.Infrastructure.Services;

public sealed class TransactionService(
    ApplicationDbContext dbContext,
    ICurrentUserService currentUserService,
    IAccountAccessService accountAccessService,
    IBalanceService balanceService,
    IRuleService ruleService,
    IAuditService auditService,
    IIntegrationEventPublisher eventPublisher) : ITransactionService
{
    public async Task<PagedResult<TransactionResponse>> GetAllAsync(TransactionQueryRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        var accessibleAccountIds = await accountAccessService.GetAccessibleAccountIdsAsync(userId, cancellationToken);
        var query = ApplyFilters(dbContext.Transactions
            .AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.DestinationAccount)
            .Include(x => x.Category)
            .Where(x => accessibleAccountIds.Contains(x.AccountId) || (x.DestinationAccountId.HasValue && accessibleAccountIds.Contains(x.DestinationAccountId.Value))), request);

        var totalCount = await query.CountAsync(cancellationToken);
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TransactionResponse>
        {
            Items = items.Select(x => x.ToResponse()).ToArray(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<TransactionResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        var accessibleAccountIds = await accountAccessService.GetAccessibleAccountIdsAsync(userId, cancellationToken);
        var transaction = await QueryTransactions()
            .SingleOrDefaultAsync(x => x.Id == id && (accessibleAccountIds.Contains(x.AccountId) || (x.DestinationAccountId.HasValue && accessibleAccountIds.Contains(x.DestinationAccountId.Value))), cancellationToken)
            ?? throw new NotFoundException("Transaction not found.");

        return transaction.ToResponse();
    }

    public async Task<TransactionResponse> CreateAsync(CreateTransactionRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        await EnsureAccountOwnershipAsync(userId, request.AccountId, request.DestinationAccountId, cancellationToken);
        ValidationGuard.AgainstNonPositiveAmount(request.Amount);

        var transaction = new Transaction
        {
            UserId = userId,
            AccountId = request.AccountId,
            DestinationAccountId = request.Type == TransactionType.Transfer ? request.DestinationAccountId : null,
            CategoryId = request.Type == TransactionType.Transfer ? null : request.CategoryId,
            Type = request.Type,
            Amount = request.Amount,
            TransactionDate = request.Date,
            Note = request.Note?.Trim(),
            Merchant = request.Merchant?.Trim(),
            PaymentMethod = request.PaymentMethod?.Trim(),
            Tags = request.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToArray(),
            RecurringTransactionId = request.RecurringTransactionId
        };

        await ruleService.ApplyRulesAsync(transaction, cancellationToken);
        ValidationGuard.AgainstInvalidTransaction(transaction.Type, transaction.CategoryId, transaction.DestinationAccountId, transaction.AccountId);
        await EnsureCategoryOwnershipAsync(userId, transaction.AccountId, transaction.CategoryId, transaction.Type, cancellationToken);

        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Transactions.AddAsync(transaction, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await RecalculateForImpactedOwnersAsync(transaction.AccountId, transaction.DestinationAccountId, cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        await auditService.WriteAsync(userId, "transaction_created", nameof(Transaction), transaction.Id, new { transaction.Type, transaction.Amount }, cancellationToken);
        await eventPublisher.PublishAsync(new TransactionCreatedEvent
        {
            UserId = userId,
            TransactionId = transaction.Id,
            AccountId = transaction.AccountId,
            CategoryId = transaction.CategoryId,
            Type = transaction.Type.ToString(),
            Amount = transaction.Amount,
            TransactionDate = transaction.TransactionDate
        }, "finance.transaction.created", cancellationToken);
        await PublishBudgetThresholdIfNeededAsync(transaction, cancellationToken);

        return await GetByIdAsync(transaction.Id, cancellationToken);
    }

    public async Task<TransactionResponse> UpdateAsync(Guid id, UpdateTransactionRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        var transaction = await dbContext.Transactions
            .Include(x => x.Account)
            .Include(x => x.DestinationAccount)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Transaction not found.");

        await accountAccessService.EnsureCanEditTransactionsAsync(userId, transaction.AccountId, cancellationToken);

        await EnsureAccountOwnershipAsync(userId, request.AccountId, request.DestinationAccountId, cancellationToken);
        ValidationGuard.AgainstNonPositiveAmount(request.Amount);

        transaction.AccountId = request.AccountId;
        transaction.DestinationAccountId = request.Type == TransactionType.Transfer ? request.DestinationAccountId : null;
        transaction.CategoryId = request.Type == TransactionType.Transfer ? null : request.CategoryId;
        transaction.Type = request.Type;
        transaction.Amount = request.Amount;
        transaction.TransactionDate = request.Date;
        transaction.Note = request.Note?.Trim();
        transaction.Merchant = request.Merchant?.Trim();
        transaction.PaymentMethod = request.PaymentMethod?.Trim();
        transaction.Tags = request.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToArray();
        await ruleService.ApplyRulesAsync(transaction, cancellationToken);
        ValidationGuard.AgainstInvalidTransaction(transaction.Type, transaction.CategoryId, transaction.DestinationAccountId, transaction.AccountId);
        await EnsureCategoryOwnershipAsync(userId, transaction.AccountId, transaction.CategoryId, transaction.Type, cancellationToken);

        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await RecalculateForImpactedOwnersAsync(transaction.AccountId, transaction.DestinationAccountId, cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        await auditService.WriteAsync(userId, "transaction_updated", nameof(Transaction), transaction.Id, new { transaction.Type, transaction.Amount }, cancellationToken);
        await eventPublisher.PublishAsync(new TransactionUpdatedEvent
        {
            UserId = userId,
            TransactionId = transaction.Id,
            AccountId = transaction.AccountId,
            Type = transaction.Type.ToString(),
            Amount = transaction.Amount
        }, "finance.transaction.updated", cancellationToken);
        await PublishBudgetThresholdIfNeededAsync(transaction, cancellationToken);

        return await GetByIdAsync(transaction.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        var transaction = await dbContext.Transactions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Transaction not found.");
        await accountAccessService.EnsureCanEditTransactionsAsync(userId, transaction.AccountId, cancellationToken);

        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Transactions.Remove(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);
        await RecalculateForImpactedOwnersAsync(transaction.AccountId, transaction.DestinationAccountId, cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        await auditService.WriteAsync(userId, "transaction_deleted", nameof(Transaction), transaction.Id, new { transaction.Type, transaction.Amount }, cancellationToken);
        await eventPublisher.PublishAsync(new TransactionDeletedEvent
        {
            UserId = userId,
            TransactionId = transaction.Id,
            AccountId = transaction.AccountId
        }, "finance.transaction.deleted", cancellationToken);
    }

    internal static IQueryable<Transaction> ApplyFilters(IQueryable<Transaction> query, TransactionQueryRequest request)
    {
        if (request.StartDate.HasValue)
        {
            query = query.Where(x => x.TransactionDate >= request.StartDate.Value);
        }

        if (request.EndDate.HasValue)
        {
            query = query.Where(x => x.TransactionDate <= request.EndDate.Value);
        }

        if (request.AccountId.HasValue)
        {
            query = query.Where(x => x.AccountId == request.AccountId.Value || x.DestinationAccountId == request.AccountId.Value);
        }

        if (request.CategoryId.HasValue)
        {
            query = query.Where(x => x.CategoryId == request.CategoryId.Value);
        }

        if (request.MinAmount.HasValue)
        {
            query = query.Where(x => x.Amount >= request.MinAmount.Value);
        }

        if (request.MaxAmount.HasValue)
        {
            query = query.Where(x => x.Amount <= request.MaxAmount.Value);
        }

        if (request.Type.HasValue)
        {
            query = query.Where(x => x.Type == request.Type.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x =>
                (x.Merchant != null && x.Merchant.ToLower().Contains(search)) ||
                (x.Note != null && x.Note.ToLower().Contains(search)));
        }

        return query;
    }

    private IQueryable<Transaction> QueryTransactions() =>
        dbContext.Transactions
            .AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.DestinationAccount)
            .Include(x => x.Category);

    private async Task EnsureAccountOwnershipAsync(
        Guid userId,
        Guid accountId,
        Guid? destinationAccountId,
        CancellationToken cancellationToken)
    {
        await accountAccessService.EnsureCanEditTransactionsAsync(userId, accountId, cancellationToken);

        if (destinationAccountId.HasValue)
        {
            await accountAccessService.EnsureCanEditTransactionsAsync(userId, destinationAccountId.Value, cancellationToken);
        }
    }

    private async Task EnsureCategoryOwnershipAsync(
        Guid userId,
        Guid accountId,
        Guid? categoryId,
        TransactionType type,
        CancellationToken cancellationToken)
    {
        if (type == TransactionType.Transfer || !categoryId.HasValue)
        {
            return;
        }

        var accountOwnerId = await dbContext.Accounts
            .Where(x => x.Id == accountId)
            .Select(x => x.UserId)
            .SingleOrDefaultAsync(cancellationToken);

        var categoryExists = await dbContext.Categories.AnyAsync(
            x => x.Id == categoryId.Value && (x.UserId == userId || x.UserId == accountOwnerId),
            cancellationToken);
        if (!categoryExists)
        {
            throw new ValidationException("Selected category does not exist.");
        }
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

    private async Task PublishBudgetThresholdIfNeededAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        if (transaction.Type != TransactionType.Expense || transaction.CategoryId is null)
        {
            return;
        }

        var budget = await dbContext.Budgets
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.UserId == transaction.UserId &&
                x.CategoryId == transaction.CategoryId.Value &&
                x.Month == transaction.TransactionDate.Month &&
                x.Year == transaction.TransactionDate.Year,
                cancellationToken);

        if (budget is null)
        {
            return;
        }

        var monthStart = new DateOnly(budget.Year, budget.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var actualSpend = await dbContext.Transactions
            .AsNoTracking()
            .Where(x =>
                x.UserId == transaction.UserId &&
                x.Type == TransactionType.Expense &&
                x.CategoryId == budget.CategoryId &&
                x.TransactionDate >= monthStart &&
                x.TransactionDate <= monthEnd)
            .SumAsync(x => x.Amount, cancellationToken);

        var utilizationPercent = budget.Amount == 0 ? 0 : Math.Round((actualSpend / budget.Amount) * 100, 2);
        if (utilizationPercent < budget.AlertThresholdPercent)
        {
            return;
        }

        await eventPublisher.PublishAsync(new BudgetThresholdCrossedEvent
        {
            UserId = transaction.UserId,
            BudgetId = budget.Id,
            CategoryId = budget.CategoryId,
            BudgetAmount = budget.Amount,
            ActualSpend = actualSpend,
            UtilizationPercent = utilizationPercent
        }, "finance.budget.threshold_crossed", cancellationToken);
    }
}
