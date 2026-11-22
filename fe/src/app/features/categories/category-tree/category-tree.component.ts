import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { CategoryService } from '../../../core/services/category.service';
import { AuthService } from '../../../core/services/auth.service';
import { CategoryDto } from '../../../core/models/category.models';

@Component({
  selector: 'app-category-tree',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="category-tree-container" style="max-width: 100%; box-sizing: border-box; overflow-x: hidden; padding: 1rem;">
      <div class="header-section" style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; flex-wrap: wrap; gap: 1rem;">
        <div>
          <h2 style="margin: 0; font-size: 1.5rem; color: #1e293b;">Cây danh mục thu chi</h2>
          <p style="margin: 0.25rem 0 0 0; color: #64748b; font-size: 0.9rem;">Quản lý phân loại chi tiêu và thu nhập theo danh mục cha-con</p>
        </div>
        <button (click)="openCreateModal()" class="btn btn-primary">+ Thêm danh mục</button>
      </div>

      <!-- Type Filter Tabs -->
      <div style="display: flex; gap: 0.5rem; margin-bottom: 1.5rem; border-bottom: 2px solid #e2e8f0; padding-bottom: 0.5rem;">
        <button
          (click)="filterType.set(1); loadCategories()"
          [class.active-tab]="filterType() === 1"
          class="tab-btn"
        >
          💸 Khoản chi (Expense)
        </button>
        <button
          (click)="filterType.set(2); loadCategories()"
          [class.active-tab]="filterType() === 2"
          class="tab-btn"
        >
          💰 Khoản thu (Income)
        </button>
      </div>

      <div *ngIf="isLoading()" style="text-align: center; padding: 2rem; color: #64748b;">
        Đang tải cây danh mục...
      </div>

      <div *ngIf="errorMessage()" class="alert alert-danger">
        {{ errorMessage() }}
      </div>

      <!-- Categories Tree View -->
      <div *ngIf="!isLoading()" class="tree-container" style="background: white; border-radius: 8px; border: 1px solid #e2e8f0; padding: 1rem;">
        <div *ngIf="categories().length === 0" style="text-align: center; padding: 2rem; color: #94a3b8;">
          Chưa có danh mục nào cho phân loại này.
        </div>

        <div *ngFor="let root of categories()" class="root-category-node" style="margin-bottom: 1rem; border-bottom: 1px solid #f1f5f9; padding-bottom: 0.75rem;">
          <!-- Root Category Header -->
          <div class="node-header" style="display: flex; justify-content: space-between; align-items: center; padding: 0.5rem; border-radius: 6px; background: #f8fafc;">
            <div style="display: flex; align-items: center; gap: 0.75rem;">
              <span class="category-icon" [style.background]="root.color || '#3b82f6'" style="display: inline-flex; align-items: center; justify-content: center; width: 32px; height: 32px; border-radius: 6px; color: white; font-size: 1rem;">
                {{ root.icon || '📁' }}
              </span>
              <span class="category-name" style="font-weight: 600; font-size: 1rem; color: #1e293b;">
                {{ root.name }}
              </span>
              <!-- F-05: Default tag for default categories -->
              <span *ngIf="root.isDefault" class="badge-default" style="background: #e0f2fe; color: #0369a1; font-size: 0.75rem; padding: 0.2rem 0.5rem; border-radius: 4px; font-weight: 500;">
                Mặc định
              </span>
            </div>

            <!-- F-05: Actions hidden for default categories unless admin -->
            <div class="node-actions" style="display: flex; gap: 0.5rem;">
              <button
                *ngIf="canModify(root)"
                (click)="openEditModal(root)"
                class="btn-icon"
                title="Sửa"
              >
                ✏️
              </button>
              <button
                *ngIf="canModify(root)"
                (click)="deleteCategory(root)"
                class="btn-icon text-danger"
                title="Xóa"
              >
                🗑️
              </button>
            </div>
          </div>

          <!-- Children Sub-categories (Level 2) -->
          <div *ngIf="root.children && root.children.length > 0" class="children-list" style="margin-left: 2.5rem; margin-top: 0.5rem; display: flex; flex-direction: column; gap: 0.35rem;">
            <div *ngFor="let child of root.children" class="child-node" style="display: flex; justify-content: space-between; align-items: center; padding: 0.4rem 0.6rem; border-radius: 4px; background: #ffffff; border: 1px solid #f1f5f9;">
              <div style="display: flex; align-items: center; gap: 0.5rem;">
                <span style="color: #94a3b8;">└─</span>
                <span class="category-icon-sm" [style.background]="child.color || '#64748b'" style="display: inline-flex; align-items: center; justify-content: center; width: 24px; height: 24px; border-radius: 4px; color: white; font-size: 0.8rem;">
                  {{ child.icon || '📄' }}
                </span>
                <span class="category-name" style="font-size: 0.9rem; color: #334155;">{{ child.name }}</span>
                <!-- F-05: Default tag -->
                <span *ngIf="child.isDefault" class="badge-default" style="background: #e0f2fe; color: #0369a1; font-size: 0.7rem; padding: 0.15rem 0.4rem; border-radius: 4px; font-weight: 500;">
                  Mặc định
                </span>
              </div>

              <!-- F-05: Actions hidden for default child unless admin -->
              <div class="node-actions" style="display: flex; gap: 0.5rem;">
                <button
                  *ngIf="canModify(child)"
                  (click)="openEditModal(child)"
                  class="btn-icon"
                  title="Sửa"
                >
                  ✏️
                </button>
                <button
                  *ngIf="canModify(child)"
                  (click)="deleteCategory(child)"
                  class="btn-icon text-danger"
                  title="Xóa"
                >
                  🗑️
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Create / Edit Modal -->
      <div *ngIf="showModal()" class="modal-backdrop" style="position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1000; padding: 1rem; box-sizing: border-box;">
        <div class="modal-content" style="background: white; border-radius: 8px; width: 100%; max-width: 440px; padding: 1.5rem; box-sizing: border-box;">
          <h3 style="margin: 0 0 1rem 0;">{{ editingCategory() ? 'Cập nhật danh mục' : 'Thêm danh mục mới' }}</h3>

          <form [formGroup]="categoryForm" (ngSubmit)="saveCategory()">
            <div class="form-group" style="margin-bottom: 1rem;">
              <label for="catName">Tên danh mục</label>
              <input id="catName" type="text" formControlName="name" class="form-control" placeholder="Tên danh mục..." />
            </div>

            <!-- F-05: Parent Selector ONLY allows Level 1 (root categories), preventing level 2 from being selected as parent -->
            <div *ngIf="!editingCategory()" class="form-group" style="margin-bottom: 1rem;">
              <label for="catParentId">Danh mục cha (Tùy chọn - chỉ chọn cấp 1)</label>
              <select id="catParentId" formControlName="parentId" class="form-control parent-selector">
                <option [value]="null">-- Không có (Danh mục gốc cấp 1) --</option>
                <!-- Only root categories (where parentId is null/undefined) are listed -->
                <option *ngFor="let root of rootCategoriesOnly()" [value]="root.id">
                  📁 {{ root.name }} (Cấp 1)
                </option>
              </select>
            </div>

            <div class="form-group" style="margin-bottom: 1rem;">
              <label for="catIcon">Icon (Emoji hoặc ký hiệu)</label>
              <input id="catIcon" type="text" formControlName="icon" class="form-control" placeholder="VD: 🍔, 🚗, 🏥..." />
            </div>

            <div class="form-group" style="margin-bottom: 1.5rem;">
              <label for="catColor">Màu sắc (#RRGGBB)</label>
              <input id="catColor" type="text" formControlName="color" class="form-control" placeholder="#3b82f6" />
            </div>

            <div style="display: flex; justify-content: flex-end; gap: 0.75rem;">
              <button type="button" (click)="closeModal()" class="btn btn-secondary">Hủy</button>
              <button type="submit" [disabled]="categoryForm.invalid" class="btn btn-primary">Lưu</button>
            </div>
          </form>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .category-tree-container {
      width: 100%;
      max-width: 100%;
      box-sizing: border-box;
      overflow-x: hidden;
    }
    .tab-btn {
      background: none;
      border: none;
      font-size: 0.95rem;
      font-weight: 600;
      color: #64748b;
      padding: 0.5rem 1rem;
      cursor: pointer;
      border-radius: 6px;
    }
    .tab-btn.active-tab {
      background: #eff6ff;
      color: #2563eb;
    }
    .btn {
      padding: 0.5rem 1rem;
      font-size: 0.9rem;
      font-weight: 600;
      border-radius: 6px;
      border: none;
      cursor: pointer;
    }
    .btn-primary { background: #2563eb; color: white; }
    .btn-secondary { background: #e2e8f0; color: #334155; }
    .btn-icon {
      background: none;
      border: none;
      cursor: pointer;
      font-size: 0.9rem;
      padding: 0.2rem 0.4rem;
    }
    .form-control {
      width: 100%;
      box-sizing: border-box;
      padding: 0.55rem 0.75rem;
      border: 1px solid #cbd5e1;
      border-radius: 6px;
      outline: none;
    }
    .form-control:focus { border-color: #3b82f6; }
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
  `]
})
export class CategoryTreeComponent implements OnInit {
  private fb = inject(FormBuilder);
  private categoryService = inject(CategoryService);
  private authService = inject(AuthService);

  filterType = signal<number>(1); // 1 = Expense, 2 = Income
  categories = signal<CategoryDto[]>([]);
  isLoading = signal<boolean>(false);
  errorMessage = signal<string | null>(null);

  showModal = signal<boolean>(false);
  editingCategory = signal<CategoryDto | null>(null);

  categoryForm: FormGroup = this.fb.group({
    name: ['', Validators.required],
    parentId: [null],
    icon: ['📁'],
    color: ['#3b82f6']
  });

  ngOnInit(): void {
    this.loadCategories();
  }

  loadCategories(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    this.categoryService.getCategories(this.filterType()).subscribe({
      next: (data) => {
        this.categories.set(data);
        this.isLoading.set(false);
      },
      error: () => {
        this.errorMessage.set('Không thể tải danh sách danh mục');
        this.isLoading.set(false);
      }
    });
  }

  // F-05: Non-admin users cannot modify/delete default categories
  canModify(category: CategoryDto): boolean {
    if (!category.isDefault) {
      return true;
    }
    return this.authService.getUserRole() === 'Admin';
  }

  // F-05: Filter parent choices: only Level 1 (root categories without parentId)
  rootCategoriesOnly(): CategoryDto[] {
    return this.categories().filter(c => !c.parentId);
  }

  openCreateModal(): void {
    this.editingCategory.set(null);
    this.categoryForm.reset({
      name: '',
      parentId: null,
      icon: '📁',
      color: '#3b82f6'
    });
    this.showModal.set(true);
  }

  openEditModal(cat: CategoryDto): void {
    this.editingCategory.set(cat);
    this.categoryForm.setValue({
      name: cat.name,
      parentId: cat.parentId || null,
      icon: cat.icon || '📁',
      color: cat.color || '#3b82f6'
    });
    this.showModal.set(true);
  }

  closeModal(): void {
    this.showModal.set(false);
    this.editingCategory.set(null);
  }

  saveCategory(): void {
    if (this.categoryForm.invalid) return;

    const val = this.categoryForm.value;
    const editing = this.editingCategory();

    if (editing) {
      this.categoryService.updateCategory(editing.id, {
        name: val.name,
        parentId: val.parentId,
        icon: val.icon,
        color: val.color
      }).subscribe({
        next: () => {
          this.closeModal();
          this.loadCategories();
        },
        error: (err) => alert(err.error?.message || 'Không thể cập nhật danh mục')
      });
    } else {
      this.categoryService.createCategory({
        name: val.name,
        type: this.filterType(),
        parentId: val.parentId,
        icon: val.icon,
        color: val.color
      }).subscribe({
        next: () => {
          this.closeModal();
          this.loadCategories();
        },
        error: (err) => alert(err.error?.message || 'Không thể tạo danh mục')
      });
    }
  }

  deleteCategory(cat: CategoryDto): void {
    if (confirm(`Bạn có chắc muốn xóa danh mục "${cat.name}"?`)) {
      this.categoryService.deleteCategory(cat.id).subscribe({
        next: () => this.loadCategories(),
        error: (err) => alert(err.error?.message || 'Không thể xóa danh mục')
      });
    }
  }
}

