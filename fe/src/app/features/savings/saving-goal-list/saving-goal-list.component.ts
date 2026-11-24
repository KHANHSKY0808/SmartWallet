import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { SavingGoalService } from '../../../core/services/saving-goal.service';
import { WalletService } from '../../../core/services/wallet.service';
import { SavingGoalDto } from '../../../core/models/saving-goal.models';
import { WalletDto } from '../../../core/models/wallet.models';

@Component({
  selector: 'app-saving-goal-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
    <div class="saving-goal-container" style="width: 100%; max-width: 100%; box-sizing: border-box; overflow-x: hidden; padding: 1rem;">
      <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; flex-wrap: wrap; gap: 0.75rem;">
        <div>
          <h2 style="margin: 0; color: #1e293b;">Mục tiêu tiết kiệm</h2>
          <p style="margin: 0.25rem 0 0; color: #64748b; font-size: 0.9rem;">Lập kế hoạch và theo dõi tiến độ tích lũy tài chính</p>
        </div>
      </div>

      <!-- Empty state -->
      <div *ngIf="goals().length === 0 && !isLoading()" class="empty-state" style="text-align: center; padding: 3rem 1rem; background: white; border-radius: 8px; border: 1px dashed #cbd5e1;">
        <div style="font-size: 2.5rem; margin-bottom: 0.5rem;">🎯</div>
        <h3 style="margin: 0 0 0.5rem; color: #334155;">Chưa có mục tiêu tiết kiệm nào</h3>
        <p style="color: #64748b; margin: 0;">Hãy tạo mục tiêu đầu tiên để bắt đầu tích lũy cho ước mơ của bạn.</p>
      </div>

      <!-- Goal cards list (F-01) -->
      <div class="goal-grid" style="display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 1.25rem;">
        <div
          *ngFor="let g of goals()"
          class="goal-card"
          data-testid="goal-card"
          style="background: white; border-radius: 8px; padding: 1.25rem; box-shadow: 0 2px 8px rgba(0,0,0,0.06); border: 1px solid #e2e8f0; display: flex; flex-direction: column; justify-content: space-between;"
        >
          <div>
            <div style="display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 0.75rem;">
              <h3 style="margin: 0; font-size: 1.15rem; color: #0f172a;">{{ g.name }}</h3>
              <!-- Completed label (F-01) -->
              <span
                *ngIf="g.statusCode === 2 || g.status === 'Completed'"
                class="badge badge-success"
                data-testid="completed-badge"
                style="background: #dcfce7; color: #15803d; padding: 0.25rem 0.6rem; border-radius: 9999px; font-size: 0.8rem; font-weight: 600;"
              >
                🎉 Hoàn thành (Completed)
              </span>
              <span
                *ngIf="g.statusCode === 1 || g.status === 'InProgress'"
                class="badge badge-info"
                style="background: #e0f2fe; color: #0369a1; padding: 0.25rem 0.6rem; border-radius: 9999px; font-size: 0.8rem; font-weight: 600;"
              >
                Đang thực hiện
              </span>
            </div>

            <!-- Amounts & Progress bar (F-01) -->
            <div style="margin-bottom: 0.5rem;">
              <div style="display: flex; justify-content: space-between; font-size: 0.95rem; margin-bottom: 0.35rem;">
                <span style="font-weight: 600; color: #2563eb;">{{ g.currentAmount | number }} {{ g.currency }}</span>
                <span style="color: #64748b;">/ {{ g.targetAmount | number }} {{ g.currency }}</span>
              </div>
              <!-- Progress bar with percent (F-01) -->
              <div style="width: 100%; height: 10px; background: #e2e8f0; border-radius: 5px; overflow: hidden; margin-bottom: 0.25rem;">
                <div
                  class="progress-fill"
                  data-testid="progress-fill"
                  [style.width.%]="Math.min(g.percent, 100)"
                  style="height: 100%; background: #22c55e; border-radius: 5px; transition: width 0.3s;"
                ></div>
              </div>
              <div style="display: flex; justify-content: space-between; font-size: 0.85rem; color: #64748b;">
                <span data-testid="percent-label" style="font-weight: 600;">{{ g.percent | number:'1.1-1' }}%</span>
                <!-- Days left (F-01) -->
                <span data-testid="days-left-label">{{ g.daysLeft }} ngày còn lại</span>
              </div>
            </div>

            <!-- Monthly required (F-01) -->
            <div style="background: #f8fafc; border-radius: 6px; padding: 0.5rem 0.75rem; margin-top: 0.75rem; font-size: 0.85rem; color: #475569;">
              <span>Tiết kiệm hàng tháng: </span>
              <strong data-testid="monthly-required-label" style="color: #0f172a;">
                {{ (g.monthlyRequired | number) || 0 }} {{ g.currency }}/tháng
              </strong>
            </div>
          </div>

          <!-- Actions -->
          <div style="display: flex; gap: 0.5rem; margin-top: 1.25rem;">
            <button
              type="button"
              (click)="openDialog('deposit', g)"
              [disabled]="g.statusCode === 2 || g.status === 'Completed' || g.statusCode === 3"
              style="flex: 1; padding: 0.5rem; background: #2563eb; color: white; border: none; border-radius: 6px; font-weight: 600; cursor: pointer; font-size: 0.9rem;"
            >
              + Nạp tiền
            </button>
            <button
              type="button"
              (click)="openDialog('withdraw', g)"
              [disabled]="g.currentAmount <= 0 || g.statusCode === 3"
              style="flex: 1; padding: 0.5rem; background: #f1f5f9; color: #334155; border: 1px solid #cbd5e1; border-radius: 6px; font-weight: 600; cursor: pointer; font-size: 0.9rem;"
            >
              - Rút tiền
            </button>
          </div>
        </div>
      </div>

      <!-- Deposit / Withdraw Dialog (F-02) -->
      <div
        *ngIf="showDialog()"
        class="modal-backdrop"
        data-testid="transfer-dialog"
        style="position: fixed; inset: 0; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1000; padding: 1rem;"
      >
        <div class="modal-card" style="background: white; border-radius: 8px; max-width: 440px; width: 100%; padding: 1.5rem; box-shadow: 0 4px 16px rgba(0,0,0,0.15);">
          <h3 style="margin-top: 0; color: #1e293b;">
            {{ dialogType() === 'deposit' ? 'Nạp tiền vào mục tiêu' : 'Rút tiền về ví' }}
          </h3>
          <p style="color: #64748b; font-size: 0.9rem; margin-top: -0.25rem;">
            Mục tiêu: <strong>{{ activeGoal()?.name }}</strong>
          </p>

          <!-- Error message in dialog (F-02) -->
          <div *ngIf="dialogError()" class="alert alert-danger" data-testid="dialog-error" style="background: #fef2f2; color: #b91c1c; padding: 0.6rem 0.75rem; border-radius: 6px; margin-bottom: 1rem; font-size: 0.9rem;">
            {{ dialogError() }}
          </div>

          <form (ngSubmit)="submitDialog()">
            <div style="margin-bottom: 1rem;">
              <label style="display: block; font-weight: 500; margin-bottom: 0.35rem; color: #334155;">Ví</label>
              <select
                [(ngModel)]="dialogWalletId"
                name="dialogWalletId"
                required
                style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 0.95rem;"
              >
                <option value="" disabled>-- Chọn ví --</option>
                <option *ngFor="let w of wallets()" [value]="w.id">
                  {{ w.name }} ({{ w.balance | number }} {{ w.currency }})
                </option>
              </select>
            </div>

            <div style="margin-bottom: 1.25rem;">
              <label style="display: block; font-weight: 500; margin-bottom: 0.35rem; color: #334155;">Số tiền</label>
              <input
                type="number"
                [(ngModel)]="dialogAmount"
                name="dialogAmount"
                placeholder="Nhập số tiền..."
                min="1"
                required
                style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 0.95rem; box-sizing: border-box;"
              />
            </div>

            <div style="display: flex; gap: 0.75rem; justify-content: flex-end;">
              <button
                type="button"
                (click)="closeDialog()"
                [disabled]="isSubmitting()"
                style="padding: 0.5rem 1rem; border: 1px solid #cbd5e1; border-radius: 6px; background: white; color: #475569; font-weight: 500; cursor: pointer;"
              >
                Hủy
              </button>
              <!-- Submit button locked when submitting (F-02) -->
              <button
                type="submit"
                data-testid="dialog-submit-btn"
                [disabled]="isSubmitting() || !dialogWalletId || !dialogAmount || dialogAmount <= 0"
                style="padding: 0.5rem 1.25rem; background: #2563eb; color: white; border: none; border-radius: 6px; font-weight: 600; cursor: pointer;"
              >
                {{ isSubmitting() ? 'Đang xử lý...' : (dialogType() === 'deposit' ? 'Xác nhận nạp' : 'Xác nhận rút') }}
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  `
})
export class SavingGoalListComponent implements OnInit {
  protected readonly Math = Math;

  private goalService = inject(SavingGoalService);
  private walletService = inject(WalletService);

  goals = signal<SavingGoalDto[]>([]);
  wallets = signal<WalletDto[]>([]);
  isLoading = signal(false);

  // Dialog state (F-02)
  showDialog = signal(false);
  dialogType = signal<'deposit' | 'withdraw'>('deposit');
  activeGoal = signal<SavingGoalDto | null>(null);
  currentIdempotencyKey = signal<string>('');
  dialogWalletId = '';
  dialogAmount: number | null = null;
  dialogError = signal<string | null>(null);
  isSubmitting = signal(false);

  ngOnInit(): void {
    this.loadGoals();
    this.loadWallets();
  }

  loadGoals(): void {
    this.isLoading.set(true);
    this.goalService.getGoals().subscribe({
      next: (list) => {
        this.goals.set(list);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  loadWallets(): void {
    this.walletService.getWallets().subscribe({
      next: (list) => {
        this.wallets.set(list);
        if (list.length > 0) {
          this.dialogWalletId = list[0].id;
        }
      }
    });
  }

  // F-02: Generates Idempotency-Key exactly ONCE per dialog open
  openDialog(type: 'deposit' | 'withdraw', goal: SavingGoalDto): void {
    this.dialogType.set(type);
    this.activeGoal.set(goal);
    this.currentIdempotencyKey.set(crypto.randomUUID());
    this.dialogAmount = null;
    this.dialogError.set(null);
    this.isSubmitting.set(false);
    this.showDialog.set(true);
  }

  closeDialog(): void {
    if (this.isSubmitting()) return;
    this.showDialog.set(false);
    this.activeGoal.set(null);
  }

  submitDialog(): void {
    const goal = this.activeGoal();
    if (!goal || !this.dialogWalletId || !this.dialogAmount || this.dialogAmount <= 0) {
      return;
    }

    this.isSubmitting.set(true);
    this.dialogError.set(null);

    const key = this.currentIdempotencyKey();

    if (this.dialogType() === 'deposit') {
      this.goalService.deposit(this.dialogWalletId, {
        goalId: goal.id,
        amount: this.dialogAmount,
        direction: 'deposit'
      }, key).subscribe({
        next: () => {
          this.isSubmitting.set(false);
          this.closeDialog();
          this.loadGoals();
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.dialogError.set(err.error?.message || 'Lỗi khi nạp tiền vào mục tiêu');
        }
      });
    } else {
      this.goalService.withdraw(goal.id, {
        walletId: this.dialogWalletId,
        amount: this.dialogAmount
      }, key).subscribe({
        next: () => {
          this.isSubmitting.set(false);
          this.closeDialog();
          this.loadGoals();
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.dialogError.set(err.error?.message || 'Lỗi khi rút tiền khỏi mục tiêu');
        }
      });
    }
  }
}
