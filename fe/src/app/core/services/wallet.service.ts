import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  WalletDto,
  CreateWalletRequest,
  UpdateWalletRequest,
  TotalAssetsResponse,
  TransferRequest,
  TransferResponse
} from '../models/wallet.models';

@Injectable({
  providedIn: 'root'
})
export class WalletService {
  private http = inject(HttpClient);
  private apiUrl = '/api/wallets';

  getWallets(includeArchived: boolean = false): Observable<WalletDto[]> {
    return this.http.get<WalletDto[]>(this.apiUrl, {
      params: { includeArchived: includeArchived.toString() }
    });
  }

  getWallet(id: string): Observable<WalletDto> {
    return this.http.get<WalletDto>(`${this.apiUrl}/${id}`);
  }

  createWallet(request: CreateWalletRequest): Observable<WalletDto> {
    return this.http.post<WalletDto>(this.apiUrl, request);
  }

  updateWallet(id: string, request: UpdateWalletRequest): Observable<WalletDto> {
    return this.http.put<WalletDto>(`${this.apiUrl}/${id}`, request);
  }

  deleteWallet(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`);
  }

  getTotalAssets(): Observable<TotalAssetsResponse> {
    return this.http.get<TotalAssetsResponse>(`${this.apiUrl}/total-assets`);
  }

  transfer(request: TransferRequest, idempotencyKey: string): Observable<TransferResponse> {
    const headers = new HttpHeaders({
      'Idempotency-Key': idempotencyKey
    });
    return this.http.post<TransferResponse>(`${this.apiUrl}/transfer`, request, { headers });
  }
}

