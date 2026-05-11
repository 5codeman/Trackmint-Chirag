namespace TrackMint.AuthService.Exceptions;

public class AppException(string message, int statusCode = StatusCodes.Status400BadRequest) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class ValidationException(string message) : AppException(message, StatusCodes.Status400BadRequest);

public sealed class UnauthorizedRequestException(string message) : AppException(message, StatusCodes.Status401Unauthorized);
