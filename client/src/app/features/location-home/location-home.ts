import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-location-home',
  imports: [RouterLink, MatCardModule, MatButtonModule, MatIconModule],
  templateUrl: './location-home.html',
  styleUrl: './location-home.scss',
})
export class LocationHome {
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;
  // Set by authInterceptor's 401 handler (see auth-interceptor.ts) so a
  // silent auto-logout reads as "your session ended, log in again" instead
  // of just an unexplained jump back to this picker screen.
  protected readonly sessionExpired = this.route.snapshot.queryParamMap.get('sessionExpired') === '1';
}
