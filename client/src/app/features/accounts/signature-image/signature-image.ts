import { Component, Input, OnChanges, OnDestroy, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';

import { WriteUpsApi } from '../../../core/write-ups-api';

// Shows a stored signature. The image is behind the API's login, so it's
// fetched with the auth token and shown from an object URL rather than a
// plain <img src>.
@Component({
  selector: 'app-signature-image',
  template: `
    @if (url(); as src) {
      <img class="signature" [src]="src" alt="Signature" />
    } @else if (failed()) {
      <span class="unavailable">Signature unavailable</span>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    // White like the paper it was signed on, so it reads the same in dark mode.
    // Sizable from outside (the print page shows it larger, unboxed).
    .signature {
      display: block;
      max-width: var(--signature-max-width, 200px);
      max-height: var(--signature-max-height, 64px);
      background: #fff;
      border: var(--signature-border, 1px solid var(--mat-sys-outline-variant));
    }

    .unavailable {
      color: var(--mat-sys-on-surface-variant);
      font-size: 12px;
    }
  `,
})
export class SignatureImage implements OnChanges, OnDestroy {
  @Input({ required: true }) accountId!: number;
  @Input({ required: true }) writeUpId!: number;
  @Input({ required: true }) signatureId!: number;

  private readonly api = inject(WriteUpsApi);

  protected readonly url = signal<string | null>(null);
  protected readonly failed = signal(false);
  private objectUrl: string | null = null;
  private request: Subscription | null = null;

  ngOnChanges(): void {
    this.release();
    this.failed.set(false);
    this.request = this.api.signature(this.accountId, this.writeUpId, this.signatureId).subscribe({
      next: (blob) => {
        this.objectUrl = URL.createObjectURL(blob);
        this.url.set(this.objectUrl);
      },
      error: () => this.failed.set(true),
    });
  }

  ngOnDestroy(): void {
    this.release();
  }

  private release(): void {
    // A response still on its way for the previous inputs must not land on top
    // of the new ones.
    this.request?.unsubscribe();
    this.request = null;
    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
      this.objectUrl = null;
    }
    this.url.set(null);
  }
}
