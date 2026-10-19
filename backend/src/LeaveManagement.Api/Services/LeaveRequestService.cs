using LeaveManagement.Api.Data;
using LeaveManagement.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveManagement.Api.Services;

public sealed class LeaveRequestException : Exception
{
    public int StatusCode { get; }
    public LeaveRequestException(int statusCode, string message) : base(message) => StatusCode = statusCode;
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
        if (dto.EmployeeId <= 0 || (dto.Type == null || !Enum.IsDefined(dto.Type.Value)) ||
            start == DateTime.MinValue || end == DateTime.MinValue || end < start)
            throw new LeaveRequestException(400, "Choose an employee, a valid leave type and an ordered date range.");

        var employee = _db.Employees.SingleOrDefault(e => e.Id == dto.EmployeeId)
            ?? throw new LeaveRequestException(404, "Employee not found.");
        EnsureBalance(employee, dto.Type.Value, start, end, 400);
        var request = new LeaveRequest
        {
            EmployeeId = employee.Id, Employee = employee, Type = dto.Type.Value,
            StartDate = start, EndDate = end, Days = (end - start).Days + 1,
            Status = LeaveStatus.Pending
        };
        _db.LeaveRequests.Add(request);
        _db.SaveChanges();
        return ToResponse(request);
    }

    public LeaveRequestResponse Approve(int id)
    {
        // Read only the immutable employee id before acquiring the shared employee lock.
        var employeeId = _db.LeaveRequests.AsNoTracking().Where(r => r.Id == id)
            .Select(r => (int?)r.EmployeeId).SingleOrDefault()
            ?? throw new LeaveRequestException(404, "Leave request not found.");

        using var transaction = _db.Database.BeginTransaction(_db.Database.IsNpgsql() ? System.Data.IsolationLevel.ReadCommitted : System.Data.IsolationLevel.Serializable);
        // PostgreSQL READ COMMITTED is deliberate: after waiting for the lock, the
        // balance query must see the previous approver's commit, not an old snapshot.
        // READ COMMITTED makes the lock and the subsequent balance read consistent.
        return ApproveLocked(id, employeeId, transaction);
    }

    private LeaveRequestResponse ApproveLocked(int id, int employeeId,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        Employee employee;
        if (_db.Database.IsNpgsql())
        {
            employee = _db.Employees.FromSqlInterpolated(
                $"SELECT * FROM \"Employees\" WHERE \"Id\" = {employeeId} FOR UPDATE")
                .AsEnumerable().Single();
        }
        else
        {
            // SQLite's serializable write transaction is a local fallback. It does
            // not establish that PostgreSQL row-lock behaviour has been tested.
            employee = _db.Employees.Single(e => e.Id == employeeId);
        }
        var request = _db.LeaveRequests.Include(r => r.Employee).Single(r => r.Id == id);
        _db.Entry(request).Reload();
        if (request.Status != LeaveStatus.Pending)
            throw new LeaveRequestException(409, "Only a pending request can be approved.");
        EnsureBalance(employee, request.Type, request.StartDate, request.EndDate, 409);
        request.Status = LeaveStatus.Approved;
        _db.SaveChanges();
        transaction.Commit();
        return ToResponse(request);
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
            if (used + OverlapDays(start, end, first, last) > employee.AnnualQuota)
                throw new LeaveRequestException(statusCode, $"Not enough vacation balance for {year}.");
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


