export enum TransactionType {
  Expense = 1,
  Income = 2,
  Transfer = 3
}

export interface TransactionDto {
  id: string;
  userId: string;
  walletId: string;
  categoryId?: string | null;
  type: number;
  typeName: string;
  amount: number;
  transactionDate: string;
  note?: string | null;
  receiptImageUrl?: string | null;
  transferId?: string | null;
  rowVersion: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateTransactionRequest {
  walletId: string;
  categoryId?: string | null;
  type: number;
  amount: number;
  transactionDate?: string;
  note?: string | null;
}

export interface UpdateTransactionRequest {
  walletId: string;
  categoryId?: string | null;
  type: number;
  amount: number;
  transactionDate: string;
  note?: string | null;
  rowVersion?: string | null;
}

export interface TransactionQueryParameters {
  page?: number;
  pageSize?: number;
  startDate?: string;
  endDate?: string;
  walletId?: string;
  categoryId?: string;
  type?: number;
  minAmount?: number;
  maxAmount?: number;
  keyword?: string;
  sortBy?: string;
  sortOrder?: string;
}

export interface PagedResult<T> {
  items: T[];
  totalItems: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

