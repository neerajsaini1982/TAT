import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { forkJoin } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

import { AccountDto, AccountsApi } from '../../../core/accounts-api';
import { Auth } from '../../../core/auth';
import { WriteUpDto, WriteUpsApi, writeUpTypeLabel } from '../../../core/write-ups-api';
import { SignatureImage } from '../../accounts/signature-image/signature-image';

// A single write-up laid out as a printable record (for the employee's file
// or legal purposes) — opened in its own tab from the admin's Write-Ups page.
// Any signature given in the app is printed with the signer's name and the
// time it was stamped; one that wasn't leaves a blank line to sign on paper.
@Component({
  selector: 'app-write-up-print-page',
  imports: [DatePipe, MatButtonModule, MatIconModule, SignatureImage],
  templateUrl: './write-up-print-page.html',
  styleUrl: './write-up-print-page.scss',
})
export class WriteUpPrintPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly accountsApi = inject(AccountsApi);
  private readonly writeUpsApi = inject(WriteUpsApi);

  protected readonly locationName = inject(Auth).locationName();
  protected readonly accountId = Number(this.route.snapshot.paramMap.get('id'));
  private readonly writeUpId = Number(this.route.snapshot.paramMap.get('writeUpId'));

  protected readonly account = signal<AccountDto | null>(null);
  protected readonly writeUp = signal<WriteUpDto | null>(null);
  protected readonly notFound = signal(false);
  protected readonly printedAt = new Date();

  protected readonly employeeName = computed(() => {
    const a = this.account();
    return a ? `${a.firstName} ${a.lastName}` : '';
  });
  protected readonly typeLabel = writeUpTypeLabel;

  ngOnInit(): void {
    forkJoin([this.accountsApi.getOne(this.accountId), this.writeUpsApi.list(this.accountId)]).subscribe({
      next: ([account, writeUps]) => {
        this.account.set(account);
        const writeUp = writeUps.find((w) => w.id === this.writeUpId) ?? null;
        this.writeUp.set(writeUp);
        this.notFound.set(writeUp === null);
      },
      error: () => this.notFound.set(true),
    });
  }

  print(): void {
    window.print();
  }
}
