import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'wallets'
  },
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'register',
    loadComponent: () => import('./features/auth/register/register.component').then(m => m.RegisterComponent)
  },
  {
    path: 'confirm-email',
    loadComponent: () => import('./features/auth/confirm-email/confirm-email.component').then(m => m.ConfirmEmailComponent)
  },
  {
    path: 'forgot-password',
    loadComponent: () => import('./features/auth/forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent)
  },
  {
    path: 'reset-password',
    loadComponent: () => import('./features/auth/reset-password/reset-password.component').then(m => m.ResetPasswordComponent)
  },
  {
    path: 'profile',
    canActivate: [authGuard],
    loadComponent: () => import('./features/profile/profile.component').then(m => m.ProfileComponent)
  },
  {
    path: 'wallets',
    canActivate: [authGuard],
    loadComponent: () => import('./features/wallets/wallet-list/wallet-list.component').then(m => m.WalletListComponent)
  },
  {
    path: 'wallets/create',
    canActivate: [authGuard],
    loadComponent: () => import('./features/wallets/wallet-form/wallet-form.component').then(m => m.WalletFormComponent)
  },
  {
    path: 'wallets/transfer',
    canActivate: [authGuard],
    loadComponent: () => import('./features/wallets/transfer-form/transfer-form.component').then(m => m.TransferFormComponent)
  },
  {
    path: 'categories',
    canActivate: [authGuard],
    loadComponent: () => import('./features/categories/category-tree/category-tree.component').then(m => m.CategoryTreeComponent)
  },
  {
    path: 'transactions',
    canActivate: [authGuard],
    loadComponent: () => import('./features/transactions/transaction-list/transaction-list.component').then(m => m.TransactionListComponent)
  },
  {
    path: 'transactions/create',
    canActivate: [authGuard],
    loadComponent: () => import('./features/transactions/transaction-form/transaction-form.component').then(m => m.TransactionFormComponent)
  },
  {
    path: 'transactions/edit/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./features/transactions/transaction-form/transaction-form.component').then(m => m.TransactionFormComponent)
  },
  {
    path: 'budgets',
    canActivate: [authGuard],
    loadComponent: () => import('./features/budgets/budget-list/budget-list.component').then(m => m.BudgetListComponent)
  },
  {
    path: 'budgets/create',
    canActivate: [authGuard],
    loadComponent: () => import('./features/budgets/budget-form/budget-form.component').then(m => m.BudgetFormComponent)
  },
  {
    path: 'budgets/edit/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./features/budgets/budget-form/budget-form.component').then(m => m.BudgetFormComponent)
  },
  {
    path: 'savings',
    canActivate: [authGuard],
    loadComponent: () => import('./features/savings/saving-goal-list/saving-goal-list.component').then(m => m.SavingGoalListComponent)
  },
  {
    path: 'recurring',
    canActivate: [authGuard],
    loadComponent: () => import('./features/recurring/recurring-list/recurring-list.component').then(m => m.RecurringListComponent)
  },
  {
    path: 'debts',
    canActivate: [authGuard],
    loadComponent: () => import('./features/debts/debt-list/debt-list.component').then(m => m.DebtListComponent)
  },
  {
    path: '**',
    redirectTo: 'wallets'
  }
];
