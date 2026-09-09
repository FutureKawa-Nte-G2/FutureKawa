import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MeasurementsCharts } from "./MeasurementsCharts";
import type { Measurement } from "@/lib/api/types";

function makeMeasurement(overrides: Partial<Measurement> = {}): Measurement {
  return {
    id: "m1",
    warehouseId: "BR-W1",
    warehouseName: "Santos",
    measDate: "2026-01-05",
    avgMeasTemp: 28.5,
    maxMeasTemp: 30.1,
    minMeasTemp: 26.9,
    avgMeasHumidity: 58.2,
    minMeasHumidity: 55.0,
    maxMeasHumidity: 61.4,
    ...overrides,
  };
}

describe("MeasurementsCharts", () => {
  it("shows an empty-state message when there are no measurements", () => {
    render(<MeasurementsCharts measurements={[]} />);

    expect(
      screen.getByText("Aucune mesure disponible pour la période de stockage de ce lot.")
    ).toBeInTheDocument();
  });

  it("renders both the temperature and humidity charts with their table view", () => {
    render(<MeasurementsCharts measurements={[makeMeasurement()]} />);

    expect(screen.getByText("Température")).toBeInTheDocument();
    expect(screen.getByText("Humidité")).toBeInTheDocument();

    // The "Voir en tableau" fallback keeps every value reachable without
    // hovering the (SVG, hard to query) chart itself.
    expect(screen.getByText("28.5°C")).toBeInTheDocument();
    expect(screen.getByText("26.9°C")).toBeInTheDocument();
    expect(screen.getByText("30.1°C")).toBeInTheDocument();
    expect(screen.getByText("58.2%")).toBeInTheDocument();
  });

  it("sorts measurements chronologically before charting, regardless of input order", () => {
    const older = makeMeasurement({ id: "m-old", measDate: "2026-01-01", avgMeasTemp: 27.0 });
    const newer = makeMeasurement({ id: "m-new", measDate: "2026-01-10", avgMeasTemp: 29.0 });

    render(<MeasurementsCharts measurements={[newer, older]} />);

    const rows = screen.getAllByText(/janv\./);
    // First matching date cell across both tables should be the older date.
    expect(rows[0].textContent).toBe("01 janv.");
  });
});
