import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-location-home',
  imports: [RouterLink, MatCardModule, MatButtonModule],
  templateUrl: './location-home.html',
  styleUrl: './location-home.scss',
})
export class LocationHome {
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;
}
