namespace FutureKawaSiege.Commons.Models.API.Responses;

/// <summary>
/// Response DTO for a batch considered affected by an alert (#85). Computed via a
/// date-overlap query, not a stored relationship: Alert carries no BatchId by
/// design (see backlog-batches-alertes-pr.md) — a batch is included when its
/// storage period at the alert's warehouse overlaps the alert's active period.
/// </summary>
public record AlertBatchDto
{
    public Guid Id { get; init; }
    public string CountryCode { get; init; } = null!;
    public string BatchRef { get; init; } = null!;
    public string FarmName { get; init; } = null!;
    public string QualityGrade { get; init; } = null!;
    public DateTime EnteredAt { get; init; }
}
