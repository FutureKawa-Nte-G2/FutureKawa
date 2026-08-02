import { describe, it, expect, beforeAll } from "vitest";
import type { Batch, BatchListResponse, Country, Warehouse } from "./types";

let getBatches: (params?: {
  countryCode?: string;
  warehouseId?: string;
  page?: number;
  pageSize?: number;
}) => Promise<BatchListResponse>;
let getCountries: () => Promise<Country[]>;
let getWarehouses: (countryCode?: string) => Promise<Warehouse[]>;

beforeAll(async () => {
  process.env.NEXT_PUBLIC_USE_MOCKS = "true";
  ({ getBatches, getCountries, getWarehouses } = await import("./batches"));
});

const ALL_IDS = Array.from({ length: 20 }, (_, i) => String(i + 1));
const BR_W1_IDS = ALL_IDS.filter((id) => Number(id) % 2 === 1); // odd ids
const BR_W2_IDS = ALL_IDS.filter((id) => Number(id) % 2 === 0); // even ids

describe("getBatches", () => {
  it("returns batches sorted oldest-first", async () => {
    // pageSize covers all 20 mock batches so the full sorted set is visible in one page
    const response = await getBatches({ pageSize: 20 });
    expect(response.batches.map((b) => b.id)).toEqual(ALL_IDS);
  });

  it("filters by countryCode", async () => {
    const brazil = await getBatches({ countryCode: "BR" });
    const ecuador = await getBatches({ countryCode: "EC" });

    expect(brazil.totalCount).toBe(20);
    expect(ecuador.totalCount).toBe(0);
  });

  it("filters by warehouseId", async () => {
    const santos = await getBatches({ warehouseId: "BR-W1", pageSize: 20 });
    const cerrado = await getBatches({ warehouseId: "BR-W2", pageSize: 20 });

    expect(santos.batches.map((b) => b.id)).toEqual(BR_W1_IDS);
    expect(cerrado.batches.map((b) => b.id)).toEqual(BR_W2_IDS);
  });

  it("combines countryCode and warehouseId filters", async () => {
    const response = await getBatches({ countryCode: "BR", warehouseId: "BR-W1", pageSize: 20 });
    expect(response.batches.map((b) => b.id)).toEqual(BR_W1_IDS);
  });

  it("defaults to page 1 and pageSize 10 when omitted", async () => {
    const response = await getBatches();
    expect(response.page).toBe(1);
    expect(response.pageSize).toBe(10);
    expect(response.batches).toHaveLength(10);
    expect(response.batches.map((b) => b.id)).toEqual(ALL_IDS.slice(0, 10));
  });

  it("returns only pageSize items and reports correct pagination metadata", async () => {
    const response = await getBatches({ pageSize: 8 });

    expect(response.batches).toHaveLength(8);
    expect(response.batches.map((b) => b.id)).toEqual(ALL_IDS.slice(0, 8));
    expect(response.totalCount).toBe(20);
    expect(response.totalPages).toBe(3);
  });

  it("returns the remaining partial page on the last page", async () => {
    const response = await getBatches({ pageSize: 8, page: 3 });

    expect(response.batches.map((b) => b.id)).toEqual(ALL_IDS.slice(16, 20));
    expect(response.page).toBe(3);
    expect(response.totalPages).toBe(3);
  });

  it("returns an empty batches array with totalPages 1 when the filtered set is empty", async () => {
    const response = await getBatches({ countryCode: "EC" });

    expect(response.batches).toEqual([]);
    expect(response.totalCount).toBe(0);
    expect(response.totalPages).toBe(1);
  });
});

describe("getCountries", () => {
  it("returns the three configured countries", async () => {
    const countries = await getCountries();
    expect(countries.map((c) => c.code)).toEqual(["BR", "EC", "CO"]);
  });
});

describe("getWarehouses", () => {
  it("filters warehouses by country", async () => {
    const brWarehouses = await getWarehouses("BR");
    const ecWarehouses = await getWarehouses("EC");

    expect(brWarehouses.map((w) => w.id)).toEqual(["BR-W1", "BR-W2"]);
    expect(ecWarehouses.map((w) => w.id)).toEqual(["EC-W1"]);
  });

  it("returns every warehouse when no country is given", async () => {
    const warehouses = await getWarehouses();
    expect(warehouses).toHaveLength(4);
  });
});