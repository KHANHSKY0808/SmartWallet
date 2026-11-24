import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DebtService } from '../../../core/services/debt.service';
import { WalletService } from '../../../core/services/wallet.service';
import { DebtDto, CreateDebtRequest, CreateDebtPaymentRequest, DebtPaymentDto } from '../../../core/models/debt.models';
import { WalletDto } from '../../../core/models/wallet.models';

@Component({
  selector: 'app-debt-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div style="padding: 1.5rem; max-width: 1000px; margin: 0 auto;">
      <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem;">
        <div>
          <h2 style="margin: 0; color: #1e293b;">📖 Sổ nợ (Cho vay & Đi vay)</h2>
          <p style="margin: 0.25rem 0 0 0; color: #64748b; font-size: 0.9rem;">Theo dõi dư nợ, tiến độ thanh toán và nhắc hẹn quá hạn</p>
        </div>
        <button (click)="openCreateModal()" style="background: #2563eb; color: white; border: none; padding: 0.5rem 1rem; border-radius: 6px; cursor: pointer; font-weight: 500;">
          + Ghi nợ mới
        </button>
      </div>

      <!-- Filter tabs -->
      <div style="display: flex; gap: 0.5rem; margin-bottom: 1rem;">
        <button (click)="filterType = 0" [style.background]="filterType === 0 ? '#1e293b' : '#f1f5f9'" [style.color]="filterType === 0 ? 'white' : '#475569'" style="border: none; padding: 0.4rem 0.8rem; border-radius: 6px; cursor: pointer; font-size: 0.85rem;">
          Tất cả
        </button>
        <button (click)="filterType = 1" [style.background]="filterType === 1 ? '#ef4444' : '#f1f5f9'" [style.color]="filterType === 1 ? 'white' : '#475569'" style="border: none; padding: 0.4rem 0.8rem; border-radius: 6px; cursor: pointer; font-size: 0.85rem;">
          Cho vay
        </button>
        <button (click)="filterType = 2" [style.background]="filterType === 2 ? '#f59e0b' : '#f1f5f9'" [style.color]="filterType === 2 ? 'white' : '#475569'" style="border: none; padding: 0.4rem 0.8rem; border-radius: 6px; cursor: pointer; font-size: 0.85rem;">
          Đi vay
        </button>
      </div>

      <!-- Loading / Empty -->
      <div *ngIf="isLoading()" style="text-align: center; padding: 2rem; color: #64748b;">Đang tải danh sách nợ...</div>
      <div *ngIf="!isLoading() && filteredDebts().length === 0" style="text-align: center; padding: 3rem; background: white; border-radius: 8px; border: 1px dashed #cbd5e1;">
        <div style="font-size: 3rem; margin-bottom: 0.5rem;">🤝</div>
        <h3 style="margin: 0 0 0.5rem 0; color: #334155;">Không có khoản nợ nào</h3>
      </div>

      <!-- F-02: Danh sách nợ, tiến độ, nhãn Overdue đỏ -->
      <div *ngIf="!isLoading() && filteredDebts().length > 0" style="display: grid; gap: 1rem;">
        <div *ngFor="let d of filteredDebts()" style="background: white; border-radius: 8px; padding: 1.25rem; border: 1px solid #e2e8f0; display: flex; flex-direction: column; gap: 0.75rem;">
          <div style="display: flex; justify-content: space-between; align-items: flex-start;">
            <div>
              <div style="display: flex; align-items: center; gap: 0.5rem;">
                <span [style.background]="d.type === 1 ? '#fee2e2' : '#fef3c7'" [style.color]="d.type === 1 ? '#b91c1c' : '#b45309'" style="padding: 0.15rem 0.5rem; border-radius: 4px; font-size: 0.75rem; font-weight: 600;">
                  {{ d.type === 1 ? 'Cho vay' : 'Đi vay' }}
                </span>
                <span style="font-weight: 600; font-size: 1.1rem; color: #1e293b;">{{ d.personName }}</span>

                <!-- Status Badge -->
                <span *ngIf="d.status === 'Active'" style="background: #e0f2fe; color: #0369a1; padding: 0.15rem 0.4rem; border-radius: 4px; font-size: 0.75rem;">
                  Đang theo dõi
                </span>
                <span *ngIf="d.status === 'PaidFull'" style="background: #dcfce7; color: #15803d; padding: 0.15rem 0.4rem; border-radius: 4px; font-size: 0.75rem;">
                  Đã trả hết
                </span>
                <span *ngIf="d.status === 'Overdue'" style="background: #fee2e2; color: #dc2626; padding: 0.15rem 0.4rem; border-radius: 4px; font-size: 0.75rem; font-weight: bold;">
                  ⚠️ Quá hạn
                </span>
              </div>
              <div style="color: #64748b; font-size: 0.85rem; margin-top: 0.25rem;">
                Hạn trả: <strong>{{ d.dueDate }}</strong>
              </div>
            </div>

            <div style="text-align: right;">
              <div style="font-size: 1.15rem; font-weight: 700; color: #1e293b;">{{ d.amount | number }} ₫</div>
              <div style="font-size: 0.85rem; color: #64748b;">Còn lại: <strong [style.color]="d.remainingAmount > 0 ? '#dc2626' : '#16a34a'">{{ d.remainingAmount | number }} ₫</strong></div>
            </div>
          </div>

          <!-- Progress bar -->
          <div style="background: #f1f5f9; height: 8px; border-radius: 4px; overflow: hidden;">
            <div [style.width.%]="getProgressPercent(d)" [style.background]="d.status === 'PaidFull' ? '#16a34a' : (d.status === 'Overdue' ? '#dc2626' : '#2563eb')" style="height: 100%;"></div>
          </div>

          <div style="display: flex; justify-content: space-between; align-items: center; border-top: 1px solid #f8fafc; padding-top: 0.5rem;">
            <div style="font-size: 0.85rem; color: #64748b;">
              Đã thanh toán: {{ d.paidAmount | number }} ₫ ({{ getProgressPercent(d) }}%)
            </div>
            <div style="display: flex; gap: 0.5rem;">
              <button *ngIf="d.status !== 'PaidFull'" (click)="openPayModal(d)" style="background: #2563eb; color: white; border: none; padding: 0.35rem 0.75rem; border-radius: 6px; cursor: pointer; font-size: 0.85rem;">
                {{ d.type === 1 ? 'Thu nợ' : 'Trả nợ' }}
              </button>
              <button *ngIf="d.paidAmount === 0" (click)="deleteDebt(d)" style="background: none; border: 1px solid #fecaca; color: #dc2626; padding: 0.35rem 0.75rem; border-radius: 6px; cursor: pointer; font-size: 0.85rem;">
                Xóa
              </button>
            </div>
          </div>
        </div>
      </div>

      <!-- F-03: Dialog thanh toán nợ có Idempotency-Key 1 lần mỗi lần mở, chặn overpay -->
      <div *ngIf="showPayModal" style="position: fixed; inset: 0; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1000;">
        <div style="background: white; border-radius: 8px; width: 100%; max-width: 450px; padding: 1.5rem;">
          <h3 style="margin-top: 0;">{{ selectedDebt?.type === 1 ? 'Thu nợ từ' : 'Trả nợ cho' }} {{ selectedDebt?.personName }}</h3>
          <p style="font-size: 0.85rem; color: #64748b; margin-top: -0.5rem;">Dư nợ còn lại: <strong>{{ selectedDebt?.remainingAmount | number }} ₫</strong></p>

          <div style="display: flex; flex-direction: column; gap: 0.85rem; margin-top: 1rem;">
            <div>
              <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Số tiền thanh toán</label>
              <input type="number" [(ngModel)]="payAmount" [max]="selectedDebt?.remainingAmount || 0" placeholder="0" style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px; box-sizing: border-box;">
              <div *ngIf="payAmount > (selectedDebt?.remainingAmount || 0)" style="color: #dc2626; font-size: 0.8rem; margin-top: 0.25rem;">
                ⚠️ Số tiền không được vượt quá dư nợ còn lại ({{ selectedDebt?.remainingAmount | number }} ₫)
              </div>
            </div>

            <div>
              <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Ví giao dịch</label>
              <select [(ngModel)]="payWalletId" style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px;">
                <option *ngFor="let w of wallets" [value]="w.id">{{ w.name }} ({{ w.balance | number }} ₫)</option>
              </select>
            </div>

            <div *ngIf="payErrorMessage" style="color: #dc2626; font-size: 0.85rem;">{{ payErrorMessage }}</div>
          </div>

          <div style="display: flex; justify-content: flex-end; gap: 0.5rem; margin-top: 1.5rem;">
            <button (click)="showPayModal = false" style="background: none; border: 1px solid #cbd5e1; padding: 0.5rem 1rem; border-radius: 6px; cursor: pointer;">Hủy</button>
            <button (click)="submitPayment()" [disabled]="isSubmittingPay || payAmount <= 0 || payAmount > (selectedDebt?.remainingAmount || 0)" style="background: #2563eb; color: white; border: none; padding: 0.5rem 1rem; border-radius: 6px; cursor: pointer;">
              {{ isSubmittingPay ? 'Đang gửi...' : 'Xác nhận thanh toán' }}
            </button>
          </div>
        </div>
      </div>

      <!-- Create Debt Modal -->
      <div *ngIf="showCreateModal" style="position: fixed; inset: 0; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1000;">
        <div style="background: white; border-radius: 8px; width: 100%; max-width: 500px; padding: 1.5rem;">
          <h3 style="margin-top: 0;">Ghi nhận khoản nợ mới</h3>

          <div style="display: flex; flex-direction: column; gap: 0.85rem;">
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem;">
              <div>
                <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Loại</label>
                <select [(ngModel)]="newDebt.type" style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px;">
                  <option [ngValue]="1">Cho vay</option>
                  <option [ngValue]="2">Đi vay</option>
                </select>
              </div>
              <div>
                <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Người liên quan</label>
                <input type="text" [(ngModel)]="newDebt.personName" placeholder="Tên người..." style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px; box-sizing: border-box;">
              </div>
            </div>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem;">
              <div>
                <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Số tiền</label>
                <input type="number" [(ngModel)]="newDebt.amount" placeholder="0" style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px; box-sizing: border-box;">
              </div>
              <div>
                <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Hạn thanh toán</label>
                <input type="date" [(ngModel)]="newDebt.dueDate" style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px; box-sizing: border-box;">
              </div>
            </div>

            <div>
              <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Ví thanh toán</label>
              <select [(ngModel)]="newDebt.walletId" style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px;">
                <option *ngFor="let w of wallets" [value]="w.id">{{ w.name }} ({{ w.balance | number }} ₫)</option>
              </select>
            </div>

            <div *ngIf="createErrorMessage" style="color: #dc2626; font-size: 0.85rem;">{{ createErrorMessage }}</div>
          </div>

          <div style="display: flex; justify-content: flex-end; gap: 0.5rem; margin-top: 1.5rem;">
            <button (click)="showCreateModal = false" style="background: none; border: 1px solid #cbd5e1; padding: 0.5rem 1rem; border-radius: 6px; cursor: pointer;">Hủy</button>
            <button (click)="submitCreate()" [disabled]="isSubmittingCreate" style="background: #2563eb; color: white; border: none; padding: 0.5rem 1rem; border-radius: 6px; cursor: pointer;">
              {{ isSubmittingCreate ? 'Đang lưu...' : 'Lưu khoản nợ' }}
            </button>
          </div>
        </div>
      </div>
    </div>
  `
})
export class DebtListComponent implements OnInit {
  private debtService = inject(DebtService);
  private walletService = inject(WalletService);

  debts = signal<DebtDto[]>([]);
  isLoading = signal<boolean>(false);
  filterType = 0; // 0: All, 1: Lend, 2: Borrow
  wallets: WalletDto[] = [];

  showCreateModal = false;
  isSubmittingCreate = false;
  createErrorMessage = '';

  newDebt: CreateDebtRequest = {
    type: 1,
    personName: '',
    amount: 1000000,
    dueDate: new Date(Date.now() + 7 * 86400000).toISOString().substring(0, 10),
    walletId: ''
  };

  showPayModal = false;
  selectedDebt: DebtDto | null = null;
  payAmount = 0;
  payWalletId = '';
  payIdempotencyKey = '';
  isSubmittingPay = false;
  payErrorMessage = '';

  ngOnInit() {
    this.loadDebts();
    this.walletService.getWallets().subscribe(ws => {
      this.wallets = ws;
      if (ws.length > 0) {
        this.newDebt.walletId = ws[0].id;
        this.payWalletId = ws[0].id;
      }
    });
  }

  loadDebts() {
    this.isLoading.set(true);
    this.debtService.getDebts().subscribe({
      next: res => {
        this.debts.set(res);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  filteredDebts(): DebtDto[] {
    if (this.filterType === 0) return this.debts();
    return this.debts().filter(d => d.type === this.filterType);
  }

  getProgressPercent(d: DebtDto): number {
    if (d.amount <= 0) return 0;
    const p = Math.round((d.paidAmount / d.amount) * 100);
    return Math.min(100, Math.max(0, p));
  }

  openCreateModal() {
    this.showCreateModal = true;
    this.createErrorMessage = '';
  }

  submitCreate() {
    if (!this.newDebt.personName.trim()) {
      this.createErrorMessage = 'Tên người liên quan không được để trống';
      return;
    }
    this.isSubmittingCreate = true;
    this.createErrorMessage = '';
    this.debtService.createDebt(this.newDebt).subscribe({
      next: created => {
        this.isSubmittingCreate = false;
        this.showCreateModal = false;
        this.debts.update(list => [created, ...list]);
      },
      error: err => {
        this.isSubmittingCreate = false;
        this.createErrorMessage = err?.error?.message || 'Lỗi khi tạo khoản nợ';
      }
    });
  }

  // F-03: Sinh Idempotency-Key 1 lần mỗi lần mở dialog
  openPayModal(debt: DebtDto) {
    this.selectedDebt = debt;
    this.payAmount = debt.remainingAmount;
    this.payIdempotencyKey = crypto.randomUUID ? crypto.randomUUID() : 'pay-' + Math.random().toString(36).substring(2);
    this.payErrorMessage = '';
    this.showPayModal = true;
  }

  submitPayment() {
    if (!this.selectedDebt) return;
    if (this.payAmount <= 0) {
      this.payErrorMessage = 'Số tiền phải lớn hơn 0';
      return;
    }
    if (this.payAmount > this.selectedDebt.remainingAmount) {
      this.payErrorMessage = 'Số tiền thanh toán vượt dư nợ còn lại';
      return;
    }

    this.isSubmittingPay = true;
    this.payErrorMessage = '';

    this.debtService.makePayment(this.selectedDebt.id, {
      amount: this.payAmount,
      walletId: this.payWalletId
    }, this.payIdempotencyKey).subscribe({
      next: () => {
        this.isSubmittingPay = false;
        this.showPayModal = false;
        this.loadDebts();
      },
      error: err => {
        this.isSubmittingPay = false;
        this.payErrorMessage = err?.error?.message || 'Lỗi thanh toán';
      }
    });
  }

  deleteDebt(debt: DebtDto) {
    if (!confirm(`Xóa khoản nợ với "${debt.personName}"?`)) return;
    this.debtService.deleteDebt(debt.id).subscribe({
      next: () => {
        this.debts.update(list => list.filter(d => d.id !== debt.id));
      },
      error: err => {
        alert(err?.error?.message || 'Không thể xóa khoản nợ');
      }
    });
  }
}

