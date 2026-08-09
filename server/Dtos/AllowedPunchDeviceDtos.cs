namespace Server.Dtos;

public record AllowedPunchDeviceDto(int Id, string IpAddress, string Label, DateTime CreatedAt);

public record CreateAllowedPunchDeviceRequest(string IpAddress, string Label);
