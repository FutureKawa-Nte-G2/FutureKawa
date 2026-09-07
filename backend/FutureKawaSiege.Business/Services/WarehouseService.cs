using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API.Responses;
using FutureKawaSiege.Data.Repositories;

namespace FutureKawaSiege.Business.Services;

public class WarehouseService : IWarehouseService
{
    private readonly IWarehouseRepository _warehouseRepository;

    public WarehouseService(IWarehouseRepository warehouseRepository)
    {
        _warehouseRepository = warehouseRepository;
    }

    /// <inheritdoc/>
    public async Task<WarehouseListResponseDto> GetWarehousesAsync(
        string? countryCode,
        CancellationToken cancellationToken = default)
    {
        var warehouses = string.IsNullOrEmpty(countryCode)
            ? await _warehouseRepository.GetAllAsync(cancellationToken)
            : await _warehouseRepository.GetByCountryCodeAsync(countryCode, cancellationToken);

        return new WarehouseListResponseDto
        {
            Warehouses = warehouses.Select(w => new WarehouseDto
            {
                Id = w.Id,
                Name = w.Name,
                CountryCode = w.Country.Code
            })
        };
    }
}
