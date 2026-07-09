// yyyy-MM-dd, matching the API's DateOnly serialization.
export function formatDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

export function parseDate(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d);
}

export function mondayOf(date: Date): Date {
  const result = new Date(date);
  const day = result.getDay(); // 0 = Sunday
  const diff = day === 0 ? -6 : 1 - day;
  result.setDate(result.getDate() + diff);
  result.setHours(0, 0, 0, 0);
  return result;
}

export function addDays(date: Date, days: number): Date {
  const result = new Date(date);
  result.setDate(result.getDate() + days);
  return result;
}

export const DAY_LABELS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
export const DAY_LABELS_SHORT = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

export function formatWeekRange(weekStart: Date): string {
  const weekEnd = addDays(weekStart, 6);
  const opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric' };
  return `${weekStart.toLocaleDateString(undefined, opts)} – ${weekEnd.toLocaleDateString(undefined, opts)}`;
}

// Plain ISO-date (yyyy-MM-dd) string comparison, no Date parsing needed.
export const isPastDate = (isoDate: string): boolean => isoDate < formatDate(new Date());

export const toMmDdYyyy = (isoDate: string): string => {
  const [y, m, d] = isoDate.split('-');
  return `${m}/${d}/${y}`;
};

// `<input type="time">` works with "HH:mm"; the API's TimeOnly fields
// serialize as "HH:mm:ss". Convert at the edges.
export const toInputTime = (apiTime: string | null): string => (apiTime ? apiTime.slice(0, 5) : '09:00');
export const toApiTime = (inputTime: string): string => (inputTime.length === 5 ? `${inputTime}:00` : inputTime);

export function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

export function addMonths(date: Date, months: number): Date {
  return new Date(date.getFullYear(), date.getMonth() + months, 1);
}

export function formatMonthLabel(monthAnchor: Date): string {
  return monthAnchor.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
}

// Full weeks (Monday-Sunday) covering every day of the given month, the
// same grid shape Google Calendar's month view uses.
export function monthGridDays(monthAnchor: Date): Date[] {
  const firstOfMonth = startOfMonth(monthAnchor);
  const lastOfMonth = new Date(monthAnchor.getFullYear(), monthAnchor.getMonth() + 1, 0);
  const gridStart = mondayOf(firstOfMonth);
  const gridEnd = addDays(mondayOf(lastOfMonth), 6);

  const days: Date[] = [];
  for (let d = gridStart; d <= gridEnd; d = addDays(d, 1)) {
    days.push(d);
  }
  return days;
}
