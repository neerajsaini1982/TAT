import { Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';

import { ShiftAssignmentDto } from '../../../core/shift-assignments-api';
import { BreakKind } from '../../../core/shifts-api';
import { employeeColor } from '../../../core/employee-colors';
import { LaidOutEvent, formatClockTime, hourLabel, layoutDayEvents, toMinutes } from '../../../core/day-view-layout';

// Minimum height for a break/lunch marker — tall enough to fit its own
// label centered inside it (see .break-label), not just a visible sliver.
// A short break on a long shift (a 15min break on an 8hr shift is ~2% of
// the card) would otherwise size down to just a couple px.
const MIN_MARKER_HEIGHT_PX = 13;

interface BreakMarker {
  topPx: number;
  heightPx: number;
  kind: BreakKind;
  label: string;
}

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
  imports: [MatButtonModule, MatIconModule, MatMenuModule],
  templateUrl: './schedule-day-view.html',
  styleUrl: './schedule-day-view.scss',
})
export class ScheduleDayView {
  readonly assignments = input<ShiftAssignmentDto[]>([]);

  // Per-card settings menu — Mark Absent and Time Punches today, with more
  // expected to land here later (see the menu in the template), so each
  // action is its own output rather than baking the note-dialog/API calls
  // into this presentational component.
  readonly markAbsent = output<ShiftAssignmentDto>();
  readonly clearAbsent = output<ShiftAssignmentDto>();
  readonly editTimes = output<ShiftAssignmentDto>();

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

  protected readonly hourLabel = hourLabel;

  timeRangeLabel(a: ShiftAssignmentDto): string {
    return `${formatClockTime(a.shiftStartTime)} – ${formatClockTime(a.shiftEndTime)}`;
  }

  // One marker per scheduled break/lunch on this assignment, positioned as a
  // px offset/height within the event card (same startMinutes-relative
  // coordinate space as the card itself, just scaled to the card's own
  // height rather than the whole timeline's). Each employee's assignment
  // already carries its own staggered break times (see
  // ShiftAssignmentsController.ComputeBreakWindows), so two employees on the
  // same shift naturally get markers at different positions. label carries
  // its own time range text so the template can print it right at the line
  // instead of bunched in with the card's header text.
  breakMarkers(laidOut: LaidOutEvent<ShiftAssignmentDto>): BreakMarker[] {
    const span = laidOut.endMinutes - laidOut.startMinutes;
    if (span <= 0) {
      return [];
    }

    const cardHeightPx = (span / 60) * this.hourHeightPx();
    return laidOut.event.scheduledBreaks.map((b) => {
      let start = toMinutes(b.startTime) - laidOut.startMinutes;
      if (start < 0) {
        start += 1440;
      }
      let end = toMinutes(b.endTime) - laidOut.startMinutes;
      if (end <= start) {
        end += 1440;
      }

      return {
        topPx: (start / span) * cardHeightPx,
        heightPx: Math.max(((end - start) / span) * cardHeightPx, MIN_MARKER_HEIGHT_PX),
        kind: b.kind,
        label: `${b.kind} ${formatClockTime(b.startTime)}–${formatClockTime(b.endTime)}`,
      };
    });
  }

  // Native tooltip for the card as a whole — the markers themselves are
  // decorative (pointer-events: none, so they never sit above the settings
  // button), so this is what actually surfaces the exact break/lunch times
  // on hover.
  breakSummary(a: ShiftAssignmentDto): string | null {
    if (a.scheduledBreaks.length === 0) {
      return null;
    }

    return a.scheduledBreaks
      .map((b) => `${b.kind}: ${formatClockTime(b.startTime)} – ${formatClockTime(b.endTime)}`)
      .join('\n');
  }
}
