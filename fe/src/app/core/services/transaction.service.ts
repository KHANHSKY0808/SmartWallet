import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  TransactionDto,
  CreateTransactionRequest,
  UpdateTransactionRequest,
  TransactionQueryParameters,
  PagedResult,
  TagDto
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
      if (params.tagIds) httpParams = httpParams.set('tagIds', params.tagIds);
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

  uploadReceipt(file: File): Observable<{ id: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<{ id: string }>('/api/files/receipts', formData);
  }

  getTags(): Observable<TagDto[]> {
    return this.http.get<TagDto[]>('/api/transactions/tags');
  }

  createTag(name: string, color?: string): Observable<TagDto> {
    return this.http.post<TagDto>('/api/transactions/tags', { name, color });
  }

  uploadImport(file: File, mapping?: Record<string, string>): Observable<{ jobId: string; status: string }> {
    const formData = new FormData();
    formData.append('file', file);
    if (mapping) {
      formData.append('mapping', JSON.stringify(mapping));
    }
    return this.http.post<{ jobId: string; status: string }>('/api/transactions/imports', formData);
  }

  getImportStatus(jobId: string): Observable<ImportJobStatusDto> {
    return this.http.get<ImportJobStatusDto>(`/api/transactions/imports/${jobId}`);
  }

  downloadImportErrors(jobId: string): Observable<Blob> {
    return this.http.get(`/api/transactions/imports/${jobId}/errors.csv`, { responseType: 'blob' });
  }
}

export interface ImportJobErrorItem {
  row: number;
  reason: string;
}

export interface ImportJobStatusDto {
  id: string;
  jobId: string;
  fileName: string;
  status: 'Queued' | 'Processing' | 'Completed' | 'Failed';
  totalRows: number;
  succeeded: number;
  failed: number;
  errorMessage?: string | null;
  errors: ImportJobErrorItem[];
  createdAt: string;
  completedAt?: string | null;
}

