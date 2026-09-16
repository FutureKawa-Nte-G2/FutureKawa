import { describe, it, expect, vi, afterEach } from "vitest";
import { getAlerts, getUnreadAlerts, resolveAlert, getAlertBatches, subscribeToAlertsChange } from "./alerts";
import type { Alert, AlertBatch } from "./types";

function jsonResponse(status: number, data: unknown) {
  return new Response(
    JSON.stringify({ success: status < 400, data, message: null, errors: null }),
    { status }
  );
}

function lastUrl(fetchMock: ReturnType<typeof vi.spyOn>) {
  return new URL(fetchMock.mock.calls.at(-1)![0] as string);
}

function lastRequestInit(fetchMock: ReturnType<typeof vi.spyOn>) {
  return fetchMock.mock.calls.at(-1)![1] as RequestInit;
}

const alert: Alert = {
  id: "1",
  warehouseId: "wh-1",
  warehouseName: "Cerrado",
  countryCode: "BR",
  countryName: "Brazil",
  type: "temperature",
  status: "active",
  createdAt: "2026-07-27T08:15:00Z",
  resolvedAt: null,
  measuredAt: "2026-07-27T08:00:00Z",
};

describe("getAlerts", () => {
  afterEach(() => vi.restoreAllMocks());

  it("requests page 1 / pageSize 10 by default, with no filters", async () => {
    const fetchMock = vi
      .spyOn(global, "fetch")
      .mockResolvedValueOnce(
        jsonResponse(200, { alerts: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 1 })
      );

    await getAlerts();

    const url = lastUrl(fetchMock);
    expect(url.pathname).toBe("/api/alerts");
    expect(url.searchParams.get("page")).toBe("1");
    expect(url.searchParams.get("pageSize")).toBe("10");
    expect(url.searchParams.has("country")).toBe(false);
    expect(url.searchParams.has("warehouseId")).toBe(false);
    expect(url.searchParams.has("status")).toBe(false);
  });

  it("forwards country, warehouseId and status filters, and custom page/pageSize", async () => {
    const fetchMock = vi
      .spyOn(global, "fetch")
      .mockResolvedValueOnce(
        jsonResponse(200, { alerts: [], page: 2, pageSize: 5, totalCount: 0, totalPages: 1 })
      );

    await getAlerts({
      countryCode: "BR",
      warehouseId: "wh-1",
      status: "active",
      page: 2,
      pageSize: 5,
    });

    const url = lastUrl(fetchMock);
    expect(url.searchParams.get("country")).toBe("BR");
    expect(url.searchParams.get("warehouseId")).toBe("wh-1");
    expect(url.searchParams.get("status")).toBe("active");
    expect(url.searchParams.get("page")).toBe("2");
    expect(url.searchParams.get("pageSize")).toBe("5");
  });

  it("returns the unwrapped alert list data", async () => {
    vi.spyOn(global, "fetch").mockResolvedValueOnce(
      jsonResponse(200, { alerts: [alert], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 })
    );

    const response = await getAlerts();

    expect(response.alerts).toEqual([alert]);
    expect(response.totalCount).toBe(1);
  });

  it("propagates an ApiError when the backend responds with an error status", async () => {
    vi.spyOn(global, "fetch").mockResolvedValueOnce(jsonResponse(500, null));

    await expect(getAlerts()).rejects.toThrow();
  });
});

describe("getUnreadAlerts", () => {
  afterEach(() => vi.restoreAllMocks());

  it("requests active alerts only, with a page size of 50", async () => {
    const fetchMock = vi
      .spyOn(global, "fetch")
      .mockResolvedValueOnce(
        jsonResponse(200, { alerts: [alert], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 })
      );

    const alerts = await getUnreadAlerts("test-token");

    const url = lastUrl(fetchMock);
    expect(url.searchParams.get("status")).toBe("active");
    expect(url.searchParams.get("pageSize")).toBe("50");
    expect(alerts).toEqual([alert]);
  });
});

describe("resolveAlert", () => {
  afterEach(() => vi.restoreAllMocks());

  it("sends a PATCH to /api/alerts/:id/resolve with the access token", async () => {
    const fetchMock = vi.spyOn(global, "fetch").mockResolvedValueOnce(jsonResponse(200, "ok"));

    await resolveAlert("1", "test-token");

    const url = lastUrl(fetchMock);
    const init = lastRequestInit(fetchMock);
    expect(url.pathname).toBe("/api/alerts/1/resolve");
    expect(init.method).toBe("PATCH");
    expect((init.headers as Record<string, string>)["Authorization"]).toBe("Bearer test-token");
  });

  it("notifies subscribers once the resolve succeeds", async () => {
    vi.spyOn(global, "fetch").mockResolvedValueOnce(jsonResponse(200, "ok"));
    const listener = vi.fn();
    const unsubscribe = subscribeToAlertsChange(listener);

    await resolveAlert("1", "test-token");

    expect(listener).toHaveBeenCalledTimes(1);
    unsubscribe();
  });

  it("does not notify listeners that already unsubscribed", async () => {
    vi.spyOn(global, "fetch").mockResolvedValueOnce(jsonResponse(200, "ok"));
    const listener = vi.fn();
    const unsubscribe = subscribeToAlertsChange(listener);
    unsubscribe();

    await resolveAlert("1", "test-token");

    expect(listener).not.toHaveBeenCalled();
  });

  it("does not notify subscribers when the request fails", async () => {
    vi.spyOn(global, "fetch").mockResolvedValueOnce(jsonResponse(500, null));
    const listener = vi.fn();
    const unsubscribe = subscribeToAlertsChange(listener);

    await expect(resolveAlert("1", "test-token")).rejects.toThrow();

    expect(listener).not.toHaveBeenCalled();
    unsubscribe();
  });
});

describe("getAlertBatches", () => {
  afterEach(() => vi.restoreAllMocks());

  it("requests /api/alerts/:id/batches and returns the unwrapped list", async () => {
    const batch: AlertBatch = {
      id: "1",
      countryCode: "BR",
      batchRef: "BR-2026-0001",
      farmName: "Fazenda Cerrado",
      qualityGrade: "A",
      enteredAt: "2026-01-01T00:00:00Z",
    };
    const fetchMock = vi.spyOn(global, "fetch").mockResolvedValueOnce(jsonResponse(200, [batch]));

    const result = await getAlertBatches("1", "test-token");

    const url = lastUrl(fetchMock);
    expect(url.pathname).toBe("/api/alerts/1/batches");
    expect(result).toEqual([batch]);
  });

  it("propagates an ApiError when the alert doesn't exist", async () => {
    vi.spyOn(global, "fetch").mockResolvedValueOnce(jsonResponse(404, null));

    await expect(getAlertBatches("unknown")).rejects.toThrow();
  });
});
