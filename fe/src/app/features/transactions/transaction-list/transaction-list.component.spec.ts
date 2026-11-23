import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { TransactionListComponent } from './transaction-list.component';
import { TransactionService } from '../../../core/services/transaction.service';
import { WalletService } from '../../../core/services/wallet.service';
import { CategoryService } from '../../../core/services/category.service';
import { TransactionDto, PagedResult } from '../../../core/models/transaction.models';

describe('TransactionListComponent', () => {
  let component: TransactionListComponent;
  let fixture: ComponentFixture<TransactionListComponent>;
  let transactionServiceMock: {
    getTransactions: ReturnType<typeof vi.fn>;
    deleteTransaction: ReturnType<typeof vi.fn>;
  };
  let walletServiceMock: {
    getWallets: ReturnType<typeof vi.fn>;
  };
  let categoryServiceMock: {
    getCategories: ReturnType<typeof vi.fn>;
  };

  const mockPagedResult: PagedResult<TransactionDto> = {
    items: [
      {
        id: 'tx1',
        userId: 'u1',
        walletId: 'w1',
        categoryId: 'c1',
        type: 1,
        typeName: 'Expense',
        amount: 50000,
        transactionDate: '2026-11-20T12:00:00Z',
        note: 'Cà phê',
        rowVersion: 'v1',
        createdAt: '2026-11-20',
        updatedAt: '2026-11-20'
      },
      {
        id: 'tx2',
        userId: 'u1',
        walletId: 'w1',
        type: 2,
        typeName: 'Income',
        amount: 5000000,
        transactionDate: '2026-11-21T09:00:00Z',
        note: 'Tiền thưởng',
        rowVersion: 'v2',
        createdAt: '2026-11-21',
        updatedAt: '2026-11-21'
      }
    ],
    totalItems: 2,
    page: 1,
    pageSize: 20,
    totalPages: 1
  };

  beforeEach(async () => {
    transactionServiceMock = {
      getTransactions: vi.fn().mockReturnValue(of(mockPagedResult)),
      deleteTransaction: vi.fn().mockReturnValue(of({ message: 'Deleted' }))
    };

    walletServiceMock = {
      getWallets: vi.fn().mockReturnValue(of([]))
    };

    categoryServiceMock = {
      getCategories: vi.fn().mockReturnValue(of([]))
    };

    await TestBed.configureTestingModule({
      imports: [TransactionListComponent],
      providers: [
        provideRouter([]),
        { provide: TransactionService, useValue: transactionServiceMock },
        { provide: WalletService, useValue: walletServiceMock },
        { provide: CategoryService, useValue: categoryServiceMock }
      ]
    }).compileComponents();
  });

  it('FIN-44: should load transactions on init and display items', () => {
    fixture = TestBed.createComponent(TransactionListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(transactionServiceMock.getTransactions).toHaveBeenCalled();
    expect(component.transactions().length).toBe(2);
    expect(component.totalItems()).toBe(2);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Cà phê');
    expect(compiled.textContent).toContain('Tiền thưởng');
  });

  it('FIN-45: should reload transactions when filters are applied', () => {
    fixture = TestBed.createComponent(TransactionListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    component.filterParams.keyword = 'Cà phê';
    component.filterParams.type = 1;
    component.applyFilters();

    expect(transactionServiceMock.getTransactions).toHaveBeenCalledTimes(2);
    expect(component.page()).toBe(1);
  });

  it('FIN-40: should delete transaction when confirmed', () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    fixture = TestBed.createComponent(TransactionListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    const txToDelete = mockPagedResult.items[0];
    component.onDelete(txToDelete);

    expect(transactionServiceMock.deleteTransaction).toHaveBeenCalledWith(txToDelete.id);
  });
});

