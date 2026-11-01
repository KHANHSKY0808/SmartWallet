import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        AuthService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    service = TestBed.inject(AuthService);
  });

  it('should initialize with logged out state', () => {
    expect(service.isLoggedIn()).toBe(false);
    expect(service.getAccessToken()).toBeNull();
    expect(service.currentUser()).toBeNull();
  });

  it('should store session and update signals on setSession', () => {
    const user = { id: 'u1', email: 'test@example.com', roles: ['User'] };
    service.setSession('fake-access-token', 'fake-refresh-token', 'User', user);

    expect(service.getAccessToken()).toBe('fake-access-token');
    expect(service.getRefreshToken()).toBe('fake-refresh-token');
    expect(service.getUserRole()).toBe('User');
    expect(service.isLoggedIn()).toBe(true);
    expect(service.currentUser()?.email).toBe('test@example.com');
  });

  it('should clear session on clearSession', () => {
    service.setSession('token', 'refresh', 'User');
    service.clearSession();

    expect(service.getAccessToken()).toBeNull();
    expect(service.getRefreshToken()).toBeNull();
    expect(service.isLoggedIn()).toBe(false);
    expect(service.currentUserRole()).toBeNull();
  });
});

