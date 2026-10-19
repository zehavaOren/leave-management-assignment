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

    [Fact]
    public void Employees_GetAll_ReturnsDtoWithoutTrackingEntities()
    {
        using var db = _database.NewDb();
        var employee = AddEmployee(db);
        db.ChangeTracker.Clear();
        var result = Assert.IsType<OkObjectResult>(new EmployeesController(new EmployeeService(db)).GetAll());
        var response = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<EmployeeResponse>>(result.Value));
        Assert.Equal(employee.Id, response.Id);
        Assert.Equal(employee.Name, response.Name);
        Assert.Equal(employee.AnnualQuota, response.AnnualQuota);
        Assert.Empty(db.ChangeTracker.Entries());
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

    [Theory]
    [InlineData(LeaveStatus.Pending, 1, 3)]
    [InlineData(LeaveStatus.Approved, 1, 3)]
    [InlineData(LeaveStatus.Pending, 3, 5)]
    [InlineData(LeaveStatus.Approved, 1, 1)]
    [InlineData(LeaveStatus.Pending, 2, 2)]
    public void Create_OverlapIncludingBoundary_Returns409(LeaveStatus status, int start, int end)
    {
        using var db = _database.NewDb();
        var employee = AddEmployee(db);
        AddRequest(db, employee, new(2026, 3, 1), new(2026, 3, 3), status, LeaveType.Sick);
        var error = Assert.Throws<LeaveRequestException>(() => new LeaveRequestService(db)
            .Create(Dto(employee, new(2026, 3, start), new(2026, 3, end))));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal("overlapping_request", error.Code);
        Assert.Single(db.LeaveRequests);
    }

    [Fact]
    public void Create_RejectedOtherEmployeeAndAdjacentDates_DoNotBlock()
    {
        using var db = _database.NewDb();
        var employee = AddEmployee(db);
        AddRequest(db, employee, new(2026, 3, 1), new(2026, 3, 3), LeaveStatus.Rejected);
        var other = AddEmployee(db);
        AddRequest(db, other, new(2026, 3, 1), new(2026, 3, 3));
        var service = new LeaveRequestService(db);
        service.Create(Dto(employee, new(2026, 3, 1), new(2026, 3, 3)));
        Assert.Equal(1, service.Create(Dto(employee, new(2026, 3, 4), new(2026, 3, 4))).Days);
    }

    [Fact]
    public void Approve_LegacyOverlappingPending_FirstWinsAndSecondStaysPending()
    {
        using var db = _database.NewDb();
        var employee = AddEmployee(db);
        var first = AddRequest(db, employee, new(2026, 3, 1), new(2026, 3, 3), LeaveStatus.Pending);
        var second = AddRequest(db, employee, new(2026, 3, 2), new(2026, 3, 4), LeaveStatus.Pending);
        var service = new LeaveRequestService(db);
        Assert.Equal(LeaveStatus.Approved, service.Approve(first.Id).Status);
        var error = Assert.Throws<LeaveRequestException>(() => service.Approve(second.Id));
        Assert.Equal("overlapping_request", error.Code);
        Assert.Equal(409, error.StatusCode);
        db.Entry(second).Reload();
        Assert.Equal(LeaveStatus.Pending, second.Status);
        Assert.Equal(3, db.LeaveRequests.Where(r => r.Status == LeaveStatus.Approved).Sum(r => r.Days));
    }

    [Fact]
    public void Approve_RejectedOverlap_DoesNotBlock()
    {
        using var db = _database.NewDb();
        var employee = AddEmployee(db);
        AddRequest(db, employee, new(2026, 3, 1), new(2026, 3, 3), LeaveStatus.Rejected);
        var request = AddRequest(db, employee, new(2026, 3, 1), new(2026, 3, 3), LeaveStatus.Pending);
        Assert.Equal(LeaveStatus.Approved, new LeaveRequestService(db).Approve(request.Id).Status);
    }

    [PostgresFact]
    public async Task ConcurrentOverlappingApprovals_OnlyOneSucceeds()
    {
        using var setup = _database.NewDb();
        var employee = AddEmployee(setup);
        var first = AddRequest(setup, employee, new(2026, 3, 1), new(2026, 3, 3), LeaveStatus.Pending);
        var second = AddRequest(setup, employee, new(2026, 3, 1), new(2026, 3, 3), LeaveStatus.Pending);
        var options = new DbContextOptionsBuilder<LeaveDbContext>().UseNpgsql(setup.Database.GetConnectionString()).Options;
        using var start = new Barrier(2);
        Task<string> Run(int id) => Task.Run(() =>
        {
            using var db = new LeaveDbContext(options);
            if (!start.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
            try { new LeaveRequestService(db).Approve(id); return "approved"; }
            catch (LeaveRequestException error) { Assert.Equal(409, error.StatusCode); return error.Code; }
        });
        Assert.Equal(new[] { "approved", "overlapping_request" }, (await Task.WhenAll(Run(first.Id), Run(second.Id))).OrderBy(x => x));
        setup.ChangeTracker.Clear();
        Assert.Single(setup.LeaveRequests.Where(r => r.Status == LeaveStatus.Approved));
    }

    [PostgresFact]
    public async Task ConcurrentOverlappingCreates_OnlyOneIsSaved()
    {
        using var setup = _database.NewDb();
        var employee = AddEmployee(setup);
        var options = new DbContextOptionsBuilder<LeaveDbContext>().UseNpgsql(setup.Database.GetConnectionString()).Options;
        using var start = new Barrier(2);
        Task<string> Run() => Task.Run(() =>
        {
            using var db = new LeaveDbContext(options);
            if (!start.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
            try { new LeaveRequestService(db).Create(Dto(employee, new(2026, 3, 1), new(2026, 3, 3))); return "created"; }
            catch (LeaveRequestException error) { Assert.Equal(409, error.StatusCode); return error.Code; }
        });
        Assert.Equal(new[] { "created", "overlapping_request" }, (await Task.WhenAll(Run(), Run())).OrderBy(x => x));
        Assert.Single(setup.LeaveRequests);
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
