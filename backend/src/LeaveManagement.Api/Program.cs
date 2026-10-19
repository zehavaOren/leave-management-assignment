using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using LeaveManagement.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<LeaveManagement.Api.Services.LeaveRequestService>();
builder.Services.AddScoped<LeaveManagement.Api.Services.EmployeeService>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    if (context.ProblemDetails.Status >= 500)
    {
        context.ProblemDetails.Title = "An unexpected error occurred.";
        context.ProblemDetails.Detail = null;
        context.ProblemDetails.Extensions.Remove("exception");
    }
});
// A leave request is a small JSON document; enforce the same limit for chunked bodies.
const long maxRequestBodyBytes = 16 * 1024;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestBodyBytes);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests. Try again shortly."
        }, cancellationToken);
    };
});

// PostgreSQL by default — `docker compose up` brings one up next to the API.
// No Docker on the machine? Set Database:Provider=Sqlite and the app runs
// against a local file instead. See the README.
var useSqlite = string.Equals(
    builder.Configuration["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<LeaveDbContext>(options =>
{
    if (useSqlite)
        options.UseSqlite(builder.Configuration.GetConnectionString("LeaveSqlite"));
    else
        options.UseNpgsql(builder.Configuration.GetConnectionString("Leave"));
});

// Leave requests are returned as DTOs; the employee lookup still serializes the
// entity, so tolerate the LeaveRequest <-> Employee navigation cycle.
builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();
// Leave data must not be stored by browsers or shared HTTP caches.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (context.Request.Path.StartsWithSegments("/api"))
            context.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    });
    if (context.Request.Path.StartsWithSegments("/api") && context.Request.ContentLength > maxRequestBodyBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status413PayloadTooLarge, Title = "The request body is too large."
        });
        return;
    }
    await next(context);
});
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var status = error is BadHttpRequestException badRequest
        ? badRequest.StatusCode : StatusCodes.Status500InternalServerError;
    var title = status == StatusCodes.Status413PayloadTooLarge
        ? "The request body is too large." : "An unexpected error occurred.";
    await Results.Problem(statusCode: status, title: title).ExecuteAsync(context);
}));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
    db.Database.EnsureCreated();
    LeaveDbContext.Seed(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseRouting();
app.UseCors("AllowAngular");
app.UseRateLimiter();
app.MapControllers().RequireRateLimiting("api");

app.Run();

// Exposed so the integration tests can reference the entry point.
public partial class Program { }

