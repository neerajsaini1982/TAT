# Call-outs: mark absent and assign cover in one step

## Summary

When an employee calls out at the last minute, let an admin mark them absent
and put someone else on the shift from a single dialog. The cover shift is
live straight away (the cover person can clock in without the week being
reposted), it remembers whose shift it covers, and the cover person can be
emailed.

Today this takes three separate steps, and the last one is easy to miss:

1. Mark the shift absent (`ShiftAssignmentsController.MarkAbsent`) from
   `schedule-day-view` or `admin-schedule-assign-page`.
2. Add the cover person to the same shift on `admin-schedule-assign-page`
   (`ShiftAssignmentsController.Create`).
3. Click Post Schedule again. The new assignment is a draft, and
   `TimeEntryPunchService` and `KioskController` both ignore unpublished
   assignments, so until the week is reposted the cover person can't clock in
   and doesn't see the shift. `Publish` reposts the whole week and, with
   Send Email ticked, emails everyone on it.

Nothing links the two assignments, so the schedule can't show who covered
for whom. The other shortcut — dragging the shift to the cover person
(`Move`) — overwrites `AccountId` and loses the absence altogether.

## User stories

**Admin or lead** (`LeadOrAbove`)
1. As an admin or lead, I can pick "Mark Absent & Assign Cover" on a shift and, in
   one dialog, enter the absence note, optionally record sick hours, and
   choose who covers.
2. The cover picker shows who can take the shift, each person's hours this
   week so I can avoid overtime, and a warning on anyone who said they were
   unavailable or who already has a shift that day.
3. When I pick someone who already has a shift that day, I see their whole
   day (both shifts and both lunches) and choose whether the cover shift
   keeps its lunch.
4. I can mark the employee absent now and assign cover later: an absent
   shift with no cover shows a "Find Cover" action.
5. I can change or remove the cover person.
6. I can choose to email the cover person about the shift.
7. As an admin, I see uncovered absences for today on the dashboard.

Leads can do all of the above except record sick hours, which stays
admin-only as it is today (`SetSickMinutes`).

**Employee**
8. As the cover employee, the shift shows on my schedule and the kiosk
   immediately, labelled "Covering for Sam R.", and I can clock into it.
9. As the absent employee, the shift stays on my schedule marked Absent.

## Rules

- **The absent assignment is kept.** It stays on the original employee with
  `IsAbsent`, the note and any sick hours, exactly as `MarkAbsent` and
  `SetSickMinutes` leave it today. Cover is a second, new assignment on the
  same shift and date.
- **Cover is published on creation** (`IsPublished = true`,
  `PublishedAt = now`) when the absent assignment is published. If the absent
  assignment is still a draft, the cover assignment is a draft too and goes
  out with the normal Post Schedule.
- **Who can cover** — same location, role is Employee/Lead/Admin, and
  active. Unlike `Create`, two things are warnings rather than blocks:
  - **Unavailable that day.** The picker lists them under the available
    people with an "Unavailable" label; choosing one asks for confirmation.
  - **Already has a shift that day (a double).** Allowed, as long as the two
    shifts' times don't overlap — see "Doubles and lunch" below.

  The server re-checks everything and only accepts an unavailable or
  already-scheduled person when the request says the warning was confirmed.
  `Create` and `Move` keep their existing blocks; the relaxed rules apply to
  cover assignments only.
- **Whole shift only.** Cover is always for the full shift. If the cover
  person arrives late, their punches show it.
- **Hours shown in the picker** are the employee's scheduled hours for that
  workweek including this shift, flagged when they'd pass the overtime
  threshold `OvertimeCalculator` uses. It's a warning, not a block.
- **One cover per absent assignment.** Assigning a new cover person replaces
  the previous one; the previous cover assignment is deleted, which is
  refused if they have already clocked in.
- **All or nothing.** Marking absent, saving sick hours and creating the
  cover assignment happen in one transaction. If the cover person fails a
  check, nothing is saved.
