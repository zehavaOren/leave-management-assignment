namespace LeaveManagement.Api.Models;

// Incoming payload for creating a leave request.
public class CreateLeaveRequestDto
{
    public int EmployeeId { get; set; }
    [System.ComponentModel.DataAnnotations.Required]
    public LeaveType? Type { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public sealed record EmployeeResponse(int Id, string Name, int AnnualQuota);
public sealed record LeaveRequestResponse(int Id, int EmployeeId, EmployeeResponse? Employee,
    LeaveType Type, DateTime StartDate, DateTime EndDate, LeaveStatus Status, int Days);

