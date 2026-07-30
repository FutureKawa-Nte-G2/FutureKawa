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
  country: string;
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

// Compliance status of a batch, derived from temperature/humidity thresholds
// and the 365-day storage limit
export type BatchStatus = "compliant" | "alert" | "expired";

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
}