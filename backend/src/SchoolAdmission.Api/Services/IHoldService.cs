using SchoolAdmission.Api.Contracts;

namespace SchoolAdmission.Api.Services;

public interface IHoldService
{
    Task<CreateHoldResponse> CreateHoldAsync(string classId, string studentId, CancellationToken ct = default);
    Task<HoldResponse> GetHoldAsync(string holdId, CancellationToken ct = default);
    Task<ConfirmHoldResponse> ConfirmHoldAsync(string holdId, string studentId, CancellationToken ct = default);
    Task CancelHoldAsync(string holdId, string studentId, CancellationToken ct = default);
    Task<List<MyHoldResponse>> GetMyHoldsAsync(string studentId, CancellationToken ct = default);
}
