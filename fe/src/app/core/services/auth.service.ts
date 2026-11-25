import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import {
  AuthResponse,
  ChangePasswordRequest,
  LoginRequest,
  RegisterRequest,
  ResetPasswordRequest,
  UserInfoDto
} from '../models/auth.models';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly tokenKey = 'access_token';
  private readonly refreshTokenKey = 'refresh_token';
  private readonly userKey = 'user_info';
  private readonly apiUrl = '/api/auth';

  currentUser = signal<UserInfoDto | null>(this.getStoredUser());
  currentUserRole = signal<string | null>(this.getStoredRole());
  currentUserRoles = signal<string[]>(this.parseTokenRoles(this.getAccessToken()));
  currentUserPermissions = signal<string[]>(this.parseTokenPermissions(this.getAccessToken()));
  isLoggedIn = signal<boolean>(this.isAuthenticated());
  sessionNotice = signal<string>('');

  constructor(private http: HttpClient) {}

  register(request: RegisterRequest): Observable<{ message: string; userId: string }> {
    return this.http.post<{ message: string; userId: string }>(`${this.apiUrl}/register`, request);
  }

  confirmEmail(token: string): Observable<{ message: string }> {
    return this.http.get<{ message: string }>(`${this.apiUrl}/confirm-email`, {
      params: { token }
    });
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    this.sessionNotice.set('');
    return this.http.post<AuthResponse>(`${this.apiUrl}/login`, request).pipe(
      tap(response => {
        const primaryRole = response.user.roles.length > 0 ? response.user.roles[0] : 'User';
        this.setSession(response.accessToken, response.refreshToken, primaryRole, response.user);
      })
    );
  }

  refreshToken(): Observable<AuthResponse> {
    const refreshToken = this.getRefreshToken();
    return this.http.post<AuthResponse>(`${this.apiUrl}/refresh`, { refreshToken }).pipe(
      tap(response => {
        const primaryRole = response.user.roles.length > 0 ? response.user.roles[0] : 'User';
        this.setSession(response.accessToken, response.refreshToken, primaryRole, response.user);
      })
    );
  }

  logout(): Observable<{ message: string }> {
    const refreshToken = this.getRefreshToken();
    return this.http.post<{ message: string }>(`${this.apiUrl}/logout`, { refreshToken }).pipe(
      tap({
        next: () => this.clearSession(),
        error: () => this.clearSession()
      })
    );
  }

  forgotPassword(email: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/forgot-password`, { email });
  }

  resetPassword(request: ResetPasswordRequest): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/reset-password`, request);
  }

  changePassword(request: ChangePasswordRequest): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/change-password`, request);
  }

  setSession(accessToken: string, refreshToken: string, role: string, user?: UserInfoDto): void {
    localStorage.setItem(this.tokenKey, accessToken);
    localStorage.setItem(this.refreshTokenKey, refreshToken);
    localStorage.setItem('user_role', role);
    if (user) {
      localStorage.setItem(this.userKey, JSON.stringify(user));
      this.currentUser.set(user);
    }
    this.currentUserRole.set(role);
    this.currentUserRoles.set(this.parseTokenRoles(accessToken, user?.roles));
    this.currentUserPermissions.set(this.parseTokenPermissions(accessToken));
    this.isLoggedIn.set(true);
  }

  clearSession(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem(this.refreshTokenKey);
    localStorage.removeItem('user_role');
    localStorage.removeItem(this.userKey);
    this.currentUser.set(null);
    this.currentUserRole.set(null);
    this.currentUserRoles.set([]);
    this.currentUserPermissions.set([]);
    this.isLoggedIn.set(false);
  }

  hasPermission(permission: string): boolean {
    if (!this.isAuthenticated()) return false;
    const role = this.currentUserRole();
    const roles = this.currentUserRoles();
    if (role === 'Admin' || roles.includes('Admin')) return true;
    return this.currentUserPermissions().includes(permission);
  }

  getAccessToken(): string | null {
    return localStorage.getItem(this.tokenKey);
  }

  getRefreshToken(): string | null {
    return localStorage.getItem(this.refreshTokenKey);
  }

  getUserRole(): string | null {
    return localStorage.getItem('user_role');
  }

  isAuthenticated(): boolean {
    return !!this.getAccessToken();
  }

  private getStoredUser(): UserInfoDto | null {
    const raw = localStorage.getItem(this.userKey);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as UserInfoDto;
    } catch {
      return null;
    }
  }

  private getStoredRole(): string | null {
    return localStorage.getItem('user_role');
  }

  private decodePayload(token: string | null): Record<string, unknown> | null {
    if (!token) return null;
    const parts = token.split('.');
    if (parts.length < 2) return null;
    try {
      const b64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
      const padded = b64 + '='.repeat((4 - (b64.length % 4)) % 4);
      return JSON.parse(atob(padded)) as Record<string, unknown>;
    } catch {
      return null;
    }
  }

  private parseTokenPermissions(token: string | null): string[] {
    const payload = this.decodePayload(token);
    const perm = payload?.['perm'];
    if (Array.isArray(perm)) return perm.map(String);
    if (typeof perm === 'string') return [perm];
    return [];
  }

  private parseTokenRoles(token: string | null, fallback?: string[]): string[] {
    const payload = this.decodePayload(token);
    const role = payload?.['role'];
    if (Array.isArray(role)) return role.map(String);
    if (typeof role === 'string') return [role];
    return fallback ?? (this.getStoredRole() ? [this.getStoredRole()!] : []);
  }
}
