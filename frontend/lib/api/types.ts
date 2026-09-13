// Generic envelope returned by every backend route
export interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  message: string | null;
  errors: string[] | null;
}

// Body sent to POST /api/auth/login
export interface LoginRequest {
  email: string;
  password: string;
}

// User info returned by /login, /refresh and /me
export interface UserResponse {
  id: string;
  email: string;
  role: string;
  warehouseId: string | null;
}

// Data returned by /login and /refresh
// Note: refreshToken is typed here for accuracy (the backend does return it),
// but the frontend must NEVER read or store it — the httpOnly cookie handles it.
export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  user: UserResponse;
}

// --- Batches (FIFO listing) ---

// Fixed set of countries FutureKawa operates in (Brazil, Ecuador, Colombia for the demo)
export interface Country {
  code: string; // ISO country code, e.g. "BR" — used in the /batches/[country]/[id] route
  name: string; // display name, e.g. "Brazil"
}

export interface CountryListResponse {
  countries: Country[];
}

// A warehouse belonging to a country
export interface Warehouse {
  id: string;
  name: string;
  countryCode: string; // FK to Country.code
}

export interface WarehouseListResponse {
  warehouses: Warehouse[];
}

// Compliance status of a batch, based on the 365-day storage limit.
// Temperature/humidity alerts are tracked at warehouse level (see /api/alerts).
export type BatchStatus = 'compliant' | 'alert' | 'expired';

// A stored batch of green coffee, as consolidated and exposed by the HQ backend
// A stored batch of green coffee, as consolidated and exposed by the HQ backend.
// Display fields (countryName, warehouseName, farmName) are provided as-is by the
// backend — the frontend never derives or reformats them from the id/code fields.
export interface Batch {
  id: string; // batch_id_pays — unique within its country only, always paired with countryCode
  countryCode: string; // FK to Country.code, used in the /batches/[country]/[id] route
  countryName: string; // display name
  warehouseId: string; // FK to Warehouse.id, used to filter the list by warehouse
  warehouseName: string; // display name
  farmName: string; // farm/exploitation display name
  batchRef: string; // ERP reference (BATCH.batch_ref), used for reconciliation/traceability
  qualityGrade: string; // quality characteristic set at batch creation (e.g. "Premium", "Robusta")
  status: BatchStatus;
  enteredAt: string; // ISO 8601 date-time — date the batch entered storage, used for FIFO sort
  shippedAt: string | null; // ISO 8601 date-time; null while in stock.
  // Note: the backend already filters out shipped batches from GET /api/batches by default,
  // this field is kept for completeness/potential future audit views.
}

export interface BatchListResponse {
  batches: Batch[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

// --- Measurements (warehouse temperature/humidity daily aggregates) ---
// Mirrors the backend MeasurementResponseDto (GET /api/measurements/{warehouseId}).
// One entry per (warehouse, day) already aggregated (avg/min/max) server-side —
// there is no raw/sub-daily reading exposed by this endpoint. Used by the batch
// detail page ("Relevés") to plot the warehouse's readings during a batch's
// storage period (measDate filtered client-side against Batch.enteredAt/shippedAt).
export interface Measurement {
  id: string;
  warehouseId: string;
  warehouseName: string;
  measDate: string; // ISO 8601 date (no time component — backend DateOnly)
  avgMeasTemp: number;
  maxMeasTemp: number;
  minMeasTemp: number;
  avgMeasHumidity: number;
  minMeasHumidity: number;
  maxMeasHumidity: number;
}

// --- Alerts (warehouse temperature/humidity threshold breaches) ---
// Mirrors the backend Alert entity (#76): WarehouseId only, no BatchId — an
// alert is scoped to a warehouse's readings, never to a specific batch (see
// backlog-batches-alertes-pr.md). GET /api/alerts doesn't exist server-side
// yet (backlog-frontend-mesures-alertes-collecte.md), so this is the target
// contract the frontend mock aligns to, not a confirmed wire format. Enum
// values follow this project's convention of exposing backend enums as
// lowercase strings on the wire (see BatchStatus above); backend enum member
// names are PascalCase (AlertType.Temperature/Humidity, AlertStatus.Active/Resolved).
export type AlertType = 'temperature' | 'humidity';
export type AlertStatus = 'active' | 'resolved';

export interface Alert {
  id: string;
  warehouseId: string; // FK to Warehouse.id, used to filter by warehouse
  warehouseName: string; // display name, consolidated by the backend like Batch.warehouseName
  countryCode: string; // FK to Country.code, used for the Pays filter cascade
  countryName: string; // display name
  type: AlertType;
  status: AlertStatus;
  createdAt: string; // ISO 8601 — when the alert was raised
  resolvedAt: string | null; // ISO 8601; null while active
  measuredAt: string | null; // ISO 8601 — timestamp of the triggering measurement
}

export interface AlertListResponse {
  alerts: Alert[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

// A batch considered "affected" by an alert (the "liste des lots" expand row, #76).
// No real backend source exists yet: Alert carries no BatchId by design (see
// backlog-batches-alertes-pr.md) — the eventual computation is a date-overlap
// query (batch StoredAt/ShippedAt vs alert CreatedAt/ResolvedAt), never
// implemented. Mocked for this issue only; shape may change once that
// endpoint (GET /api/alerts/{id}/batches) is actually built.
export interface AlertBatch {
  id: string;
  countryCode: string;
  batchRef: string;
  farmName: string;
  qualityGrade: string;
  enteredAt: string; // ISO 8601
}
