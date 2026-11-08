using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data.Abstractions;
using SchoolAdmission.Api.Middleware;

namespace SchoolAdmission.Api.Services;

public class ClassService : IClassService
{
    private readonly IClassStore _store;
    private readonly IdGenerator _idGen;

    public ClassService(IClassStore store, IdGenerator idGen)
    {
        _store = store;
        _idGen = idGen;
    }

    public async Task<ClassResponse> CreateAsync(CreateClassRequest req, CancellationToken ct = default)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 100)
            throw AppException.ValidationError("'name' must be between 1 and 100 characters.");
        if (req.TotalSeats < 1 || req.TotalSeats > 100_000)
            throw AppException.ValidationError("'totalSeats' must be between 1 and 100000.");

        var id = _idGen.NewClassId();
        return await _store.CreateClassAsync(id, name, req.TotalSeats, DateTime.UtcNow, ct);
    }

    public async Task<List<ClassDetailResponse>> GetAllAsync(CancellationToken ct = default)
    {
        return await _store.GetAllClassesAsync(ct);
    }

    public async Task<ClassDetailResponse> GetByIdAsync(string classId, CancellationToken ct = default)
    {
        var cls = await _store.GetClassByIdAsync(classId, ct);
        if (cls is null)
            throw AppException.NotFound($"Class '{classId}' not found.");

        return cls;
    }
}
