import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import {
  AuthResponse,
  ChangePasswordRequest,
  ForgotPasswordRequest,
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
  isLoggedIn = signal<boolean>(this.isAuthenticated());

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
    this.isLoggedIn.set(true);
  }

  clearSession(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem(this.refreshTokenKey);
    localStorage.removeItem('user_role');
    localStorage.removeItem(this.userKey);
    this.currentUser.set(null);
    this.currentUserRole.set(null);
    this.isLoggedIn.set(false);
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
}
