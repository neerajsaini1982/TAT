// Minutes -> "4h 30m", or just "30m" under an hour — short enough to never
// wrap in a table cell, and no point showing a "0h" that never varies.
export function formatDurationMinutes(totalMinutes: number): string {
  const hrs = Math.floor(totalMinutes / 60);
  const mins = totalMinutes % 60;
  return hrs > 0 ? `${hrs}h ${mins}m` : `${mins}m`;
}

// Same as formatDurationMinutes, but a flat zero (no break taken, no lunch
// taken, etc.) renders as a dash rather than "0m" — reads more clearly as
// "nothing happened" than an explicit zero would.
export function formatDurationOrDash(totalMinutes: number): string {
  return totalMinutes === 0 ? '–' : formatDurationMinutes(totalMinutes);
}
