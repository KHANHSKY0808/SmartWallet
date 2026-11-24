import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, ActivatedRoute, Router } from '@angular/router';
import { FormsModule, ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { BudgetService } from '../../../core/services/budget.service';
import { CategoryService } from '../../../core/services/category.service';
import { CategoryDto } from '../../../core/models/category.models';

@Component({
  selector: 'app-budget-form',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule, ReactiveFormsModule],
  template: `
    <div class="budget-form-container" style="max-width: 600px; margin: 0 auto; padding: 1rem; box-sizing: border-box;">
      <div style="background: white; border: 1px solid #e2e8f0; border-radius: 12px; padding: 1.5rem; box-shadow: 0 1px 3px rgba(0,0,0,0.05);">
        <div style="margin-bottom: 1.5rem;">
          <a routerLink="/budgets" style="color: #64748b; text-decoration: none; font-size: 0.85rem; display: inline-flex; align-items: center; gap: 0.3rem; margin-bottom: 0.5rem;">
            ← Quay lại danh sách ngân sách
          </a>
          <h2 style="margin: 0; font-size: 1.35rem; color: #0f172a; font-weight: 700;">
            {{ isEditMode() ? 'Chỉnh sửa Hạn mức Ngân sách' : 'Thiết lập Ngân sách Mới' }}
          </h2>
          <p style="margin: 0.25rem 0 0; color: #64748b; font-size: 0.85rem;">
            {{ isEditMode() ? 'Cập nhật lại hạn mức chi tiêu cho danh mục' : 'Hệ thống sẽ tự động quét chi tiêu hiện có và theo dõi thời gian thực' }}
          </p>
        </div>

        <!-- Global Alert Message -->
        <div *ngIf="errorMessage()" style="background: #fef2f2; border: 1px solid #fecaca; color: #991b1b; padding: 0.75rem 1rem; border-radius: 6px; font-size: 0.85rem; margin-bottom: 1.25rem;">
          {{ errorMessage() }}
        </div>

        <form [formGroup]="budgetForm" (ngSubmit)="onSubmit()">
          <!-- Category Select (F-02: Only Expense Categories Type === 1) -->
          <div style="margin-bottom: 1.25rem;">
            <label style="display: block; font-size: 0.85rem; font-weight: 600; color: #334155; margin-bottom: 0.4rem;">
              Danh mục Chi tiêu <span style="color: #ef4444;">*</span>
            </label>
            <select formControlName="categoryId" 
                    id="categoryId"
                    [disabled]="isEditMode()"
                    style="width: 100%; padding: 0.6rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 0.95rem; box-sizing: border-box; background: white;">
              <option value="">-- Chọn danh mục chi tiêu --</option>
              <ng-container *ngFor="let cat of expenseCategories()">
                <option [value]="cat.id">
                  {{ cat.icon || '🛒' }} {{ cat.name }}
                </option>
                <option *ngFor="let child of cat.children" [value]="child.id">
                  &nbsp;&nbsp;&nbsp;↳ {{ child.icon || '🔹' }} {{ child.name }}
                </option>
              </ng-container>
            </select>
            <!-- F-02: Hiển thị lỗi trùng 409 ngay dưới ô danh mục -->
            <div *ngIf="categoryError()" class="category-error" style="color: #ef4444; font-size: 0.8rem; margin-top: 0.35rem; font-weight: 500;">
              {{ categoryError() }}
            </div>
            <div *ngIf="budgetForm.get('categoryId')?.touched && budgetForm.get('categoryId')?.hasError('required')" style="color: #ef4444; font-size: 0.8rem; margin-top: 0.35rem;">
              Vui lòng chọn danh mục chi tiêu.
            </div>
          </div>

          <!-- Amount Input (F-02: Validate amount > 0) -->
          <div style="margin-bottom: 1.25rem;">
            <label style="display: block; font-size: 0.85rem; font-weight: 600; color: #334155; margin-bottom: 0.4rem;">
              Hạn mức ngân sách (VNĐ) <span style="color: #ef4444;">*</span>
            </label>
            <input type="number" 
                   id="amount"
                   formControlName="amount" 
                   placeholder="Nhập số tiền hạn mức (vd: 2000000)"
                   style="width: 100%; padding: 0.6rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 0.95rem; box-sizing: border-box;" />
            <div *ngIf="budgetForm.get('amount')?.touched && budgetForm.get('amount')?.invalid" class="amount-error" style="color: #ef4444; font-size: 0.8rem; margin-top: 0.35rem;">
              <span *ngIf="budgetForm.get('amount')?.hasError('required')">Số tiền không được để trống.</span>
              <span *ngIf="budgetForm.get('amount')?.hasError('min')">Số tiền hạn mức phải lớn hơn 0.</span>
            </div>
          </div>

          <!-- Month & Year Fields -->
          <div style="display: flex; gap: 1rem; margin-bottom: 1.5rem; flex-wrap: wrap;">
            <div style="flex: 1; min-width: 130px;">
              <label style="display: block; font-size: 0.85rem; font-weight: 600; color: #334155; margin-bottom: 0.4rem;">
                Tháng <span style="color: #ef4444;">*</span>
              </label>
              <select formControlName="month" 
                      id="month"
                      [disabled]="isEditMode()"
                      style="width: 100%; padding: 0.6rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 0.95rem; box-sizing: border-box; background: white;">
                <option *ngFor="let m of months" [value]="m">Tháng {{ m }}</option>
              </select>
            </div>
            <div style="flex: 1; min-width: 130px;">
              <label style="display: block; font-size: 0.85rem; font-weight: 600; color: #334155; margin-bottom: 0.4rem;">
                Năm <span style="color: #ef4444;">*</span>
              </label>
              <select formControlName="year" 
                      id="year"
                      [disabled]="isEditMode()"
                      style="width: 100%; padding: 0.6rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 0.95rem; box-sizing: border-box; background: white;">
                <option *ngFor="let y of years" [value]="y">Năm {{ y }}</option>
              </select>
            </div>
          </div>

          <!-- Buttons -->
          <div style="display: flex; justify-content: flex-end; gap: 0.75rem;">
            <a routerLink="/budgets" style="padding: 0.6rem 1.2rem; border: 1px solid #cbd5e1; border-radius: 6px; color: #475569; text-decoration: none; font-size: 0.9rem; font-weight: 500;">
              Hủy
            </a>
            <button type="submit" 
                    [disabled]="budgetForm.invalid || submitting()"
                    style="padding: 0.6rem 1.5rem; background: #2563eb; color: white; border: none; border-radius: 6px; font-size: 0.9rem; font-weight: 600; cursor: pointer;">
              {{ submitting() ? 'Đang lưu...' : (isEditMode() ? 'Cập nhật hạn mức' : 'Lưu ngân sách') }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class BudgetFormComponent implements OnInit {
  private fb = inject(FormBuilder);
  private budgetService = inject(BudgetService);
  private categoryService = inject(CategoryService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  budgetForm!: FormGroup;
  isEditMode = signal<boolean>(false);
  budgetId: string | null = null;
  expenseCategories = signal<CategoryDto[]>([]);
  categoryError = signal<string | null>(null);
  errorMessage = signal<string | null>(null);
  submitting = signal<boolean>(false);

  months = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
  years = [2024, 2025, 2026, 2027, 2028];

  ngOnInit() {
    const currentMonth = new Date().getMonth() + 1;
    const currentYear = new Date().getFullYear();

    this.budgetForm = this.fb.group({
      categoryId: ['', Validators.required],
      amount: [null, [Validators.required, Validators.min(0.01)]],
      month: [currentMonth, Validators.required],
      year: [currentYear, Validators.required]
    });

    this.loadExpenseCategories();

    this.budgetId = this.route.snapshot.paramMap.get('id');
    if (this.budgetId) {
      this.isEditMode.set(true);
      this.loadBudgetDetails(this.budgetId);
    }
  }

  // F-02: Bộ chọn danh mục chỉ hiển thị danh mục Chi (type === 1)
  loadExpenseCategories() {
    this.categoryService.getCategories(1).subscribe({
      next: (cats) => {
        // Lọc chắc chắn chỉ lấy Type === 1
        this.expenseCategories.set(cats.filter(c => c.type === 1));
      },
      error: () => {
        this.errorMessage.set('Không thể tải danh sách danh mục.');
      }
    });
  }

  loadBudgetDetails(id: string) {
    this.budgetService.getBudget(id).subscribe({
      next: (b) => {
        this.budgetForm.patchValue({
          categoryId: b.categoryId,
          amount: b.amount,
          month: b.month,
          year: b.year
        });
      },
      error: (err) => {
        this.errorMessage.set(err?.error?.message || 'Không tìm thấy thông tin ngân sách');
      }
    });
  }

  onSubmit() {
    this.categoryError.set(null);
    this.errorMessage.set(null);

    if (this.budgetForm.invalid) {
      this.budgetForm.markAllAsTouched();
      return;
    }

    this.submitting.set(true);

    if (this.isEditMode() && this.budgetId) {
      this.budgetService.updateBudget(this.budgetId, {
        amount: Number(this.budgetForm.get('amount')?.value)
      }).subscribe({
        next: () => {
          this.submitting.set(false);
          this.router.navigate(['/budgets']);
        },
        error: (err) => {
          this.submitting.set(false);
          this.errorMessage.set(err?.error?.message || 'Lỗi khi cập nhật hạn mức');
        }
      });
    } else {
      const val = this.budgetForm.getRawValue();
      this.budgetService.createBudget({
        categoryId: val.categoryId,
        amount: Number(val.amount),
        month: Number(val.month),
        year: Number(val.year)
      }).subscribe({
        next: () => {
          this.submitting.set(false);
          this.router.navigate(['/budgets']);
        },
        error: (err) => {
          this.submitting.set(false);
          // F-02: 409 hiển thị ngay dưới ô danh mục
          if (err.status === 409) {
            this.categoryError.set(err.error?.message || 'Ngân sách cho danh mục này trong tháng đã tồn tại');
          } else {
            this.errorMessage.set(err?.error?.message || 'Lỗi khi tạo ngân sách mới');
          }
        }
      });
    }
  }
}

