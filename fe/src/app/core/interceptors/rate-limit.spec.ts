import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpHeaders, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { RateLimitNoticeService } from '../services/rate-limit-notice.service';

@Component({
  standalone: true,
  template: `<div data-testid="xss-note">{{ userNote }}</div>`
})
class XssEscapingProbeComponent {
  userNote = '<img src=x onerror=alert(1)>';
}

describe('Sprint 7b Frontend Security & RateLimit (F-01, F-03)', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let noticeService: RateLimitNoticeService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [XssEscapingProbeComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ]
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    noticeService = TestBed.inject(RateLimitNoticeService);
  });

  it('F-01: displays Retry-After countdown message on 429 RATE_LIMITED', () => {
    let caughtStatus = 0;
    http.post('/api/auth/login', {}).subscribe({
      error: err => { caughtStatus = err.status; }
    });

    const req = httpMock.expectOne('/api/auth/login');
    req.flush(
      { code: 'RATE_LIMITED', message: 'Quá nhiều yêu cầu', retryAfterSeconds: 45 },
      {
        status: 429,
        statusText: 'Too Many Requests',
        headers: new HttpHeaders({ 'Retry-After': '45' })
      }
    );

    expect(caughtStatus).toBe(429);
    expect(noticeService.retryAfterSeconds()).toBe(45);
    expect(noticeService.notice()).toBe('Bạn thao tác quá nhanh, thử lại sau 45 giây');
  });

  it('F-03: escapes user-controlled strings safely without rendering raw HTML/img', () => {
    const fixture = TestBed.createComponent(XssEscapingProbeComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const div = el.querySelector('[data-testid="xss-note"]')!;
    expect(div.textContent).toBe('<img src=x onerror=alert(1)>');
    expect(div.querySelector('img')).toBeNull();
  });
});

