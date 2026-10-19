using LeaveManagement.Api.Data;
using LeaveManagement.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveManagement.Api.Services;

public sealed class LeaveRequestException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }
    public IReadOnlyDictionary<string, object> Details { get; }
    public LeaveRequestException(int statusCode, string message)
        : this(statusCode, "invalid_request", message) { }

    public LeaveRequestException(int statusCode, string code, string message,
        IReadOnlyDictionary<string, object>? details = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Details = details ?? new Dictionary<string, object>();
    }
}

public sealed class LeaveRequestService
{
    private readonly LeaveDbContext _db;
    public LeaveRequestService(LeaveDbContext db) => _db = db;

    public IReadOnlyList<LeaveRequestResponse> GetAll(string? name = null)
    {
        var query = _db.LeaveRequests.AsNoTracking().Include(r => r.Employee).AsQueryable();
        // LINQ parameters keep employee names out of executable SQL.
        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(r => r.Employee != null && r.Employee.Name.Contains(name));
        return query.OrderByDescending(r => r.StartDate).ToList().Select(ToResponse).ToList();
    }

    public LeaveRequestResponse Create(CreateLeaveRequestDto dto)
    {
        var start = dto.StartDate.Date;
        var end = dto.EndDate.Date;
        if (dto.EmployeeId <= 0)
            throw new LeaveRequestException(400, "invalid_employee", "Choose a valid employee.");
        if (dto.Type == null)
            throw new LeaveRequestException(400, "leave_type_required", "Choose a leave type.");
        if (!Enum.IsDefined(dto.Type.Value))
            throw new LeaveRequestException(400, "invalid_leave_type", "The selected leave type is invalid.");
        if (start == DateTime.MinValue)
            throw new LeaveRequestException(400, "start_date_required", "Enter a valid start date.");
        if (end == DateTime.MinValue)
            throw new LeaveRequestException(400, "end_date_required", "Enter a valid end date.");
        if (end < start)
            throw new LeaveRequestException(400, "invalid_date_range", "End date must be on or after start date.");

        using var transaction = _db.Database.BeginTransaction(_db.Database.IsNpgsql() ? System.Data.IsolationLevel.ReadCommitted : System.Data.IsolationLevel.Serializable);
        var employee = LockEmployee(dto.EmployeeId);
        EnsureNoOverlap(employee.Id, start, end, includePending: true);
        EnsureBalance(employee, dto.Type.Value, start, end, 400);
        var request = new LeaveRequest
        {
            EmployeeId = employee.Id, Employee = employee, Type = dto.Type.Value,
            StartDate = start, EndDate = end, Days = (end - start).Days + 1,
            Status = LeaveStatus.Pending
        };
        _db.LeaveRequests.Add(request);
        _db.SaveChanges();
        transaction.Commit();
        return ToResponse(request);
    }

    public LeaveRequestResponse Approve(int id)
    {
        // Read only the immutable employee id before acquiring the shared employee lock.
        var employeeId = _db.LeaveRequests.AsNoTracking().Where(r => r.Id == id)
            .Select(r => (int?)r.EmployeeId).SingleOrDefault()
            ?? throw new LeaveRequestException(404, "request_not_found", "The leave request no longer exists.");

        using var transaction = _db.Database.BeginTransaction(_db.Database.IsNpgsql() ? System.Data.IsolationLevel.ReadCommitted : System.Data.IsolationLevel.Serializable);
        // PostgreSQL READ COMMITTED is deliberate: after waiting for the lock, the
        // balance query must see the previous approver's commit, not an old snapshot.
        // READ COMMITTED makes the lock and the subsequent balance read consistent.
        return ApproveLocked(id, employeeId, transaction);
    }