- **Clearing the absence** (`MarkAbsent` with `IsAbsent = false`, or the
  original employee clocking in, which already clears `IsAbsent`) does not
  remove the cover assignment — both people may end up working. The cover
  assignment keeps its link and the admin removes it by hand if it isn't
  wanted.
- **Deleting the absent assignment** clears the link on the cover assignment
  rather than deleting it.
- **Payroll rules are unchanged.** The cover person's hours come from their own
  punches on their own assignment; the absent employee's sick hours stay on
  theirs. No report column changes.
- Marking absent still wipes an existing `TimeEntry` on the absent
  assignment, as today.

## Doubles and lunch

An employee covering on a day they already work ends up with two assignments
that date. Punches, the hours report and the kiosk already work per
assignment, so the cover shift is a second clock-in with its own
`TimeEntry`: they clock out of their own shift and into the cover shift.

The problem is lunch. Every shift template carries its own scheduled lunch
(`ScheduledBreak` with `Kind = Lunch`), so a double gets two. Working 7–3
and then covering 3–11 is one 16-hour day with lunches at, say, 11:00 and
7:00 — sometimes right, sometimes not what the admin wants. Lunch is unpaid
(the report subtracts lunch punches from worked time), so it matters to the
employee too.

- **The dialog shows the combined day** when the chosen person has another
  shift: both shifts' times, total scheduled hours, the gap between them,
  and where each lunch falls.
- **The assigner chooses whether the cover shift keeps its lunch.** Default
  is to keep it. Meal-break rules for long days differ by state, so the app
  shows the facts and leaves the call to the admin or lead.
- **"No lunch on the cover shift"** is saved on the cover assignment
  (`SkipScheduledLunch`). When set:
  - the cover shift's scheduled hours are the full span, with no lunch taken
    off — in `ShiftAssignmentsController` hours, `ReportsController`
    scheduled minutes, and the picker's weekly-hours figure;
  - its lunch is left out of `ComputeBreakWindows`, so it isn't shown on the
    schedule or kiosk and doesn't push anyone else's lunch;
  - short breaks on the cover shift are unaffected.
- **Pay still follows punches.** The flag changes what is scheduled, not
  what is paid: a lunch is only deducted when one is punched. If the
  employee punches a lunch on a no-lunch cover shift it is recorded and
  deducted as normal.
- **Overlapping times are refused.** If the person's own shift overlaps the
  cover shift (own 9–5, cover 2–10) they can't be clocked into both, so the
  picker greys them out with the reason. Back-to-back shifts (one ends as
  the other starts) are fine.
- **Daily overtime.** The hours report already totals every assignment on a
  date before `OvertimeCalculator` runs, so a double counts towards daily
  and weekly overtime with no change. The picker's overtime warning must
  count the person's other shift that day.

## Data model (server)

New fields on `ShiftAssignment`:

| Field | Type | Notes |
|---|---|---|
| CoversAssignmentId / CoversAssignment | int? | the absent assignment this one covers; null for ordinary shifts |
| CoverAssignedByAccountId / CoverAssignedByAccount | int? | who assigned the cover |
| CoverAssignedAt | DateTime? | |
| SkipScheduledLunch | bool | default false; only settable on a cover assignment — see "Doubles and lunch" |

`CoversAssignmentId` is a self-reference with a unique index (one cover per
absent assignment) and `OnDelete(SetNull)`.

Add an EF Core migration in `server/Data/Migrations` and configure the
relationship in `AppDbContext`.

`ShiftAssignmentDto` gains:
- `CoversAssignmentId`, `CoversAccountFirstName`, `CoversAccountLastName` —
  set on the cover assignment.
- `CoveredByAssignmentId`, `CoveredByAccountFirstName`,
  `CoveredByAccountLastName` — set on the absent assignment.
- `SkipScheduledLunch`; `ScheduledBreaks` and `Hours` already reflect it.

