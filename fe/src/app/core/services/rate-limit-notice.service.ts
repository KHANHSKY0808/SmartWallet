import { Injectable, signal } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class RateLimitNoticeService {
  readonly notice = signal<string | null>(null);
  readonly retryAfterSeconds = signal<number | null>(null);

  show(retryAfterSeconds: number): void {
    const safeSeconds = Number.isFinite(retryAfterSeconds) && retryAfterSeconds > 0
      ? Math.ceil(retryAfterSeconds)
      : 60;
    this.retryAfterSeconds.set(safeSeconds);
    this.notice.set(`Bạn thao tác quá nhanh, thử lại sau ${safeSeconds} giây`);
  }

  clear(): void {
    this.notice.set(null);
    this.retryAfterSeconds.set(null);
  }
}

