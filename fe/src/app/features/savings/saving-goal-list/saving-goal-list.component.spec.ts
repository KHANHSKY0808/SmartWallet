import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { SavingGoalListComponent } from './saving-goal-list.component';
import { SavingGoalService } from '../../../core/services/saving-goal.service';
import { WalletService } from '../../../core/services/wallet.service';
import { SavingGoalDto } from '../../../core/models/saving-goal.models';
import { WalletDto } from '../../../core/models/wallet.models';

describe('SavingGoalListComponent', () => {
  let component: SavingGoalListComponent;
  let fixture: ComponentFixture<SavingGoalListComponent>;
  let goalServiceMock: {
    getGoals: ReturnType<typeof vi.fn>;
    deposit: ReturnType<typeof vi.fn>;
    withdraw: ReturnType<typeof vi.fn>;
  };
  let walletServiceMock: {
    getWallets: ReturnType<typeof vi.fn>;
  };

  const mockGoals: SavingGoalDto[] = [
    {
      id: 'g1',
      name: 'Du lịch Đà Lạt',
      targetAmount: 10_000_000,
      currentAmount: 3_000_000,
      percent: 30,
      currency: 'VND',
      targetDate: '2027-02-20',
      status: 'InProgress',
      statusCode: 1,
      daysLeft: 88,
      monthlyRequired: 2_334_000
    },
    {
      id: 'g2',
      name: 'Mua laptop mới',
      targetAmount: 20_000_000,
      currentAmount: 20_000_000,
      percent: 100,
      currency: 'VND',
      targetDate: '2026-12-01',
      status: 'Completed',
      statusCode: 2,
      daysLeft: 7,
      monthlyRequired: 0
    }
  ];

  const mockWallets: WalletDto[] = [
    {
      id: 'w1',
      userId: 'u1',
      name: 'Ví chính',
      type: 1,
      balance: 5_000_000,
      initialBalance: 5_000_000,
      currency: 'VND',
      isDefault: true,
      isArchived: false,
      createdAt: '2026-11-20',
      updatedAt: '2026-11-20'
    }
  ];

  beforeEach(async () => {
    goalServiceMock = {
      getGoals: vi.fn().mockReturnValue(of(mockGoals)),
      deposit: vi.fn().mockReturnValue(of({})),
      withdraw: vi.fn().mockReturnValue(of({}))
    };

    walletServiceMock = {
      getWallets: vi.fn().mockReturnValue(of(mockWallets))
    };

    await TestBed.configureTestingModule({
      imports: [SavingGoalListComponent],
      providers: [
        provideRouter([]),
        { provide: SavingGoalService, useValue: goalServiceMock },
        { provide: WalletService, useValue: walletServiceMock }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(SavingGoalListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  // F-01: Thẻ mục tiêu: Thanh %, số ngày còn lại, số cần tiết kiệm mỗi tháng, nhãn Completed
  it('F-01: should display progress bar, days left, monthly required amount, and completed label', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    const cards = compiled.querySelectorAll('[data-testid="goal-card"]');
    expect(cards.length).toBe(2);

    // Card 1: In progress
    const card1 = cards[0];
    const percent1 = card1.querySelector('[data-testid="percent-label"]');
    const daysLeft1 = card1.querySelector('[data-testid="days-left-label"]');
    const monthlyReq1 = card1.querySelector('[data-testid="monthly-required-label"]');
    expect(percent1?.textContent).toContain('30.0%');
    expect(daysLeft1?.textContent).toContain('88 ngày còn lại');
    expect(monthlyReq1?.textContent).toContain('2,334,000 VND/tháng');

    // Card 2: Completed label
    const card2 = cards[1];
    const completedBadge = card2.querySelector('[data-testid="completed-badge"]');
    expect(completedBadge).toBeTruthy();
    expect(completedBadge?.textContent).toContain('Completed');
  });

  // F-02: Dialog nạp/rút: Sinh Idempotency-Key 1 lần mỗi lần mở dialog, nút bị khóa khi đang gửi, hiển thị lỗi 400/409
  it('F-02: should generate Idempotency-Key once per dialog open, lock submit button, and display errors', () => {
    // Open deposit dialog
    component.openDialog('deposit', mockGoals[0]);
    fixture.detectChanges();

    const key1 = component.currentIdempotencyKey();
    expect(key1).toBeTruthy();

    component.dialogWalletId = 'w1';
    component.dialogAmount = 1_000_000;

    // Simulate error 400
    goalServiceMock.deposit.mockReturnValueOnce(
      throwError(() => ({ status: 400, error: { message: 'Số dư ví không đủ' } }))
    );

    component.submitDialog();
    fixture.detectChanges();

    // Verify key did NOT change on error / retry
    expect(component.currentIdempotencyKey()).toBe(key1);
    expect(component.dialogError()).toBe('Số dư ví không đủ');

    const compiled = fixture.nativeElement as HTMLElement;
    const errorEl = compiled.querySelector('[data-testid="dialog-error"]');
    expect(errorEl?.textContent).toContain('Số dư ví không đủ');

    // Submit button disabled when isSubmitting is true
    component.isSubmitting.set(true);
    fixture.detectChanges();
    const submitBtn = compiled.querySelector('[data-testid="dialog-submit-btn"]') as HTMLButtonElement;
    expect(submitBtn.disabled).toBe(true);

    // When dialog is closed and reopened, a NEW key is generated
    component.isSubmitting.set(false);
    component.closeDialog();
    component.openDialog('deposit', mockGoals[0]);
    const key2 = component.currentIdempotencyKey();
    expect(key2).not.toBe(key1);
  });

  // F-06: Responsive styling
  it('F-06: should not overflow horizontally at 360px', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    const container = compiled.querySelector('.saving-goal-container') as HTMLElement;
    expect(container).toBeTruthy();
    expect(container.style.maxWidth).toBe('100%');
    expect(container.style.overflowX).toBe('hidden');
  });
});