Every place that looks up "the" assignment for an employee and date must
cope with two (check `GetMine` consumers, `current-week-schedule`,
`employee-schedule-page`, `kiosk-schedule`, `admin-dashboard`).

## API (server)

In `ShiftAssignmentsController`:

| Method | Route | Who | Purpose |
|---|---|---|---|
| GET | `/api/shift-assignments/{id}/cover-candidates` | LeadOrAbove | everyone at the location who could cover, each with: scheduled hours for the week, overtime flag, isAvailable, their other shift that day (times and lunch) if any, and a blocked reason when the shifts overlap |
| PUT | `/api/shift-assignments/{id}/call-out` | LeadOrAbove | body: note (required), sickMinutes? (admins only), coverAccountId?, skipScheduledLunch, confirmUnavailable, confirmDouble, sendEmail — marks absent, records sick hours, creates the cover assignment |
| PUT | `/api/shift-assignments/{id}/cover` | LeadOrAbove | body: coverAccountId, skipScheduledLunch, confirmUnavailable, confirmDouble, sendEmail — assign or replace cover on an assignment that is already absent |
| DELETE | `/api/shift-assignments/{id}/cover` | LeadOrAbove | remove the cover assignment; 409 if the cover person has clocked in |

A lead sending `sickMinutes` gets 403.

`call-out` and `cover` return both assignments so the client can update the
schedule without a refetch. `MarkAbsent` and `SetSickMinutes` stay as they
are. All routes use the existing `CanAccess` location check and call
`notifier.NotifyLocationChanged`.

## Emails

Add a key to `EmailTemplateKeys` / `EmailTemplateCatalog` so it can be edited
in the email template editor:
- **Cover Shift Assigned** — to the cover employee, with the date, shift name
  and times, and who they're covering for.

Sent with `EmailSender` using the location's SMTP settings when Send Email is
ticked. Sending is best-effort: a failure doesn't undo the cover assignment,
and the box is disabled when SMTP isn't set up or the employee has no email.

## UI (client)

Admin (`features/admin`):
- New `call-out-dialog`: absence note, optional sick hours (same input as
  `sick-hours-dialog`; hidden for leads), cover picker, "No cover yet"
  option, and the Send Email checkbox.
  - Picker order: available with no shift that day, then people already
    working that day, then unavailable people. Each row shows hours this
    week and any overtime, "Unavailable" or "Already working 7:00–3:00"
    label. People whose own shift overlaps are greyed out with the reason.
  - Choosing someone already working shows the combined-day summary and the
    "Cover shift lunch: Keep / No lunch" choice.
  - Choosing someone unavailable or already working needs a confirm tick
    before Save.
- `schedule-day-view` gear menu and the `admin-schedule-assign-page` row
  actions, for leads as well as admins wherever Mark Absent shows today: add "Mark Absent & Assign Cover" next to Mark Absent; on an absent
  shift show "Find Cover", or "Change Cover" / "Remove Cover" once it has
  one.
- `schedule-day-view`, `schedule-week-timeline`,
  `schedule-week-days-timeline`, `admin-schedule-assign-page`: the absent
  shift shows "Covered by Alex M."; the cover shift shows a "Covering for
  Sam R." badge. These views must lay out two shifts for one employee on the
  same day, and draw no lunch marker on a cover shift with no lunch.
- `admin-dashboard`: the Absent stat and Today's Schedule mark absences that
  have no cover; Needs Attention lists them with a link that opens the
  dialog.
- `shift-assignments-api.ts`: the new calls and DTO fields.

Employee: `employee-schedule-page` and `current-week-schedule` show
"Covering for Sam R." on a cover shift, and both shifts on a double.

Kiosk: `kiosk-schedule` lists the cover shift like any other published
shift. On a double the employee appears once per shift and clocks into each
separately.

## Tests (`server.Tests`)

- `call-out` with a cover person: absent assignment keeps its employee and
  gets the note and sick minutes; a published, linked cover assignment is
  created; the cover person can clock in through `TimeEntryPunchService`
  without `Publish` being called.
