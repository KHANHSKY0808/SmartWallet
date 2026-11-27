/**
 * Compile-time OpenAPI Contract Verification (D74 / SW-04 / F-02).
 * If a Backend DTO property or path is renamed/removed without updating Frontend models,
 * TypeScript (both ng build and ng test / tsc) fails immediately.
 */
import type {
  IdentityComponents,
  IdentityPaths,
  WalletComponents,
  WalletPaths,
  CategoryComponents,
  CategoryPaths,
  TransactionComponents,
  TransactionPaths,
  BudgetComponents,
  BudgetPaths,
  SavingGoalComponents,
  SavingGoalPaths,
  RecurringDebtComponents,
  RecurringDebtPaths,
  ReportComponents,
  ReportPaths,
  NotificationPaths,
  AuditPaths,
  FilePaths,
  UserProfileComponents,
  UserProfilePaths,
} from './generated';

import type {
  RegisterRequest,
  LoginRequest,
  RefreshTokenRequest,
  ForgotPasswordRequest,
  ResetPasswordRequest,
  ChangePasswordRequest,
} from '../models/auth.models';
import type {
  CreateWalletRequest,
  UpdateWalletRequest,
  TransferRequest,
} from '../models/wallet.models';
import type {
  CreateCategoryRequest,
  UpdateCategoryRequest,
} from '../models/category.models';
import type {
  CreateTransactionRequest,
  UpdateTransactionRequest,
} from '../models/transaction.models';
import type {
  CreateBudgetRequest,
  UpdateBudgetRequest,
} from '../models/budget.models';
import type {
  CreateSavingGoalRequest,
  WithdrawGoalRequest,
} from '../models/saving-goal.models';
import type {
  CreateDebtRequest,
  UpdateDebtRequest,
  CreateDebtPaymentRequest,
} from '../models/debt.models';
import type { CreateExportRequest } from '../models/report.models';

type AssertExactKeys<FE, BE> = Exclude<keyof FE, keyof BE> extends never
  ? Exclude<keyof BE, keyof FE> extends never
    ? true
    : ['BE has extra/renamed field(s) missing on FE:', Exclude<keyof BE, keyof FE>]
  : ['FE has extra/renamed field(s) missing on BE:', Exclude<keyof FE, keyof BE>];

type AssertPath<Paths, P extends keyof Paths> = P;

export type ContractChecks = [
  // Identity
  AssertExactKeys<RegisterRequest, IdentityComponents['schemas']['RegisterRequest']>,
  AssertExactKeys<LoginRequest, IdentityComponents['schemas']['LoginRequest']>,
  AssertExactKeys<RefreshTokenRequest, IdentityComponents['schemas']['RefreshTokenRequest']>,
  AssertExactKeys<ForgotPasswordRequest, IdentityComponents['schemas']['ForgotPasswordRequest']>,
  AssertExactKeys<ResetPasswordRequest, IdentityComponents['schemas']['ResetPasswordRequest']>,
  AssertExactKeys<ChangePasswordRequest, IdentityComponents['schemas']['ChangePasswordRequest']>,
  AssertPath<IdentityPaths, '/api/auth/login'>,
  AssertPath<IdentityPaths, '/api/auth/register'>,
  AssertPath<IdentityPaths, '/api/auth/resend-confirmation'>,

  // Wallet
  AssertExactKeys<CreateWalletRequest, WalletComponents['schemas']['CreateWalletRequest']>,
  AssertExactKeys<UpdateWalletRequest, WalletComponents['schemas']['UpdateWalletRequest']>,
  AssertExactKeys<TransferRequest, WalletComponents['schemas']['TransferRequest']>,
  AssertPath<WalletPaths, '/api/Wallets'>,
  AssertPath<WalletPaths, '/api/Wallets/transfer'>,

  // Category
  AssertExactKeys<CreateCategoryRequest, CategoryComponents['schemas']['CreateCategoryRequest']>,
  AssertExactKeys<UpdateCategoryRequest, CategoryComponents['schemas']['UpdateCategoryRequest']>,
  AssertPath<CategoryPaths, '/api/Categories'>,

  // Transaction
  AssertExactKeys<CreateTransactionRequest, TransactionComponents['schemas']['CreateTransactionRequest']>,
  AssertExactKeys<UpdateTransactionRequest, TransactionComponents['schemas']['UpdateTransactionRequest']>,
  AssertPath<TransactionPaths, '/api/Transactions'>,
  AssertPath<TransactionPaths, '/api/transactions/imports'>,

  // Budget
  AssertExactKeys<CreateBudgetRequest, BudgetComponents['schemas']['CreateBudgetRequest']>,
  AssertExactKeys<UpdateBudgetRequest, BudgetComponents['schemas']['UpdateBudgetRequest']>,
  AssertPath<BudgetPaths, '/api/Budgets'>,

  // SavingGoal
  AssertExactKeys<CreateSavingGoalRequest, SavingGoalComponents['schemas']['CreateSavingGoalRequest']>,
  AssertExactKeys<WithdrawGoalRequest, SavingGoalComponents['schemas']['WithdrawRequest']>,
  AssertPath<SavingGoalPaths, '/api/savings'>,

  // RecurringDebt
  AssertExactKeys<CreateDebtRequest, RecurringDebtComponents['schemas']['CreateDebtRequest']>,
  AssertExactKeys<UpdateDebtRequest, RecurringDebtComponents['schemas']['UpdateDebtRequest']>,
  AssertExactKeys<CreateDebtPaymentRequest, RecurringDebtComponents['schemas']['CreateDebtPaymentRequest']>,
  AssertPath<RecurringDebtPaths, '/api/debts'>,
  AssertPath<RecurringDebtPaths, '/api/recurring'>,

  // Report, Notification, Audit, File, UserProfile
  AssertExactKeys<CreateExportRequest, ReportComponents['schemas']['CreateExportRequest']>,
  AssertPath<ReportPaths, '/api/reports/dashboard'>,
  AssertPath<ReportPaths, '/api/reports/exports'>,
  AssertPath<NotificationPaths, '/api/Notifications'>,
  AssertPath<AuditPaths, '/api/audit/logs'>,
  AssertPath<FilePaths, '/api/Files/receipts'>,
  AssertPath<UserProfilePaths, '/api/profile'>,
  UserProfileComponents['schemas']['UpdateProfileRequest'],
];

// Compile-time check: every element in ContractChecks must strictly evaluate to `true` (not error tuple)
export type AssertAllContractsValid = Extract<ContractChecks[number], [string, unknown]> extends never
  ? true
  : never;

export const OPENAPI_CONTRACT_VERIFIED: AssertAllContractsValid = true;
