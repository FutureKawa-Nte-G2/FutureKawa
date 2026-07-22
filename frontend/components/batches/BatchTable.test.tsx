import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { BatchTable } from "./BatchTable";
import type { Batch } from "@/lib/api/types";

// BatchRow uses useRouter for navigation; only its presence is required here, not its behavior.
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

function makeBatch(overrides: Partial<Batch>): Batch {
  return {
    id: "1",
    countryCode: "BR",
    warehouseId: "BR-W1",
    batchRef: "ERP-BR-2026-0001",
    farm: "Fazenda Test",
    warehouse: "Test Warehouse",
    enteredAt: "2026-01-01T00:00:00Z",
    status: "compliant",
    shippedAt: null,
    ...overrides,
  };
}

describe("BatchTable", () => {
  it("shows an empty state message when there are no batches", () => {
    render(<BatchTable batches={[]} />);
    expect(screen.getByText("Aucun lot en stock pour cette sélection.")).toBeInTheDocument();
  });

  it("renders the column headers", () => {
    render(<BatchTable batches={[makeBatch({})]} />);

    ["Id lot", "Exploitation", "Entrepôt", "Date de stockage", "Statut", "Actions"].forEach((label) => {
      expect(screen.getByText(label)).toBeInTheDocument();
    });
  });

  it("renders one row per batch, preserving the given order", () => {
    const batches = [
      makeBatch({ id: "1", batchRef: "ERP-BR-2026-0001" }),
      makeBatch({ id: "2", batchRef: "ERP-BR-2026-0002" }),
      makeBatch({ id: "3", batchRef: "ERP-BR-2026-0003" }),
    ];

    render(<BatchTable batches={batches} />);

    const refs = screen.getAllByText(/ERP-BR-2026-000\d/).map((el) => el.textContent);
    expect(refs).toEqual(["ERP-BR-2026-0001", "ERP-BR-2026-0002", "ERP-BR-2026-0003"]);
  });
});