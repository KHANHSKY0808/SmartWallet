import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, catchError, filter, switchMap, take, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { RateLimitNoticeService } from '../services/rate-limit-notice.service';

let isRefreshing = false;
const refreshTokenSubject = new BehaviorSubject<string | null>(null);

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const rateLimitNotice = inject(RateLimitNoticeService);
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
        if (!isRefreshing) {
          isRefreshing = true;
          refreshTokenSubject.next(null);

          return authService.refreshToken().pipe(
            switchMap(response => {
              isRefreshing = false;
              refreshTokenSubject.next(response.accessToken);

              const retryReq = req.clone({
                setHeaders: {
                  Authorization: `Bearer ${response.accessToken}`
                }
              });
              return next(retryReq);
            }),
            catchError(refreshError => {
              isRefreshing = false;
              refreshTokenSubject.next(null);
              authService.sessionNotice.set('Phiên đăng nhập đã hết hiệu lực hoặc tài khoản đã bị khóa. Vui lòng đăng nhập lại.');
              authService.clearSession();
              router.navigate(['/login'], { queryParams: { reason: 'revoked' } });
              return throwError(() => refreshError);
            })
          );
        } else {
          // Nếu đang có request refresh chạy dở -> xếp hàng chờ token mới
          return refreshTokenSubject.pipe(
            filter(newToken => newToken !== null),
            take(1),
            switchMap(newToken => {
              const retryReq = req.clone({
                setHeaders: {
                  Authorization: `Bearer ${newToken}`
                }
              });
              return next(retryReq);
            })
          );
        }
      }

      if (error.status === 429) {
        const headerVal = error.headers?.get('Retry-After');
        const bodyVal = typeof error.error === 'object' && error.error !== null
          ? Number((error.error as { retryAfterSeconds?: unknown }).retryAfterSeconds)
          : NaN;
        const parsedHeader = headerVal ? Number.parseInt(headerVal, 10) : NaN;
        const seconds = Number.isFinite(parsedHeader) && parsedHeader > 0
          ? parsedHeader
          : (Number.isFinite(bodyVal) && bodyVal > 0 ? bodyVal : 60);
        rateLimitNotice.show(seconds);
      }

      if (error.status === 401 && !isAuthUrl) {
        authService.sessionNotice.set('Phiên đăng nhập đã hết hiệu lực hoặc tài khoản đã bị khóa. Vui lòng đăng nhập lại.');
        authService.clearSession();
        router.navigate(['/login'], { queryParams: { reason: 'revoked' } });
      }

      return throwError(() => error);
    })
  );
};
