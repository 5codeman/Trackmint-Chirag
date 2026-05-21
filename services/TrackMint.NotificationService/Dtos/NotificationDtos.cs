namespace TrackMint.NotificationService.Dtos;

public sealed class CreateNotificationRequest
{
    public required Guid UserId { get; init; }
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
}

public sealed class NotificationResponse
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required bool IsRead { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? ReadAtUtc { get; init; }
}
