using SchoolAdmission.Api.Middleware;

namespace SchoolAdmission.Api.Middleware;

public static class StudentIdValidation
{
    /// <summary>
    /// Validates X-Student-Id: present, non-blank after trim, ≤64 chars, no control chars.
    /// Throws AppException.InvalidStudentId on failure.
    /// </summary>
    public static string ValidateAndGet(HttpRequest request)
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
        return trimmed;
    }
}
