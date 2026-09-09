import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AlertRow } from "./AlertRow";
import type { Alert } from "@/lib/api/types";

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

describe("AlertRow", () => {
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

  it("disables the Acquitter button once the alert is resolved", () => {
    render(<AlertRow alert={makeAlert({ status: "resolved" })} />);
    expect(screen.getByRole("button", { name: "Acquitter" })).toBeDisabled();
  });

  it("enables the Acquitter button while the alert is active", () => {
    render(<AlertRow alert={makeAlert({ status: "active" })} />);
    expect(screen.getByRole("button", { name: "Acquitter" })).toBeEnabled();
  });

  it("toggles the batches panel when 'Liste des lots' is clicked", async () => {
    const user = userEvent.setup();
    render(<AlertRow alert={makeAlert({})} />);

    expect(screen.queryByText(/Chargement des lots/)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Liste des lots/ }));
    expect(screen.getByText(/Chargement des lots/)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Liste des lots/ }));
    expect(screen.queryByText(/Chargement des lots/)).not.toBeInTheDocument();
  });
});
