import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AlertRow } from "./AlertRow";
import type { Alert, AlertBatch } from "@/lib/api/types";

const mockGetAlertBatches = vi.fn();
const mockResolveAlert = vi.fn();

vi.mock("@/lib/api/alerts", () => ({
  getAlertBatches: (id: string) => mockGetAlertBatches(id),
  resolveAlert: (id: string, accessToken?: string | null) => mockResolveAlert(id, accessToken),
}));

vi.mock("@/context/AuthContext", () => ({
  useAuth: () => ({ accessToken: "test-access-token" }),
}));

function makeAlert(overrides: Partial<Alert>): Alert {
  return {
    id: "1",
    warehouseId: "wh-1",
    warehouseName: "Cerrado",
    countryCode: "BR",
    countryName: "Brésil",
    type: "temperature",
    status: "active",
    createdAt: "2026-07-01T07:45:00Z",
    resolvedAt: null,
    measuredAt: "2026-07-01T07:30:00Z",
    ...overrides,
  };
}

function makeAlertBatch(overrides: Partial<AlertBatch>): AlertBatch {
  return {
    id: "b1",
    countryCode: "BR",
    batchRef: "BR-2026-0341",
    farmName: "Fazenda Boa Vista",
    qualityGrade: "A",
    enteredAt: "2026-06-01T00:00:00Z",
    ...overrides,
  };
}

describe("AlertRow", () => {
  beforeEach(() => {
    mockGetAlertBatches.mockReset();
    mockResolveAlert.mockReset().mockResolvedValue(undefined);
  });

  it("displays the warehouse, country, alert date and type", () => {
    render(<AlertRow alert={makeAlert({})} />);

    expect(screen.getByText("Cerrado (Brésil)")).toBeInTheDocument();
    expect(screen.getByText("1 juil. 2026")).toBeInTheDocument();
    expect(screen.getByText("Température")).toBeInTheDocument();
  });

  it("displays a dash for the resolution date while the alert is active", () => {
    render(<AlertRow alert={makeAlert({ status: "active", resolvedAt: null })} />);
    expect(screen.getByText("—")).toBeInTheDocument();
  });

  it("displays the resolution date once resolved", () => {
    render(<AlertRow alert={makeAlert({ status: "resolved", resolvedAt: "2026-07-03T00:00:00Z" })} />);
    expect(screen.getByText("3 juil. 2026")).toBeInTheDocument();
  });

  it("displays the status badge", () => {
    render(<AlertRow alert={makeAlert({ status: "resolved" })} />);
    expect(screen.getByText("résolue")).toBeInTheDocument();
  });

  it("disables the Acquitter button once the alert is resolved and relabels it Acquitté", () => {
    render(<AlertRow alert={makeAlert({ status: "resolved" })} />);
    expect(screen.getByRole("button", { name: "Acquitté" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Acquitter" })).not.toBeInTheDocument();
  });

  it("enables the Acquitter button while the alert is active", () => {
    render(<AlertRow alert={makeAlert({ status: "active" })} />);
    expect(screen.getByRole("button", { name: "Acquitter" })).toBeEnabled();
  });

  it("fetches and displays the affected batches only once expanded", async () => {
    mockGetAlertBatches.mockResolvedValue([makeAlertBatch({})]);
    const user = userEvent.setup();
    render(<AlertRow alert={makeAlert({ id: "alert-1" })} />);

    expect(mockGetAlertBatches).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: /Liste des lots/ }));

    expect(await screen.findByText("BR-2026-0341")).toBeInTheDocument();
    expect(screen.getByText("Fazenda Boa Vista")).toBeInTheDocument();
    expect(mockGetAlertBatches).toHaveBeenCalledWith("alert-1");
    expect(mockGetAlertBatches).toHaveBeenCalledTimes(1);
  });

  it("does not refetch when collapsed and re-expanded", async () => {
    mockGetAlertBatches.mockResolvedValue([makeAlertBatch({})]);
    const user = userEvent.setup();
    render(<AlertRow alert={makeAlert({})} />);

    await user.click(screen.getByRole("button", { name: /Liste des lots/ }));
    await screen.findByText("BR-2026-0341");

    await user.click(screen.getByRole("button", { name: /Liste des lots/ }));
    expect(screen.queryByText("BR-2026-0341")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Liste des lots/ }));
    expect(await screen.findByText("BR-2026-0341")).toBeInTheDocument();
    expect(mockGetAlertBatches).toHaveBeenCalledTimes(1);
  });

  it("shows an empty state when the alert has no affected batches", async () => {
    mockGetAlertBatches.mockResolvedValue([]);
    const user = userEvent.setup();
    render(<AlertRow alert={makeAlert({})} />);

    await user.click(screen.getByRole("button", { name: /Liste des lots/ }));

    expect(await screen.findByText("Aucun lot concerné.")).toBeInTheDocument();
  });

  it("shows an error message when the batches fail to load", async () => {
    mockGetAlertBatches.mockRejectedValue(new Error("network error"));
    const user = userEvent.setup();
    render(<AlertRow alert={makeAlert({})} />);

    await user.click(screen.getByRole("button", { name: /Liste des lots/ }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Impossible de charger les lots concernés.");
  });

  it("resolves the alert and notifies the parent when Acquitter is clicked", async () => {
    const onResolved = vi.fn();
    const user = userEvent.setup();
    render(<AlertRow alert={makeAlert({ id: "alert-1", status: "active" })} onResolved={onResolved} />);

    await user.click(screen.getByRole("button", { name: "Acquitter" }));

    expect(mockResolveAlert).toHaveBeenCalledWith("alert-1", "test-access-token");
    expect(onResolved).toHaveBeenCalledTimes(1);
  });

  it("shows an error message and does not notify the parent when resolving fails", async () => {
    mockResolveAlert.mockRejectedValue(new Error("network error"));
    const onResolved = vi.fn();
    const user = userEvent.setup();
    render(<AlertRow alert={makeAlert({ status: "active" })} onResolved={onResolved} />);

    await user.click(screen.getByRole("button", { name: "Acquitter" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Impossible d'acquitter cette alerte.");
    expect(onResolved).not.toHaveBeenCalled();
  });
});
