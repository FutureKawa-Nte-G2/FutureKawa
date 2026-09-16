// Shared grid-template-columns definition for the batch table.
// Used by both BatchTable (header row) and BatchRow (data rows) so they never
// drift out of alignment when column widths are adjusted.
// Status/Actions use fixed pixel widths (not `auto`) because each row and the
// header are separate CSS Grid containers — `auto` tracks are sized per-grid
// based on that grid's own content, so they don't stay aligned across grids
// even with an identical template string. Fixed widths guarantee identical
// columns everywhere. 96px matches the Badge's fixed width (w-24).
export const GRID_TEMPLATE = "grid-cols-[2fr_2fr_2fr_1fr_1.5fr_96px_160px]";

// Shared row shell (dimensions, border, radius, spacing), background excluded
// since the header and data rows use different Tokens/Background values.
const ROW_SHELL =
  "grid min-h-[48px] w-full max-w-[990px] items-center justify-items-center gap-4 rounded-[4px] border border-border-primary px-4";

export const ROW_CLASSES = `${ROW_SHELL} bg-white`;
export const HEADER_ROW_CLASSES = `${ROW_SHELL} bg-background-secondary`;