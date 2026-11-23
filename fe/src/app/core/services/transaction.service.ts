import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  TransactionDto,
  CreateTransactionRequest,
  UpdateTransactionRequest,
  TransactionQueryParameters,
  PagedResult
} from '../models/transaction.models';

@Injectable({
  providedIn: 'root'
})
export class TransactionService {
  private http = inject(HttpClient);
  private apiUrl = '/api/transactions';

  getTransactions(params?: TransactionQueryParameters): Observable<PagedResult<TransactionDto>> {
    let httpParams = new HttpParams();

    if (params) {
      if (params.page !== undefined) httpParams = httpParams.set('page', params.page.toString());
      if (params.pageSize !== undefined) httpParams = httpParams.set('pageSize', params.pageSize.toString());
      if (params.startDate) httpParams = httpParams.set('startDate', params.startDate);
      if (params.endDate) httpParams = httpParams.set('endDate', params.endDate);
      if (params.walletId) httpParams = httpParams.set('walletId', params.walletId);
      if (params.categoryId) httpParams = httpParams.set('categoryId', params.categoryId);
      if (params.type !== undefined) httpParams = httpParams.set('type', params.type.toString());
      if (params.minAmount !== undefined) httpParams = httpParams.set('minAmount', params.minAmount.toString());
      if (params.maxAmount !== undefined) httpParams = httpParams.set('maxAmount', params.maxAmount.toString());
      if (params.keyword) httpParams = httpParams.set('keyword', params.keyword);
      if (params.sortBy) httpParams = httpParams.set('sortBy', params.sortBy);
      if (params.sortOrder) httpParams = httpParams.set('sortOrder', params.sortOrder);
    }

    return this.http.get<PagedResult<TransactionDto>>(this.apiUrl, { params: httpParams });
  }

  getTransaction(id: string): Observable<TransactionDto> {
    return this.http.get<TransactionDto>(`${this.apiUrl}/${id}`);
  }

  createTransaction(request: CreateTransactionRequest): Observable<TransactionDto> {
    return this.http.post<TransactionDto>(this.apiUrl, request);
  }

  updateTransaction(id: string, request: UpdateTransactionRequest): Observable<TransactionDto> {
    return this.http.put<TransactionDto>(`${this.apiUrl}/${id}`, request);
  }

  deleteTransaction(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`);
  }
}

