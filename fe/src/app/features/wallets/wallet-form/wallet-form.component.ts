import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { WalletService } from '../../../core/services/wallet.service';

@Component({
  selector: 'app-wallet-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  template: `
    <div class="wallet-form-container" style="max-width: 100%; box-sizing: border-box; overflow-x: hidden; padding: 1rem;">
      <div class="card" style="width: 100%; max-width: 480px; margin: 0 auto; box-sizing: border-box;">
        <h2>Tạo ví mới</h2>
        <p class="subtitle">Thêm tài khoản quản lý thu chi</p>

        <div *ngIf="generalError()" class="alert alert-danger" data-testid="general-error">
          {{ generalError() }}
        </div>

        <form [formGroup]="walletForm" (ngSubmit)="onSubmit()">
          <!-- Wallet Name (F-06) -->
          <div class="form-group">
            <label for="name">Tên ví</label>
            <input
              id="name"
              type="text"
              formControlName="name"
              class="form-control"
              placeholder="VD: Ví tiền mặt, VCB chính..."
              [class.is-invalid]="f['name'].touched && (f['name'].invalid || nameConflictError())"
            />
            <div *ngIf="f['name'].touched && f['name'].errors?.['required']" class="invalid-feedback">
              Tên ví không được để trống
            </div>
            <!-- F-06: 409 conflict error attached directly to name -->
            <div *ngIf="nameConflictError()" class="invalid-feedback d-block name-error" data-testid="name-conflict-error">
              {{ nameConflictError() }}
            </div>
          </div>

          <!-- Type -->
          <div class="form-group">
            <label for="type">Loại ví</label>
            <select id="type" formControlName="type" class="form-control">
              <option [value]="1">Tiền mặt</option>
              <option [value]="2">Tài khoản ngân hàng</option>
              <option [value]="3">Ví điện tử</option>
              <option [value]="4">Thẻ tín dụng</option>
            </select>
          </div>

          <!-- Initial Balance (F-06) -->
          <div class="form-group">
            <label for="initialBalance">Số dư ban đầu</label>
            <input
              id="initialBalance"
              type="number"
              formControlName="initialBalance"
              class="form-control"
              placeholder="0"
              [class.is-invalid]="f['initialBalance'].touched && f['initialBalance'].invalid"
            />
            <div *ngIf="f['initialBalance'].touched && f['initialBalance'].errors?.['required']" class="invalid-feedback">
              Số dư ban đầu không được để trống
            </div>
            <div *ngIf="f['initialBalance'].touched && f['initialBalance'].errors?.['min']" class="invalid-feedback" data-testid="negative-balance-error">
              Số dư ban đầu không được là số âm
            </div>
          </div>

          <!-- Currency -->
          <div class="form-group">
            <label for="currency">Đơn vị tiền tệ</label>
            <select id="currency" formControlName="currency" class="form-control">
              <option value="VND">VND (Việt Nam Đồng)</option>
              <option value="USD">USD (Đô la Mỹ)</option>
              <option value="EUR">EUR (Euro)</option>
              <option value="JPY">JPY (Yên Nhật)</option>
            </select>
          </div>

          <!-- Actions -->
          <div class="form-actions" style="margin-top: 1.5rem; display: flex; gap: 1rem; flex-wrap: wrap;">
            <button
              type="submit"
              id="submitWalletBtn"
              [disabled]="walletForm.invalid || isLoading()"
              class="btn btn-primary"
              style="flex: 1;"
            >
              <span *ngIf="!isLoading()">Lưu ví</span>
              <span *ngIf="isLoading()">Đang lưu...</span>
            </button>
            <button
              type="button"
              routerLink="/wallets"
              class="btn btn-secondary"
              [disabled]="isLoading()"
            >
              Hủy
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [`
    .wallet-form-container {
      width: 100%;
      max-width: 100%;
      box-sizing: border-box;
      overflow-x: hidden;
      padding: 1rem;
    }
    .card {
      background: #ffffff;
      border-radius: 8px;
      box-shadow: 0 4px 12px rgba(0,0,0,0.08);
      padding: 2rem;
      width: 100%;
      box-sizing: border-box;
    }
    @media (max-width: 480px) {
      .card { padding: 1rem; }
    }
    h2 {
      margin: 0 0 0.25rem 0;
      color: #1e293b;
      font-size: 1.35rem;
      font-weight: 700;
    }
    .subtitle {
      color: #64748b;
      margin-bottom: 1.25rem;
      font-size: 0.875rem;
    }
    .form-group {
      margin-bottom: 1rem;
      width: 100%;
      box-sizing: border-box;
    }
    label {
      display: block;
      margin-bottom: 0.35rem;
      font-size: 0.875rem;
      font-weight: 500;
      color: #334155;
    }
    .form-control {
      width: 100%;
      box-sizing: border-box;
      padding: 0.6rem 0.75rem;
      font-size: 0.95rem;
      border: 1px solid #cbd5e1;
      border-radius: 6px;
      outline: none;
    }
    .form-control:focus {
      border-color: #3b82f6;
    }
    .form-control.is-invalid {
      border-color: #ef4444;
    }
    .invalid-feedback {
      color: #ef4444;
      font-size: 0.8rem;
      margin-top: 0.25rem;
    }
    .d-block { display: block; }
    .alert {
      padding: 0.75rem 1rem;
      border-radius: 6px;
      margin-bottom: 1rem;
      font-size: 0.875rem;
    }
    .alert-danger {
      background: #fef2f2;
      color: #b91c1c;
      border: 1px solid #fecaca;
    }
    .btn {
      padding: 0.65rem 1.25rem;
      font-size: 0.95rem;
      font-weight: 600;
      border: none;
      border-radius: 6px;
      cursor: pointer;
    }
    .btn:disabled { opacity: 0.6; cursor: not-allowed; }
    .btn-primary { background: #2563eb; color: white; }
    .btn-secondary { background: #e2e8f0; color: #334155; }
  `]
})
export class WalletFormComponent implements OnInit {
  private fb = inject(FormBuilder);
  private walletService = inject(WalletService);
  private router = inject(Router);

