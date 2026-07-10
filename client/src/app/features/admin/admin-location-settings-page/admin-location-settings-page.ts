import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatDialog } from '@angular/material/dialog';
import { forkJoin } from 'rxjs';

import { DateFormat, LocationSettingsApi, LocationSettingsDto, TimeFormat } from '../../../core/location-settings-api';
import { EmailTemplateDto, EmailTemplatesApi } from '../../../core/email-templates-api';
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
  smtpHost: string;
  smtpPort: number | null;
  smtpUsername: string;
  smtpPassword: string;
  smtpUseSsl: boolean;
  smtpFromAddress: string;
  smtpFromName: string;
}

const emptyForm = (): FormModel => ({
  timeFormat: 'TwelveHour',
  dateFormat: 'MmDdYyyy',
  timeZone: 'America/Los_Angeles',
  availabilityDays: 7,
  smtpHost: '',
  smtpPort: null,
  smtpUsername: '',
  smtpPassword: '',
  smtpUseSsl: true,
  smtpFromAddress: '',
  smtpFromName: '',
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
  ],
  templateUrl: './admin-location-settings-page.html',
  styleUrl: './admin-location-settings-page.scss',
})
export class AdminLocationSettingsPage implements OnInit {
  private readonly settingsApi = inject(LocationSettingsApi);
  private readonly templatesApi = inject(EmailTemplatesApi);
  private readonly dialog = inject(MatDialog);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  protected readonly timeZones = TIME_ZONES;
  protected readonly dateFormats = DATE_FORMATS;
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal(false);
  protected readonly hasSmtpPassword = signal(false);
  protected readonly templates = signal<EmailTemplateDto[]>([]);

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
    }).subscribe({
      next: ({ settings, templates }) => {
        this.applySettings(settings);
        this.templates.set(templates);
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
      smtpHost: settings.smtpHost ?? '',
      smtpPort: settings.smtpPort,
      smtpUsername: settings.smtpUsername ?? '',
      smtpPassword: '',
      smtpUseSsl: settings.smtpUseSsl,
      smtpFromAddress: settings.smtpFromAddress ?? '',
      smtpFromName: settings.smtpFromName ?? '',
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

  editTemplate(template: EmailTemplateDto): void {
    this.dialog
      .open<EmailTemplateEditorDialog, unknown, EmailTemplateEditorResult>(EmailTemplateEditorDialog, {
        data: { displayName: template.displayName, subject: template.subject, bodyHtml: template.bodyHtml },
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
}
