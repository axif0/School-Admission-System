using System.Security.Cryptography;
using System.Text;

namespace SchoolAdmission.Api.Middleware;

public static class StudentIdValidation
{
    /// <summary>
    /// Validates X-Student-Id: present, non-blank after trim, ≤64 chars, no control chars.
    /// When studentAuthSecret is configured, also enforces constant-time HMAC-SHA256 verification
    /// via X-Student-Token (or Authorization: Bearer) to prevent IDOR / header spoofing.
    /// </summary>
    public static string ValidateAndGet(HttpRequest request, string? studentAuthSecret = null)
    {
        var raw = request.Headers["X-Student-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
            throw AppException.InvalidStudentId();

        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
            throw AppException.InvalidStudentId();
        if (trimmed.Length > 64)
            throw AppException.InvalidStudentId("X-Student-Id must be at most 64 characters.");
        foreach (var c in trimmed)
        {
            if (char.IsControl(c))
                throw AppException.InvalidStudentId("X-Student-Id must not contain control characters.");
        }

        if (!string.IsNullOrEmpty(studentAuthSecret))
        {
            var token = request.Headers["X-Student-Token"].FirstOrDefault()?.Trim();
            if (string.IsNullOrEmpty(token))
            {
                var authHeader = request.Headers.Authorization.FirstOrDefault();
                if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = authHeader["Bearer ".Length..].Trim();
                }
            }

            if (string.IsNullOrEmpty(token))
                throw AppException.StudentTokenRequired();

            var expectedSignature = ComputeHmacToken(trimmed, studentAuthSecret);
            var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);
            var actualBytes = Encoding.UTF8.GetBytes(token.ToLowerInvariant());

            if (expectedBytes.Length != actualBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
            {
                throw AppException.StudentTokenInvalid();
            }
        }

        return trimmed;
    }

    public static string ComputeHmacToken(string studentId, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(studentId);
        var hash = HMACSHA256.HashData(keyBytes, dataBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
