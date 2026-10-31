import { Injectable, signal } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly tokenKey = 'access_token';
  private readonly refreshTokenKey = 'refresh_token';
  private readonly userRoleKey = 'user_role';

  currentUserRole = signal<string | null>(this.getUserRole());
  isLoggedIn = signal<boolean>(this.isAuthenticated());

  setSession(accessToken: string, refreshToken: string, role: string): void {
    localStorage.setItem(this.tokenKey, accessToken);
    localStorage.setItem(this.refreshTokenKey, refreshToken);
    localStorage.setItem(this.userRoleKey, role);
    this.currentUserRole.set(role);
    this.isLoggedIn.set(true);
  }

  clearSession(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem(this.refreshTokenKey);
    localStorage.removeItem(this.userRoleKey);
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
    return localStorage.getItem(this.userRoleKey);
  }

  isAuthenticated(): boolean {
    return !!this.getAccessToken();
  }
}