    private LeaveRequestResponse ApproveLocked(int id, int employeeId,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        var employee = LockEmployee(employeeId);
        var request = _db.LeaveRequests.Include(r => r.Employee).Single(r => r.Id == id);
        _db.Entry(request).Reload();
        if (request.Status != LeaveStatus.Pending)
            throw new LeaveRequestException(409,
                request.Status == LeaveStatus.Approved ? "request_already_approved" : "request_rejected",
                request.Status == LeaveStatus.Approved
                    ? "This request has already been approved."
                    : "This request has been rejected and cannot be approved.");
        EnsureNoOverlap(employee.Id, request.StartDate, request.EndDate, includePending: false, excludedId: request.Id);
        EnsureBalance(employee, request.Type, request.StartDate, request.EndDate, 409);
        request.Status = LeaveStatus.Approved;
        _db.SaveChanges();
        transaction.Commit();
        return ToResponse(request);
    }

    private Employee LockEmployee(int employeeId)
    {
        if (_db.Database.IsNpgsql())
        {
            return _db.Employees.FromSqlInterpolated(
                $"SELECT * FROM \"Employees\" WHERE \"Id\" = {employeeId} FOR UPDATE")
                .AsEnumerable().SingleOrDefault()
                ?? throw new LeaveRequestException(404, "employee_not_found", "The selected employee no longer exists.");
        }
        // SQLite's serializable write transaction is a local fallback. It does
        // not establish that PostgreSQL row-lock behaviour has been tested.
        return _db.Employees.SingleOrDefault(e => e.Id == employeeId)
            ?? throw new LeaveRequestException(404, "employee_not_found", "The selected employee no longer exists.");
    }

    private void EnsureNoOverlap(int employeeId, DateTime start, DateTime end,
        bool includePending, int excludedId = 0)
    {
        if (_db.LeaveRequests.AsNoTracking().Any(r => r.EmployeeId == employeeId &&
            r.Id != excludedId && (r.Status == LeaveStatus.Approved ||
                (includePending && r.Status == LeaveStatus.Pending)) &&
            r.StartDate <= end && r.EndDate >= start))
            throw new LeaveRequestException(409, "overlapping_request",
                "These dates overlap another leave request for this employee. Choose different dates.");
    }

    private void EnsureBalance(Employee employee, LeaveType type, DateTime start, DateTime end, int statusCode)
    {
        if (type != LeaveType.Vacation) return;
        // Annual quota counts approved calendar days in each affected year.
        // Pending requests do not reserve days; approval always rechecks the balance.
        var approved = _db.LeaveRequests.AsNoTracking().Where(r =>
            r.EmployeeId == employee.Id && r.Type == LeaveType.Vacation &&
            r.Status == LeaveStatus.Approved && r.StartDate <= new DateTime(end.Year, 12, 31) &&
            r.EndDate >= new DateTime(start.Year, 1, 1)).Select(r => new { r.StartDate, r.EndDate }).ToList();
        for (var year = start.Year; year <= end.Year; year++)
        {
            var first = new DateTime(year, 1, 1);
            var last = new DateTime(year, 12, 31);
            var used = approved.Sum(r => OverlapDays(r.StartDate, r.EndDate, first, last));
            var requested = OverlapDays(start, end, first, last);
            var remaining = Math.Max(0, employee.AnnualQuota - used);
            if (requested > remaining)
                throw new LeaveRequestException(statusCode, "insufficient_balance",
                    $"Not enough vacation balance for {year}: requested {requested} days, available {remaining} days.",
                    new Dictionary<string, object>
                    {
                        ["year"] = year, ["requestedDays"] = requested, ["remainingDays"] = remaining
                    });
        }
    }

    private static int OverlapDays(DateTime start, DateTime end, DateTime first, DateTime last)
    {
        var left = start.Date > first ? start.Date : first;
        var right = end.Date < last ? end.Date : last;
        return right < left ? 0 : (right - left).Days + 1;
    }

    private static LeaveRequestResponse ToResponse(LeaveRequest request) => new(
        request.Id, request.EmployeeId,
        request.Employee == null ? null : new EmployeeResponse(request.Employee.Id, request.Employee.Name, request.Employee.AnnualQuota),
        request.Type, request.StartDate, request.EndDate, request.Status, request.Days);
}
