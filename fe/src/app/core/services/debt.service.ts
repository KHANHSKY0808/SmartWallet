import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CreateDebtPaymentRequest, CreateDebtRequest, DebtDto, DebtPaymentDto, UpdateDebtRequest } from '../models/debt.models';

@Injectable({
  providedIn: 'root'
})
export class DebtService {
  private http = inject(HttpClient);
  private apiUrl = '/api/debts';

  getDebts(): Observable<DebtDto[]> {
    return this.http.get<DebtDto[]>(this.apiUrl);
  }

  getDebt(id: string): Observable<DebtDto> {
    return this.http.get<DebtDto>(`${this.apiUrl}/${id}`);
  }

  createDebt(req: CreateDebtRequest): Observable<DebtDto> {
    return this.http.post<DebtDto>(this.apiUrl, req);
  }

  updateDebt(id: string, req: UpdateDebtRequest): Observable<DebtDto> {
    return this.http.put<DebtDto>(`${this.apiUrl}/${id}`, req);
  }

  deleteDebt(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`);
  }

  // F-03: Gửi kèm Idempotency-Key
  makePayment(debtId: string, req: CreateDebtPaymentRequest, idempotencyKey: string): Observable<DebtPaymentDto> {
    const headers = new HttpHeaders({
      'Idempotency-Key': idempotencyKey
    });
    return this.http.post<DebtPaymentDto>(`${this.apiUrl}/${debtId}/payments`, req, { headers });
  }

  getPayments(debtId: string): Observable<DebtPaymentDto[]> {
    return this.http.get<DebtPaymentDto[]>(`${this.apiUrl}/${debtId}/payments`);
  }
}

