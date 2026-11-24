import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { BudgetService } from '../../../core/services/budget.service';
import { CategoryService } from '../../../core/services/category.service';
import { BudgetDto } from '../../../core/models/budget.models';
import { CategoryDto } from '../../../core/models/category.models';

@Component({
  selector: 'app-budget-list',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule],
  template: `
    <div class="budget-container" style="max-width: 900px; margin: 0 auto; padding: 1rem;">
      <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; flex-wrap: wrap; gap: 1rem;">
        <div>
          <h2 style="margin: 0; font-size: 1.5rem; color: #0f172a; font-weight: 700;">🎯 Quản lý Ngân sách</h2>
          <p style="margin: 0.25rem 0 0; color: #64748b; font-size: 0.9rem;">Kiểm soát hạn mức chi tiêu hàng tháng theo danh mục</p>
        </div>
        <div style="display: flex; gap: 0.75rem; align-items: center; flex-wrap: wrap;">
          <select [(ngModel)]="selectedMonth" (change)="loadBudgets()" style="padding: 0.5rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 0.9rem; background: white;">
            <option *ngFor="let m of months" [value]="m">Tháng {{ m }}</option>
          </select>
          <select [(ngModel)]="selectedYear" (change)="loadBudgets()" style="padding: 0.5rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 0.9rem; background: white;">
            <option *ngFor="let y of years" [value]="y">Năm {{ y }}</option>
          </select>
          <a routerLink="/budgets/create" style="background: #2563eb; color: white; padding: 0.5rem 1rem; border-radius: 6px; text-decoration: none; font-weight: 600; font-size: 0.9rem; display: inline-flex; align-items: center; gap: 0.4rem;">
            <span>➕</span> Đặt ngân sách
          </a>
        </div>
      </div>

      <!-- Loading State -->
      <div *ngIf="loading()" style="text-align: center; padding: 2rem; color: #64748b;">
        Đang tải dữ liệu ngân sách...
      </div>

      <!-- Empty State -->
      <div *ngIf="!loading() && budgets().length === 0" style="background: white; border: 1px dashed #cbd5e1; border-radius: 12px; padding: 3rem 1.5rem; text-align: center;">
        <span style="font-size: 3rem; display: block; margin-bottom: 1rem;">📊</span>
        <h3 style="margin: 0 0 0.5rem; color: #1e293b; font-size: 1.15rem;">Chưa có ngân sách cho Tháng {{ selectedMonth }}/{{ selectedYear }}</h3>
        <p style="margin: 0 0 1.5rem; color: #64748b; font-size: 0.9rem;">Hãy thiết lập hạn mức chi tiêu để nhận cảnh báo thông minh khi chạm ngưỡng 80% & 100%.</p>
        <a routerLink="/budgets/create" style="background: #2563eb; color: white; padding: 0.6rem 1.2rem; border-radius: 6px; text-decoration: none; font-weight: 600; font-size: 0.9rem;">
          Tạo ngân sách đầu tiên
        </a>
      </div>

      <!-- Budget Cards List -->
      <div *ngIf="!loading() && budgets().length > 0" style="display: flex; flex-direction: column; gap: 1rem;">
        <div *ngFor="let b of budgets()" 
             class="budget-card" 
             style="background: white; border: 1px solid #e2e8f0; border-radius: 10px; padding: 1.25rem; box-shadow: 0 1px 3px rgba(0,0,0,0.05);">
          <div style="display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 0.75rem; flex-wrap: wrap; gap: 0.5rem;">
            <div style="display: flex; align-items: center; gap: 0.75rem;">
              <span style="font-size: 1.75rem; background: #f1f5f9; padding: 0.4rem 0.6rem; border-radius: 8px;">
                {{ getCategoryIcon(b.categoryId) }}
              </span>
              <div>
                <h4 style="margin: 0; font-size: 1.1rem; color: #0f172a; font-weight: 600;">
                  {{ getCategoryName(b.categoryId) }}
                </h4>
                <span [style.background]="getStatusBadgeBg(b.percent)"
                      [style.color]="getStatusBadgeColor(b.percent)"
                      style="font-size: 0.75rem; padding: 0.15rem 0.5rem; border-radius: 9999px; font-weight: 600; display: inline-block; margin-top: 0.25rem;">
                  {{ getStatusText(b.percent) }}
                </span>
              </div>
            </div>

            <div style="display: flex; gap: 0.5rem; align-items: center;">
              <button (click)="editBudget(b)" style="background: #f8fafc; border: 1px solid #cbd5e1; padding: 0.35rem 0.65rem; border-radius: 6px; font-size: 0.85rem; cursor: pointer; color: #334155;">
                ✏️ Sửa hạn mức
              </button>
              <button (click)="deleteBudget(b.id)" style="background: #fff1f2; border: 1px solid #fecdd3; padding: 0.35rem 0.65rem; border-radius: 6px; font-size: 0.85rem; cursor: pointer; color: #e11d48;">
                🗑️ Xóa
              </button>
            </div>
          </div>

          <!-- Numbers: Spent / Limit -->
          <div style="display: flex; justify-content: space-between; font-size: 0.9rem; margin-bottom: 0.5rem; color: #475569;">
            <span>Đã chi: <strong style="color: #0f172a;">{{ b.currentSpent | number:'1.0-0' }} đ</strong></span>
            <span>Hạn mức: <strong style="color: #0f172a;">{{ b.amount | number:'1.0-0' }} đ</strong></span>
          </div>

          <!-- Progress Bar (F-01: max width 100%, 3 colors) -->
          <div class="progress-bar-track" style="width: 100%; height: 12px; background: #e2e8f0; border-radius: 6px; overflow: hidden; position: relative;">
            <div class="progress-bar-fill" 
                 [style.width.%]="getBarWidth(b.percent)" 
                 [style.background]="getProgressColor(b.percent)"
                 style="height: 100%; transition: width 0.3s ease; max-width: 100%;">
            </div>
          </div>

          <div style="display: flex; justify-content: space-between; font-size: 0.8rem; margin-top: 0.4rem; color: #64748b;">
            <span>Tiến độ: <strong [style.color]="getProgressColor(b.percent)">{{ b.percent | number:'1.1-1' }}%</strong></span>
            <span *ngIf="b.amount >= b.currentSpent">Còn lại: {{ (b.amount - b.currentSpent) | number:'1.0-0' }} đ</span>
            <span *ngIf="b.currentSpent > b.amount" style="color: #ef4444; font-weight: 600;">Vượt: {{ (b.currentSpent - b.amount) | number:'1.0-0' }} đ</span>
          </div>
        </div>
      </div>
    </div>
  `
})
export class BudgetListComponent implements OnInit {
  private budgetService = inject(BudgetService);
  private categoryService = inject(CategoryService);
  private router = inject(Router);

