using SchoolAdmission.Api.Contracts;

namespace SchoolAdmission.Api.Data.Abstractions;

public interface IClassStore
{
    Task<ClassResponse> CreateClassAsync(string id, string name, int totalSeats, DateTime createdAt, CancellationToken ct = default);
    Task<List<ClassDetailResponse>> GetAllClassesAsync(CancellationToken ct = default);
    Task<ClassDetailResponse?> GetClassByIdAsync(string classId, CancellationToken ct = default);
}
