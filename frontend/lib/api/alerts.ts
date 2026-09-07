import { apiRequest } from "./client";
import type { AlertItem, AlertListResponse } from "./types";

// GET /api/alerts — open alerts only (threshold breach or batch past 365-day expiry).
export async function getUnreadAlerts(accessToken?: string | null): Promise<AlertItem[]> {
  const { alerts } = await apiRequest<AlertListResponse>("/api/alerts", { accessToken });
  return alerts;
}

// PUT /api/alerts/:id/resolve — marks an alert as resolved (alertStatus: "resolved").
export async function resolveAlert(id: string, accessToken?: string | null): Promise<void> {
  await apiRequest<void>(`/api/alerts/${id}/resolve`, { method: "PUT", accessToken });
}