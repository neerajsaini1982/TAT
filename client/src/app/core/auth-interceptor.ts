import { inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

import { Auth } from './auth';
import { API_BASE_URL } from './api-config';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(Auth);
  const router = inject(Router);
  const token = auth.token();

  if (!token || !req.url.startsWith(API_BASE_URL)) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401) {
        // Token expired or otherwise rejected — same logged-out landing
        // spot as a manual logout (see App.logout in app.ts).
        const locationCode = auth.locationCode();
        auth.logout();
        router.navigateByUrl(locationCode ? `/${locationCode}` : '/');
      }
      return throwError(() => err);
    }),
  );
};
