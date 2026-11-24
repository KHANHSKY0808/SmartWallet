import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DebtListComponent } from './debt-list.component';
import { DebtService } from '../../../core/services/debt.service';
import { WalletService } from '../../../core/services/wallet.service';
import { of } from 'rxjs';
import { DebtDto } from '../../../core/models/debt.models';

describe('DebtListComponent (F-02, F-03)', () => {
  let component: DebtListComponent;
  let fixture: ComponentFixture<DebtListComponent>;
  let mockDebtService: any;
  let mockWalletService: any;

  const testDebt: DebtDto = {
    id: 'debt-1',
    userId: 'u1',
    type: 1,
    typeName: 'Lend',
    personName: 'Anh Nam',
    amount: 5000000,
    paidAmount: 2000000,
    remainingAmount: 3000000,
    interestRate: null,
    currency: 'VND',
    dueDate: '2027-02-10',
    status: 'Active',
    statusCode: 1,
    walletId: 'w1',
    note: null,
    createdAt: '2027-01-01',
    updatedAt: '2027-01-01'
  };

  beforeEach(async () => {
    mockDebtService = {
      getDebts: () => of([testDebt]),
      createDebt: () => of(testDebt),
      makePayment: () => of({}),
      deleteDebt: () => of({})
    };
    mockWalletService = {
      getWallets: () => of([{ id: 'w1', name: 'Ví chính', balance: 10000000 }])
    };

    await TestBed.configureTestingModule({
      imports: [DebtListComponent],
      providers: [
        { provide: DebtService, useValue: mockDebtService },
        { provide: WalletService, useValue: mockWalletService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(DebtListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('F-02: should calculate progress bar percentage accurately', () => {
    const percent = component.getProgressPercent(testDebt);
    expect(percent).toBe(40);
  });

  it('F-03: should generate fresh Idempotency-Key on openPayModal', () => {
    component.openPayModal(testDebt);
    expect(component.showPayModal).toBe(true);
    expect(component.payIdempotencyKey).toBeTruthy();
    expect(component.payAmount).toBe(3000000);
  });
});

