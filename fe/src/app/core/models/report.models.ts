export interface CategoryBreakdownDto {
  categoryId: string | null;
  categoryName: string;
  amount: number;
  percent: number;
}

export interface TopExpenseDto {
  transactionId: string;
  categoryId: string | null;
  categoryName: string;
  amount: number;
  originalAmount: number;
  currency: string;
  transactionDate: string;
  note: string | null;
}

export interface DashboardBudgetDto {
  budgetId: string;
  categoryName: string;
  limit: number;
  spent: number;
  percent: number;
}

export interface DashboardResponse {
  month: number;
  year: number;
  totalIncome: number;
  totalExpense: number;
  net: number;
  byCategory: CategoryBreakdownDto[];
  topExpenses: TopExpenseDto[];
  budgets: DashboardBudgetDto[] | null;
  budgetsUnavailable: boolean;
  excludedCurrencies: string[];
}

export interface AnalysisPointDto {
  period: string;
  income: number;
  expense: number;
  net: number;
}

export interface AnalysisTotalsDto {
  income: number;
  expense: number;
  net: number;
}

export interface PreviousPeriodDto {
  from: string;
  to: string;
  totals: AnalysisTotalsDto;
}

export interface PeriodChangeDto {
  income: number | null;
  expense: number | null;
  net: number | null;
}

export interface AnalysisResponse {
  from: string;
  to: string;
  groupBy: 'day' | 'week' | 'month' | 'year';
  points: AnalysisPointDto[];
  totals: AnalysisTotalsDto;
  previous: PreviousPeriodDto | null;
  change: PeriodChangeDto | null;
  excludedCurrencies: string[];
}

export interface CreateExportRequest {
  format: 'xlsx' | 'pdf';
  from: string;
  to: string;
}

export interface ExportJobDto {
  jobId: string;
  id?: string;
  format: 'xlsx' | 'pdf';
  from: string;
  to: string;
  status: 'Queued' | 'Processing' | 'Completed' | 'Failed';
  errorMessage?: string | null;
  createdAt: string;
  completedAt?: string | null;
  expiresAt?: string | null;
}

export interface ServiceHealthItem {
  service: string;
  status: 'Healthy' | 'Degraded' | 'Unhealthy';
  latencyMs: number;
  checkedAt: string;
}

