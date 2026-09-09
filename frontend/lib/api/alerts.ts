import { getCountries, getWarehouses } from "./batches";
import type { Alert, AlertBatch, AlertListResponse, AlertStatus, AlertType } from "./types";

// GET /api/alerts, PATCH /api/alerts/:id/resolve and GET /api/alerts/:id/batches
// don't exist on the backend yet 
const MOCK_LATENCY_MS = 250;
const DAY_MS = 24 * 60 * 60 * 1000;
const ALERT_TYPES: AlertType[] = ["temperature", "humidity"];
const QUALITY_GRADES = ["A", "B", "C"];
const FARM_NAMES = ["Fazenda Boa Vista", "Fazenda Serra Alta", "Fazenda Rio Verde"];

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

// Tiny deterministic hash + PRNG (mulberry32), seeded from an id string
function hashString(input: string): number {
  let hash = 0;
  for (let i = 0; i < input.length; i++) {
    hash = (hash * 31 + input.charCodeAt(i)) | 0;
  }
  return Math.abs(hash);
}

function mulberry32(seed: number): () => number {
  let a = seed;
  return function random() {
    a |= 0;
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// Module-level mock store, regenerated whenever the real warehouse set changes (cacheKey)
let mockAlerts: Alert[] | null = null;
let mockAlertsCacheKey: string | null = null;

async function ensureMockAlerts(accessToken?: string | null): Promise<Alert[]> {
  const countries = await getCountries(accessToken);
  const warehousesByCountry = await Promise.all(
    countries.map((country) => getWarehouses(country.code, accessToken))
  );
  const warehouses = warehousesByCountry.flat();
  const cacheKey = warehouses
    .map((w) => w.id)
    .sort()
    .join(",");

  if (mockAlerts && mockAlertsCacheKey === cacheKey) {
    return mockAlerts;
  }

  const countryNameByCode = new Map(countries.map((c) => [c.code, c.name]));
  mockAlerts = warehouses.flatMap((warehouse) =>
    generateMockAlertsForWarehouse(warehouse, countryNameByCode.get(warehouse.countryCode) ?? warehouse.countryCode)
  );
  mockAlertsCacheKey = cacheKey;
  return mockAlerts;
}

function generateMockAlertsForWarehouse(
  warehouse: { id: string; name: string; countryCode: string },
  countryName: string
): Alert[] {
  const rng = mulberry32(hashString(warehouse.id));
  const alertCount = 2 + Math.floor(rng() * 2); // 2-3 alerts per warehouse
  const now = Date.now();

  return Array.from({ length: alertCount }, (_, i) => {
    const type = ALERT_TYPES[Math.floor(rng() * ALERT_TYPES.length)];
    const isResolved = rng() > 0.5;
    // Resolved alerts are seeded further in the past than active ones, so a
    // status filter reads as plausible (active alerts are recent, unresolved).
    const createdDaysAgo = isResolved ? 5 + rng() * 20 : rng() * 4;
    const createdAt = new Date(now - createdDaysAgo * DAY_MS);
    const measuredAt = new Date(createdAt.getTime() - Math.floor(rng() * 3) * 60 * 60 * 1000);
    const resolvedAt = isResolved ? new Date(createdAt.getTime() + (1 + rng() * 3) * DAY_MS) : null;

    return {
      id: `mock-alert-${warehouse.id}-${i}`,
      warehouseId: warehouse.id,
      warehouseName: warehouse.name,
      countryCode: warehouse.countryCode,
      countryName,
      type,
      status: (isResolved ? "resolved" : "active") as AlertStatus,
      createdAt: createdAt.toISOString(),
      resolvedAt: resolvedAt ? resolvedAt.toISOString() : null,
      measuredAt: measuredAt.toISOString(),
    };
  });
}

export interface GetAlertsParams {
  countryCode?: string;
  warehouseId?: string;
  status?: AlertStatus;
  page?: number;
  pageSize?: number;
  accessToken?: string | null;
}

const DEFAULT_PAGE = 1;
const DEFAULT_PAGE_SIZE = 10;

// alerts are expected to stay unresolved and should surface first (#76).
export async function getAlerts(params: GetAlertsParams = {}): Promise<AlertListResponse> {
  const page = params.page ?? DEFAULT_PAGE;
  const pageSize = params.pageSize ?? DEFAULT_PAGE_SIZE;

  await delay(MOCK_LATENCY_MS);
  const all = await ensureMockAlerts(params.accessToken);

  const filtered = all
    .filter((a) => !params.countryCode || a.countryCode === params.countryCode)
    .filter((a) => !params.warehouseId || a.warehouseId === params.warehouseId)
    .filter((a) => !params.status || a.status === params.status)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt));

  const start = (page - 1) * pageSize;
  const pageItems = filtered.slice(start, start + pageSize);

  return {
    alerts: pageItems,
    page,
    pageSize,
    totalCount: filtered.length,
    totalPages: Math.max(1, Math.ceil(filtered.length / pageSize)),
  };
}

// Convenience wrapper used by AlertButton's bell (active alerts only).
export async function getUnreadAlerts(accessToken?: string | null): Promise<Alert[]> {
  const { alerts } = await getAlerts({ status: "active", pageSize: 50, accessToken });
  return alerts;
}

// Target contract: PATCH /api/alerts/:id/resolve
export async function resolveAlert(id: string, accessToken?: string | null): Promise<void> {
  await delay(MOCK_LATENCY_MS);
  const all = await ensureMockAlerts(accessToken);
  const alert = all.find((a) => a.id === id);
  if (alert) {
    alert.status = "resolved";
    alert.resolvedAt = new Date().toISOString();
  }
}

// Target contract: GET /api/alerts/:id/batches — see the AlertBatch doc comment
export async function getAlertBatches(alertId: string, accessToken?: string | null): Promise<AlertBatch[]> {
  await delay(MOCK_LATENCY_MS);
  const all = await ensureMockAlerts(accessToken);
  const alert = all.find((a) => a.id === alertId);
  const countryCode = alert?.countryCode ?? "BR";
  const reference = alert ? new Date(alert.createdAt).getTime() : Date.now();

  const rng = mulberry32(hashString(alertId));
  const count = 1 + Math.floor(rng() * 4);

  return Array.from({ length: count }, (_, i) => ({
    id: `mock-alert-batch-${alertId}-${i}`,
    countryCode,
    batchRef: `${countryCode}-2026-${1000 + Math.floor(rng() * 9000)}`,
    farmName: FARM_NAMES[Math.floor(rng() * FARM_NAMES.length)],
    qualityGrade: QUALITY_GRADES[Math.floor(rng() * QUALITY_GRADES.length)],
    enteredAt: new Date(reference - Math.floor(rng() * 60) * DAY_MS).toISOString(),
  }));
}
