import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { WalletService } from '../../../core/services/wallet.service';
import { WalletDto } from '../../../core/models/wallet.models';

@Component({
  selector: 'app-transfer-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  template: `
    <div class="transfer-container" style="max-width: 100%; box-sizing: border-box; overflow-x: hidden; padding: 1rem;">
      <div class="card" style="width: 100%; max-width: 480px; margin: 0 auto; box-sizing: border-box;">
        <h2>Chuyển tiền giữa các ví</h2>
        <p class="subtitle">Thực hiện chuyển quỹ an toàn và tức thì</p>

        <div *ngIf="errorMessage()" class="alert alert-danger" data-testid="error-alert">
          {{ errorMessage() }}
        </div>

        <div *ngIf="successMessage()" class="alert alert-success" data-testid="success-alert">
          {{ successMessage() }}
        </div>

        <form [formGroup]="transferForm" (ngSubmit)="onSubmit()">
          <!-- Source Wallet -->
          <div class="form-group">
            <label for="fromWalletId">Ví nguồn</label>
            <select
              id="fromWalletId"
              formControlName="fromWalletId"
              class="form-control"
              (change)="onWalletChange()"
              [class.is-invalid]="f['fromWalletId'].touched && (f['fromWalletId'].invalid || transferForm.errors?.['sameWallet'])"
            >
              <option value="" disabled selected>-- Chọn ví nguồn --</option>
              <option *ngFor="let w of wallets()" [value]="w.id">
                {{ w.name }} ({{ w.balance | number }} {{ w.currency }})
              </option>
            </select>
            <div *ngIf="f['fromWalletId'].touched && f['fromWalletId'].errors?.['required']" class="invalid-feedback">
              Vui lòng chọn ví nguồn
            </div>
          </div>

          <!-- Target Wallet -->
          <div class="form-group">
            <label for="toWalletId">Ví đích</label>
            <select
              id="toWalletId"
              formControlName="toWalletId"
              class="form-control"
              (change)="onWalletChange()"
              [class.is-invalid]="f['toWalletId'].touched && (f['toWalletId'].invalid || transferForm.errors?.['sameWallet'])"
            >
              <option value="" disabled selected>-- Chọn ví đích --</option>
              <option *ngFor="let w of wallets()" [value]="w.id">
                {{ w.name }} ({{ w.balance | number }} {{ w.currency }})
              </option>
            </select>
            <div *ngIf="f['toWalletId'].touched && f['toWalletId'].errors?.['required']" class="invalid-feedback">
              Vui lòng chọn ví đích
            </div>
            <div *ngIf="transferForm.errors?.['sameWallet']" class="invalid-feedback d-block" data-testid="same-wallet-error">
              Ví nguồn và ví đích không được trùng nhau
            </div>
          </div>

          <!-- Amount -->
          <div class="form-group">
            <label for="amount">Số tiền</label>
            <input
              id="amount"
              type="number"
              formControlName="amount"
              class="form-control"
              placeholder="0"
              (input)="onAmountChange()"
              [class.is-invalid]="(f['amount'].touched && f['amount'].invalid) || transferForm.errors?.['insufficientBalance']"
            />
            <div *ngIf="f['amount'].touched && f['amount'].errors?.['required']" class="invalid-feedback">
              Số tiền không được để trống
            </div>
            <div *ngIf="f['amount'].touched && f['amount'].errors?.['min']" class="invalid-feedback">
              Số tiền phải lớn hơn 0
            </div>
            <div *ngIf="transferForm.errors?.['insufficientBalance']" class="invalid-feedback d-block" data-testid="insufficient-balance-error">
              Số dư ví nguồn không đủ (bao gồm cả phí)
            </div>
          </div>

          <!-- Fee -->
          <div class="form-group">
            <label for="fee">Phí chuyển tiền</label>
            <input
              id="fee"
              type="number"
              formControlName="fee"
              class="form-control"
              placeholder="0"
              (input)="onAmountChange()"
            />
          </div>

          <!-- Note -->
          <div class="form-group">
            <label for="note">Ghi chú</label>
            <textarea
              id="note"
              formControlName="note"
              class="form-control"
              rows="2"
              placeholder="Ghi chú chuyển tiền (tùy chọn)"
            ></textarea>
          </div>

          <!-- Actions -->
          <div class="form-actions" style="margin-top: 1.5rem; display: flex; gap: 1rem; flex-wrap: wrap;">
            <button
              type="submit"
              id="submitTransferBtn"
              [disabled]="transferForm.invalid || isLoading()"
              class="btn btn-primary"
              style="flex: 1; min-width: 140px;"
            >
              <span *ngIf="!isLoading()">Chuyển tiền</span>
              <span *ngIf="isLoading()">Đang xử lý...</span>
            </button>
            <button
              type="button"
              routerLink="/wallets"
              class="btn btn-secondary"
              [disabled]="isLoading()"
              style="min-width: 100px;"
            >
              Hủy
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [`
    .transfer-container {
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
      .card {
        padding: 1rem;
      }
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
      box-sizing: border-box;
      width: 100%;
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
      transition: border-color 0.2s;
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
    .d-block {
      display: block;
    }
    .alert {
      padding: 0.75rem 1rem;
      border-radius: 6px;
      margin-bottom: 1rem;
      font-size: 0.875rem;
      word-break: break-word;
    }
    .alert-danger {
      background: #fef2f2;
      color: #b91c1c;
      border: 1px solid #fecaca;
    }
    .alert-success {
      background: #f0fdf4;
      color: #15803d;
      border: 1px solid #bbf7d0;
    }
    .btn {
      padding: 0.65rem 1.25rem;
      font-size: 0.95rem;
      font-weight: 600;
      border: none;
      border-radius: 6px;
      cursor: pointer;
      text-align: center;
      transition: background 0.2s;
    }
    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
    .btn-primary {
      background: #2563eb;
      color: white;
    }
    .btn-primary:hover:not(:disabled) {
      background: #1d4ed8;
    }
    .btn-secondary {
      background: #e2e8f0;
      color: #334155;
    }
    .btn-secondary:hover:not(:disabled) {
      background: #cbd5e1;
    }
  `]
})
export class TransferFormComponent implements OnInit {
  private fb = inject(FormBuilder);
  private walletService = inject(WalletService);
  private router = inject(Router);

