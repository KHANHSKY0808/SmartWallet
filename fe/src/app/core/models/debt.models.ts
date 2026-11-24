export interface DebtDto {
  id: string;
  userId: string;
  type: number; // 1: Cho vay (Lend), 2: Đi vay (Borrow)
  typeName: string;
  personName: string;
  amount: number;
  paidAmount: number;
  remainingAmount: number;
  interestRate?: number | null;
  currency: string;
  dueDate: string;
  status: string; // "Active", "PaidFull", "Overdue"
  statusCode: number;
  walletId: string;
  note?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateDebtRequest {
  type: number;
  personName: string;
  amount: number;
  dueDate: string;
  walletId: string;
  interestRate?: number | null;
  note?: string | null;
}

export interface UpdateDebtRequest {
  amount?: number;
  personName?: string;
  dueDate?: string;
  interestRate?: number | null;
  note?: string | null;
}

export interface CreateDebtPaymentRequest {
  amount: number;
  walletId: string;
  note?: string | null;
}

export interface DebtPaymentDto {
  id: string;
  debtId: string;
  userId: string;
  walletId: string;
  amount: number;
  paymentDate: string;
  note?: string | null;
  idempotencyKey?: string | null;
  createdAt: string;
}

