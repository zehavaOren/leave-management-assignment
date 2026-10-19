using LeaveManagement.Api.Controllers;
using LeaveManagement.Api.Data;
using LeaveManagement.Api.Models;
using LeaveManagement.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LeaveManagement.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (TestDatabase.UseSqlite) Skip = "Requires real PostgreSQL row locks (run with Docker).";
    }
}

public class LeaveRequestRulesTests : IClassFixture<TestDatabase>
{
    private readonly TestDatabase _database;
    public LeaveRequestRulesTests(TestDatabase database) => _database = database;
    private static Employee AddEmployee(LeaveDbContext db, int quota = 20)
    {
        var employee = new Employee { Name = "Rules employee", AnnualQuota = quota };
        db.Employees.Add(employee);
        db.SaveChanges();
        return employee;
    }
    private static LeaveRequest AddRequest(LeaveDbContext db, Employee employee, DateTime start,
        DateTime end, LeaveStatus status = LeaveStatus.Approved, LeaveType type = LeaveType.Vacation)
    {
        var request = new LeaveRequest { EmployeeId = employee.Id, StartDate = start,
            EndDate = end, Days = (end - start).Days + 1, Type = type, Status = status };
        db.LeaveRequests.Add(request);
        db.SaveChanges();
        return request;
    }
    private static CreateLeaveRequestDto Dto(Employee employee, DateTime start, DateTime end,
        LeaveType? type = LeaveType.Vacation) => new() { EmployeeId = employee.Id,
        StartDate = start, EndDate = end, Type = type };

    [Fact]
    public void Create_ExactRemainingBalance_Succeeds()
    {
        using var db = _database.NewDb();
        var e = AddEmployee(db);
        AddRequest(db, e, new(2026, 1, 1), new(2026, 1, 18));
        var result = new LeaveRequestService(db).Create(Dto(e, new(2026, 3, 1), new(2026, 3, 2)));
        Assert.Equal(2, result.Days);
        Assert.Equal(LeaveStatus.Pending, result.Status);
    }

