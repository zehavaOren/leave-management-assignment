using LeaveManagement.Api.Controllers;
using LeaveManagement.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace LeaveManagement.Tests;

public class BalanceRegressionTests : IClassFixture<TestDatabase>
{
    private readonly TestDatabase _database;
    public BalanceRegressionTests(TestDatabase database) => _database = database;

    [Fact]
    public void Create_ExceedingRemainingBalance_IsRejected()
    {
        using var db = _database.NewDb();
        var employee = new Employee { Name = "Balance regression", AnnualQuota = 20 };
        db.Employees.Add(employee);
        db.SaveChanges();
        db.LeaveRequests.Add(new LeaveRequest
        {
            EmployeeId = employee.Id, Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 1, 18),
            Days = 18, Status = LeaveStatus.Approved
        });
        db.SaveChanges();
        var controller = new LeaveRequestsController(new LeaveManagement.Api.Services.LeaveRequestService(db));
        var result = controller.Create(new CreateLeaveRequestDto
        {
            EmployeeId = employee.Id, Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 3, 1), EndDate = new DateTime(2026, 3, 3)
        });
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Single(db.LeaveRequests);
    }
}

