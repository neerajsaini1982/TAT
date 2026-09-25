using System.Globalization;
using System.Net;
using Server.Dtos;
using Server.Models;

namespace Server.Services;

// Renders the PayrollHours template for one employee's row of the hours
// report — shared by ReportsController.EmailHoursReport (the real send) and
// EmailTemplatesController.SendTest, so a test email looks exactly like what
// employees get. The {{hours}} table mirrors the Payroll Report page: a
// summary strip (Regular / Overtime / Double Time / Sick / Total Paid) and
// the per-day breakdown, decimal hours first with hours-and-minutes beneath.
public static class PayrollHoursEmail
{
    public static Dictionary<string, string> Placeholders(
        EmployeeHoursReportDto employee, string locationName, DateOnly startDate, DateOnly endDate, DateFormat dateFormat)
    {
        var dateRange = $"{FormatDate(startDate, dateFormat)} – {FormatDate(endDate, dateFormat)}";
        return new Dictionary<string, string>
        {
            ["{{employeeName}}"] = employee.FullName,
            ["{{locationName}}"] = locationName,
            ["{{dateRange}}"] = dateRange,
            // Same value under the name the other templates use, so an
            // admin who reaches for the familiar field still gets the range.
            ["{{weekRange}}"] = dateRange,
            ["{{totalHours}}"] = DecimalHours(TotalPaidMinutes(employee)),
            ["{{hours}}"] = BuildHoursHtml(employee, dateFormat),
        };
    }

    // Same as the report page's totalMinutes(): every paid minute, i.e. net
    // worked (regular + overtime + double time) plus sick.
    public static int TotalPaidMinutes(EmployeeHoursReportDto employee) =>
        employee.TotalNetWorkedMinutes + employee.TotalSickMinutes;

    public static string BuildHoursHtml(EmployeeHoursReportDto employee, DateFormat dateFormat)
    {
        const string font = "font-family:Arial,Helvetica,sans-serif;";
        const string label = font + "font-size:11px;letter-spacing:0.06em;text-transform:uppercase;color:#555;padding:8px 12px 2px;";
        const string big = font + "font-size:22px;font-weight:bold;color:#111;padding:0 12px;";
        const string small = font + "font-size:12px;color:#555;padding:2px 12px 10px;";
        const string th = font + "font-size:11px;letter-spacing:0.06em;text-transform:uppercase;color:#555;padding:8px 12px;border-bottom:1px solid #ccc;";
        const string td = font + "font-size:14px;color:#111;padding:8px 12px;border-bottom:1px solid #eee;";
        const string num = "text-align:right;white-space:nowrap;";

        var totalPaid = TotalPaidMinutes(employee);
        var summary = new (string Label, int Minutes)[]
        {
            ("Regular", employee.TotalRegularMinutes),
            ("Overtime", employee.TotalOvertimeMinutes),
            ("Double Time", employee.TotalDoubleTimeMinutes),
            ("Sick", employee.TotalSickMinutes),
            ("Total Paid", totalPaid),
        };

        var summaryHtml =
            "<table role=\"presentation\" style=\"border-collapse:collapse;margin:12px 0 20px;border-top:2px solid #111;border-bottom:2px solid #111;\">"
            + "<tr>" + string.Concat(summary.Select(s => $"<td style=\"{label}\">{s.Label}</td>")) + "</tr>"
            + "<tr>" + string.Concat(summary.Select(s => $"<td style=\"{big}\">{DecimalHours(s.Minutes)}</td>")) + "</tr>"
            + "<tr>" + string.Concat(summary.Select(s => $"<td style=\"{small}\">{DurationOrDash(s.Minutes)}</td>")) + "</tr>"
            + "</table>";

        var rows = employee.Days.Select(day =>
        {
            var dateCell = $"<td style=\"{td}\">{WebUtility.HtmlEncode(FormatDayLabel(day.Date, dateFormat))}</td>";

            string payCells;
            if (day.IsAbsent)
            {
                var note = string.IsNullOrWhiteSpace(day.AbsenceNote) ? string.Empty : $" — {WebUtility.HtmlEncode(day.AbsenceNote)}";
                payCells = $"<td colspan=\"3\" style=\"{td}color:#a33;\">Absent{note}</td>";
            }
            else
            {
                // Still clocked in: nothing is final yet, same "—" the report page shows.
                var regular = day.NetWorkedMinutes is null ? "—" : DurationOrDash(day.RegularMinutes);
                payCells = $"<td style=\"{td}{num}\">{regular}</td>"
                    + $"<td style=\"{td}{num}\">{DurationOrDash(day.OvertimeMinutes)}</td>"
                    + $"<td style=\"{td}{num}\">{DurationOrDash(day.DoubleTimeMinutes)}</td>";
            }

            var total = DurationOrDash((day.NetWorkedMinutes ?? 0) + day.SickMinutes);
            return "<tr>" + dateCell + payCells
                + $"<td style=\"{td}{num}\">{DurationOrDash(day.SickMinutes)}</td>"
                + $"<td style=\"{td}{num}font-weight:bold;\">{total}</td></tr>";
        });

        string TotalCell(int minutes) =>
            $"<td style=\"{td}{num}border-top:2px solid #111;\"><div style=\"font-size:18px;font-weight:bold;\">{DecimalHours(minutes)}</div>"
            + $"<div style=\"font-size:12px;color:#555;\">{DurationOrDash(minutes)}</div></td>";

        var totalsRow = $"<tr><td style=\"{td}border-top:2px solid #111;font-weight:bold;\">Total</td>"
            + TotalCell(employee.TotalRegularMinutes)
            + TotalCell(employee.TotalOvertimeMinutes)
            + TotalCell(employee.TotalDoubleTimeMinutes)
            + TotalCell(employee.TotalSickMinutes)
            + TotalCell(totalPaid)
            + "</tr>";

        var dayTable =
            "<table role=\"presentation\" style=\"border-collapse:collapse;min-width:100%;\">"
            + $"<tr><th style=\"{th}text-align:left;\">Day</th><th style=\"{th}{num}\">Regular</th><th style=\"{th}{num}\">Overtime</th>"
            + $"<th style=\"{th}{num}\">Double Time</th><th style=\"{th}{num}\">Sick</th><th style=\"{th}{num}\">Total</th></tr>"
            + string.Concat(rows)
            + totalsRow
            + "</table>";

        return summaryHtml + dayTable;
    }

    // Decimal hours as keyed into ADP, e.g. 72h 15m -> "72.25".
    public static string DecimalHours(int minutes) =>
        (minutes / 60.0).ToString("0.00", CultureInfo.InvariantCulture);

    // Server-side twin of the client's formatDurationOrDash.
    private static string DurationOrDash(int minutes)
    {
        if (minutes == 0)
        {
            return "–";
        }

        var hours = minutes / 60;
        var mins = minutes % 60;
        return hours > 0 ? $"{hours}h {mins}m" : $"{mins}m";
    }

    private static string FormatDayLabel(DateOnly date, DateFormat dateFormat) =>
        $"{date.ToString("dddd", CultureInfo.InvariantCulture)} {FormatDate(date, dateFormat)}";

    public static string FormatDate(DateOnly date, DateFormat dateFormat) => date.ToString(dateFormat switch
    {
        DateFormat.DdMmYyyy => "dd/MM/yyyy",
        DateFormat.YyyyMmDd => "yyyy-MM-dd",
        DateFormat.DdMmmYyyy => "dd-MMM-yyyy",
        DateFormat.MmmDdYyyy => "MMM d, yyyy",
        _ => "MM/dd/yyyy",
    }, CultureInfo.InvariantCulture);
}
