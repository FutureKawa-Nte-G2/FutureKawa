import { apiRequest } from "./client";
import type {
  Batch,
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

interface GetBatchesParams {
  countryCode?: string;
  warehouseId?: string;
}

// GET /api/batches — batches still in stock, sorted oldest-first (FIFO).
// The real endpoint filters out shipped batches and applies sort=entered_at_asc
// server-side; the mock replicates both behaviors so the UI sees identical data shapes.
export async function getBatches(params: GetBatchesParams = {}): Promise<Batch[]> {
  if (USE_MOCKS) {
    const { batches } = mockBatches as BatchListResponse;
    return batches
      .filter(
        (batch) =>
          (!params.countryCode || batch.countryCode === params.countryCode) &&
          (!params.warehouseId || batch.warehouseId === params.warehouseId)
      )
      .sort((a, b) => a.enteredAt.localeCompare(b.enteredAt));
  }

  const query = new URLSearchParams({ sort: "entered_at_asc" });
  if (params.countryCode) query.set("country", params.countryCode);
  if (params.warehouseId) query.set("warehouseId", params.warehouseId);

  const { batches } = await apiRequest<BatchListResponse>(`/api/batches?${query}`);
  return batches;
}

// GET /api/countries
export async function getCountries(): Promise<Country[]> {
  if (USE_MOCKS) {
    return (mockCountries as CountryListResponse).countries;
  }
  const { countries } = await apiRequest<CountryListResponse>("/api/countries");
  return countries;
}

// GET /api/warehouses?country=BR
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