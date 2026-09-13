"use client";

import {
  Area,
  CartesianGrid,
  ComposedChart,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

export interface MetricPoint {
  date: string; // Measurement.measDate (ISO date, no time component)
  avg: number;
  min: number;
  max: number;
}

interface ChartRow extends MetricPoint {
  // Stacked on top of an invisible `min` area so the visible band spans
  // exactly [min, max] instead of [0, max] — the standard Recharts trick for
  // a min/max range band (see MeasurementChart's <Area> pair below).
  range: number;
}

interface MeasurementChartProps {
  title: string; // e.g. "Température"
  unit: string; // e.g. "°C"
  color: string; // single hex hue for this metric's line + band
  data: MetricPoint[];
}

function formatDate(iso: string): string {
  return new Date(`${iso}T00:00:00`).toLocaleDateString("fr-FR", {
    day: "2-digit",
    month: "short",
  });
}

function formatValue(value: number, unit: string): string {
  return `${value.toFixed(1)}${unit}`;
}

function CustomTooltip({
  active,
  payload,
  unit,
}: {
  active?: boolean;
  payload?: Array<{ payload: ChartRow }>;
  unit: string;
}) {
  if (!active || !payload?.length) return null;
  const point = payload[0].payload;
  return (
    <div className="rounded-md border border-border-primary bg-white px-3 py-2 text-xs shadow-sm">
      <p className="mb-1 font-medium text-foreground">{formatDate(point.date)}</p>
      <p className="text-foreground">
        <span className="font-semibold">{formatValue(point.avg, unit)}</span> en moyenne
      </p>
      <p className="text-input-text">
        min {formatValue(point.min, unit)} · max {formatValue(point.max, unit)}
      </p>
    </div>
  );
}

// Single-metric chart: an average line (2px, the entity's color) over a
// min/max band (same color, ~10% opacity). One series -> no legend box (the
// title + subtitle already say what's plotted); a "Voir en tableau" fallback
// keeps every value reachable without hovering the chart.
export function MeasurementChart({ title, unit, color, data }: MeasurementChartProps) {
  if (data.length === 0) {
    return (
      <div className="rounded-[4px] border border-border-primary p-6 text-center text-sm text-input-text">
        Aucune mesure de {title.toLowerCase()} disponible pour la période de stockage de ce lot.
      </div>
    );
  }

  const chartData: ChartRow[] = data.map((point) => ({
    ...point,
    range: point.max - point.min,
  }));

  return (
    <div className="rounded-[4px] border border-border-primary p-4">
      <h3 className="text-sm font-medium text-foreground">{title}</h3>
      <p className="mb-2 text-xs text-input-text">
        Ligne : moyenne journalière · Bande : min–max journalier ({unit})
      </p>
      <ResponsiveContainer width="100%" height={220}>
        <ComposedChart data={chartData} margin={{ top: 8, right: 12, left: 0, bottom: 0 }}>
          <CartesianGrid stroke="#e4e4e4" vertical={false} />
          <XAxis
            dataKey="date"
            tickFormatter={formatDate}
            tick={{ fontSize: 11, fill: "#636060" }}
            axisLine={{ stroke: "#e4e4e4" }}
            tickLine={false}
          />
          <YAxis
            unit={unit}
            tick={{ fontSize: 11, fill: "#636060" }}
            axisLine={false}
            tickLine={false}
            width={48}
          />
          <Tooltip content={<CustomTooltip unit={unit} />} cursor={{ stroke: "#636060", strokeWidth: 1 }} />
          <Area dataKey="min" stackId="range" stroke="none" fill="transparent" isAnimationActive={false} />
          <Area
            dataKey="range"
            stackId="range"
            stroke="none"
            fill={color}
            fillOpacity={0.1}
            isAnimationActive={false}
          />
          <Line
            dataKey="avg"
            stroke={color}
            strokeWidth={2}
            dot={false}
            activeDot={{ r: 4, stroke: "#ffffff", strokeWidth: 2 }}
            isAnimationActive={false}
          />
        </ComposedChart>
      </ResponsiveContainer>
      <details className="mt-2">
        <summary className="cursor-pointer text-xs text-input-text underline">Voir en tableau</summary>
        <div className="mt-2 overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="text-input-text">
                <th className="py-1 pr-4 font-medium">Date</th>
                <th className="py-1 pr-4 font-medium">Moyenne</th>
                <th className="py-1 pr-4 font-medium">Min</th>
                <th className="py-1 font-medium">Max</th>
              </tr>
            </thead>
            <tbody>
              {data.map((point) => (
                <tr key={point.date} className="border-t border-border-primary">
                  <td className="py-1 pr-4 text-foreground">{formatDate(point.date)}</td>
                  <td className="py-1 pr-4 text-foreground">{formatValue(point.avg, unit)}</td>
                  <td className="py-1 pr-4 text-input-text">{formatValue(point.min, unit)}</td>
                  <td className="py-1 text-input-text">{formatValue(point.max, unit)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </div>
  );
}
