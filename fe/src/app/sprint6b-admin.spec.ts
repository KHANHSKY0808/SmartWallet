import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpRequest } from '@angular/common/http';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AdminUsersComponent } from './features/admin/admin-users/admin-users.component';
import { AdminAuditComponent } from './features/admin/admin-audit/admin-audit.component';
import { LoginComponent } from './features/auth/login/login.component';
import { NotificationBellComponent } from './shared/components/notification-bell/notification-bell.component';
import { AdminService } from './core/services/admin.service';
import { AuthService } from './core/services/auth.service';
import { NotificationService } from './core/services/notification.service';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { permissionGuard } from './core/guards/role.guard';
import { App } from './app';

function createJwt(payload: Record<string, unknown>): string {
  const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const body = btoa(JSON.stringify(payload));
  return `${header}.${body}.sig`;
}

describe('Sprint 6b Admin, RBAC, Audit, Notifications & 360px UI (F-01..F-06 & B6A-3)', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('F-01: Notification bell shows unread badge, marks single read and marks all as read', () => {
    const notiMock = {
      getUnreadCount: vi.fn().mockReturnValue(of({ count: 2 })),
      getNotifications: vi.fn().mockReturnValue(of({
        items: [
          { id: '1', userId: 'u1', type: 'MoneyTransferred', title: 'Chuyển tiền', message: '1000', isRead: false, createdAt: '2026-11-25T10:00:00Z' },
          { id: '2', userId: 'u1', type: 'BudgetAlert', title: 'Ngân sách', message: '80%', isRead: false, createdAt: '2026-11-25T09:00:00Z' }
        ],
        totalItems: 2, pageNumber: 1, pageSize: 20, totalPages: 1
      })),
      markAsRead: vi.fn().mockReturnValue(of({ message: 'OK' })),
      markAllAsRead: vi.fn().mockReturnValue(of({ message: 'OK', count: 1 }))
    };

    TestBed.configureTestingModule({
      imports: [NotificationBellComponent],
      providers: [{ provide: NotificationService, useValue: notiMock }]
    });

    const fixture = TestBed.createComponent(NotificationBellComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();

    expect(comp.unreadCount()).toBe(2);
    comp.onNotificationClick(comp.notifications()[0]);
    expect(notiMock.markAsRead).toHaveBeenCalledWith('1');
    expect(comp.unreadCount()).toBe(1);

    comp.markAllAsRead();
    expect(notiMock.markAllAsRead).toHaveBeenCalled();
    expect(comp.unreadCount()).toBe(0);
  });

  it('F-02 & F-03: AdminUsersComponent searches, opens lock confirmation, displays 409 errors and role matrix', () => {
    const adminMock = {
      getRoles: vi.fn().mockReturnValue(of([
        { id: 1, name: 'Admin', permissions: ['users.read', 'users.lock'] },
        { id: 3, name: 'Support', permissions: ['users.read', 'audit.read'] },
        { id: 2, name: 'User', permissions: [] }
      ])),
      getUsers: vi.fn().mockReturnValue(of({
        items: [{ id: 'u1', email: 'u1@test.local', fullName: 'User 1', status: 'Active', roles: ['User'], createdAt: '2026-11-25T00:00:00Z' }],
        totalItems: 1, totalCount: 1, page: 1, pageSize: 20
      })),
      lockUser: vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 409,
        error: { code: 'CANNOT_LOCK_SELF', message: 'Không thể tự khóa tài khoản của chính mình.' }
      }))),
      unlockUser: vi.fn(),
      updateUserRoles: vi.fn().mockReturnValue(of({ userId: 'u1', roles: ['Support'] }))
    };

    const authStub = { hasPermission: vi.fn().mockReturnValue(true) };

    TestBed.configureTestingModule({
      imports: [AdminUsersComponent],
      providers: [
        { provide: AdminService, useValue: adminMock },
        { provide: AuthService, useValue: authStub }
      ]
    });

    const fixture = TestBed.createComponent(AdminUsersComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();

    expect(comp.roles().length).toBe(3);
    expect(comp.users().length).toBe(1);

    comp.openLockConfirm(comp.users()[0]);
    expect(comp.lockTarget()?.id).toBe('u1');
    comp.confirmLock();
    expect(comp.errorMsg()).toContain('Không thể tự khóa');

    comp.onRoleSelect(comp.users()[0], 'Support');
    expect(adminMock.updateUserRoles).toHaveBeenCalledWith('u1', ['Support']);
  });

  it('F-04: AdminAuditComponent filters and paginates audit logs', () => {
    const adminMock = {
      getAuditLogs: vi.fn().mockReturnValue(of({
        items: [
          { id: 1, serviceName: 'TransactionService', action: 'TransactionCreatedEvent', userId: 'u1', entityId: 'tx1', newValueJson: '{"email":"a***@qa.local"}', timestamp: '2026-11-25T10:00:00Z' }
        ],
        totalItems: 25, totalCount: 25, page: 1, pageSize: 20
      }))
    };

    TestBed.configureTestingModule({
      imports: [AdminAuditComponent],
      providers: [{ provide: AdminService, useValue: adminMock }]
    });

    const fixture = TestBed.createComponent(AdminAuditComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();

    expect(comp.logs().length).toBe(1);
    comp.actionFilter = 'UserLocked';
    comp.applyFilter();
    expect(adminMock.getAuditLogs).toHaveBeenLastCalledWith(expect.objectContaining({ action: 'UserLocked', page: 1 }));
    comp.nextPage();
    expect(comp.page).toBe(2);
  });

  it('F-05: Admin menu hidden for regular User and permissionGuard blocks direct URL navigation', () => {
    const userToken = createJwt({ sub: 'u1', role: 'User', perm: [] });
    localStorage.setItem('access_token', userToken);
    localStorage.setItem('user_role', 'User');

    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])]
    });

    const authService = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);
    const navSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const html = (fixture.nativeElement as HTMLElement).innerHTML;
    expect(html).not.toContain('/admin/users');
    expect(html).not.toContain('/admin/audit');
    expect(html).not.toContain('/admin/health');

    const guardFn = permissionGuard(['users.read']);
    const allowed = TestBed.runInInjectionContext(() => guardFn({} as any, {} as any));
    expect(allowed).toBe(false);
    expect(navSpy).toHaveBeenCalledWith(['/wallets']);

    expect(authService.hasPermission('users.read')).toBe(false);
  });

  it('F-06 (6b): Redirects locked/revoked user on 401 to /login with friendly message', () => {
    localStorage.setItem('access_token', createJwt({ sub: 'u1', role: 'User' }));

    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: (k: string) => (k === 'reason' ? 'revoked' : null) } } } }
      ]
    });

    const authService = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);
    const navSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    const req = new HttpRequest('GET', '/api/wallets');
    const next = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));

    TestBed.runInInjectionContext(() => {
      authInterceptor(req, next).subscribe({ error: () => {} });
    });

    expect(authService.isAuthenticated()).toBe(false);
    expect(authService.sessionNotice()).toContain('Phiên đăng nhập');
    expect(navSpy).toHaveBeenCalledWith(['/login'], { queryParams: { reason: 'revoked' } });

    const loginFixture = TestBed.createComponent(LoginComponent);
    loginFixture.detectChanges();
    expect((loginFixture.nativeElement as HTMLElement).textContent).toContain('Phiên đăng nhập đã hết hiệu lực');
  });

  it('F-06 (B6A-3): Layout container enforces overflow-x hidden & wrapping at 360px mobile viewport', () => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])]
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const rootDiv = (fixture.nativeElement as HTMLElement).firstElementChild as HTMLElement;
    expect(rootDiv.style.overflowX).toBe('hidden');
    expect(rootDiv.style.maxWidth).toBe('100%');
    const headerNav = rootDiv.querySelector('nav') as HTMLElement;
    expect(headerNav.style.flexWrap).toBe('wrap');
  });
});

