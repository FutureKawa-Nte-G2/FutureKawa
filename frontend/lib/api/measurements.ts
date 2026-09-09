import { apiRequest } from "./client";
import type { Measurement } from "./types";

// GET /api/measurements/{warehouseId} — daily temperature/humidity aggregates
// for a single warehouse, sorted newest first by the backend. Used by the
// batch detail page to plot a batch's "Relevés": readings are scoped to the
// batch's warehouse (not the batch itself — see backlog-frontend-mesures-alertes-collecte.md),
// so callers filter the result to the batch's storage period (enteredAt/shippedAt)
// client-side.
export async function getWarehouseMeasurements(
  warehouseId: string,
  accessToken?: string | null
): Promise<Measurement[]> {
  return apiRequest<Measurement[]>(`/api/measurements/${warehouseId}`, { accessToken });
}

// Narrows a warehouse's daily measurements down to a single batch's storage
// window. Measurement.measDate and the batch's enteredAt/shippedAt are both
// ISO 8601 strings (date-only for the former, date-time for the latter) —
// comparing their yyyy-MM-dd prefixes lexicographically is equivalent to
// comparing dates, without needing a date library. shippedAt: null means the
// batch is still in stock, so nothing is excluded on the end side.
export function filterMeasurementsByStoragePeriod(
  measurements: Measurement[],
  batch: { enteredAt: string; shippedAt: string | null }
): Measurement[] {
  const start = batch.enteredAt.slice(0, 10);
  const end = batch.shippedAt?.slice(0, 10) ?? null;
  return measurements.filter((m) => {
    if (m.measDate < start) return false;
    if (end && m.measDate > end) return false;
    return true;
  });
}