  isLoading = signal<boolean>(false);
  generalError = signal<string | null>(null);
  nameConflictError = signal<string | null>(null);

  // F-06: Validate empty name, negative balance
  walletForm: FormGroup = this.fb.group({
    name: ['', [Validators.required]],
    type: [1, [Validators.required]],
    initialBalance: [0, [Validators.required, Validators.min(0)]],
    currency: ['VND', [Validators.required]]
  });

  get f() {
    return this.walletForm.controls;
  }

  ngOnInit(): void {
    this.f['name'].valueChanges.subscribe(() => {
      if (this.nameConflictError()) {
        this.nameConflictError.set(null);
      }
    });
  }

  onSubmit(): void {
    if (this.walletForm.invalid || this.isLoading()) {
      return;
    }

    this.isLoading.set(true);
    this.generalError.set(null);
    this.nameConflictError.set(null);

    const val = this.walletForm.value;
    this.walletService.createWallet({
      name: val.name.trim(),
      type: Number(val.type),
      initialBalance: Number(val.initialBalance),
      currency: val.currency
    }).subscribe({
      next: () => {
        this.isLoading.set(false);
        this.router.navigate(['/wallets']);
      },
      error: (err) => {
        this.isLoading.set(false);
        // F-06: 409 conflict error attached directly to name input
        if (err.status === 409) {
          this.nameConflictError.set(err.error?.message || 'Tên ví đã tồn tại trong danh sách ví đang hoạt động');
          this.f['name'].setErrors({ duplicate: true });
        } else {
          this.generalError.set(err.error?.message || 'Không thể tạo ví, vui lòng thử lại');
        }
      }
    });
  }
}

