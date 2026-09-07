using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API;
using FutureKawaSiege.Commons.Models.API.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FutureKawaSiege.API.Controllers;

/// <summary>
/// Controller for accessing countries.
/// All endpoints require JWT authentication.
/// </summary>
[ApiController]
[Route("api/countries")]
[Authorize]
public class CountriesController : ControllerBase
{
    private readonly ICountryService _countryService;
    private readonly ILogger<CountriesController> _logger;

    public CountriesController(
        ICountryService countryService,
        ILogger<CountriesController> logger)
    {
        _countryService = countryService;
        _logger = logger;
    }

    /// <summary>
    /// Returns all countries where FutureKawa operates.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<CountryListResponseDto>>> GetAll(
        CancellationToken cancellationToken)
    {
        var countries = await _countryService.GetCountriesAsync(cancellationToken);
        return Ok(ApiResponse<CountryListResponseDto>.Ok(countries));
    }
}
