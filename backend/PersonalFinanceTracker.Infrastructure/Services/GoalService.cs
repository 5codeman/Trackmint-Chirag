using Microsoft.EntityFrameworkCore;
using PersonalFinanceTracker.Application.Abstractions;
using PersonalFinanceTracker.Application.DTOs.Goals;
using PersonalFinanceTracker.Application.Exceptions;
using PersonalFinanceTracker.Domain.Entities;
using PersonalFinanceTracker.Domain.Enums;
using PersonalFinanceTracker.Infrastructure.Persistence;
using TrackMint.Contracts.Events;

namespace PersonalFinanceTracker.Infrastructure.Services;

public sealed class GoalService(
    ApplicationDbContext dbContext,
    ICurrentUserService currentUserService,
    IBalanceService balanceService,
    IAuditService auditService,
    IIntegrationEventPublisher eventPublisher) : IGoalService
{
    public async Task<IReadOnlyCollection<GoalResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        var goals = await dbContext.Goals
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.TargetDate)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return goals.Select(x => x.ToResponse()).ToArray();
    }

    public async Task<GoalResponse> CreateAsync(CreateGoalRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        ValidationGuard.AgainstBlank(request.Name, "Goal name");
        ValidationGuard.AgainstNonPositiveAmount(request.TargetAmount, "Target amount");

        if (request.LinkedAccountId.HasValue)
        {
            await EnsureAccountOwnershipAsync(userId, request.LinkedAccountId.Value, cancellationToken);
        }

        var goal = new Goal
        {
            UserId = userId,
            Name = request.Name.Trim(),
            TargetAmount = request.TargetAmount,
            CurrentAmount = 0,
            TargetDate = request.TargetDate,
            LinkedAccountId = request.LinkedAccountId,
            Icon = request.Icon,
            Color = request.Color
        };

        await dbContext.Goals.AddAsync(goal, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(userId, "goal_created", nameof(Goal), goal.Id, new { goal.Name, goal.TargetAmount }, cancellationToken);

        return goal.ToResponse();
    }

    public async Task<GoalResponse> UpdateAsync(Guid id, UpdateGoalRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        ValidationGuard.AgainstBlank(request.Name, "Goal name");
        ValidationGuard.AgainstNonPositiveAmount(request.TargetAmount, "Target amount");

        var goal = await dbContext.Goals.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Goal not found.");

        if (request.LinkedAccountId.HasValue)
        {
            await EnsureAccountOwnershipAsync(userId, request.LinkedAccountId.Value, cancellationToken);
        }

        goal.Name = request.Name.Trim();
        goal.TargetAmount = request.TargetAmount;
        goal.TargetDate = request.TargetDate;
        goal.LinkedAccountId = request.LinkedAccountId;
        goal.Icon = request.Icon;
        goal.Color = request.Color;
        goal.Status = request.Status;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(userId, "goal_updated", nameof(Goal), goal.Id, new { goal.Name, goal.TargetAmount }, cancellationToken);

        return goal.ToResponse();
    }

    public Task<GoalResponse> ContributeAsync(Guid id, GoalContributionRequest request, CancellationToken cancellationToken) =>
        AdjustGoalAsync(id, request, increase: true, cancellationToken);

    public Task<GoalResponse> WithdrawAsync(Guid id, GoalContributionRequest request, CancellationToken cancellationToken) =>
        AdjustGoalAsync(id, request, increase: false, cancellationToken);

    private async Task<GoalResponse> AdjustGoalAsync(Guid id, GoalContributionRequest request, bool increase, CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        ValidationGuard.AgainstNonPositiveAmount(request.Amount);

        var goal = await dbContext.Goals.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Goal not found.");

        if (!increase && goal.CurrentAmount < request.Amount)
        {
            throw new ValidationException("Withdrawal cannot exceed current goal amount.");
        }

        Account? selectedAccount = null;
        if (request.SourceAccountId.HasValue)
        {
            selectedAccount = await dbContext.Accounts.SingleOrDefaultAsync(x => x.Id == request.SourceAccountId.Value && x.UserId == userId, cancellationToken)
                ?? throw new ValidationException("Selected account does not exist.");
        }

        if (increase && selectedAccount is not null && selectedAccount.CurrentBalance < request.Amount)
        {
            throw new ValidationException("Contribution cannot exceed available account balance.");
        }

        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (selectedAccount is not null || goal.LinkedAccountId.HasValue)
        {
            var accountId = increase
                ? selectedAccount?.Id ?? throw new ValidationException("Source account is required for contribution.")
                : goal.LinkedAccountId ?? selectedAccount?.Id ?? throw new ValidationException("Account is required for withdrawal.");

            var destinationAccountId = increase && goal.LinkedAccountId.HasValue && goal.LinkedAccountId != selectedAccount?.Id
                ? goal.LinkedAccountId
                : (!increase && selectedAccount is not null && goal.LinkedAccountId.HasValue && goal.LinkedAccountId != selectedAccount.Id
                    ? selectedAccount.Id
                    : null);

            var transactionType = destinationAccountId.HasValue
                ? TransactionType.Transfer
                : (increase ? TransactionType.Expense : TransactionType.Income);

            var transaction = new Transaction
            {
                UserId = userId,
                AccountId = accountId,
                DestinationAccountId = destinationAccountId,
                Type = transactionType,
                Amount = request.Amount,
                TransactionDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Merchant = goal.Name,
                Note = increase ? "Goal contribution" : "Goal withdrawal",
                Tags = ["goal"]
            };

            await dbContext.Transactions.AddAsync(transaction, cancellationToken);
        }

        var wasCompleted = goal.Status == GoalStatus.Completed;
        goal.CurrentAmount = increase ? goal.CurrentAmount + request.Amount : goal.CurrentAmount - request.Amount;
        goal.Status = goal.CurrentAmount >= goal.TargetAmount ? GoalStatus.Completed : GoalStatus.Active;

        await dbContext.SaveChangesAsync(cancellationToken);
        await balanceService.RecalculateForUserAsync(userId, cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        await auditService.WriteAsync(userId, increase ? "goal_contribution" : "goal_withdrawal", nameof(Goal), goal.Id, new { request.Amount }, cancellationToken);
        if (!wasCompleted && goal.Status == GoalStatus.Completed)
        {
            await eventPublisher.PublishAsync(new GoalCompletedEvent
            {
                UserId = userId,
                GoalId = goal.Id,
                GoalName = goal.Name,
                TargetAmount = goal.TargetAmount
            }, "finance.goal.completed", cancellationToken);
        }

        return goal.ToResponse();
    }

    private async Task EnsureAccountOwnershipAsync(Guid userId, Guid accountId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Accounts.AnyAsync(x => x.Id == accountId && x.UserId == userId, cancellationToken);
        if (!exists)
        {
            throw new ValidationException("Selected account does not exist.");
        }
    }
}
