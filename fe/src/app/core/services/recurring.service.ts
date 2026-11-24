import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CreateRecurringRuleRequest, RecurringRuleDto, UpdateRecurringRuleRequest } from '../models/recurring.models';

@Injectable({
  providedIn: 'root'
})
export class RecurringService {
  private http = inject(HttpClient);
  private apiUrl = '/api/recurring';

  getRules(): Observable<RecurringRuleDto[]> {
    return this.http.get<RecurringRuleDto[]>(this.apiUrl);
  }

  getRule(id: string): Observable<RecurringRuleDto> {
    return this.http.get<RecurringRuleDto>(`${this.apiUrl}/${id}`);
  }

  createRule(req: CreateRecurringRuleRequest): Observable<RecurringRuleDto> {
    return this.http.post<RecurringRuleDto>(this.apiUrl, req);
  }

  updateRule(id: string, req: UpdateRecurringRuleRequest): Observable<RecurringRuleDto> {
    return this.http.put<RecurringRuleDto>(`${this.apiUrl}/${id}`, req);
  }

  deleteRule(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`);
  }

  // F-01: Tính trước 3 kỳ kế tiếp theo quy tắc ngày neo (anchorDay)
  previewNextOccurrences(startDateStr: string, freq: number, count: number = 3): string[] {
    if (!startDateStr) return [];
    const [y, m, d] = startDateStr.split('-').map(Number);
    if (!y || !m || !d) return [];

    const occurrences: string[] = [];
    let current = new Date(y, m - 1, d);
    const anchorDay = d;

    for (let i = 0; i < count; i++) {
      if (freq === 1) { // Daily
        current = new Date(current.getFullYear(), current.getMonth(), current.getDate() + 1);
      } else if (freq === 2) { // Weekly
        current = new Date(current.getFullYear(), current.getMonth(), current.getDate() + 7);
      } else if (freq === 3) { // Monthly
        const nextMonth = current.getMonth() + 1;
        const nextYear = current.getFullYear() + Math.floor(nextMonth / 12);
        const adjustedMonth = nextMonth % 12;
        const daysInMonth = new Date(nextYear, adjustedMonth + 1, 0).getDate();
        const day = Math.min(anchorDay, daysInMonth);
        current = new Date(nextYear, adjustedMonth, day);
      } else if (freq === 4) { // Yearly
        const nextYear = current.getFullYear() + 1;
        const month = m - 1;
        const daysInMonth = new Date(nextYear, month + 1, 0).getDate();
        const day = Math.min(anchorDay, daysInMonth);
        current = new Date(nextYear, month, day);
      }
      const pad = (n: number) => n.toString().padStart(2, '0');
      occurrences.push(`${current.getFullYear()}-${pad(current.getMonth() + 1)}-${pad(current.getDate())}`);
    }

    return occurrences;
  }
}
