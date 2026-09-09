import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { AlertTable } from "./AlertTable";
import type { Alert } from "@/lib/api/types";

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
    createdAt: "2026-01-01T00:00:00Z",
    resolvedAt: null,
    measuredAt: null,
    ...overrides,
  };
}

describe("AlertTable", () => {
  it("shows an empty state message when there are no alerts", () => {
    render(<AlertTable alerts={[]} />);
    expect(screen.getByText("Aucune alerte pour cette sélection.")).toBeInTheDocument();
  });

  it("renders the column headers", () => {
    render(<AlertTable alerts={[makeAlert({})]} />);

    ["Entrepôt", "Date alerte", "Date résolution", "Statut", "Type d'alerte", "Lots concernés", "Actions"].forEach(
      (label) => {
        expect(screen.getByText(label)).toBeInTheDocument();
      }
    );
  });

  it("renders one row per alert, preserving the given order", () => {
    const alerts = [
      makeAlert({ id: "1", warehouseName: "Cerrado" }),
      makeAlert({ id: "2", warehouseName: "Santos" }),
      makeAlert({ id: "3", warehouseName: "Quito" }),
    ];

    render(<AlertTable alerts={alerts} />);

    const names = screen.getAllByText(/^(Cerrado|Santos|Quito) \(Brésil\)$/).map((el) => el.textContent);
    expect(names).toEqual(["Cerrado (Brésil)", "Santos (Brésil)", "Quito (Brésil)"]);
  });
});
