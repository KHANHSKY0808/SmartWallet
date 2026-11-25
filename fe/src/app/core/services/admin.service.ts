import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface AdminUserItem {
  id: string;
  email: string;
  fullName: string;
  status: string;
  roles: string[];
  lockReason?: string | null;
  lockedAt?: string | null;
  createdAt: string;
}

export interface RoleMatrixItem {
  id: number;
  name: string;
  description?: string;
  permissions: string[];
}

export interface AuditLogItem {
  id: number;
  eventId?: string | null;
  userId?: string | null;
  serviceName: string;
  action: string;
  entityType?: string | null;
  entityId?: string | null;
  newValueJson?: string | null;
  correlationId?: string | null;
  timestamp: string;
}

export interface PagedResponse<T> {
  items: T[];
  totalItems: number;
  totalCount: number;
  page: number;
  pageSize: number;
}

@Injectable({
  providedIn: 'root'
})
export class AdminService {
  private http = inject(HttpClient);

  getUsers(params: { q?: string; status?: string; page?: number; pageSize?: number } = {}): Observable<PagedResponse<AdminUserItem>> {
    let httpParams = new HttpParams();
    if (params.q) httpParams = httpParams.set('q', params.q);
    if (params.status) httpParams = httpParams.set('status', params.status);
    if (params.page) httpParams = httpParams.set('page', params.page);
    if (params.pageSize) httpParams = httpParams.set('pageSize', params.pageSize);
    return this.http.get<PagedResponse<AdminUserItem>>('/api/admin/users', { params: httpParams });
  }

  lockUser(id: string, reason: string): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`/api/admin/users/${id}/lock`, { reason });
  }

  unlockUser(id: string): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`/api/admin/users/${id}/unlock`, {});
  }

  getRoles(): Observable<RoleMatrixItem[]> {
    return this.http.get<RoleMatrixItem[]>('/api/admin/roles');
  }

  updateUserRoles(id: string, roles: string[]): Observable<{ userId: string; roles: string[] }> {
    return this.http.put<{ userId: string; roles: string[] }>(`/api/admin/users/${id}/roles`, { roles });
  }

  getAuditLogs(params: {
    userId?: string;
    action?: string;
    serviceName?: string;
    from?: string;
    to?: string;
    page?: number;
    pageSize?: number;
  } = {}): Observable<PagedResponse<AuditLogItem>> {
    let httpParams = new HttpParams();
    if (params.userId) httpParams = httpParams.set('userId', params.userId);
    if (params.action) httpParams = httpParams.set('action', params.action);
    if (params.serviceName) httpParams = httpParams.set('serviceName', params.serviceName);
    if (params.from) httpParams = httpParams.set('from', params.from);
    if (params.to) httpParams = httpParams.set('to', params.to);
    if (params.page) httpParams = httpParams.set('page', params.page);
    if (params.pageSize) httpParams = httpParams.set('pageSize', params.pageSize);
    return this.http.get<PagedResponse<AuditLogItem>>('/api/audit/logs', { params: httpParams });
  }
}

