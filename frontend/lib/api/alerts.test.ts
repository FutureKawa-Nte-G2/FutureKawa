import { describe, it, expect, vi } from "vitest";
import { getAlerts, getUnreadAlerts, resolveAlert, getAlertBatches, subscribeToAlertsChange } from "./alerts";

// getAlerts/resolveAlert/getAlertBatches are mock-backed for now (#76, no
// GET /api/alerts server-side yet) but are generated against the real
// getCountries/getWarehouses — mock those two instead of `fetch` directly.
vi.mock("./batches", () => ({
  getCountries: vi.fn().mockResolvedValue([{ code: "BR", name: "Brésil" }]),
  getWarehouses: vi.fn().mockResolvedValue([{ id: "wh-1", name: "Cerrado", countryCode: "BR" }]),
}));

describe("getAlerts", () => {
  it("returns alerts generated for the real warehouses only", async () => {
    const { alerts } = await getAlerts({ pageSize: 100 });

    expect(alerts.length).toBeGreaterThan(0);
    expect(alerts.every((a) => a.warehouseId === "wh-1" && a.countryCode === "BR")).toBe(true);
  });

  it("filters by status", async () => {
    const { alerts } = await getAlerts({ status: "active", pageSize: 100 });

    expect(alerts.every((a) => a.status === "active")).toBe(true);
  });

  it("sorts alerts most-recent first", async () => {
    const { alerts } = await getAlerts({ pageSize: 100 });
    const timestamps = alerts.map((a) => new Date(a.createdAt).getTime());

    expect(timestamps).toEqual([...timestamps].sort((a, b) => b - a));
  });

  it("paginates using page/pageSize/totalCount/totalPages", async () => {
    const all = await getAlerts({ pageSize: 100 });
    const firstPage = await getAlerts({ pageSize: 1, page: 1 });

    expect(firstPage.alerts).toHaveLength(1);
    expect(firstPage.totalCount).toBe(all.alerts.length);
    expect(firstPage.totalPages).toBe(all.alerts.length);
  });
});

describe("resolveAlert", () => {
  it("marks the alert resolved and sets resolvedAt", async () => {
    const { alerts } = await getAlerts({ status: "active", pageSize: 100 });
    const target = alerts[0];

    await resolveAlert(target.id);

    const { alerts: refreshed } = await getAlerts({ pageSize: 100 });
    const updated = refreshed.find((a) => a.id === target.id);
    expect(updated?.status).toBe("resolved");
    expect(updated?.resolvedAt).not.toBeNull();
  });

  it("notifies subscribers", async () => {
    const { alerts } = await getAlerts({ pageSize: 100 });
    const listener = vi.fn();
    const unsubscribe = subscribeToAlertsChange(listener);

    await resolveAlert(alerts[0].id);

    expect(listener).toHaveBeenCalledTimes(1);
    unsubscribe();
  });

  it("does not notify unsubscribed listeners", async () => {
    const { alerts } = await getAlerts({ pageSize: 100 });
    const listener = vi.fn();
    const unsubscribe = subscribeToAlertsChange(listener);
    unsubscribe();

    await resolveAlert(alerts[0].id);

    expect(listener).not.toHaveBeenCalled();
  });
});

describe("getUnreadAlerts", () => {
  it("only returns active alerts", async () => {
    const alerts = await getUnreadAlerts();

    expect(alerts.every((a) => a.status === "active")).toBe(true);
  });
});

describe("getAlertBatches", () => {
  it("returns a non-empty mock batch list for a given alert id", async () => {
    const { alerts } = await getAlerts({ pageSize: 100 });
    const batches = await getAlertBatches(alerts[0].id);

    expect(batches.length).toBeGreaterThan(0);
    expect(batches[0]).toMatchObject({
      batchRef: expect.any(String),
      farmName: expect.any(String),
      qualityGrade: expect.any(String),
    });
  });

  it("is deterministic for the same alert id", async () => {
    const { alerts } = await getAlerts({ pageSize: 100 });
    const first = await getAlertBatches(alerts[0].id);
    const second = await getAlertBatches(alerts[0].id);

    expect(second).toEqual(first);
  });
});