  wallets = signal<WalletDto[]>([]);
  isLoading = signal<boolean>(false);
  errorMessage = signal<string | null>(null);
  successMessage = signal<string | null>(null);

  // F-01: Idempotency-Key generated once per form instance and reused across retries
  readonly idempotencyKey: string = this.generateGuid();

  transferForm: FormGroup = this.fb.group({
    fromWalletId: ['', Validators.required],
    toWalletId: ['', Validators.required],
    amount: ['', [Validators.required, Validators.min(1)]],
    fee: [0, [Validators.min(0)]],
    note: ['']
  }, { validators: [this.validateTransfer.bind(this)] });

  get f() {
    return this.transferForm.controls;
  }

  ngOnInit(): void {
    this.loadWallets();
  }

  loadWallets(): void {
    this.walletService.getWallets().subscribe({
      next: (list) => this.wallets.set(list),
      error: () => this.errorMessage.set('Không thể tải danh sách ví')
    });
  }

  onWalletChange(): void {
    this.transferForm.updateValueAndValidity();
  }

  onAmountChange(): void {
    this.transferForm.updateValueAndValidity();
  }

  // F-02: Validate same wallet and insufficient balance
  private validateTransfer(form: FormGroup): { [key: string]: any } | null {
    const fromId = form.get('fromWalletId')?.value;
    const toId = form.get('toWalletId')?.value;
    const amount = Number(form.get('amount')?.value || 0);
    const fee = Number(form.get('fee')?.value || 0);

    if (fromId && toId && fromId === toId) {
      return { sameWallet: true };
    }

    if (fromId && amount > 0) {
      const source = this.wallets().find(w => w.id === fromId);
      if (source && (amount + fee) > source.balance) {
        return { insufficientBalance: true };
      }
    }

    return null;
  }

  // F-01, F-03: Disable submit on click, send idempotency key, handle errors (400, 409, 422) and reset isLoading
  onSubmit(): void {
    if (this.transferForm.invalid || this.isLoading()) {
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    const val = this.transferForm.value;
    this.walletService.transfer({
      fromWalletId: val.fromWalletId,
      toWalletId: val.toWalletId,
      amount: Number(val.amount),
      fee: Number(val.fee || 0),
      note: val.note || ''
    }, this.idempotencyKey).subscribe({
      next: (res) => {
        this.isLoading.set(false);
        this.successMessage.set(`Chuyển tiền thành công! Mã GD: ${res.transferId}`);
        setTimeout(() => this.router.navigate(['/wallets']), 1500);
      },
      error: (err) => {
        this.isLoading.set(false);
        if (err.status === 400) {
          this.errorMessage.set(err.error?.message || 'Yêu cầu không hợp lệ hoặc số dư không đủ (400)');
        } else if (err.status === 409) {
          this.errorMessage.set(err.error?.message || 'Xung đột dữ liệu hoặc giao dịch đang được xử lý, vui lòng thử lại (409)');
        } else if (err.status === 422) {
          this.errorMessage.set(err.error?.message || 'Idempotency key đã được sử dụng với payload khác (422)');
        } else {
          this.errorMessage.set(err.error?.message || 'Có lỗi xảy ra khi chuyển tiền');
        }
      }
    });
  }

  private generateGuid(): string {
    if (typeof crypto !== 'undefined' && crypto.randomUUID) {
      return crypto.randomUUID();
    }
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
      const r = Math.random() * 16 | 0;
      const v = c === 'x' ? r : (r & 0x3 | 0x8);
      return v.toString(16);
    });
  }
}

