import { Component, computed, input } from '@angular/core';

import { ShiftAssignmentDto } from '../../../core/shift-assignments-api';
import { employeeColor } from '../../../core/employee-colors';
import { LaidOutEvent, formatClockTime, hourLabel, layoutDayEvents, toMinutes } from '../../../core/day-view-layout';
import { WeekTimelineDay } from '../schedule-week-timeline/schedule-week-timeline';

// Each day's timeline area is this tall regardless of how many hours it
// spans (see hourHeightPx below) — much shorter than ScheduleDayView's own
// 560px, since here all 7 days stack on one page rather than one day
// filling the whole view.
const TIMELINE_HEIGHT_PX = 90;
const FALLBACK_START_HOUR = 9;
const FALLBACK_END_HOUR = 17;

interface DayRow {
  label: string;
  dateLabel: string;
  startHour: number;
  hours: number[];
  hourHeightPx: number;
  laidOutEvents: LaidOutEvent<ShiftAssignmentDto>[];
}

// Alternative to ScheduleWeekTimeline for the print view: that one lays the
// week out as 7 narrow side-by-side columns (a literal week grid), which
// reads fine on screen but gets cramped once every shift's text has to fit
// in a sliver 1/7th of the page wide. This instead stacks each day as its
// own full-width row — same per-day layout ScheduleDayView already uses
// (independent hour range scoped to that day's own earliest/latest shift,
// not one shared axis for the whole week) — so a day's shifts get the full
// page width to lay out in, at the cost of using more vertical space.
// Deliberately much sparser event cards than either of those two (just
// name + time, no shift name/menu/badges) since this is print-only, and 7
// of these stacked needs to still fit on a printable page or two.
@Component({
  selector: 'app-schedule-week-days-timeline',
  imports: [],
  templateUrl: './schedule-week-days-timeline.html',
  styleUrl: './schedule-week-days-timeline.scss',
})
export class ScheduleWeekDaysTimeline {
  readonly days = input<WeekTimelineDay[]>([]);

  protected readonly employeeColor = employeeColor;
  protected readonly hourLabel = hourLabel;
  protected readonly totalHeightPx = TIMELINE_HEIGHT_PX;

  protected readonly dayRows = computed<DayRow[]>(() =>
    this.days().map((day) => {
      const events = day.assignments;
      const { startHour, endHour } =
        events.length === 0
          ? { startHour: FALLBACK_START_HOUR, endHour: FALLBACK_END_HOUR }
          : (() => {
              const starts = events.map((a) => toMinutes(a.shiftStartTime));
              const ends = events.map((a) => toMinutes(a.shiftEndTime));
              const s = Math.floor(Math.min(...starts) / 60);
              const e = Math.ceil(Math.max(...ends) / 60);
              return { startHour: s, endHour: Math.max(e, s + 1) };
            })();

      const hourCount = endHour - startHour;
      return {
        label: day.label,
        dateLabel: day.dateLabel,
        startHour,
        hours: Array.from({ length: hourCount }, (_, i) => startHour + i),
        hourHeightPx: TIMELINE_HEIGHT_PX / hourCount,
        laidOutEvents: layoutDayEvents(
          events.map((a) => ({
            ...a,
            startMinutes: toMinutes(a.shiftStartTime),
            endMinutes: toMinutes(a.shiftEndTime),
          })),
        ),
      };
    }),
  );

  eventStyle(row: DayRow, laidOut: LaidOutEvent<ShiftAssignmentDto>): Record<string, string> {
    const top = ((laidOut.startMinutes - row.startHour * 60) / 60) * row.hourHeightPx;
    const height = ((laidOut.endMinutes - laidOut.startMinutes) / 60) * row.hourHeightPx;
    const width = (100 / laidOut.columnCount) * laidOut.columnSpan;
    const left = (100 / laidOut.columnCount) * laidOut.column;
    return {
      top: `${top}px`,
      height: `${Math.max(height, 18)}px`,
      left: `calc(${left}% + 2px)`,
      width: `calc(${width}% - 4px)`,
    };
  }

  timeRangeLabel(a: ShiftAssignmentDto): string {
    return `${formatClockTime(a.shiftStartTime)}–${formatClockTime(a.shiftEndTime)}`;
  }
}
