import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

import { Auth } from '../../../core/auth';
import { WriteUpsManager } from '../../accounts/write-ups-manager/write-ups-manager';

@Component({
  selector: 'app-my-write-ups-page',
  imports: [RouterLink, MatIconModule, MatButtonModule, WriteUpsManager],
  templateUrl: './my-write-ups-page.html',
  styleUrl: './my-write-ups-page.scss',
})
export class MyWriteUpsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(Auth);

  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;
  protected readonly accountId = this.auth.accountId()!;
}
