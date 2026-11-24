import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { WalletFormComponent } from './wallet-form.component';
import { WalletService } from '../../../core/services/wallet.service';
import { WalletDto } from '../../../core/models/wallet.models';

describe('WalletFormComponent', () => {
  let component: WalletFormComponent;
  let fixture: ComponentFixture<WalletFormComponent>;
  let walletServiceMock: {
    createWallet: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    walletServiceMock = {
      createWallet: vi.fn().mockReturnValue(of({
        id: 'w-new',
        name: 'Ví mới',
        balance: 100000
      } as WalletDto))
    };

    await TestBed.configureTestingModule({
      imports: [WalletFormComponent],
      providers: [
        provideRouter([]),
        { provide: WalletService, useValue: walletServiceMock }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(WalletFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  // F-06: Validate tên rỗng và số dư âm. Trùng tên (409) hiện lỗi ngay tại ô tên.
  it('F-06: should validate empty name and negative initial balance', () => {
    // 1. Empty name
    component.walletForm.patchValue({
      name: '',
      type: 1,
      initialBalance: 100_000,
      currency: 'VND'
    });
    expect(component.walletForm.controls['name'].valid).toBe(false);
    expect(component.walletForm.valid).toBe(false);

    // 2. Negative initial balance
    component.walletForm.patchValue({
      name: 'Ví tiền mặt',
      type: 1,
      initialBalance: -50_000,
      currency: 'VND'
    });
    expect(component.walletForm.controls['initialBalance'].valid).toBe(false);
    expect(component.walletForm.valid).toBe(false);

    // 3. Valid values
    component.walletForm.patchValue({
      name: 'Ví tiền mặt',
      type: 1,
      initialBalance: 50_000,
      currency: 'VND'
    });
    expect(component.walletForm.valid).toBe(true);
  });

  it('F-06: should display conflict error directly on name field when API returns 409', () => {
    walletServiceMock.createWallet.mockReturnValueOnce(throwError(() => ({
      status: 409,
      error: { message: 'Tên ví đã tồn tại trong danh sách ví đang hoạt động' }
    })));

    component.walletForm.patchValue({
      name: 'Ví tiền mặt trùng',
      type: 1,
      initialBalance: 100_000,
      currency: 'VND'
    });

    component.onSubmit();
    fixture.detectChanges();

    // Verify error state on name control and error message signal
    expect(component.nameConflictError()).toContain('Tên ví đã tồn tại');
    expect(component.walletForm.controls['name'].errors?.['duplicate']).toBe(true);

    const compiled = fixture.nativeElement as HTMLElement;
    const errorEl = compiled.querySelector('[data-testid="name-conflict-error"]');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.textContent).toContain('Tên ví đã tồn tại');
  });

  it('F-03: should offer VND, USD, and EUR as currency options (lifting D29)', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    const select = compiled.querySelector('#currency') as HTMLSelectElement;
    expect(select).toBeTruthy();
    expect(select.options.length).toBe(3);
    const values = Array.from(select.options).map(o => o.value);
    expect(values).toEqual(['VND', 'USD', 'EUR']);
  });
});

