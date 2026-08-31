import { describe, it, expect, beforeAll } from "vitest";
import type { AlertItem } from "./types";

let getUnreadAlerts: () => Promise<AlertItem[]>;
let resolveAlert: (id: string) => Promise<void>;

beforeAll(async () => {
  process.env.NEXT_PUBLIC_USE_MOCKS = "true";
  ({ getUnreadAlerts, resolveAlert } = await import("./alerts"));
});

describe("getUnreadAlerts", () => {
  it("returns the mocked open alerts", async () => {
    const alerts = await getUnreadAlerts();
    expect(alerts.map((a) => a.id)).toEqual(["1", "2"]);
  });

  it("includes both alert types from the fixture", async () => {
    const alerts = await getUnreadAlerts();
    expect(alerts.map((a) => a.alertType)).toEqual(["expired", "alert"]);
  });
});

describe("resolveAlert", () => {
  it("resolves without throwing in mock mode", async () => {
    await expect(resolveAlert("1")).resolves.toBeUndefined();
  });
});