import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

import { ShiftsManager } from '../../shifts/shifts-manager/shifts-manager';

@Component({
  selector: 'app-admin-shifts-page',
  imports: [RouterLink, MatIconModule, MatButtonModule, ShiftsManager],
  templateUrl: './admin-shifts-page.html',
  styleUrl: './admin-shifts-page.scss',
})
export class AdminShiftsPage {
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;
}