- `call-out` without a cover person behaves like `MarkAbsent` (+ sick hours).
- Cover on a draft absent assignment is created as a draft.
- Cover person rejected when in another location, inactive, or the wrong
  role — and nothing is saved, including the absence.
- Unavailable cover person: rejected without `confirmUnavailable`, accepted
  with it. `Create` still blocks unavailable employees.
- Double: rejected without `confirmDouble`, accepted with it when the shifts
  don't overlap (including back-to-back), rejected when they overlap.
  `Create` still blocks a second assignment on the same date.
- Double punches: the employee can clock out of their own shift and into the
  cover shift the same day, with a separate `TimeEntry` for each.
- `SkipScheduledLunch`: scheduled hours are the full span; the lunch is
  absent from `ComputeBreakWindows` and doesn't shift other employees'
  lunches; short breaks remain; a punched lunch is still deducted. Rejected
  on a non-cover assignment.
- `cover-candidates` returns correct weekly hours, overtime flag (counting
  a same-day shift), availability, other-shift details and overlap block.
- Replacing cover deletes the old cover assignment; replacing or removing is
  refused once the cover person has clocked in.
- Clearing the absence, and the original employee clocking in, both leave
  the cover assignment in place.
- Deleting the absent assignment nulls the link on the cover assignment.
- Permissions: leads can call the new routes but not send sick minutes;
  employees can't call them; leads and admins are limited to their location.
- `HoursReportTests`: cover hours count for the cover employee only; sick
  hours stay with the absent employee; a double's two shifts total into one
  day for daily overtime; scheduled minutes respect `SkipScheduledLunch`.
- Migration test, like the existing `*MigrationTests`.

## Acceptance criteria

- [ ] An admin or lead can mark an employee absent and assign cover from
      one dialog, saved together or not at all. Admins can also record sick
      hours there.
- [ ] The cover picker shows weekly hours with an overtime warning, and
      allows unavailable people after a confirmation.
- [ ] Someone already working that day can cover a non-overlapping shift,
      clocks into each shift separately, and the assigner chooses whether
      the cover shift keeps its lunch.
- [ ] A cover shift with no lunch shows full scheduled hours and no lunch
      window, and doesn't move anyone else's lunch.
- [ ] A cover shift on a published shift is live immediately: visible to the
      cover employee and on the kiosk, and clockable, without reposting the
      week.
- [ ] The absent shift stays with the original employee and shows who
      covered; the cover shift shows who it covers.
- [ ] Cover can be assigned later, changed or removed.
- [ ] The cover employee can be emailed, with a template editable in the
      template editor.
- [ ] The dashboard highlights today's absences with no cover.
- [ ] Payroll report totals are unchanged in shape; cover hours and sick
      hours land on the right employees.
- [ ] Server tests cover the cases above.

## Open questions

1. Overlapping doubles — someone on 9–5 staying on to cover a 2–10. V1
   refuses these. Supporting them means either trimming the cover shift to
   start when their own ends, or one merged clock-in across both.
2. Should "no lunch on the cover shift" only be offered below some combined
   length (e.g. hide it when the day is over 12 hours), or always be the
   assigner's call?
3. Should a lead's cover assignment that pushes someone into overtime need
   an admin's approval, or is the warning enough?

## Decided

- Leads can assign cover. Recording sick hours stays admin-only.
- Someone who already has a shift that day can cover, as a second
  assignment with its own clock-in; the assigner decides whether the cover
  shift keeps its lunch (default: keep).
- Availability can be overridden for cover, with a warning.
- Cover is for the whole shift; partial cover is left to actual punches.

## Out of scope (follow-up tickets)

- Employees offering up or swapping shifts themselves.
- Broadcasting an open shift to all eligible employees to claim.
- Text/push notifications.
- A call-out report (counts per employee over time). The link added here
  makes it possible later.
- Tying call-outs to time-off requests (see `time-off-requests.md`).
