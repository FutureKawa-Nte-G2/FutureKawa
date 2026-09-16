namespace FutureKawaSiege.Commons.Models.API.Responses;

public record UserResponse(
    Guid Id,
    string Email,
    string Role,
    Guid? WarehouseId);
