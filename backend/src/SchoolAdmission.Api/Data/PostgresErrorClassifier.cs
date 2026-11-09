using Npgsql;

namespace SchoolAdmission.Api.Data;

public static class PostgresErrorClassifier
{
    public static bool IsBusy(Exception e, CancellationToken callerToken = default)
    {
        if (e is PostgresException pgEx)
        {
            if (pgEx.SqlState is PostgresErrorCodes.LockNotAvailable     // 55P03
                               or PostgresErrorCodes.TooManyConnections    // 53300
                               or PostgresErrorCodes.DeadlockDetected      // 40P01
                               or PostgresErrorCodes.SerializationFailure) // 40001
            {
                return true;
            }

            if (pgEx.SqlState == PostgresErrorCodes.QueryCanceled && !callerToken.IsCancellationRequested) // 57014 (statement timeout / lock timeout, not client abort)
            {
                return true;
            }
        }

        if (e is NpgsqlException { InnerException: TimeoutException } or TimeoutException)
        {
            return true;
        }

        return false;
    }
}
