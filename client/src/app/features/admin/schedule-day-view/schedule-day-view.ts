import { Component, computed, input } from '@angular/core';

import { ShiftAssignmentDto } from '../../../core/shift-assignments-api';
import { employeeColor } from '../../../core/employee-colors';
import { LaidOutEvent, layoutDayEvents, toMinutes } from '../../../core/day-view-layout';

// The whole timeline always renders at this height, however many hours the
// day spans — see hourHeightPx below — so the day's full start-to-end range
// is visible in one section without scrolling, rather than a fixed
// per-hour pixel height that could run taller than the viewport on a long
// day (or leave a fixed 8am-8pm range mostly empty on a short one).
const TIMELINE_HEIGHT_PX = 560;
const FALLBACK_START_HOUR = 9;
const FALLBACK_END_HOUR = 17;

@Component({
  selector: 'app-schedule-day-view',
  imports: [],
  templateUrl: './schedule-day-view.html',
  styleUrl: './schedule-day-view.scss',
})
export class ScheduleDayView {
  readonly assignments = input<ShiftAssignmentDto[]>([]);

  protected readonly employeeColor = employeeColor;

  // Bounded to the day's actual earliest start / latest end (rounded out to
  // the hour for clean gridlines) — no padding before the first shift or
  // after the last, per the "just start with the first shift's time" ask.
  protected readonly timeRange = computed(() => {
    const events = this.assignments();
    if (events.length === 0) {
      return { startHour: FALLBACK_START_HOUR, endHour: FALLBACK_END_HOUR };
    }

    const starts = events.map((a) => toMinutes(a.shiftStartTime));
    const ends = events.map((a) => toMinutes(a.shiftEndTime));
    const startHour = Math.floor(Math.min(...starts) / 60);
    const endHour = Math.ceil(Math.max(...ends) / 60);
    return { startHour, endHour: Math.max(endHour, startHour + 1) };
  });

  protected readonly hours = computed(() => {
    const { startHour, endHour } = this.timeRange();
    return Array.from({ length: endHour - startHour }, (_, i) => startHour + i);
  });

  protected readonly hourHeightPx = computed(() => {
    const { startHour, endHour } = this.timeRange();
    return TIMELINE_HEIGHT_PX / (endHour - startHour);
  });

  protected readonly totalHeightPx = TIMELINE_HEIGHT_PX;

  protected readonly laidOutEvents = computed<LaidOutEvent<ShiftAssignmentDto>[]>(() =>
    layoutDayEvents(
      this.assignments().map((a) => ({
        ...a,
        startMinutes: toMinutes(a.shiftStartTime),
        endMinutes: toMinutes(a.shiftEndTime),
      })),
    ),
  );

  eventStyle(laidOut: LaidOutEvent<ShiftAssignmentDto>): Record<string, string> {
    const { startHour } = this.timeRange();
    const hourHeight = this.hourHeightPx();
    const top = ((laidOut.startMinutes - startHour * 60) / 60) * hourHeight;
    const height = ((laidOut.endMinutes - laidOut.startMinutes) / 60) * hourHeight;
    const width = (100 / laidOut.columnCount) * laidOut.columnSpan;
    const left = (100 / laidOut.columnCount) * laidOut.column;
    return {
      top: `${top}px`,
      height: `${Math.max(height, 38)}px`,
      left: `calc(${left}% + 4px)`,
      width: `calc(${width}% - 8px)`,
    };
  }

  hourLabel(hour: number): string {
    const h = hour % 24;
    const period = h < 12 ? 'AM' : 'PM';
    const display = h % 12 === 0 ? 12 : h % 12;
    return `${display} ${period}`;
  }

  timeRangeLabel(a: ShiftAssignmentDto): string {
    return `${formatClockTime(a.shiftStartTime)} – ${formatClockTime(a.shiftEndTime)}`;
  }
}

// "13:30:00" -> "1:30pm"; drops :00 minutes the way Google Calendar's day
// view does ("12 – 1pm" rather than "12:00pm – 1:00pm").
function formatClockTime(time: string): string {
  const [hStr, mStr] = time.split(':');
  const h24 = Number(hStr);
  const period = h24 < 12 ? 'am' : 'pm';
  const h = h24 % 12 === 0 ? 12 : h24 % 12;
  return mStr === '00' ? `${h}${period}` : `${h}:${mStr}${period}`;
}
