import { describe, it, expect, vi, afterEach } from "vitest";
import { getUnreadAlerts, resolveAlert } from "./alerts";
import type { AlertItem } from "./types";

function jsonResponse(status: number, data: unknown) {
  return new Response(
    JSON.stringify({ success: status < 400, data, message: null, errors: null }),
    { status }
  );
}

const alert: AlertItem = {
  id: "1",
  alertType: "expired",
  alertStatus: "open",
  batchRef: "BR-2026-0341",
  warehouseName: "Cerrado",
  message: "Le lot BR-2026-0341 a dépassé 365 jours de stockage dans l'entrepôt Cerrado (Brésil).",
  createdAt: "2026-07-27T08:15:00Z",
  resolvedAt: null,
};

describe("getUnreadAlerts", () => {
  afterEach(() => vi.restoreAllMocks());

  it("returns the unwrapped alert list", async () => {
    vi.spyOn(global, "fetch").mockResolvedValueOnce(jsonResponse(200, { alerts: [alert] }));

    expect(await getUnreadAlerts()).toEqual([alert]);
  });

  it("requests GET /api/alerts", async () => {
    const fetchMock = vi
      .spyOn(global, "fetch")
      .mockResolvedValueOnce(jsonResponse(200, { alerts: [] }));

    await getUnreadAlerts();

    const [url, init] = fetchMock.mock.calls[0];
    expect(new URL(url as string).pathname).toBe("/api/alerts");
    expect((init as RequestInit).method).toBe("GET");
  });
});

describe("resolveAlert", () => {
  afterEach(() => vi.restoreAllMocks());

  it("sends a PUT to /api/alerts/:id/resolve", async () => {
    const fetchMock = vi
      .spyOn(global, "fetch")
      .mockResolvedValueOnce(jsonResponse(200, null));

    await resolveAlert("1");

    const [url, init] = fetchMock.mock.calls[0];
    expect(new URL(url as string).pathname).toBe("/api/alerts/1/resolve");
    expect((init as RequestInit).method).toBe("PUT");
  });
});