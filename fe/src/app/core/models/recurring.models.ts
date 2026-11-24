export interface RecurringRuleDto {
  id: string;
  userId: string;
  name: string;
  walletId: string;
  categoryId: string;
  type: number; // 1: Chi, 2: Thu
  amount: number;
  currency: string;
  frequency: number; // 1: Daily, 2: Weekly, 3: Monthly, 4: Yearly
  startDate: string;
  endDate?: string | null;
  nextExecutionDate: string;
  anchorDay: number;
  isActive: boolean;
  lastExecutedDate?: string | null;
  lastError?: string | null;
  note?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateRecurringRuleRequest {
  name: string;
  walletId: string;
  categoryId: string;
  type: number;
  amount: number;
  frequency: number;
  startDate: string;
  endDate?: string | null;
  note?: string | null;
}

export interface UpdateRecurringRuleRequest {
  name?: string;
  walletId?: string;
  categoryId?: string;
  type?: number;
  amount?: number;
  frequency?: number;
  startDate?: string;
  endDate?: string | null;
  isActive?: boolean;
  note?: string | null;
}

