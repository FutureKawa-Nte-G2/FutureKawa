import { describe, it, expect, vi, afterEach } from "vitest";
import { getBatches, getCountries, getWarehouses } from "./batches";
import type { Batch, Country, Warehouse } from "./types";

function jsonResponse(status: number, data: unknown) {
  return new Response(
    JSON.stringify({ success: status < 400, data, message: null, errors: null }),
    { status }
  );
}

function lastUrl(fetchMock: ReturnType<typeof vi.spyOn>) {
  return new URL(fetchMock.mock.calls.at(-1)![0] as string);
}

describe("getBatches", () => {
  afterEach(() => vi.restoreAllMocks());

  it("requests page 1 / pageSize 10 by default, with no country/warehouse filters", async () => {
    const fetchMock = vi
      .spyOn(global, "fetch")
      .mockResolvedValueOnce(
        jsonResponse(200, { batches: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 1 })
      );

    await getBatches();

    const url = lastUrl(fetchMock);
    expect(url.pathname).toBe("/api/batches");
    expect(url.searchParams.get("page")).toBe("1");
    expect(url.searchParams.get("pageSize")).toBe("10");
    expect(url.searchParams.has("country")).toBe(false);
    expect(url.searchParams.has("warehouseId")).toBe(false);
  });

  it("forwards country and warehouseId filters, and custom page/pageSize", async () => {
    const fetchMock = vi
      .spyOn(global, "fetch")
      .mockResolvedValueOnce(
        jsonResponse(200, { batches: [], page: 2, pageSize: 5, totalCount: 0, totalPages: 1 })
      );

    await getBatches({ countryCode: "BR", warehouseId: "BR-W1", page: 2, pageSize: 5 });

    const url = lastUrl(fetchMock);
    expect(url.searchParams.get("country")).toBe("BR");
    expect(url.searchParams.get("warehouseId")).toBe("BR-W1");
    expect(url.searchParams.get("page")).toBe("2");
    expect(url.searchParams.get("pageSize")).toBe("5");
  });

  it("returns the unwrapped batch list data", async () => {
    const batch: Batch = {
      id: "1",
      countryCode: "BR",
      countryName: "Brazil",
      warehouseId: "BR-W1",
      warehouseName: "Santos",
      farmName: "Fazenda Cerrado",
      batchRef: "BR-2026-0001",
      qualityGrade: "A",
      status: "compliant",
      enteredAt: "2026-01-01T00:00:00Z",
      shippedAt: null,
    };
    vi.spyOn(global, "fetch").mockResolvedValueOnce(
      jsonResponse(200, { batches: [batch], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 })
    );

    const response = await getBatches();

    expect(response.batches).toEqual([batch]);
    expect(response.totalCount).toBe(1);
  });

  it("propagates an ApiError when the backend responds with an error status", async () => {
    vi.spyOn(global, "fetch").mockResolvedValueOnce(jsonResponse(500, null));

    await expect(getBatches()).rejects.toThrow();
  });
});

describe("getCountries", () => {
  afterEach(() => vi.restoreAllMocks());

  it("returns the unwrapped country list", async () => {
    const countries: Country[] = [{ code: "BR", name: "Brazil" }];
    vi.spyOn(global, "fetch").mockResolvedValueOnce(jsonResponse(200, { countries }));

    expect(await getCountries()).toEqual(countries);
  });
});

describe("getWarehouses", () => {
  afterEach(() => vi.restoreAllMocks());

  it("requests /api/warehouses without a country param when none is given", async () => {
    const fetchMock = vi
      .spyOn(global, "fetch")
      .mockResolvedValueOnce(jsonResponse(200, { warehouses: [] }));

    await getWarehouses();

    expect(lastUrl(fetchMock).searchParams.has("country")).toBe(false);
  });

  it("filters by country when given", async () => {
    const warehouses: Warehouse[] = [{ id: "BR-W1", name: "Santos", countryCode: "BR" }];
    const fetchMock = vi
      .spyOn(global, "fetch")
      .mockResolvedValueOnce(jsonResponse(200, { warehouses }));

    const response = await getWarehouses("BR");

    expect(lastUrl(fetchMock).searchParams.get("country")).toBe("BR");
    expect(response).toEqual(warehouses);
  });
});