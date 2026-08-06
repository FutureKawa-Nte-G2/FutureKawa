import { apiRequest } from "./client";
import type {
  BatchListResponse,
  Country,
  CountryListResponse,
  Warehouse,
  WarehouseListResponse,
} from "./types";

import mockBatches from "./mocks/batches.json";
import mockCountries from "./mocks/countries.json";
import mockWarehouses from "./mocks/warehouses.json";

const USE_MOCKS = process.env.NEXT_PUBLIC_USE_MOCKS === "true";

const DEFAULT_PAGE = 1;
const DEFAULT_PAGE_SIZE = 10;

interface GetBatchesParams {
  countryCode?: string;
  warehouseId?: string;
  page?: number;
  pageSize?: number;
}

// GET /api/batches — a single page of batches still in stock, sorted oldest-first (FIFO).
// Pagination is enforced server-side: the backend returns only the requested page,
// never the full dataset. page defaults to 1, pageSize to 10 when omitted.
export async function getBatches(params: GetBatchesParams = {}): Promise<BatchListResponse> {
  const page = params.page ?? DEFAULT_PAGE;
  const pageSize = params.pageSize ?? DEFAULT_PAGE_SIZE;

  if (USE_MOCKS) {
    const { batches } = mockBatches as { batches: BatchListResponse["batches"] };

    const filtered = batches
      .filter(
        (batch) =>
          (!params.countryCode || batch.countryCode === params.countryCode) &&
          (!params.warehouseId || batch.warehouseId === params.warehouseId)
      )
      .sort((a, b) => a.enteredAt.localeCompare(b.enteredAt));

    const totalCount = filtered.length;
    const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
    const start = (page - 1) * pageSize;
    const pageItems = filtered.slice(start, start + pageSize);

    return { batches: pageItems, page, pageSize, totalCount, totalPages };
  }

  const query = new URLSearchParams({
    sort: "entered_at_asc",
    page: String(page),
    pageSize: String(pageSize),
  });
  if (params.countryCode) query.set("country", params.countryCode);
  if (params.warehouseId) query.set("warehouseId", params.warehouseId);

  return apiRequest<BatchListResponse>(`/api/batches?${query}`);
}

// GET /api/countries — small, fixed dataset, no pagination
export async function getCountries(): Promise<Country[]> {
  if (USE_MOCKS) {
    return (mockCountries as CountryListResponse).countries;
  }
  const { countries } = await apiRequest<CountryListResponse>("/api/countries");
  return countries;
}

// GET /api/warehouses?country=BR — small, fixed dataset, no pagination
export async function getWarehouses(countryCode?: string): Promise<Warehouse[]> {
  if (USE_MOCKS) {
    const { warehouses } = mockWarehouses as WarehouseListResponse;
    return countryCode
      ? warehouses.filter((warehouse) => warehouse.countryCode === countryCode)
      : warehouses;
  }

  const query = countryCode ? `?country=${countryCode}` : "";
  const { warehouses } = await apiRequest<WarehouseListResponse>(`/api/warehouses${query}`);
  return warehouses;
}