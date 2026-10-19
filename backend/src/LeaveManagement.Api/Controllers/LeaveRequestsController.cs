using LeaveManagement.Api.Models;
using LeaveManagement.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeaveManagement.Api.Controllers;

// NOTE: This controller was written quickly for a POC.
// It does data access, business logic and validation all in one place.
// Historical POC note above: these responsibilities now live in LeaveRequestService.
[ApiController]
[Route("api/leave-requests")]
public class LeaveRequestsController : ControllerBase
{
    private readonly LeaveRequestService _service;
    public LeaveRequestsController(LeaveRequestService service) => _service = service;

    // GET /api/leave-requests
    [HttpGet]
    public IActionResult GetAll() => Ok(_service.GetAll());

    // GET /api/leave-requests/search?name=Dana
    // Lets the UI quickly find requests by employee name.
    [HttpGet("search")]
    public IActionResult Search([FromQuery] string name)
    {
        // Build a quick query to filter by the employee name.
        return Ok(_service.GetAll(name));
    }

    // POST /api/leave-requests
    [HttpPost]
    public IActionResult Create([FromBody] CreateLeaveRequestDto dto)
    {
        // How many vacation days has the employee already used this year?
        // Make sure the request does not exceed the quota.
        return Execute(() => _service.Create(dto));
    }

    [HttpPost("{id:int}/approve")]
    public IActionResult Approve(int id) => Execute(() => _service.Approve(id));

    private IActionResult Execute(Func<LeaveRequestResponse> operation)
    {
        try { return Ok(operation()); }
        catch (LeaveRequestException exception)
        {
            var problem = new ProblemDetails { Status = exception.StatusCode, Title = exception.Message };
            return exception.StatusCode switch
            {
                400 => BadRequest(problem),
                404 => NotFound(problem),
                _ => Conflict(problem)
            };
        }
    }
}
