import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  SavingGoalDto,
  CreateSavingGoalRequest,
  WithdrawGoalRequest,
  GoalTransferRequest
} from '../models/saving-goal.models';

@Injectable({
  providedIn: 'root'
})
export class SavingGoalService {
  private http = inject(HttpClient);
  private apiUrl = '/api/savings';

  getGoals(status?: string): Observable<SavingGoalDto[]> {
    const url = status ? `${this.apiUrl}?status=${status}` : this.apiUrl;
    return this.http.get<SavingGoalDto[]>(url);
  }

  getGoalById(id: string): Observable<SavingGoalDto> {
    return this.http.get<SavingGoalDto>(`${this.apiUrl}/${id}`);
  }

  createGoal(request: CreateSavingGoalRequest): Observable<SavingGoalDto> {
    return this.http.post<SavingGoalDto>(this.apiUrl, request);
  }

  cancelGoal(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}`);
  }

  // D50: Rút tiền đồng bộ từ SavingGoalService
  withdraw(goalId: string, request: WithdrawGoalRequest, idempotencyKey: string): Observable<SavingGoalDto> {
    const headers = new HttpHeaders({ 'Idempotency-Key': idempotencyKey });
    return this.http.post<SavingGoalDto>(`${this.apiUrl}/${goalId}/withdraw`, request, { headers });
  }

  // D42: Nạp tiền đồng bộ từ ví
  deposit(walletId: string, request: GoalTransferRequest, idempotencyKey: string): Observable<any> {
    const headers = new HttpHeaders({ 'Idempotency-Key': idempotencyKey });
    return this.http.post(`/api/wallets/${walletId}/goal-transfer`, request, { headers });
  }
}
