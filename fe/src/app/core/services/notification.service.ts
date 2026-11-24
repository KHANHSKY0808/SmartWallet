import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { NotificationDto, NotificationFilterParameters } from '../models/notification.models';
import { PagedResult } from '../models/transaction.models';

@Injectable({
  providedIn: 'root'
})
export class NotificationService {
  private http = inject(HttpClient);
  private baseUrl = '/api/notifications';

  getNotifications(filter?: NotificationFilterParameters): Observable<PagedResult<NotificationDto>> {
    let params = new HttpParams();
    if (filter?.page) {
      params = params.set('page', filter.page.toString());
    }
    if (filter?.pageSize) {
      params = params.set('pageSize', filter.pageSize.toString());
    }
    return this.http.get<PagedResult<NotificationDto>>(this.baseUrl, { params });
  }

  markAsRead(id: string): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.baseUrl}/${id}/read`, {});
  }

  markAllAsRead(): Observable<{ message: string; count: number }> {
    return this.http.put<{ message: string; count: number }>(`${this.baseUrl}/read-all`, {});
  }
}

