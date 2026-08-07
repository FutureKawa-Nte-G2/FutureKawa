namespace FutureKawaSiege.Commons.Models.API.Requests;

/// <summary>
/// Payload received from the Odoo ERP webhook when a sales order is confirmed.
/// </summary>
public record OdooOrderWebhookDto
{
    public int OrderId { get; init; }
    public string? Client { get; init; }
    public string? OrderDate { get; init; }
    public string? Country { get; init; }
    public string? QualityGrade { get; init; }
    public IEnumerable<string> BatchReferences { get; init; } = [];
    public IEnumerable<OdooOrderLineDto> Lines { get; init; } = [];
}

public record OdooOrderLineDto
{
    public string? Product { get; init; }
    public decimal Quantity { get; init; }
}