    [Fact]
    public void Create_CountsApprovedDaysLaterInSameYear()
    {
        using var db = _database.NewDb();
        var e = AddEmployee(db, 2);
        AddRequest(db, e, new(2026, 12, 1), new(2026, 12, 2));
        var error = Assert.Throws<LeaveRequestException>(() => new LeaveRequestService(db)
            .Create(Dto(e, new(2026, 1, 1), new(2026, 1, 1))));
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public void Create_PreviousYearDoesNotConsumeThisYearsQuota()
    {
        using var db = _database.NewDb();
        var e = AddEmployee(db, 2);
        AddRequest(db, e, new(2025, 1, 1), new(2025, 1, 2));
        Assert.Equal(2, new LeaveRequestService(db).Create(Dto(e, new(2026, 1, 1), new(2026, 1, 2))).Days);
    }

    [Fact]
    public void Create_CrossYearRequestIsAllocatedToEachYear()
    {
        using var db = _database.NewDb();
        var e = AddEmployee(db, 2);
        var result = new LeaveRequestService(db).Create(Dto(e, new(2026, 12, 31), new(2027, 1, 2)));
        Assert.Equal(3, result.Days);
    }

    [Fact]
    public void Create_ApprovedCrossYearRequestConsumesBothYears()
    {
        using var db = _database.NewDb();
        var e = AddEmployee(db, 2);
        AddRequest(db, e, new(2025, 12, 31), new(2026, 1, 2));
        Assert.Throws<LeaveRequestException>(() => new LeaveRequestService(db)
            .Create(Dto(e, new(2026, 3, 1), new(2026, 3, 1))));
    }

    [Theory]
    [InlineData(LeaveStatus.Pending)]
    [InlineData(LeaveStatus.Rejected)]
    public void Create_UnapprovedRequestsDoNotReserveBalance(LeaveStatus status)
    {
        using var db = _database.NewDb();
        var e = AddEmployee(db, 2);
        AddRequest(db, e, new(2026, 1, 1), new(2026, 1, 2), status);
        Assert.Equal(2, new LeaveRequestService(db).Create(Dto(e, new(2026, 3, 1), new(2026, 3, 2))).Days);
    }

    [Theory]
    [InlineData(LeaveType.Sick)]
    [InlineData(LeaveType.Unpaid)]
    public void Create_NonVacationDoesNotUseVacationQuota(LeaveType type)
    {
        using var db = _database.NewDb();
        var e = AddEmployee(db, 0);
        Assert.Equal(1, new LeaveRequestService(db).Create(Dto(e, new(2026, 3, 1), new(2026, 3, 1), type)).Days);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    public void Create_MissingOrUnknownType_IsRejected(int? type)
    {
        using var db = _database.NewDb();
        var e = AddEmployee(db);
        Assert.Throws<LeaveRequestException>(() => new LeaveRequestService(db)
            .Create(Dto(e, new(2026, 3, 1), new(2026, 3, 1), (LeaveType?)type)));
    }

    [Fact]
    public void Create_ReversedDates_IsRejectedWithoutSaving()
    {
        using var db = _database.NewDb();
        var e = AddEmployee(db);
        Assert.Throws<LeaveRequestException>(() => new LeaveRequestService(db)
            .Create(Dto(e, new(2026, 3, 2), new(2026, 3, 1))));
        Assert.Empty(db.LeaveRequests);
    }

    [Fact]
    public void Approve_MissingRequest_Returns404()
    {
        using var db = _database.NewDb();
        Assert.IsType<NotFoundObjectResult>(new LeaveRequestsController(new LeaveRequestService(db), Microsoft.Extensions.Logging.Abstractions.NullLogger<LeaveRequestsController>.Instance).Approve(999));
    }

    [Theory]
    [InlineData(LeaveStatus.Approved)]
    [InlineData(LeaveStatus.Rejected)]
    public void Approve_FinalRequest_Returns409(LeaveStatus status)
    {
        using var db = _database.NewDb();
        var e = AddEmployee(db);
        var request = AddRequest(db, e, new(2026, 3, 1), new(2026, 3, 1), status);
        Assert.IsType<ConflictObjectResult>(new LeaveRequestsController(new LeaveRequestService(db), Microsoft.Extensions.Logging.Abstractions.NullLogger<LeaveRequestsController>.Instance).Approve(request.Id));
    }

    [Fact]
    public void Approve_RechecksBalanceAndLeavesFailedRequestPending()
    {
        using var db = _database.NewDb();
        var e = AddEmployee(db, 3);
        var first = AddRequest(db, e, new(2026, 3, 1), new(2026, 3, 2), LeaveStatus.Pending);
        var second = AddRequest(db, e, new(2026, 4, 1), new(2026, 4, 2), LeaveStatus.Pending);
        var service = new LeaveRequestService(db);
        Assert.Equal(LeaveStatus.Approved, service.Approve(first.Id).Status);
        Assert.Equal(409, Assert.Throws<LeaveRequestException>(() => service.Approve(second.Id)).StatusCode);
        db.Entry(second).Reload();
        Assert.Equal(LeaveStatus.Pending, second.Status);
    }

    [Fact]
    public void Search_InjectionPayloadIsTreatedAsLiteralText()
    {
        using var db = _database.NewDb();
        var e = AddEmployee(db);
        AddRequest(db, e, new(2026, 3, 1), new(2026, 3, 1));
        var service = new LeaveRequestService(db);
        Assert.Empty(service.GetAll("' OR 1=1 --"));
        Assert.Single(service.GetAll("Rules"));
    }

    [PostgresFact]
    public async Task ConcurrentApprovals_CannotTogetherExceedQuota()
    {
        await AssertConcurrentApprovals(sameRequest: false);
    }

    [PostgresFact]
    public async Task ConcurrentApprovals_OfSameRequest_OnlyOneSucceeds()
    {
        await AssertConcurrentApprovals(sameRequest: true);
    }

    private async Task AssertConcurrentApprovals(bool sameRequest)
    {
        using var setup = _database.NewDb();
        var employee = AddEmployee(setup, 3);
        var first = AddRequest(setup, employee, new(2026, 3, 1), new(2026, 3, 2), LeaveStatus.Pending);
        var second = sameRequest ? first : AddRequest(setup, employee, new(2026, 4, 1), new(2026, 4, 2), LeaveStatus.Pending);
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseNpgsql(setup.Database.GetConnectionString()).Options;
        using var start = new Barrier(2);
        Task<int> Run(int id) => Task.Run(() =>
        {
            using var db = new LeaveDbContext(options);
            if (!start.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
            try { new LeaveRequestService(db).Approve(id); return 200; }
            catch (LeaveRequestException error) { return error.StatusCode; }
        });
        var statuses = await Task.WhenAll(Run(first.Id), Run(second.Id));
        Assert.Equal(new[] { 200, 409 }, statuses.OrderBy(s => s));
        setup.ChangeTracker.Clear();
        Assert.Single(setup.LeaveRequests.Where(r => r.Status == LeaveStatus.Approved));
    }
}
