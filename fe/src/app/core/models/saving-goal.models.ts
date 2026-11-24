export interface SavingGoalDto {
  id: string;
  userId?: string;
  name: string;
  targetAmount: number;
  currentAmount: number;
  percent: number;
  currency: string;
  targetDate: string;
  status: string;
  statusCode: number;
  completedAt?: string | null;
  daysLeft: number;
  monthlyRequired: number;
  rowVersion?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface CreateSavingGoalRequest {
  name: string;
  targetAmount: number;
  targetDate: string;
  currency?: string;
}

export interface WithdrawGoalRequest {
  walletId: string;
  amount: number;
}

export interface GoalTransferRequest {
  goalId: string;
  amount: number;
  direction: 'deposit';
}
