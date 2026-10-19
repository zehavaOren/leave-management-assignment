using System.Text.Json.Serialization;

namespace LeaveManagement.Api.Models;

public class Employee
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Annual paid-vacation quota in days.</summary>
    public int AnnualQuota { get; set; }

    // Back-reference is not serialized: EmployeesController returns this entity
    // directly, and the LeaveRequest <-> Employee navigation is circular.
    [JsonIgnore]
    public List<LeaveRequest> LeaveRequests { get; set; } = new();
}
