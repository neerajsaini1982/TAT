import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

import { ShiftsManager } from '../../shifts/shifts-manager/shifts-manager';

@Component({
  selector: 'app-sa-shifts-page',
  imports: [RouterLink, MatIconModule, MatButtonModule, ShiftsManager],
  templateUrl: './sa-shifts-page.html',
  styleUrl: './sa-shifts-page.scss',
})
export class SaShiftsPage {}
