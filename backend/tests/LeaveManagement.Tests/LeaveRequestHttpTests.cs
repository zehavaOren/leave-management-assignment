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
}
