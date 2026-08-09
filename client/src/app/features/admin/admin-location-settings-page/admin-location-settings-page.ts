import { Component, OnInit, inject, isDevMode, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { forkJoin } from 'rxjs';

import { DateFormat, LocationSettingsApi, LocationSettingsDto, TimeFormat } from '../../../core/location-settings-api';
import { EmailTemplateDto, EmailTemplatesApi } from '../../../core/email-templates-api';
import { AllowedPunchDeviceDto, AllowedPunchDevicesApi } from '../../../core/allowed-punch-devices-api';
import { AccountsApi } from '../../../core/accounts-api';
import { Auth } from '../../../core/auth';
import { DevToolsApi } from '../../../core/dev-tools-api';
import {
  EmailTemplateEditorDialog,
  EmailTemplateEditorResult,
} from '../email-template-editor-dialog/email-template-editor-dialog';

interface TimeZoneOption {
  value: string;
  label: string;
}

interface DateFormatOption {
  value: DateFormat;
  label: string;
}

// Live example using today's date, so the option reads clearly regardless
// of when the admin is looking at this screen.
function dateFormatExample(value: DateFormat): string {
  const d = new Date();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  const yyyy = d.getFullYear();
  const mon = d.toLocaleDateString(undefined, { month: 'short' });
  switch (value) {
    case 'MmDdYyyy':
      return `${mm}/${dd}/${yyyy}`;
    case 'DdMmYyyy':
      return `${dd}/${mm}/${yyyy}`;
    case 'YyyyMmDd':
      return `${yyyy}-${mm}-${dd}`;
    case 'DdMmmYyyy':
      return `${dd}-${mon}-${yyyy}`;
    case 'MmmDdYyyy':
      return `${mon} ${dd}, ${yyyy}`;
  }
}

const DATE_FORMATS: DateFormatOption[] = [
  { value: 'MmDdYyyy', label: `MM/DD/YYYY (${dateFormatExample('MmDdYyyy')})` },
  { value: 'DdMmYyyy', label: `DD/MM/YYYY (${dateFormatExample('DdMmYyyy')})` },
  { value: 'YyyyMmDd', label: `YYYY-MM-DD (${dateFormatExample('YyyyMmDd')})` },
  { value: 'DdMmmYyyy', label: `DD-MMM-YYYY (${dateFormatExample('DdMmmYyyy')})` },
  { value: 'MmmDdYyyy', label: `MMM DD, YYYY (${dateFormatExample('MmmDdYyyy')})` },
];

const TIME_ZONES: TimeZoneOption[] = [
  { value: 'America/Los_Angeles', label: 'Pacific Time (US)' },
  { value: 'America/Denver', label: 'Mountain Time (US)' },
  { value: 'America/Chicago', label: 'Central Time (US)' },
  { value: 'America/New_York', label: 'Eastern Time (US)' },
  { value: 'America/Anchorage', label: 'Alaska Time (US)' },
  { value: 'Pacific/Honolulu', label: 'Hawaii Time (US)' },
  { value: 'UTC', label: 'UTC' },
  { value: 'Europe/London', label: 'London' },
  { value: 'Europe/Paris', label: 'Paris / Berlin / Madrid' },
  { value: 'Asia/Kolkata', label: 'India' },
];

interface FormModel {
  timeFormat: TimeFormat;
  dateFormat: DateFormat;
  timeZone: string;
  availabilityDays: number;
  clockInWindowMinutes: number;
  lateClockInGraceMinutes: number;
  breakLimitMinutes: number;
  lunchLimitMinutes: number;
  overtimeDailyThresholdMinutes: number;
  developmentMode: boolean;
  scheduleVisibilityEnabled: boolean;
  adminSeesAllSchedules: boolean;
  leadSeesAllSchedules: boolean;
  employeeSeesAllSchedules: boolean;
  clockInAnywhere: boolean;
  smtpHost: string;
  smtpPort: number | null;
  smtpUsername: string;
  smtpPassword: string;
  smtpUseSsl: boolean;
  smtpFromAddress: string;
  smtpFromName: string;
  payDayStartDate: string;
  payPeriodDays: number | null;
}

const emptyForm = (): FormModel => ({
  timeFormat: 'TwelveHour',
  dateFormat: 'MmDdYyyy',
  timeZone: 'America/Los_Angeles',
  availabilityDays: 7,
  clockInWindowMinutes: 15,
  lateClockInGraceMinutes: 5,
  breakLimitMinutes: 15,
  lunchLimitMinutes: 30,
  overtimeDailyThresholdMinutes: 480,
  developmentMode: false,
  scheduleVisibilityEnabled: true,
  adminSeesAllSchedules: true,
  leadSeesAllSchedules: false,
  employeeSeesAllSchedules: false,
  clockInAnywhere: true,
  smtpHost: '',
  smtpPort: null,
  smtpUsername: '',
  smtpPassword: '',
  smtpUseSsl: true,
  smtpFromAddress: '',
  smtpFromName: '',
  payDayStartDate: '',
  payPeriodDays: null,
});

@Component({
  selector: 'app-admin-location-settings-page',
  imports: [
    FormsModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatCheckboxModule,
  ],
  templateUrl: './admin-location-settings-page.html',
  styleUrl: './admin-location-settings-page.scss',
})
export class AdminLocationSettingsPage implements OnInit {
  private readonly settingsApi = inject(LocationSettingsApi);
  private readonly templatesApi = inject(EmailTemplatesApi);
  private readonly devicesApi = inject(AllowedPunchDevicesApi);
  private readonly accountsApi = inject(AccountsApi);
  private readonly devToolsApi = inject(DevToolsApi);
  private readonly auth = inject(Auth);
  private readonly dialog = inject(MatDialog);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  // isDevMode() alone isn't enough here: this app's dev server is routinely
  // run LAN-accessible (see AngularDevClient CORS policy in Program.cs), so
  // a `ng serve` build stays "dev mode" for every device on the LAN, not
  // just this machine. The sync tool is tied to *this* machine's own `az
  // login` session, so it also needs the browser itself to be on
  // localhost — otherwise a coworker testing the dev build over the LAN
  // would see (and could click) a button that isn't theirs to use.
  protected readonly showDevTools =
    isDevMode() && (location.hostname === 'localhost' || location.hostname === '127.0.0.1' || location.hostname === '[::1]');
  // Matches DevToolsController's AdminOrAbove policy. This page (adminGuard)
  // is only ever reached as Admin or Lead — Sa can't get here — so in
  // practice this excludes just Lead, but checking the real policy keeps it
  // honest if that ever changes.
  protected readonly canSyncDb = this.auth.role() === 'Admin' || this.auth.role() === 'Sa';
  protected readonly syncingDb = signal(false);
  protected readonly syncDbResult = signal<string | null>(null);
  protected readonly syncDbError = signal<string | null>(null);

  protected readonly timeZones = TIME_ZONES;
  protected readonly dateFormats = DATE_FORMATS;
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal(false);
  protected readonly hasSmtpPassword = signal(false);
  protected readonly templates = signal<EmailTemplateDto[]>([]);
  protected readonly testingEmail = signal(false);
  protected readonly testEmailResult = signal<'success' | 'error' | null>(null);
  protected readonly testEmailError = signal<string | null>(null);
  protected testEmailAddress = '';
  protected readonly allowedDevices = signal<AllowedPunchDeviceDto[]>([]);
  protected readonly addingDevice = signal(false);
  protected readonly deviceError = signal<string | null>(null);
  protected newDeviceIp = '';
  protected newDeviceLabel = '';

  protected form: FormModel = emptyForm();

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      settings: this.settingsApi.get(this.locationCode),
      templates: this.templatesApi.getAll(this.locationCode),
      devices: this.devicesApi.getAll(this.locationCode),
      me: this.accountsApi.getMine(),
    }).subscribe({
      next: ({ settings, templates, devices, me }) => {
        this.applySettings(settings);
        this.templates.set(templates);
        this.allowedDevices.set(devices);
        this.testEmailAddress = me.email;
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load settings.');
        this.loading.set(false);
      },
    });
  }

  private applySettings(settings: LocationSettingsDto): void {
    this.hasSmtpPassword.set(settings.hasSmtpPassword);
    this.form = {
      timeFormat: settings.timeFormat,
      dateFormat: settings.dateFormat,
      timeZone: settings.timeZone,
      availabilityDays: settings.availabilityDays,
      clockInWindowMinutes: settings.clockInWindowMinutes,
      lateClockInGraceMinutes: settings.lateClockInGraceMinutes,
      breakLimitMinutes: settings.breakLimitMinutes,
      lunchLimitMinutes: settings.lunchLimitMinutes,
      overtimeDailyThresholdMinutes: settings.overtimeDailyThresholdMinutes,
      developmentMode: settings.developmentMode,
      scheduleVisibilityEnabled: settings.scheduleVisibilityEnabled,
      adminSeesAllSchedules: settings.adminSeesAllSchedules,
      leadSeesAllSchedules: settings.leadSeesAllSchedules,
      employeeSeesAllSchedules: settings.employeeSeesAllSchedules,
      clockInAnywhere: settings.clockInAnywhere,
      smtpHost: settings.smtpHost ?? '',
      smtpPort: settings.smtpPort,
      smtpUsername: settings.smtpUsername ?? '',
      smtpPassword: '',
      smtpUseSsl: settings.smtpUseSsl,
      smtpFromAddress: settings.smtpFromAddress ?? '',
      smtpFromName: settings.smtpFromName ?? '',
      payDayStartDate: settings.payDayStartDate ?? '',
      payPeriodDays: settings.payPeriodDays,
    };
  }

  save(): void {
    this.saving.set(true);
    this.error.set(null);
    this.saved.set(false);
    this.settingsApi
      .update(
        {
          timeFormat: this.form.timeFormat,
          dateFormat: this.form.dateFormat,
          timeZone: this.form.timeZone,
          availabilityDays: this.form.availabilityDays,
          clockInWindowMinutes: this.form.clockInWindowMinutes,
          lateClockInGraceMinutes: this.form.lateClockInGraceMinutes,
          breakLimitMinutes: this.form.breakLimitMinutes,
          lunchLimitMinutes: this.form.lunchLimitMinutes,
          overtimeDailyThresholdMinutes: this.form.overtimeDailyThresholdMinutes,
          developmentMode: this.form.developmentMode,
          scheduleVisibilityEnabled: this.form.scheduleVisibilityEnabled,
          adminSeesAllSchedules: this.form.adminSeesAllSchedules,
          leadSeesAllSchedules: this.form.leadSeesAllSchedules,
          employeeSeesAllSchedules: this.form.employeeSeesAllSchedules,
          clockInAnywhere: this.form.clockInAnywhere,
          smtpHost: this.form.smtpHost || null,
          smtpPort: this.form.smtpPort,
          smtpUsername: this.form.smtpUsername || null,
          smtpPassword: this.form.smtpPassword || null,
          smtpUseSsl: this.form.smtpUseSsl,
          smtpFromAddress: this.form.smtpFromAddress || null,
          smtpFromName: this.form.smtpFromName || null,
          payDayStartDate: this.form.payDayStartDate || null,
          payPeriodDays: this.form.payPeriodDays,
        },
        this.locationCode,
      )
      .subscribe({
        next: (settings) => {
          this.applySettings(settings);
          this.saving.set(false);
          this.saved.set(true);
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(err?.error ?? 'Failed to save settings.');
        },
      });
  }

  // Tests whatever SMTP fields are currently in the form, not necessarily
  // what's saved — so an admin can check a freshly-pasted App Password
  // works before committing to Save.
  sendTestEmail(): void {
    this.testingEmail.set(true);
    this.testEmailResult.set(null);
    this.testEmailError.set(null);
    this.settingsApi
      .sendTestEmail(
        {
          toAddress: this.testEmailAddress,
          smtpHost: this.form.smtpHost || null,
          smtpPort: this.form.smtpPort,
          smtpUsername: this.form.smtpUsername || null,
          smtpPassword: this.form.smtpPassword || null,
          smtpUseSsl: this.form.smtpUseSsl,
          smtpFromAddress: this.form.smtpFromAddress || null,
          smtpFromName: this.form.smtpFromName || null,
        },
        this.locationCode,
      )
      .subscribe({
        next: () => {
          this.testingEmail.set(false);
          this.testEmailResult.set('success');
        },
        error: (err) => {
          this.testingEmail.set(false);
          this.testEmailResult.set('error');
          this.testEmailError.set(err?.error ?? 'Failed to send test email.');
        },
      });
  }

  // Local dev only (see DevToolsController) — pulls the live Azure database
  // down and replaces this machine's local one so it can be tested against
  // real data.
  syncDbFromLive(): void {
    if (!confirm('Replace your local database with a copy of the live one? Your current local data will be backed up, but you should restart the local server afterward.')) {
      return;
    }

    this.syncingDb.set(true);
    this.syncDbResult.set(null);
    this.syncDbError.set(null);
    this.devToolsApi.syncDbFromLive().subscribe({
      next: (result) => {
        this.syncingDb.set(false);
        this.syncDbResult.set(result.message);
      },
      error: (err) => {
        this.syncingDb.set(false);
        this.syncDbError.set(
          err?.status === 403
            ? "Forbidden — Lead accounts can't do this; sign in as Admin instead."
            : (err?.error ?? 'Failed to sync the database from live.'),
        );
      },
    });
  }

  editTemplate(template: EmailTemplateDto): void {
    this.dialog
      .open<EmailTemplateEditorDialog, unknown, EmailTemplateEditorResult>(EmailTemplateEditorDialog, {
        data: { templateKey: template.key, displayName: template.displayName, subject: template.subject, bodyHtml: template.bodyHtml },
      })
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.saveTemplate(template.key, result);
        }
      });
  }

  private saveTemplate(key: string, result: EmailTemplateEditorResult): void {
    this.error.set(null);
    this.templatesApi.update(key, result, this.locationCode).subscribe({
      next: (updated) => {
        this.templates.update((list) => list.map((t) => (t.key === key ? updated : t)));
      },
      error: (err) => this.error.set(err?.error ?? 'Failed to save template.'),
    });
  }

  addDevice(): void {
    if (!this.newDeviceIp.trim() || !this.newDeviceLabel.trim()) {
      return;
    }
    this.addingDevice.set(true);
    this.deviceError.set(null);
    this.devicesApi
      .create({ ipAddress: this.newDeviceIp.trim(), label: this.newDeviceLabel.trim() }, this.locationCode)
      .subscribe({
        next: (device) => {
          this.allowedDevices.update((list) => [...list, device]);
          this.newDeviceIp = '';
          this.newDeviceLabel = '';
          this.addingDevice.set(false);
        },
        error: (err) => {
          this.addingDevice.set(false);
          this.deviceError.set(err?.error ?? 'Failed to add device.');
        },
      });
  }

  removeDevice(device: AllowedPunchDeviceDto): void {
    this.deviceError.set(null);
    this.devicesApi.delete(device.id, this.locationCode).subscribe({
      next: () => this.allowedDevices.update((list) => list.filter((d) => d.id !== device.id)),
      error: (err) => this.deviceError.set(err?.error ?? 'Failed to remove device.'),
    });
  }
}
