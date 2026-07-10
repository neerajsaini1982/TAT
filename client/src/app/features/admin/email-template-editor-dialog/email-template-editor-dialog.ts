import { AfterViewInit, Component, ElementRef, Inject, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export interface EmailTemplateEditorData {
  displayName: string;
  subject: string;
  bodyHtml: string;
}

export interface EmailTemplateEditorResult {
  subject: string;
  bodyHtml: string;
}

interface PlaceholderField {
  label: string;
  token: string;
}

const PLACEHOLDER_FIELDS: PlaceholderField[] = [
  { label: 'Employee Name', token: '{{employeeName}}' },
  { label: 'Location Name', token: '{{locationName}}' },
  { label: 'Week Range', token: '{{weekRange}}' },
];

// Lightweight, dependency-free WYSIWYG using contenteditable + execCommand
// rather than pulling in a rich-text editor library for one small screen.
@Component({
  selector: 'app-email-template-editor-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule],
  templateUrl: './email-template-editor-dialog.html',
  styleUrl: './email-template-editor-dialog.scss',
})
export class EmailTemplateEditorDialog implements AfterViewInit {
  @ViewChild('bodyEditor') private readonly bodyEditorRef!: ElementRef<HTMLDivElement>;

  protected readonly placeholderFields = PLACEHOLDER_FIELDS;
  protected subject: string;

  constructor(
    private readonly dialogRef: MatDialogRef<EmailTemplateEditorDialog, EmailTemplateEditorResult>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: EmailTemplateEditorData,
  ) {
    this.subject = data.subject;
  }

  ngAfterViewInit(): void {
    this.bodyEditorRef.nativeElement.innerHTML = this.data.bodyHtml;
  }

  format(command: string, value?: string): void {
    this.bodyEditorRef.nativeElement.focus();
    document.execCommand(command, false, value);
  }

  insertLink(): void {
    const url = prompt('Link URL:', 'https://');
    if (url) {
      this.format('createLink', url);
    }
  }

  insertPlaceholder(token: string): void {
    this.bodyEditorRef.nativeElement.focus();
    document.execCommand('insertText', false, token);
  }

  save(): void {
    this.dialogRef.close({
      subject: this.subject,
      bodyHtml: this.bodyEditorRef.nativeElement.innerHTML,
    });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
