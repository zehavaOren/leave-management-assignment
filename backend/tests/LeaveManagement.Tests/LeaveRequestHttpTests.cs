using System.Net;
using System.Net.Http.Json;
using LeaveManagement.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using LeaveManagement.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LeaveManagement.Tests;

// Exercises routing, JSON validation and persistence through the real HTTP pipeline.
public class LeaveRequestHttpTests
{
    private sealed class HttpTestApp : WebApplicationFactory<Program>
    {
        private readonly Microsoft.Data.Sqlite.SqliteConnection _anchor = new(
            $"Data Source=leave-http-{Guid.NewGuid()};Mode=Memory;Cache=Shared");
        public HttpTestApp() => _anchor.Open();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Override the resolved context, not late host configuration: Program
            // chooses its provider before ConfigureAppConfiguration is applied.
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<LeaveDbContext>();
                services.RemoveAll<DbContextOptions<LeaveDbContext>>();
                services.AddScoped(_ => new LeaveDbContext(
                    new DbContextOptionsBuilder<LeaveDbContext>().UseSqlite(_anchor).Options));
            });
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _anchor.Dispose();
        }
    }
    private static WebApplicationFactory<Program> CreateApp() => new HttpTestApp();

    [Fact]
    public async Task Http_CreateApproveAndConflict_UsesExpectedStatusCodes()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        var employees = await client.GetFromJsonAsync<EmployeeResponse[]>("/api/employees");
        var employee = Assert.Single(employees!, e => e.Name == "Dana Levi");
        var created = await client.PostAsJsonAsync("/api/leave-requests", new
        {
            employeeId = employee.Id, type = 0, startDate = "2026-03-01", endDate = "2026-03-02"
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var request = await created.Content.ReadFromJsonAsync<LeaveRequestResponse>();
        Assert.Equal(2, request!.Days);
        var approval = await client.PostAsJsonAsync($"/api/leave-requests/{request.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        Assert.Equal(LeaveStatus.Approved, (await approval.Content.ReadFromJsonAsync<LeaveRequestResponse>())!.Status);
        var repeat = await client.PostAsJsonAsync($"/api/leave-requests/{request.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        var conflict = await repeat.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("request_already_approved", conflict.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", repeat.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/leave-requests/999/approve", new { })).StatusCode);
    }

    [Fact]
    public async Task Http_MissingType_IsRejectedByModelValidation()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/leave-requests", new
        {
            employeeId = 1, startDate = "2026-03-01", endDate = "2026-03-02"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Theory]
    [InlineData("2026-03-03", "2026-03-01", 0, "invalid_date_range")]
    [InlineData("2026-03-01", "2026-03-02", 99, "invalid_leave_type")]
    [InlineData("0001-01-01", "2026-03-02", 0, "start_date_required")]
    public async Task Http_InvalidRequest_IdentifiesTheSpecificProblem(string start, string end, int type, string code)
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/leave-requests", new
        {
            employeeId = 1, type, startDate = start, endDate = end
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Http_InsufficientBalance_ReturnsYearRequestedAndRemainingDays()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/leave-requests", new
        {
            employeeId = 1, type = 0, startDate = "2026-03-01", endDate = "2026-03-03"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("insufficient_balance", problem.GetProperty("code").GetString());
        Assert.Equal(2026, problem.GetProperty("year").GetInt32());
        Assert.Equal(3, problem.GetProperty("requestedDays").GetInt32());
        Assert.Equal(2, problem.GetProperty("remainingDays").GetInt32());
    }

    [Theory]
    [InlineData("/api/employees")]
    [InlineData("/api/leave-requests")]
    public async Task Http_PersonalData_IsNotCacheable(string path)
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task Http_Swagger_IsNotExposedInProduction()
    {
        using var app = CreateApp().WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Http_UnexpectedFailure_DoesNotDiscloseInternalDetails(string environment)
    {
        using var app = CreateApp().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureServices(services => services.AddControllers()
                .AddApplicationPart(typeof(SecurityFailureTestController).Assembly));
        });
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/api/security-test-failure");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SECRET_CONNECTION_DETAIL", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("stackTrace", body);
        Assert.Contains("An unexpected error occurred.", body);
    }

    [Fact]
    public async Task Http_OversizedPayload_IsRejectedBeforeItIsProcessed()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        using var content = new StringContent("{\"padding\":\"" + new string('x', 17000) + "\"}",
            System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/leave-requests", content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Http_ServerBodyLimitFailure_Remains413InsteadOf500()
    {
        using var app = CreateApp().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(SecurityFailureTestController).Assembly)));
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/api/security-test-failure/body-too-large");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.DoesNotContain("INTERNAL_BODY_LIMIT_DETAIL", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Http_Overposting_CannotSetApprovalDaysOrEmployeeQuota()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/leave-requests", new
        {
            employeeId = 2, type = 0, startDate = "2027-01-01", endDate = "2027-01-02",
            status = 1, days = -999, employee = new { id = 2, annualQuota = 999999 }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = await response.Content.ReadFromJsonAsync<LeaveRequestResponse>();
        Assert.Equal(LeaveStatus.Pending, request!.Status);
        Assert.Equal(2, request.Days);
        Assert.Equal(14, request.Employee!.AnnualQuota);
    }

    [Fact]
    public async Task Http_Search_RejectsExcessivelyLongInput()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/api/leave-requests/search?name=" + new string('x', 101));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Http_RateLimit_CannotBeBypassedWithSpoofedForwardedHeaders()
    {
        using var app = CreateApp();
        using var client = app.CreateClient();
        for (var i = 1; i <= 120; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/employees");
            request.Headers.Add("X-Forwarded-For", $"203.0.113.{i}");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var rejected = await client.GetAsync("/api/employees");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
    }

}

// This controller exists only in the test assembly and is registered by its test factory.
[Microsoft.AspNetCore.Mvc.ApiController]
[Microsoft.AspNetCore.Mvc.Route("api/security-test-failure")]
public sealed class SecurityFailureTestController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    [Microsoft.AspNetCore.Mvc.HttpGet]
    public Microsoft.AspNetCore.Mvc.IActionResult Get() =>
        throw new InvalidOperationException("SECRET_CONNECTION_DETAIL");
    [Microsoft.AspNetCore.Mvc.HttpGet("body-too-large")]
    public Microsoft.AspNetCore.Mvc.IActionResult BodyTooLarge() =>
        throw new Microsoft.AspNetCore.Http.BadHttpRequestException("INTERNAL_BODY_LIMIT_DETAIL", 413);
}
