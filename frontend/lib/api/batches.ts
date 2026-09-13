import { apiRequest } from "./client";
import type {
  Batch,
  BatchListResponse,
  Country,
  CountryListResponse,
  Warehouse,
  WarehouseListResponse,
} from "./types";

const DEFAULT_PAGE = 1;
const DEFAULT_PAGE_SIZE = 10;

interface GetBatchesParams {
  countryCode?: string;
  warehouseId?: string;
  page?: number;
  pageSize?: number;
  accessToken?: string | null;
}

// GET /api/batches — a single page of batches still in stock, sorted oldest-first (FIFO).
// Pagination is enforced server-side: the backend returns only the requested page,
export async function getBatches(params: GetBatchesParams = {}): Promise<BatchListResponse> {
  const page = params.page ?? DEFAULT_PAGE;
  const pageSize = params.pageSize ?? DEFAULT_PAGE_SIZE;

  // No `sort` param: the real contract (BatchesController.GetAll) doesn't accept
  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });
  if (params.countryCode) query.set("country", params.countryCode);
  if (params.warehouseId) query.set("warehouseId", params.warehouseId);

  return apiRequest<BatchListResponse>(`/api/batches?${query}`, {
    accessToken: params.accessToken,
  });
}

// GET /api/batches/{id} — a single batch, regardless of shipped status.
// Used by the batch detail page (measurement curves) to resolve the batch's
// warehouse and storage period (enteredAt/shippedAt).
export async function getBatchById(id: string, accessToken?: string | null): Promise<Batch> {
  return apiRequest<Batch>(`/api/batches/${id}`, { accessToken });
}

// GET /api/countries — small, fixed dataset, no pagination
export async function getCountries(accessToken?: string | null): Promise<Country[]> {
  const { countries } = await apiRequest<CountryListResponse>("/api/countries", { accessToken });
  return countries;
}

// GET /api/warehouses?country=BR — small, fixed dataset, no pagination
export async function getWarehouses(
  countryCode?: string,
  accessToken?: string | null
): Promise<Warehouse[]> {
  const query = countryCode ? `?country=${countryCode}` : "";
  const { warehouses } = await apiRequest<WarehouseListResponse>(`/api/warehouses${query}`, {
    accessToken,
  });
  return warehouses;
}