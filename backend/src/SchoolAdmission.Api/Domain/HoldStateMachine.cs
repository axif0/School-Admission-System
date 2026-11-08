using SchoolAdmission.Api.Middleware;

namespace SchoolAdmission.Api.Domain;

/// <summary>
/// Pure domain state machine governing Hold status transitions.
/// Contains no I/O, no database dependencies, and is 100% unit-testable.
/// </summary>
public static class HoldStateMachine
{
    public static bool IsTerminal(string status) =>
        status is HoldStatus.Confirmed or HoldStatus.Cancelled or HoldStatus.Expired;

    public static string DeriveStatus(string status, DateTime expiresAt, DateTime now)
    {
        if (status == HoldStatus.Held && expiresAt <= now)
            return HoldStatus.Expired;

        return status;
    }

    public static bool ValidateConfirmation(
        string currentStatus,
        DateTime expiresAt,
        DateTime now,
        out AppException? error)
    {
        error = null;

        if (currentStatus == HoldStatus.Confirmed)
            return true; // Idempotent success

        if (currentStatus == HoldStatus.Cancelled)
        {
            error = AppException.HoldCancelled();
            return false;
        }

        if (currentStatus == HoldStatus.Expired || (currentStatus == HoldStatus.Held && expiresAt <= now))
        {
            error = AppException.HoldExpired();
            return false;
        }

        return true;
    }

    public static bool ValidateCancellation(
        string currentStatus,
        out bool isAlreadyDone,
        out AppException? error)
    {
        error = null;
        isAlreadyDone = false;

        if (currentStatus is HoldStatus.Cancelled or HoldStatus.Expired)
        {
            isAlreadyDone = true;
            return true; // Idempotent success
        }

        if (currentStatus == HoldStatus.Confirmed)
        {
            error = AppException.AlreadyConfirmed();
            return false;
        }

        return true;
    }
}
