import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { BudgetDto, CreateBudgetRequest, UpdateBudgetRequest, BudgetFilterParameters } from '../models/budget.models';

@Injectable({
  providedIn: 'root'
})
export class BudgetService {
  private http = inject(HttpClient);
  private baseUrl = '/api/budgets';

  getBudgets(filter?: BudgetFilterParameters): Observable<BudgetDto[]> {
    let params = new HttpParams();
    if (filter?.month) {
      params = params.set('month', filter.month.toString());
    }
    if (filter?.year) {
      params = params.set('year', filter.year.toString());
    }
    return this.http.get<BudgetDto[]>(this.baseUrl, { params });
  }

  getBudget(id: string): Observable<BudgetDto> {
    return this.http.get<BudgetDto>(`${this.baseUrl}/${id}`);
  }

  createBudget(request: CreateBudgetRequest): Observable<BudgetDto> {
    return this.http.post<BudgetDto>(this.baseUrl, request);
  }

  updateBudget(id: string, request: UpdateBudgetRequest): Observable<BudgetDto> {
    return this.http.put<BudgetDto>(`${this.baseUrl}/${id}`, request);
  }

  deleteBudget(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.baseUrl}/${id}`);
  }
}

