import { describe, it, expect, beforeAll } from "vitest";
import type { AlertItem } from "./types";

let getUnreadAlerts: () => Promise<AlertItem[]>;
let markAlertAsRead: (id: string) => Promise<void>;

beforeAll(async () => {
  process.env.NEXT_PUBLIC_USE_MOCKS = "true";
  ({ getUnreadAlerts, markAlertAsRead } = await import("./alerts"));
});

describe("getUnreadAlerts", () => {
  it("returns the mocked unread alerts", async () => {
    const alerts = await getUnreadAlerts();
    expect(alerts.map((a) => a.id)).toEqual(["alert-1", "alert-2"]);
  });

  it("includes both alert kinds from the fixture", async () => {
    const alerts = await getUnreadAlerts();
    expect(alerts.map((a) => a.kind)).toEqual(["expired", "alert"]);
  });
});

describe("markAlertAsRead", () => {
  it("resolves without throwing in mock mode", async () => {
    await expect(markAlertAsRead("alert-1")).resolves.toBeUndefined();
  });
});