namespace FutureKawaSiege.Commons.Models.API.Responses;

public record LoginResponse(string AccessToken, string RefreshToken, UserResponse User);
