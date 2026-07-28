import { apiRequest } from "./client";
import type { AlertItem, AlertListResponse } from "./types";

import mockAlerts from "./mocks/alerts.json";

const USE_MOCKS = process.env.NEXT_PUBLIC_USE_MOCKS === "true";

// GET /api/alerts — unread alerts only (threshold breach or batch past 365-day expiry).
export async function getUnreadAlerts(): Promise<AlertItem[]> {
  if (USE_MOCKS) {
    return (mockAlerts as AlertListResponse).alerts;
  }
  const { alerts } = await apiRequest<AlertListResponse>("/api/alerts");
  return alerts;
}

// PATCH /api/alerts/:id/read
export async function markAlertAsRead(id: string): Promise<void> {
  if (USE_MOCKS) {
    // No backend in mock mode: the component already removes the item from
    // local state, which is enough for the demo.
    return;
  }
  await apiRequest<void>(`/api/alerts/${id}/read`, { method: "PUT" });
}