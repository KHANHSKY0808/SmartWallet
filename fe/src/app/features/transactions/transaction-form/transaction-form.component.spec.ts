import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { TransactionFormComponent } from './transaction-form.component';
import { TransactionService } from '../../../core/services/transaction.service';
import { WalletService } from '../../../core/services/wallet.service';
import { CategoryService } from '../../../core/services/category.service';
import { WalletDto } from '../../../core/models/wallet.models';
import { CategoryDto } from '../../../core/models/category.models';

describe('TransactionFormComponent', () => {
  let component: TransactionFormComponent;
  let fixture: ComponentFixture<TransactionFormComponent>;
  let transactionServiceMock: {
    getTransaction: ReturnType<typeof vi.fn>;
    createTransaction: ReturnType<typeof vi.fn>;
    updateTransaction: ReturnType<typeof vi.fn>;
  };
  let walletServiceMock: {
    getWallets: ReturnType<typeof vi.fn>;
  };
  let categoryServiceMock: {
    getCategories: ReturnType<typeof vi.fn>;
  };

  const mockWallets: WalletDto[] = [
    {
      id: 'w1',
      userId: 'u1',
      name: 'Ví Tiền Mặt',
      type: 1,
      balance: 1000000,
      initialBalance: 1000000,
      currency: 'VND',
      isDefault: true,
      isArchived: false,
      createdAt: '2026-11-20',
      updatedAt: '2026-11-20'
    }
  ];

  const mockCategories: CategoryDto[] = [
    {
      id: 'c1',
      name: 'Ăn uống',
      type: 1,
      icon: '🍔',
      color: '#ef4444',
      isDefault: true,
      isActive: true,
      sortOrder: 1,
      children: []
    },
    {
      id: 'c2',
      name: 'Lương',
      type: 2,
      icon: '💵',
      color: '#22c55e',
      isDefault: true,
      isActive: true,
      sortOrder: 2,
      children: []
    }
  ];

  beforeEach(async () => {
    transactionServiceMock = {
      getTransaction: vi.fn(),
      createTransaction: vi.fn().mockReturnValue(of({ id: 'tx1', amount: 50000 })),
      updateTransaction: vi.fn().mockReturnValue(of({ id: 'tx1', amount: 70000 }))
    };

    walletServiceMock = {
      getWallets: vi.fn().mockReturnValue(of(mockWallets))
    };

    categoryServiceMock = {
      getCategories: vi.fn().mockReturnValue(of(mockCategories))
    };

    await TestBed.configureTestingModule({
      imports: [TransactionFormComponent],
      providers: [
        provideRouter([
          { path: 'transactions', component: TransactionFormComponent }
        ]),
        { provide: TransactionService, useValue: transactionServiceMock },
        { provide: WalletService, useValue: walletServiceMock },
        { provide: CategoryService, useValue: categoryServiceMock }
      ]
    }).compileComponents();
  });

  it('FIN-37: should initialize form and select default wallet', () => {
    fixture = TestBed.createComponent(TransactionFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.form).toBeTruthy();
    expect(component.form.value.type).toBe(1);
    expect(component.form.value.walletId).toBe('w1');
  });

  it('FIN-37: should require valid amount > 0', () => {
    fixture = TestBed.createComponent(TransactionFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    component.form.patchValue({ amount: 0 });
    expect(component.form.controls['amount'].invalid).toBe(true);

    component.form.patchValue({ amount: 50000 });
    expect(component.form.controls['amount'].valid).toBe(true);
  });

  it('FIN-37: should call createTransaction when submitting valid form', () => {
    fixture = TestBed.createComponent(TransactionFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    component.form.patchValue({
      walletId: 'w1',
      categoryId: 'c1',
      type: 1,
      amount: 50000,
      note: 'Ăn trưa'
    });

    component.onSubmit();
    expect(transactionServiceMock.createTransaction).toHaveBeenCalled();
  });

  it('FIN-40: should show 409 conflict message on optimistic concurrency conflict', () => {
    fixture = TestBed.createComponent(TransactionFormComponent);
    component = fixture.componentInstance;
    component.isEditMode.set(true);
    component.transactionId.set('tx1');
    component.currentRowVersion.set('old-version');

    transactionServiceMock.updateTransaction.mockReturnValue(
      throwError(() => ({ status: 409, error: { message: 'Concurrency conflict' } }))
    );

    fixture.detectChanges();

    component.form.patchValue({
      walletId: 'w1',
      type: 1,
      amount: 70000
    });

    component.onSubmit();

    expect(component.errorMessage()).toContain('phiên làm việc khác');
  });
});
