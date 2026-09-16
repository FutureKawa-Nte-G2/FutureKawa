import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AlertFilter } from "./AlertFilter";

const mockGetCountries = vi.fn();
const mockGetWarehouses = vi.fn();

vi.mock("@/lib/api/batches", () => ({
  getCountries: () => mockGetCountries(),
  getWarehouses: (countryCode?: string) => mockGetWarehouses(countryCode),
}));

vi.mock("@/context/AuthContext", () => ({
  useAuth: () => ({ accessToken: "test-access-token" }),
}));

const countries = [
  { code: "BR", name: "Brazil" },
  { code: "EC", name: "Ecuador" },
  { code: "CO", name: "Colombia" },
];

const brWarehouses = [
  { id: "BR-W1", name: "Santos Warehouse", countryCode: "BR" },
  { id: "BR-W2", name: "Cerrado Warehouse", countryCode: "BR" },
];

describe("AlertFilter", () => {
  beforeEach(() => {
    mockGetCountries.mockReset().mockResolvedValue(countries);
    mockGetWarehouses.mockReset().mockResolvedValue(brWarehouses);
  });

  it("loads and displays the countries on mount", async () => {
    render(<AlertFilter onSelectionChange={vi.fn()} />);

    expect(await screen.findByRole("option", { name: "Brazil" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Ecuador" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Colombia" })).toBeInTheDocument();
  });

  it("loads warehouses for the selected country and reports the selection", async () => {
    const onSelectionChange = vi.fn();
    const user = userEvent.setup();
    render(<AlertFilter onSelectionChange={onSelectionChange} />);

    await screen.findByRole("option", { name: "Brazil" });
    await user.selectOptions(screen.getByLabelText("Pays"), "BR");

    expect(mockGetWarehouses).toHaveBeenCalledWith("BR");
    expect(await screen.findByRole("option", { name: "Santos Warehouse" })).toBeInTheDocument();
    expect(onSelectionChange).toHaveBeenCalledWith({
      country: countries[0],
      warehouse: null,
      status: null,
    });
  });

  it("resets the warehouse selection when the country changes", async () => {
    const onSelectionChange = vi.fn();
    const user = userEvent.setup();
    render(<AlertFilter onSelectionChange={onSelectionChange} />);

    await screen.findByRole("option", { name: "Brazil" });
    await user.selectOptions(screen.getByLabelText("Pays"), "BR");
    await screen.findByRole("option", { name: "Santos Warehouse" });
    await user.selectOptions(screen.getByLabelText("Entrepôt"), "BR-W1");

    onSelectionChange.mockClear();
    await user.selectOptions(screen.getByLabelText("Pays"), "EC");

    expect(onSelectionChange).toHaveBeenCalledWith({
      country: countries[1],
      warehouse: null,
      status: null,
    });
    expect(screen.getByLabelText("Entrepôt")).toHaveDisplayValue("Tous les entrepôts");
  });

  it("resets the warehouse when 'Tous les pays' is re-selected", async () => {
    const onSelectionChange = vi.fn();
    const user = userEvent.setup();
    render(<AlertFilter onSelectionChange={onSelectionChange} />);

    await screen.findByRole("option", { name: "Brazil" });
    await user.selectOptions(screen.getByLabelText("Pays"), "BR");
    await screen.findByRole("option", { name: "Santos Warehouse" });
    await user.selectOptions(screen.getByLabelText("Entrepôt"), "BR-W1");

    onSelectionChange.mockClear();
    await user.selectOptions(screen.getByLabelText("Pays"), "Tous les pays");

    expect(onSelectionChange).toHaveBeenCalledWith({
      country: null,
      warehouse: null,
      status: null,
    });
  });

  it("reports the selected warehouse as a full object, not just its id", async () => {
    const onSelectionChange = vi.fn();
    const user = userEvent.setup();
    render(<AlertFilter onSelectionChange={onSelectionChange} />);

    await screen.findByRole("option", { name: "Brazil" });
    await user.selectOptions(screen.getByLabelText("Pays"), "BR");
    await screen.findByRole("option", { name: "Santos Warehouse" });
    await user.selectOptions(screen.getByLabelText("Entrepôt"), "BR-W1");

    expect(onSelectionChange).toHaveBeenLastCalledWith({
      country: countries[0],
      warehouse: brWarehouses[0],
      status: null,
    });
  });

  it("renders the status options", async () => {
    render(<AlertFilter onSelectionChange={vi.fn()} />);

    expect(screen.getByRole("option", { name: "Tous les statuts" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Active" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Résolue" })).toBeInTheDocument();
  });

  it("reports the selected status, preserving the current location selection", async () => {
    const onSelectionChange = vi.fn();
    const user = userEvent.setup();
    render(<AlertFilter onSelectionChange={onSelectionChange} />);

    await screen.findByRole("option", { name: "Brazil" });
    await user.selectOptions(screen.getByLabelText("Pays"), "BR");

    onSelectionChange.mockClear();
    await user.selectOptions(screen.getByLabelText("Statut"), "Résolue");

    expect(onSelectionChange).toHaveBeenCalledWith({
      country: countries[0],
      warehouse: null,
      status: "resolved",
    });
  });

  it("clears the status when 'Tous les statuts' is re-selected", async () => {
    const onSelectionChange = vi.fn();
    const user = userEvent.setup();
    render(<AlertFilter onSelectionChange={onSelectionChange} />);

    await screen.findByRole("option", { name: "Brazil" });
    await user.selectOptions(screen.getByLabelText("Statut"), "Active");

    onSelectionChange.mockClear();
    await user.selectOptions(screen.getByLabelText("Statut"), "Tous les statuts");

    expect(onSelectionChange).toHaveBeenCalledWith({
      country: null,
      warehouse: null,
      status: null,
    });
  });
});
