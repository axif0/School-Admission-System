using System.Net;

namespace SchoolAdmission.Api.Middleware;

public class AppException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public AppException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    // 400 errors
    public static AppException InvalidStudentId(string msg = "X-Student-Id header must be present, non-blank, at most 64 characters, and contain no control characters.")
        => new(400, "INVALID_STUDENT_ID", msg);

    public static AppException ValidationError(string msg)
        => new(400, "VALIDATION_ERROR", msg);

    // 403
    public static AppException NotYourHold()
        => new(403, "NOT_YOUR_HOLD", "You do not own this hold.");

    // 404
    public static AppException NotFound(string msg = "The requested resource was not found.")
        => new(404, "NOT_FOUND", msg);

    // 409
    public static AppException NoSeatsAvailable()
        => new(409, "NO_SEATS_AVAILABLE", "No seats are left in this class.");

    public static AppException AlreadyApplied()
        => new(409, "ALREADY_APPLIED", "You already have an active or confirmed application for this class.");

    public static AppException HoldExpired()
        => new(409, "HOLD_EXPIRED", "This hold has expired and can no longer be confirmed.");

    public static AppException HoldCancelled()
        => new(409, "HOLD_CANCELLED", "This hold has been cancelled.");

    public static AppException AlreadyConfirmed()
        => new(409, "ALREADY_CONFIRMED", "This hold is already confirmed and cannot be cancelled.");

    // 503
    public static AppException TemporarilyBusy()
        => new(503, "TEMPORARILY_BUSY", "The system is temporarily busy. Please try again in a moment.");
}
