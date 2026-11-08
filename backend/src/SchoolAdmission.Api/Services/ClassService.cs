using System.Diagnostics;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data.Abstractions;
using SchoolAdmission.Api.Middleware;
using SchoolAdmission.Api.Observability;

namespace SchoolAdmission.Api.Services;

public class ClassService : IClassService
{
    private readonly IClassStore _store;
    private readonly IdGenerator _idGen;
    private readonly ClassQueryCoalescer _coalescer;
    private readonly AppMetrics? _metrics;

    public ClassService(
        IClassStore store,
        IdGenerator idGen,
        ClassQueryCoalescer coalescer,
        AppMetrics? metrics = null)
    {
        _store = store;
        _idGen = idGen;
        _coalescer = coalescer;
        _metrics = metrics;
    }

    public async Task<ClassResponse> CreateAsync(CreateClassRequest req, CancellationToken ct = default)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 100)
            throw AppException.ValidationError("'name' must be between 1 and 100 characters.");
        if (req.TotalSeats < 1 || req.TotalSeats > 100_000)
            throw AppException.ValidationError("'totalSeats' must be between 1 and 100000.");

        var id = _idGen.NewClassId();
        var res = await _store.CreateClassAsync(id, name, req.TotalSeats, DateTime.UtcNow, ct);
        _coalescer.OnWriteOccurred();
        return res;
    }

    public async Task<List<ClassDetailResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await _coalescer.ExecuteCoalescedAsync(() => _store.GetAllClassesAsync(ct));
        sw.Stop();
        _metrics?.ClassReadLatencySeconds.Record(sw.Elapsed.TotalSeconds);
        return result;
    }

    public async Task<ClassDetailResponse> GetByIdAsync(string classId, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var cls = await _store.GetClassByIdAsync(classId, ct);
        sw.Stop();
        _metrics?.ClassReadLatencySeconds.Record(sw.Elapsed.TotalSeconds);
        if (cls is null)
            throw AppException.NotFound($"Class '{classId}' not found.");

        return cls;
    }
}
