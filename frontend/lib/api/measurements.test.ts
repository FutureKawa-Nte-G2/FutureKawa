import { describe, it, expect, vi, afterEach } from "vitest";
import { filterMeasurementsByStoragePeriod, getWarehouseMeasurements } from "./measurements";
import type { Measurement } from "./types";

function jsonResponse(status: number, data: unknown) {
  return new Response(
    JSON.stringify({ success: status < 400, data, message: null, errors: null }),
    { status }
  );
}

function lastUrl(fetchMock: ReturnType<typeof vi.spyOn>) {
  return new URL(fetchMock.mock.calls.at(-1)![0] as string);
}

describe("getWarehouseMeasurements", () => {
  afterEach(() => vi.restoreAllMocks());

  it("requests /api/measurements/{warehouseId} and returns the unwrapped list", async () => {
    const measurements: Measurement[] = [
      {
        id: "m1",
        warehouseId: "BR-W1",
        warehouseName: "Santos",
        measDate: "2026-01-05",
        avgMeasTemp: 28.5,
        maxMeasTemp: 30.1,
        minMeasTemp: 26.9,
        avgMeasHumidity: 58.2,
        minMeasHumidity: 55.0,
        maxMeasHumidity: 61.4,
      },
    ];
    const fetchMock = vi
      .spyOn(global, "fetch")
      .mockResolvedValueOnce(jsonResponse(200, measurements));

    const result = await getWarehouseMeasurements("BR-W1");

    const url = lastUrl(fetchMock);
    expect(url.pathname).toBe("/api/measurements/BR-W1");
    expect(result).toEqual(measurements);
  });

  it("propagates an ApiError when the backend responds with an error status", async () => {
    vi.spyOn(global, "fetch").mockResolvedValueOnce(jsonResponse(500, null));

    await expect(getWarehouseMeasurements("BR-W1")).rejects.toThrow();
  });
});

describe("filterMeasurementsByStoragePeriod", () => {
  function makeMeasurement(measDate: string): Measurement {
    return {
      id: `m-${measDate}`,
      warehouseId: "BR-W1",
      warehouseName: "Santos",
      measDate,
      avgMeasTemp: 28,
      maxMeasTemp: 29,
      minMeasTemp: 27,
      avgMeasHumidity: 58,
      minMeasHumidity: 56,
      maxMeasHumidity: 60,
    };
  }

  it("keeps only measurements within [enteredAt, shippedAt]", () => {
    const measurements = [
      makeMeasurement("2025-12-30"),
      makeMeasurement("2026-01-01"),
      makeMeasurement("2026-01-05"),
      makeMeasurement("2026-01-10"),
      makeMeasurement("2026-01-15"),
    ];
    const batch = { enteredAt: "2026-01-01T08:00:00Z", shippedAt: "2026-01-10T17:00:00Z" };

    const result = filterMeasurementsByStoragePeriod(measurements, batch);

    expect(result.map((m) => m.measDate)).toEqual(["2026-01-01", "2026-01-05", "2026-01-10"]);
  });

  it("keeps everything from enteredAt onward when the batch hasn't shipped (shippedAt: null)", () => {
    const measurements = [
      makeMeasurement("2025-12-30"),
      makeMeasurement("2026-01-01"),
      makeMeasurement("2026-01-20"),
    ];
    const batch = { enteredAt: "2026-01-01T08:00:00Z", shippedAt: null };

    const result = filterMeasurementsByStoragePeriod(measurements, batch);

    expect(result.map((m) => m.measDate)).toEqual(["2026-01-01", "2026-01-20"]);
  });

  it("returns an empty array when no measurement falls within the storage period", () => {
    const measurements = [makeMeasurement("2025-01-01")];
    const batch = { enteredAt: "2026-01-01T00:00:00Z", shippedAt: null };

    expect(filterMeasurementsByStoragePeriod(measurements, batch)).toEqual([]);
  });
});
