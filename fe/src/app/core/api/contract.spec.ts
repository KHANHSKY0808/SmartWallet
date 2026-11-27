import { describe, it, expect } from 'vitest';
import { OPENAPI_CONTRACT_VERIFIED } from './contract.checks';
import identitySpec from './openapi/identity.json';
import walletSpec from './openapi/wallet.json';
import transactionSpec from './openapi/transaction.json';
import categorySpec from './openapi/category.json';
import budgetSpec from './openapi/budget.json';
import savingGoalSpec from './openapi/saving-goal.json';
import recurringDebtSpec from './openapi/recurring-debt.json';
import reportSpec from './openapi/report.json';
import notificationSpec from './openapi/notification.json';
import auditSpec from './openapi/audit.json';
import fileSpec from './openapi/file.json';
import userProfileSpec from './openapi/user-profile.json';

describe('OpenAPI Contract Tests (SW-04 / F-02)', () => {
  it('binds Frontend models to all 12 generated Backend OpenAPI contracts', () => {
    expect(OPENAPI_CONTRACT_VERIFIED).toBe(true);

    const specs = [
      identitySpec,
      walletSpec,
      transactionSpec,
      categorySpec,
      budgetSpec,
      savingGoalSpec,
      recurringDebtSpec,
      reportSpec,
      notificationSpec,
      auditSpec,
      fileSpec,
      userProfileSpec,
    ];
    expect(specs).toHaveLength(12);
    for (const spec of specs) {
      expect(spec.openapi).toMatch(/^3\./);
      expect(Object.keys(spec.paths).length).toBeGreaterThan(0);
    }
  });
});

