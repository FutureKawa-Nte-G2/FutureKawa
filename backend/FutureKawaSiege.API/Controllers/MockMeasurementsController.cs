using FutureKawaSiege.Commons.Models.API.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FutureKawaSiege.API.Controllers;

/// <summary>
/// Mock controller that simulates the local warehouse API by returning fake
/// aggregated measurement data. Only available in the Development environment
/// for testing the measurement sync workflow without a real local API.
/// </summary>
[ApiController]
[Route("api/mock/measurements")]
[ApiExplorerSettings(IgnoreApi = true)]
public class MockMeasurementsController : ControllerBase
{
    private static readonly Random Rng = new();

    /// <summary>
    /// Returns fake aggregated measurement data (avg/min/max temperature and humidity)
    /// with a small random variation around typical coffee storage conditions.
    /// </summary>
    [HttpGet]
    public ActionResult<LocalMeasurementDto> Get()
    {
        var baseTemp = 25.0m;
        var tempVariance = (decimal)Math.Round(Rng.NextDouble() * 4 - 2, 2);
        var baseHumidity = 60.0m;
        var humidityVariance = (decimal)Math.Round(Rng.NextDouble() * 6 - 3, 2);

        var dto = new LocalMeasurementDto
        {
            AvgTemp = baseTemp + tempVariance,
            MaxTemp = baseTemp + tempVariance + 2.5m,
            MinTemp = baseTemp + tempVariance - 2.5m,
            AvgHumidity = baseHumidity + humidityVariance,
            MaxHumidity = baseHumidity + humidityVariance + 4.0m,
            MinHumidity = baseHumidity + humidityVariance - 4.0m,
            MeasDate = DateOnly.FromDateTime(DateTime.UtcNow),
        };

        return Ok(dto);
    }
}