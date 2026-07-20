import { parseDate } from './week-utils';
import { ShiftAssignmentDto } from './shift-assignments-api';
import { TimeEntryDto, TimeEntrySegmentDto } from './time-entries-api';

function combineDateAndTime(dateIso: string, time: string): Date {
  const [hours, minutes] = time.slice(0, 5).split(':').map(Number);
  const date = parseDate(dateIso);
  date.setHours(hours, minutes, 0, 0);
  return date;
}

// A clock-in more than graceMinutes after the shift's scheduled start.
export function isLateClockIn(
  entry: Pick<TimeEntryDto, 'clockInAt'>,
  assignment: Pick<ShiftAssignmentDto, 'date' | 'shiftStartTime'>,
  graceMinutes: number,
): boolean {
  const latestOnTime = combineDateAndTime(assignment.date, assignment.shiftStartTime);
  latestOnTime.setMinutes(latestOnTime.getMinutes() + graceMinutes);
  return new Date(entry.clockInAt) > latestOnTime;
}

// Measured against `now` while still open, so a break/lunch flags live once
// it's run long, not only after it's closed out.
export function isSegmentOverLimit(segment: Pick<TimeEntrySegmentDto, 'startAt' | 'endAt'>, limitMinutes: number, now: Date): boolean {
  const start = new Date(segment.startAt);
  const end = segment.endAt ? new Date(segment.endAt) : now;
  return (end.getTime() - start.getTime()) / 60_000 > limitMinutes;
}

// Whether any of the entry's segments of the given kind has run long —
// covers however many breaks/lunches the employee has taken, not just a
// fixed first/second slot.
export function isAnySegmentOverLimit(
  entry: Pick<TimeEntryDto, 'segments'>,
  kind: TimeEntrySegmentDto['kind'],
  limitMinutes: number,
  now: Date,
): boolean {
  return entry.segments.some((s) => s.kind === kind && isSegmentOverLimit(s, limitMinutes, now));
}
