using LeaveManagement.Api.Data;
using LeaveManagement.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveManagement.Api.Services;

public sealed class EmployeeService
{
    private readonly LeaveDbContext _db;
    public EmployeeService(LeaveDbContext db) => _db = db;

    public IReadOnlyList<EmployeeResponse> GetAll() => _db.Employees.AsNoTracking()
        .OrderBy(e => e.Id)
        .Select(e => new EmployeeResponse(e.Id, e.Name, e.AnnualQuota)).ToList();
}
