using LeaveManagement.Api.Models;
using LeaveManagement.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeaveManagement.Api.Controllers;

// Thin HTTP layer: business rules and data access live in LeaveRequestService.
[ApiController]
[Route("api/leave-requests")]
public class LeaveRequestsController : ControllerBase
{
    private readonly LeaveRequestService _service;
    private readonly ILogger<LeaveRequestsController> _logger;
    public LeaveRequestsController(LeaveRequestService service, ILogger<LeaveRequestsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // GET /api/leave-requests
    [HttpGet]
    public IActionResult GetAll() => Ok(_service.GetAll());

    // GET /api/leave-requests/search?name=Dana
    // Lets the UI quickly find requests by employee name.
    [HttpGet("search")]
    public IActionResult Search([FromQuery, System.ComponentModel.DataAnnotations.StringLength(100)] string name)
        => Ok(_service.GetAll(name));

    // POST /api/leave-requests
    [HttpPost]
    public IActionResult Create([FromBody] CreateLeaveRequestDto dto) => Execute(() => _service.Create(dto));

    // POST /api/leave-requests/{id}/approve
    [HttpPost("{id:int}/approve")]
    public IActionResult Approve(int id) => Execute(() => _service.Approve(id));

    private IActionResult Execute(Func<LeaveRequestResponse> operation)
    {
        try { return Ok(operation()); }
        catch (LeaveRequestException exception)
        {
            _logger.LogWarning(exception, "Leave request rejected with HTTP {StatusCode}: {Reason}", exception.StatusCode, exception.Message);
            var problem = new ProblemDetails { Status = exception.StatusCode, Title = exception.Message };
            problem.Extensions["code"] = exception.Code;
            foreach (var detail in exception.Details) problem.Extensions[detail.Key] = detail.Value;
            return exception.StatusCode switch
            {
                400 => BadRequest(problem),
                404 => NotFound(problem),
                _ => Conflict(problem)
            };
        }
    }
}