  budgets = signal<BudgetDto[]>([]);
  categories = signal<CategoryDto[]>([]);
  loading = signal<boolean>(false);

  selectedMonth = new Date().getMonth() + 1;
  selectedYear = new Date().getFullYear();

  months = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
  years = [2024, 2025, 2026, 2027, 2028];

  ngOnInit() {
    this.loadCategories();
    this.loadBudgets();
  }

  loadCategories() {
    this.categoryService.getCategories(1).subscribe({
      next: (cats) => this.categories.set(cats),
      error: () => {}
    });
  }

  loadBudgets() {
    this.loading.set(true);
    this.budgetService.getBudgets({ month: this.selectedMonth, year: this.selectedYear }).subscribe({
      next: (data) => {
        this.budgets.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      }
    });
  }

  editBudget(budget: BudgetDto) {
    this.router.navigate(['/budgets/edit', budget.id]);
  }

  deleteBudget(id: string) {
    if (confirm('Bạn có chắc chắn muốn xóa ngân sách này? Hành động này sẽ xóa sổ cái tính toán liên quan.')) {
      this.budgetService.deleteBudget(id).subscribe({
        next: () => this.loadBudgets(),
        error: (err) => alert(err?.error?.message || 'Lỗi khi xóa ngân sách')
      });
    }
  }

  getCategoryName(categoryId: string): string {
    const flat = this.flattenCategories(this.categories());
    const found = flat.find(c => c.id === categoryId);
    return found ? found.name : 'Danh mục chi tiêu';
  }

  getCategoryIcon(categoryId: string): string {
    const flat = this.flattenCategories(this.categories());
    const found = flat.find(c => c.id === categoryId);
    return found?.icon || '🛒';
  }

  private flattenCategories(cats: CategoryDto[]): CategoryDto[] {
    const result: CategoryDto[] = [];
    for (const c of cats) {
      result.push(c);
      if (c.children && c.children.length > 0) {
        result.push(...this.flattenCategories(c.children));
      }
    }
    return result;
  }

  // F-01: Thanh tiến độ không tràn quá 100%
  getBarWidth(percent: number): number {
    return Math.min(Math.max(percent, 0), 100);
  }

  // F-01: 79.9% (xanh), 80-99.9% (vàng), >=100% (đỏ)
  getProgressColor(percent: number): string {
    if (percent >= 100) return '#ef4444'; // Red
    if (percent >= 80) return '#eab308'; // Yellow
    return '#22c55e'; // Green
  }

  getStatusBadgeBg(percent: number): string {
    if (percent >= 100) return '#fee2e2';
    if (percent >= 80) return '#fef9c3';
    return '#dcfce7';
  }

  getStatusBadgeColor(percent: number): string {
    if (percent >= 100) return '#991b1b';
    if (percent >= 80) return '#854d0e';
    return '#166534';
  }

  getStatusText(percent: number): string {
    if (percent >= 100) return 'Vượt ngân sách (≥100%)';
    if (percent >= 80) return 'Cảnh báo chi tiêu (≥80%)';
    return 'Bình thường';
  }
}

