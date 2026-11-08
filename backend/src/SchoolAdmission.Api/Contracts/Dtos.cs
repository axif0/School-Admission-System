namespace SchoolAdmission.Api.Contracts;

public record CreateClassRequest(string Name, int TotalSeats);

public record ClassResponse(
    string Id,
    string Name,
    int TotalSeats
);

public record ClassDetailResponse(
    string Id,
    string Name,
    int TotalSeats,
    int HeldSeats,
    int ConfirmedSeats,
    int AvailableSeats
);

public record CreateHoldResponse(
    string HoldId,
    string ClassId,
    string StudentId,
    string Status,
    DateTime ExpiresAt,
    DateTime ServerTime
);

public record HoldResponse(
    string HoldId,
    string ClassId,
    string Status,
    DateTime ExpiresAt,
    DateTime ServerTime
);

public record ConfirmHoldResponse(
    string HoldId,
    string Status
);

public record MyHoldResponse(
    string HoldId,
    string ClassId,
    string ClassName,
    string Status,
    DateTime ExpiresAt
);

public record ErrorDetail(string Code, string Message, string? TraceId = null);
public record ErrorResponse(ErrorDetail Error);
