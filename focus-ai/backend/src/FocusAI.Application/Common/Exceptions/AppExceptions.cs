namespace FocusAI.Application.Common.Exceptions;

public class NotFoundException(string message) : Exception(message)
{
    public static NotFoundException For(string entity, object key) =>
        new($"{entity} '{key}' bulunamadı.");
}

public class ConflictException(string message) : Exception(message);

public class ForbiddenException(string message = "Bu işlem için yetkiniz yok.") : Exception(message);

public class UnauthorizedException(string message = "Giriş yapmanız gerekiyor.") : Exception(message);

/// <summary>
/// Thrown by the validation behaviour. Carries a field → messages map so the API
/// can emit an RFC 7807 problem document with per-field errors.
/// </summary>
public class AppValidationException(IDictionary<string, string[]> errors)
    : Exception("Bir veya daha fazla doğrulama hatası oluştu.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
