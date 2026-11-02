import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  const token = authService.getAccessToken();

  let authReq = req;
  if (token) {
    authReq = req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`
      }
    });
  }

  return next(authReq).pipe(
    catchError((error: HttpErrorResponse) => {
      // Bỏ qua các endpoint auth công khai để tránh loop vô tận
      const isAuthUrl = req.url.includes('/api/auth/login') ||
                        req.url.includes('/api/auth/refresh') ||
                        req.url.includes('/api/auth/register') ||
                        req.url.includes('/api/auth/forgot-password') ||
                        req.url.includes('/api/auth/reset-password') ||
                        req.url.includes('/api/auth/confirm-email');

      if (error.status === 401 && !isAuthUrl && authService.getRefreshToken()) {
        // Thực hiện Silent Refresh Token khi nhận lỗi 401
        return authService.refreshToken().pipe(
          switchMap(response => {
            const retryReq = req.clone({
              setHeaders: {
                Authorization: `Bearer ${response.accessToken}`
              }
            });
            return next(retryReq);
          }),
          catchError(refreshError => {
            authService.clearSession();
            router.navigate(['/login']);
            return throwError(() => refreshError);
          })
        );
      }

      if (error.status === 401 && !isAuthUrl) {
        authService.clearSession();
        router.navigate(['/login']);
      }

      return throwError(() => error);
    })
  );
};
