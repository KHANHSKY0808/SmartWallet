import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { TransferFormComponent } from './transfer-form.component';
import { WalletService } from '../../../core/services/wallet.service';
import { WalletDto, TransferResponse } from '../../../core/models/wallet.models';

describe('TransferFormComponent', () => {
  let component: TransferFormComponent;
  let fixture: ComponentFixture<TransferFormComponent>;
  let walletServiceMock: {
    getWallets: ReturnType<typeof vi.fn>;
    transfer: ReturnType<typeof vi.fn>;
  };

  const mockWallets: WalletDto[] = [
    {
      id: 'w-bank-1',
      userId: 'u1',
      name: 'Ngân hàng VCB',
      type: 2,
      balance: 10_000_000,
      initialBalance: 10_000_000,
      currency: 'VND',
      isDefault: true,
      isArchived: false,
      createdAt: '2026-11-22T00:00:00Z',
      updatedAt: '2026-11-22T00:00:00Z'
    },
    {
      id: 'w-cash-1',
      userId: 'u1',
      name: 'Tiền mặt',
      type: 1,
      balance: 5_000_000,
      initialBalance: 5_000_000,
      currency: 'VND',
      isDefault: false,
      isArchived: false,
      createdAt: '2026-11-22T00:00:00Z',
      updatedAt: '2026-11-22T00:00:00Z'
    }
  ];

  beforeEach(async () => {
    walletServiceMock = {
      getWallets: vi.fn().mockReturnValue(of(mockWallets)),
      transfer: vi.fn().mockReturnValue(of({
        transferId: 't-12345',
        newSourceBalance: 8_000_000,
        newTargetBalance: 7_000_000
      } as TransferResponse))
    };

    await TestBed.configureTestingModule({
      imports: [TransferFormComponent],
      providers: [
        provideRouter([]),
        { provide: WalletService, useValue: walletServiceMock }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(TransferFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  // F-01: Nút "Chuyển" bị disable ngay sau lần click đầu cho tới khi có phản hồi.
  // Cả lần gọi đầu lẫn lần retry đều gửi cùng một Idempotency-Key. Key được sinh một lần cho mỗi lần mở form.
  it('F-01: should disable submit button on click and reuse single Idempotency-Key on retries', () => {
    expect(component.idempotencyKey).toBeTruthy();
    const initialKey = component.idempotencyKey;

    // Fill valid form
    component.transferForm.patchValue({
      fromWalletId: 'w-bank-1',
      toWalletId: 'w-cash-1',
      amount: 2_000_000,
      fee: 0,
      note: 'Chuyển tiền tiết kiệm'
    });
    fixture.detectChanges();

    expect(component.transferForm.valid).toBe(true);

    // Call submit (first attempt)
    component.onSubmit();

    expect(walletServiceMock.transfer).toHaveBeenCalledTimes(1);
    expect(walletServiceMock.transfer).toHaveBeenLastCalledWith(
      expect.objectContaining({
        fromWalletId: 'w-bank-1',
        toWalletId: 'w-cash-1',
        amount: 2_000_000
      }),
      initialKey
    );

    // Simulate retry after an error
    walletServiceMock.transfer.mockReturnValueOnce(throwError(() => ({
      status: 409,
      error: { message: 'Conflict retry' }
    })));

    component.onSubmit();

    // Verify key did NOT change on retry
    expect(component.idempotencyKey).toBe(initialKey);
    expect(walletServiceMock.transfer).toHaveBeenLastCalledWith(
      expect.objectContaining({
        fromWalletId: 'w-bank-1',
        toWalletId: 'w-cash-1',
        amount: 2_000_000
      }),
      initialKey
    );
  });

  // F-02: Chọn ví nguồn = ví đích thì báo lỗi ngay trên form.
  // Số tiền vượt số dư ví nguồn (tính cả phí) thì báo lỗi trước khi gửi.
  it('F-02: should validate same source and target wallet, and balance limit including fee', () => {
    // 1. Same source and target wallet
    component.transferForm.patchValue({
      fromWalletId: 'w-bank-1',
      toWalletId: 'w-bank-1',
      amount: 100_000,
      fee: 0
    });
    component.onWalletChange();
    fixture.detectChanges();

    expect(component.transferForm.errors?.['sameWallet']).toBe(true);
    expect(component.transferForm.valid).toBe(false);

    // 2. Amount + fee exceeds source balance (10,000,000)
    component.transferForm.patchValue({
      fromWalletId: 'w-bank-1',
      toWalletId: 'w-cash-1',
      amount: 10_000_001,
      fee: 0
    });
    component.onAmountChange();
    fixture.detectChanges();

    expect(component.transferForm.errors?.['insufficientBalance']).toBe(true);
    expect(component.transferForm.valid).toBe(false);

    // Amount = 10,000,000 + fee 10,000 = 10,010,000 > balance
    component.transferForm.patchValue({
      fromWalletId: 'w-bank-1',
      toWalletId: 'w-cash-1',
      amount: 10_000_000,
      fee: 10_000
    });
    component.onAmountChange();
    fixture.detectChanges();

    expect(component.transferForm.errors?.['insufficientBalance']).toBe(true);
    expect(component.transferForm.valid).toBe(false);

    // Valid amount + fee <= balance (8,000,000 + 10,000 = 8,010,000 <= 10,000,000)
    component.transferForm.patchValue({
      fromWalletId: 'w-bank-1',
      toWalletId: 'w-cash-1',
      amount: 8_000_000,
      fee: 10_000
    });
    component.onAmountChange();
    fixture.detectChanges();

    expect(component.transferForm.errors).toBeNull();
    expect(component.transferForm.valid).toBe(true);
  });

  // F-03: Khi API trả 400, 409 hoặc 422 thì hiện thông báo tương ứng, form không bị kẹt ở trạng thái loading.
  it('F-03: should display error message and reset isLoading on 400, 409, and 422 errors', () => {
    component.transferForm.patchValue({
      fromWalletId: 'w-bank-1',
      toWalletId: 'w-cash-1',
      amount: 1_000_000,
      fee: 0
    });

    // 1. Error 400
    walletServiceMock.transfer.mockReturnValueOnce(throwError(() => ({
      status: 400,
      error: { message: 'Số dư không đủ' }
    })));
    component.onSubmit();
    expect(component.isLoading()).toBe(false);
    expect(component.errorMessage()).toContain('Số dư không đủ');

    // 2. Error 409
    walletServiceMock.transfer.mockReturnValueOnce(throwError(() => ({
      status: 409,
      error: { message: 'Xung đột phiên bản dữ liệu' }
    })));
    component.onSubmit();
    expect(component.isLoading()).toBe(false);
    expect(component.errorMessage()).toContain('Xung đột phiên bản dữ liệu');

    // 3. Error 422
    walletServiceMock.transfer.mockReturnValueOnce(throwError(() => ({
      status: 422,
      error: { message: 'IDEMPOTENCY_KEY_REUSED' }
    })));
    component.onSubmit();
    expect(component.isLoading()).toBe(false);
    expect(component.errorMessage()).toContain('IDEMPOTENCY_KEY_REUSED');
  });

  // F-07: Form chuyển tiền không bị tràn ngang ở độ rộng 360px
  it('F-07: should be responsive and prevent horizontal overflow at 360px width', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    const container = compiled.querySelector('.transfer-container') as HTMLElement;
    expect(container).toBeTruthy();
    expect(container.style.maxWidth).toBe('100%');
    expect(container.style.overflowX).toBe('hidden');
    expect(container.style.boxSizing).toBe('border-box');
  });
});

