using SchoolAdmission.Api.Contracts;

namespace SchoolAdmission.Api.Services;

public interface IClassService
{
    Task<ClassResponse> CreateAsync(CreateClassRequest req, CancellationToken ct = default);
    Task<List<ClassDetailResponse>> GetAllAsync(CancellationToken ct = default);
    Task<ClassDetailResponse> GetByIdAsync(string classId, CancellationToken ct = default);
}
