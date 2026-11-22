export interface WalletDto {
  id: string;
  userId: string;
  name: string;
  type: number; // 1=Cash, 2=Bank, 3=EWallet, 4=CreditCard
  balance: number;
  initialBalance: number;
  currency: string;
  isDefault: boolean;
  isArchived: boolean;
  archivedAt?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateWalletRequest {
  name: string;
  type: number;
  initialBalance: number;
  currency?: string;
}

export interface UpdateWalletRequest {
  name: string;
  currency?: string;
  balance?: number;
}

export interface ExcludedWalletDto {
  id: string;
  name: string;
  currency: string;
  balance: number;
}

export interface TotalAssetsResponse {
  totalAssetsVnd: number;
  ratesUnavailable: boolean;
  excludedWallets: ExcludedWalletDto[];
}

export interface TransferRequest {
  fromWalletId: string;
  toWalletId: string;
  amount: number;
  fee?: number;
  note?: string;
}

export interface TransferResponse {
  transferId: string;
  newSourceBalance: number;
  newTargetBalance: number;
}

