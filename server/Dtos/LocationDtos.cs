namespace Server.Dtos;

public record LocationDto(
    int Id,
    string Name,
    string Address,
    string LocationCode,
    string Phone,
    string Email,
    bool IsActive);

public record CreateLocationRequest(
    string Name,
    string Address,
    string LocationCode,
    string Phone,
    string Email);

public record UpdateLocationRequest(
    string Name,
    string Address,
    string Phone,
    string Email,
    bool IsActive);
