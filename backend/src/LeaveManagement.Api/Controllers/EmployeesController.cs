using LeaveManagement.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeaveManagement.Api.Controllers;

// Read-only lookup so the UI can offer an employee picker.
[ApiController]
[Route("api/employees")]
public class EmployeesController : ControllerBase
{
    private readonly EmployeeService _service;

    public EmployeesController(EmployeeService service)
    {
        _service = service;
    }

    // GET /api/employees
    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(_service.GetAll());
    }
}
