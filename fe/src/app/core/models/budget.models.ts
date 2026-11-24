export interface BudgetDto {
  id: string;
  userId: string;
  categoryId: string;
  amount: number;
  month: number;
  year: number;
  currentSpent: number;
  percent: number;
  status: 'normal' | 'warning' | 'exceeded';
  isAlertSent80: boolean;
  isAlertSent100: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateBudgetRequest {
  categoryId: string;
  amount: number;
  month: number;
  year: number;
}

export interface UpdateBudgetRequest {
  amount: number;
}

export interface BudgetFilterParameters {
  month?: number;
  year?: number;
}

