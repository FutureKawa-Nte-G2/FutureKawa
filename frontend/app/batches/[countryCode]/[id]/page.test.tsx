import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, waitFor } from "@testing-library/react";
import BatchDetailPage from "./page";
import type { Batch } from "@/lib/api/types";

const mockRegisterRefreshHandler = vi.fn();
const mockGetBatchById = vi.fn();
const mockGetWarehouseMeasurements = vi.fn();

vi.mock("next/navigation", () => ({
  useParams: () => ({ countryCode: "BR", id: "batch-1" }),
  useRouter: () => ({ back: vi.fn(), replace: vi.fn() }),
}));

vi.mock("@/context/AuthContext", () => ({
  useAuth: () => ({ accessToken: "test-token", isAuthenticated: true, isLoading: false }),
}));

vi.mock("@/context/RefreshContext", () => ({
  useRefresh: () => ({ registerRefreshHandler: mockRegisterRefreshHandler, triggerRefresh: vi.fn() }),
}));

vi.mock("@/lib/api/batches", () => ({
  getBatchById: (...args: unknown[]) => mockGetBatchById(...args),
}));

vi.mock("@/lib/api/measurements", () => ({
  getWarehouseMeasurements: (...args: unknown[]) => mockGetWarehouseMeasurements(...args),
  // Not under test here (see measurements.test.ts) — identity is enough to
  // exercise this page's data flow.
  filterMeasurementsByStoragePeriod: (measurements: unknown) => measurements,
}));

// Avoids pulling in Recharts/ResponsiveContainer for a test that only cares
// about the page's refresh-handler registration, not the charts themselves
// (see MeasurementsCharts.test.tsx for that).
vi.mock("@/components/batches/MeasurementsCharts", () => ({
  MeasurementsCharts: () => null,
}));

const batch: Batch = {
  id: "batch-1",
  countryCode: "BR",
  countryName: "Brazil",
  warehouseId: "wh-1",
  warehouseName: "Santos",
  farmName: "Fazenda Cerrado",
  batchRef: "BR-2026-0001",
  qualityGrade: "A",
  status: "compliant",
  enteredAt: "2026-01-01T00:00:00Z",
  shippedAt: null,
};

describe("BatchDetailPage", () => {
  beforeEach(() => {
    mockRegisterRefreshHandler.mockReset();
    mockGetBatchById.mockReset().mockResolvedValue(batch);
    mockGetWarehouseMeasurements.mockReset().mockResolvedValue([]);
  });

  it("registers a refresh handler on mount, and unregisters it on unmount", async () => {
    const { unmount } = render(<BatchDetailPage />);

    await waitFor(() => expect(mockGetBatchById).toHaveBeenCalledTimes(1));
    expect(mockRegisterRefreshHandler).toHaveBeenCalledWith(expect.any(Function));
    expect(mockRegisterRefreshHandler).not.toHaveBeenCalledWith(null);

    unmount();

    expect(mockRegisterRefreshHandler).toHaveBeenLastCalledWith(null);
  });

  // This is the bug fixed in this issue: previously this page never called
  // registerRefreshHandler, so the Navbar's refresh button (which now also
  // triggers a manual measurement collection, see RefreshButton.tsx) had no
  // effect at all while this page was displayed.
  it("re-fetches the batch and its measurements when the registered refresh handler is invoked", async () => {
    render(<BatchDetailPage />);

    await waitFor(() => expect(mockGetBatchById).toHaveBeenCalledTimes(1));
    const registeredHandler = mockRegisterRefreshHandler.mock.calls.find(([handler]) => handler !== null)?.[0];
    expect(registeredHandler).toBeDefined();

    await registeredHandler!();

    expect(mockGetBatchById).toHaveBeenCalledTimes(2);
    expect(mockGetWarehouseMeasurements).toHaveBeenCalledTimes(2);
  });
});
