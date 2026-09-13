"use client";

import { MeasurementChart, type MetricPoint } from "./MeasurementChart";
import type { Measurement } from "@/lib/api/types";

interface MeasurementsChartsProps {
  measurements: Measurement[];
}

// Both hues are picked only for these single-series charts — neither is part
// of the app's shared status/action palette (compliant green, alert red,
// expired orange, see globals.css), so they can't be misread as a status.
// Temperature reuses the brand green since it's the primary quality driver
// for green coffee storage; humidity gets a distinct blue for quick visual
// separation between the two charts.
const TEMPERATURE_COLOR = "#59c144";
const HUMIDITY_COLOR = "#2563eb";

function toPoints(
  measurements: Measurement[],
  pick: (m: Measurement) => Pick<MetricPoint, "avg" | "min" | "max">
): MetricPoint[] {
  return measurements
    .slice()
    .sort((a, b) => a.measDate.localeCompare(b.measDate))
    .map((m) => ({ date: m.measDate, ...pick(m) }));
}

// Renders the "Relevés" section of a batch's detail page: two charts
// (température, humidité) built from the batch's warehouse measurements,
// already filtered by the caller to the batch's storage period.
export function MeasurementsCharts({ measurements }: MeasurementsChartsProps) {
  if (measurements.length === 0) {
    return (
      <p className="py-8 text-center text-sm text-input-text">
        Aucune mesure disponible pour la période de stockage de ce lot.
      </p>
    );
  }

  const temperature = toPoints(measurements, (m) => ({
    avg: m.avgMeasTemp,
    min: m.minMeasTemp,
    max: m.maxMeasTemp,
  }));

  const humidity = toPoints(measurements, (m) => ({
    avg: m.avgMeasHumidity,
    min: m.minMeasHumidity,
    max: m.maxMeasHumidity,
  }));

  return (
    <div className="flex flex-col gap-6">
      <MeasurementChart title="Température" unit="°C" color={TEMPERATURE_COLOR} data={temperature} />
      <MeasurementChart title="Humidité" unit="%" color={HUMIDITY_COLOR} data={humidity} />
    </div>
  );
}
