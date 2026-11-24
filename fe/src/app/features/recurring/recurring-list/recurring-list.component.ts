import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RecurringService } from '../../../core/services/recurring.service';
import { WalletService } from '../../../core/services/wallet.service';
import { CategoryService } from '../../../core/services/category.service';
import { RecurringRuleDto, CreateRecurringRuleRequest } from '../../../core/models/recurring.models';
import { WalletDto } from '../../../core/models/wallet.models';
import { CategoryDto } from '../../../core/models/category.models';

@Component({
  selector: 'app-recurring-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div style="padding: 1.5rem; max-width: 1000px; margin: 0 auto;">
      <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem;">
        <div>
          <h2 style="margin: 0; color: #1e293b;">🔄 Giao dịch định kỳ</h2>
          <p style="margin: 0.25rem 0 0 0; color: #64748b; font-size: 0.9rem;">Tự động tạo giao dịch lặp lại theo chu kỳ ngày, tuần, tháng, năm</p>
        </div>
        <button (click)="openCreateModal()" style="background: #2563eb; color: white; border: none; padding: 0.5rem 1rem; border-radius: 6px; cursor: pointer; font-weight: 500;">
          + Tạo quy tắc mới
        </button>
      </div>

      <!-- Loading / Error -->
      <div *ngIf="isLoading()" style="text-align: center; padding: 2rem; color: #64748b;">Đang tải danh sách...</div>

      <!-- Empty State -->
      <div *ngIf="!isLoading() && rules().length === 0" style="text-align: center; padding: 3rem; background: white; border-radius: 8px; border: 1px dashed #cbd5e1;">
        <div style="font-size: 3rem; margin-bottom: 0.5rem;">📅</div>
        <h3 style="margin: 0 0 0.5rem 0; color: #334155;">Chưa có giao dịch định kỳ nào</h3>
        <p style="color: #64748b; font-size: 0.9rem;">Hãy tạo quy tắc đầu tiên để tự động hóa quản lý thu chi.</p>
      </div>

      <!-- Rules List -->
      <div *ngIf="!isLoading() && rules().length > 0" style="display: grid; gap: 1rem;">
        <div *ngFor="let r of rules()" style="background: white; border-radius: 8px; padding: 1.25rem; border: 1px solid #e2e8f0; display: flex; justify-content: space-between; align-items: center;">
          <div>
            <div style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.25rem;">
              <span style="font-weight: 600; font-size: 1.05rem; color: #1e293b;">{{ r.name }}</span>
              <span [style.background]="r.type === 1 ? '#fee2e2' : '#dcfce7'" [style.color]="r.type === 1 ? '#b91c1c' : '#15803d'" style="padding: 0.15rem 0.5rem; border-radius: 4px; font-size: 0.75rem; font-weight: 600;">
                {{ r.type === 1 ? 'Chi tiêu' : 'Thu nhập' }}
              </span>
              <span [style.background]="r.isActive ? '#e0f2fe' : '#f1f5f9'" [style.color]="r.isActive ? '#0369a1' : '#64748b'" style="padding: 0.15rem 0.5rem; border-radius: 4px; font-size: 0.75rem;">
                {{ r.isActive ? 'Đang hoạt động' : 'Tạm dừng' }}
              </span>
            </div>
            <div style="color: #64748b; font-size: 0.85rem; display: flex; gap: 1rem; margin-top: 0.5rem;">
              <span>Số tiền: <strong>{{ r.amount | number }} ₫</strong></span>
              <span>Tần suất: <strong>{{ getFrequencyName(r.frequency) }}</strong></span>
              <span>Kỳ tới: <strong style="color: #2563eb;">{{ r.nextExecutionDate }}</strong> (Neo ngày {{ r.anchorDay }})</span>
            </div>
            <div *ngIf="r.lastError" style="margin-top: 0.5rem; color: #dc2626; font-size: 0.8rem; background: #fef2f2; padding: 0.25rem 0.5rem; border-radius: 4px;">
              ⚠️ Lỗi kỳ trước: {{ r.lastError }}
            </div>
          </div>
          <div style="display: flex; gap: 0.5rem; align-items: center;">
            <button (click)="toggleActive(r)" [style.background]="r.isActive ? '#fef3c7' : '#dcfce7'" [style.color]="r.isActive ? '#b45309' : '#15803d'" style="border: none; padding: 0.4rem 0.75rem; border-radius: 6px; cursor: pointer; font-size: 0.85rem; font-weight: 500;">
              {{ r.isActive ? 'Tạm dừng' : 'Kích hoạt' }}
            </button>
            <button (click)="deleteRule(r)" style="background: none; border: 1px solid #fecaca; color: #dc2626; padding: 0.4rem 0.75rem; border-radius: 6px; cursor: pointer; font-size: 0.85rem;">
              Xóa
            </button>
          </div>
        </div>
      </div>

      <!-- Create Modal (F-01: Form rule xem trước 3 kỳ) -->
      <div *ngIf="showModal" style="position: fixed; inset: 0; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1000;">
        <div style="background: white; border-radius: 8px; width: 100%; max-width: 500px; padding: 1.5rem; max-height: 90vh; overflow-y: auto;">
          <h3 style="margin-top: 0; margin-bottom: 1rem;">Tạo quy tắc định kỳ mới</h3>

          <div style="display: flex; flex-direction: column; gap: 0.85rem;">
            <div>
              <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Tên quy tắc</label>
              <input type="text" [(ngModel)]="newRule.name" placeholder="Ví dụ: Netflix, Tiền nhà..." style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px; box-sizing: border-box;">
            </div>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem;">
              <div>
                <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Loại</label>
                <select [(ngModel)]="newRule.type" style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px;">
                  <option [ngValue]="1">Chi tiêu</option>
                  <option [ngValue]="2">Thu nhập</option>
                </select>
              </div>
              <div>
                <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Số tiền</label>
                <input type="number" [(ngModel)]="newRule.amount" placeholder="0" style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px; box-sizing: border-box;">
              </div>
            </div>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem;">
              <div>
                <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Ví</label>
                <select [(ngModel)]="newRule.walletId" style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px;">
                  <option *ngFor="let w of wallets" [value]="w.id">{{ w.name }}</option>
                </select>
              </div>
              <div>
                <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Danh mục</label>
                <select [(ngModel)]="newRule.categoryId" style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px;">
                  <option *ngFor="let c of filteredCategories()" [value]="c.id">{{ c.name }}</option>
                </select>
              </div>
            </div>

            <div>
              <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Tần suất</label>
              <select [(ngModel)]="newRule.frequency" (change)="updatePreview()" style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px;">
                <option [ngValue]="1">Hằng ngày (Daily)</option>
                <option [ngValue]="2">Hằng tuần (Weekly)</option>
                <option [ngValue]="3">Hằng tháng (Monthly)</option>
                <option [ngValue]="4">Hằng năm (Yearly)</option>
              </select>
            </div>

            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem;">
              <div>
                <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Ngày bắt đầu</label>
                <input type="date" [(ngModel)]="newRule.startDate" (change)="updatePreview()" style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px; box-sizing: border-box;">
              </div>
              <div>
                <label style="display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.25rem;">Ngày kết thúc (tùy chọn)</label>
                <input type="date" [(ngModel)]="newRule.endDate" style="width: 100%; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px; box-sizing: border-box;">
              </div>
            </div>

            <!-- F-01: Xem trước 3 kỳ kế tiếp -->
            <div *ngIf="previewOccurrences.length > 0" style="background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 0.75rem;">
              <span style="font-size: 0.8rem; font-weight: 600; color: #475569;">👁️ Xem trước 3 kỳ kế tiếp:</span>
              <ul style="margin: 0.35rem 0 0 0; padding-left: 1.25rem; font-size: 0.85rem; color: #2563eb;">
                <li *ngFor="let occ of previewOccurrences"><strong>{{ occ }}</strong></li>
              </ul>
            </div>

            <div *ngIf="errorMessage" style="color: #dc2626; font-size: 0.85rem;">{{ errorMessage }}</div>
          </div>

          <div style="display: flex; justify-content: flex-end; gap: 0.5rem; margin-top: 1.5rem;">
            <button (click)="showModal = false" style="background: none; border: 1px solid #cbd5e1; padding: 0.5rem 1rem; border-radius: 6px; cursor: pointer;">Hủy</button>
            <button (click)="submitCreate()" [disabled]="isSubmitting" style="background: #2563eb; color: white; border: none; padding: 0.5rem 1rem; border-radius: 6px; cursor: pointer;">
              {{ isSubmitting ? 'Đang lưu...' : 'Lưu quy tắc' }}
            </button>
          </div>
        </div>
      </div>
    </div>
  `
})
export class RecurringListComponent implements OnInit {
  private recurringService = inject(RecurringService);
  private walletService = inject(WalletService);
  private categoryService = inject(CategoryService);

  rules = signal<RecurringRuleDto[]>([]);
  isLoading = signal<boolean>(false);
  wallets: WalletDto[] = [];
  categories: CategoryDto[] = [];

  showModal = false;
  isSubmitting = false;
  errorMessage = '';

  newRule: CreateRecurringRuleRequest = {
    name: '',
    walletId: '',
    categoryId: '',
    type: 1,
    amount: 100000,
    frequency: 3,
    startDate: new Date().toISOString().substring(0, 10),
    endDate: null
  };

  previewOccurrences: string[] = [];

  ngOnInit() {
    this.loadRules();
    this.walletService.getWallets().subscribe(ws => {
      this.wallets = ws;
      if (ws.length > 0 && !this.newRule.walletId) {
        this.newRule.walletId = ws[0].id;
      }
    });
    this.categoryService.getCategories().subscribe(cs => {
      this.categories = cs;
      const filtered = this.filteredCategories();
      if (filtered.length > 0 && !this.newRule.categoryId) {
        this.newRule.categoryId = filtered[0].id;
      }
    });
  }

  loadRules() {
    this.isLoading.set(true);
    this.recurringService.getRules().subscribe({
      next: res => {
        this.rules.set(res);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  filteredCategories(): CategoryDto[] {
    return this.categories.filter(c => c.type === this.newRule.type);
  }

  getFrequencyName(f: number): string {
    switch (f) {
      case 1: return 'Hằng ngày';
      case 2: return 'Hằng tuần';
      case 3: return 'Hằng tháng';
      case 4: return 'Hằng năm';
      default: return 'Khác';
    }
  }

  openCreateModal() {
    this.showModal = true;
    this.errorMessage = '';
    this.updatePreview();
  }

  updatePreview() {
    this.previewOccurrences = this.recurringService.previewNextOccurrences(
      this.newRule.startDate, this.newRule.frequency, 3);
  }

  toggleActive(rule: RecurringRuleDto) {
    this.recurringService.updateRule(rule.id, { isActive: !rule.isActive }).subscribe({
      next: updated => {
        this.rules.update(list => list.map(r => r.id === updated.id ? updated : r));
      }
    });
  }

  deleteRule(rule: RecurringRuleDto) {
    if (!confirm(`Xóa quy tắc định kỳ "${rule.name}"?`)) return;
    this.recurringService.deleteRule(rule.id).subscribe({
      next: () => {
        this.rules.update(list => list.filter(r => r.id !== rule.id));
      }
    });
  }

  submitCreate() {
    if (!this.newRule.name.trim()) {
      this.errorMessage = 'Tên không được để trống';
      return;
    }
    this.isSubmitting = true;
    this.errorMessage = '';
    this.recurringService.createRule(this.newRule).subscribe({
      next: created => {
        this.isSubmitting = false;
        this.showModal = false;
        this.rules.update(list => [created, ...list]);
      },
      error: err => {
        this.isSubmitting = false;
        this.errorMessage = err?.error?.message || 'Lỗi khi tạo quy tắc';
      }
    });
  }
}

