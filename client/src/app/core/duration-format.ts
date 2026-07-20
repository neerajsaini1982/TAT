// Minutes -> "08 hrs 30 mins", the zero-padded format the admin hours report
// (issue #18) asks for.
export function formatDurationMinutes(totalMinutes: number): string {
  const hrs = Math.floor(totalMinutes / 60);
  const mins = totalMinutes % 60;
  return `${String(hrs).padStart(2, '0')} hrs ${String(mins).padStart(2, '0')} mins`;
}
