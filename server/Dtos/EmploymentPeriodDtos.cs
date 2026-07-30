namespace Server.Dtos;

public record EmploymentPeriodDto(int Id, string HireDate, string? EndDate, string? Notes);

public record CreateEmploymentPeriodRequest(DateOnly HireDate, DateOnly? EndDate, string? Notes);

public record UpdateEmploymentPeriodRequest(DateOnly HireDate, DateOnly? EndDate, string? Notes);
