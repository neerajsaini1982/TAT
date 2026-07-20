// Minutes -> "4h 30m" — short enough to never wrap in a table cell.
export function formatDurationMinutes(totalMinutes: number): string {
  const hrs = Math.floor(totalMinutes / 60);
  const mins = totalMinutes % 60;
  return `${hrs}h ${mins}m`;
}
