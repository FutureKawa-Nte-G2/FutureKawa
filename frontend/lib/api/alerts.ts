import { apiRequest } from "./client";
import type { Alert, AlertBatch, AlertListResponse, AlertStatus } from "./types";

const DEFAULT_PAGE = 1;
const DEFAULT_PAGE_SIZE = 10;

export interface GetAlertsParams {
  countryCode?: string;
  warehouseId?: string;
  status?: AlertStatus;
  page?: number;
  pageSize?: number;
  accessToken?: string | null;
}

type AlertsChangeListener = () => void;
const alertsChangeListeners = new Set<AlertsChangeListener>();

// Lets components outside the /alertes page (e.g. AlertButton's bell in the
// navbar) know an alert was resolved elsewhere, so they can refetch their own
// view instead of going stale until their next mount.
export function subscribeToAlertsChange(listener: AlertsChangeListener): () => void {
  alertsChangeListeners.add(listener);
  return () => alertsChangeListeners.delete(listener);
}

function notifyAlertsChange(): void {
  alertsChangeListeners.forEach((listener) => listener());
}

// GET /api/alerts — a single page of alerts, most recent first (#76/#85).
// Pagination is enforced server-side: the backend returns only the requested page.
export async function getAlerts(params: GetAlertsParams = {}): Promise<AlertListResponse> {
  const page = params.page ?? DEFAULT_PAGE;
  const pageSize = params.pageSize ?? DEFAULT_PAGE_SIZE;

  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });
  if (params.countryCode) query.set("country", params.countryCode);
  if (params.warehouseId) query.set("warehouseId", params.warehouseId);
  if (params.status) query.set("status", params.status);

  return apiRequest<AlertListResponse>(`/api/alerts?${query}`, {
    accessToken: params.accessToken,
  });
}

// Convenience wrapper used by AlertButton's bell (active alerts only).
export async function getUnreadAlerts(accessToken?: string | null): Promise<Alert[]> {
  const { alerts } = await getAlerts({ status: "active", pageSize: 50, accessToken });
  return alerts;
}

// PATCH /api/alerts/:id/resolve
export async function resolveAlert(id: string, accessToken?: string | null): Promise<void> {
  await apiRequest<string>(`/api/alerts/${id}/resolve`, {
    method: "PATCH",
    accessToken,
  });
  notifyAlertsChange();
}

// GET /api/alerts/:id/batches
export async function getAlertBatches(alertId: string, accessToken?: string | null): Promise<AlertBatch[]> {
  return apiRequest<AlertBatch[]>(`/api/alerts/${alertId}/batches`, {
    accessToken,
  });
}
