import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { TransactionService } from '../../../core/services/transaction.service';
import { WalletService } from '../../../core/services/wallet.service';
import { CategoryService } from '../../../core/services/category.service';
import { WalletDto } from '../../../core/models/wallet.models';
import { CategoryDto } from '../../../core/models/category.models';
import { CreateTransactionRequest, UpdateTransactionRequest, TagDto } from '../../../core/models/transaction.models';

@Component({
  selector: 'app-transaction-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  template: `
    <div class="transaction-form-container" style="width: 100%; max-width: 100%; box-sizing: border-box; overflow-x: hidden; padding: 1rem;">
      <div class="card" style="width: 100%; max-width: 540px; margin: 0 auto; background: white; border-radius: 8px; padding: 1.5rem; box-shadow: 0 2px 8px rgba(0,0,0,0.06); box-sizing: border-box;">
        <h2 style="margin-top: 0; color: #1e293b;">{{ isEditMode() ? 'Sửa giao dịch' : 'Ghi nhanh giao dịch' }}</h2>
        <p style="color: #64748b; font-size: 0.9rem; margin-top: -0.5rem; margin-bottom: 1.5rem;">
          {{ isEditMode() ? 'Cập nhật thông tin giao dịch tài chính' : 'Ghi chép khoản thu hoặc chi tiêu vào ví' }}
        </p>

        <div *ngIf="errorMessage()" class="alert alert-danger" style="background: #fef2f2; color: #b91c1c; padding: 0.75rem 1rem; border-radius: 6px; margin-bottom: 1rem;">
          {{ errorMessage() }}
        </div>

        <form [formGroup]="form" (ngSubmit)="onSubmit()">
          <!-- Loại giao dịch (1: Chi tiêu, 2: Thu nhập) -->
          <div class="form-group" style="margin-bottom: 1.25rem;">
            <label style="display: block; font-weight: 500; margin-bottom: 0.5rem; color: #334155;">Loại giao dịch</label>
            <div style="display: flex; gap: 1rem;">
              <label style="display: flex; align-items: center; gap: 0.5rem; cursor: pointer;">
                <input type="radio" [value]="1" formControlName="type" (change)="onTypeChange()" />
                <span style="color: #dc2626; font-weight: 600;">💸 Chi tiêu</span>
              </label>
              <label style="display: flex; align-items: center; gap: 0.5rem; cursor: pointer;">
                <input type="radio" [value]="2" formControlName="type" (change)="onTypeChange()" />
                <span style="color: #16a34a; font-weight: 600;">💰 Thu nhập</span>
              </label>
            </div>
          </div>

          <!-- Số tiền -->
          <div class="form-group" style="margin-bottom: 1.25rem;">
            <label for="amount" style="display: block; font-weight: 500; margin-bottom: 0.5rem; color: #334155;">Số tiền (₫)</label>
            <input
              id="amount"
              type="number"
              formControlName="amount"
              class="form-control"
              placeholder="VD: 50000"
              style="width: 100%; padding: 0.6rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 1rem; box-sizing: border-box;"
            />
            <div *ngIf="f['amount'].touched && f['amount'].invalid" style="color: #dc2626; font-size: 0.85rem; margin-top: 0.25rem;">
              Số tiền phải lớn hơn 0 và tối đa 1.000 tỷ
            </div>
          </div>

          <!-- Ví thanh toán -->
          <div class="form-group" style="margin-bottom: 1.25rem;">
            <label for="walletId" style="display: block; font-weight: 500; margin-bottom: 0.5rem; color: #334155;">Ví</label>
            <select
              id="walletId"
              formControlName="walletId"
              class="form-control"
              style="width: 100%; padding: 0.6rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 1rem; box-sizing: border-box;"
            >
              <option value="" disabled>-- Chọn ví --</option>
              <option *ngFor="let w of wallets()" [value]="w.id">
                {{ w.name }} ({{ w.balance | number }} {{ w.currency }})
              </option>
            </select>
            <div *ngIf="f['walletId'].touched && f['walletId'].invalid" style="color: #dc2626; font-size: 0.85rem; margin-top: 0.25rem;">
              Vui lòng chọn ví
            </div>
          </div>

          <!-- Danh mục -->
          <div class="form-group" style="margin-bottom: 1.25rem;">
            <label for="categoryId" style="display: block; font-weight: 500; margin-bottom: 0.5rem; color: #334155;">Danh mục</label>
            <select
              id="categoryId"
              formControlName="categoryId"
              class="form-control"
              style="width: 100%; padding: 0.6rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 1rem; box-sizing: border-box;"
            >
              <option [value]="null">-- Không phân loại --</option>
              <option *ngFor="let c of filteredCategories()" [value]="c.id">
                {{ c.icon || '📁' }} {{ c.name }}
              </option>
            </select>
          </div>

          <!-- Ngày giao dịch -->
          <div class="form-group" style="margin-bottom: 1.25rem;">
            <label for="transactionDate" style="display: block; font-weight: 500; margin-bottom: 0.5rem; color: #334155;">Ngày giao dịch</label>
            <input
              id="transactionDate"
              type="datetime-local"
              formControlName="transactionDate"
              class="form-control"
              style="width: 100%; padding: 0.6rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 1rem; box-sizing: border-box;"
            />
          </div>

          <!-- Ghi chú -->
          <div class="form-group" style="margin-bottom: 1.25rem;">
            <label for="note" style="display: block; font-weight: 500; margin-bottom: 0.5rem; color: #334155;">Ghi chú</label>
            <textarea
              id="note"
              formControlName="note"
              rows="3"
              class="form-control"
              placeholder="VD: Ăn trưa bún bò Huế với đồng nghiệp"
              style="width: 100%; padding: 0.6rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 1rem; box-sizing: border-box;"
            ></textarea>
          </div>

          <!-- Hóa đơn đính kèm (F-04) -->
          <div class="form-group receipt-dropzone-group" style="margin-bottom: 1.25rem;">
            <label style="display: block; font-weight: 500; margin-bottom: 0.5rem; color: #334155;">Ảnh hóa đơn (≤ 5MB)</label>
            <div
              class="dropzone"
              (dragover)="onDragOver($event)"
              (dragleave)="onDragLeave($event)"
              (drop)="onFileDrop($event)"
              (click)="fileInput.click()"
              style="border: 2px dashed #94a3b8; border-radius: 8px; padding: 1.25rem; text-align: center; cursor: pointer; background: #f8fafc; transition: all 0.2s;"
              [style.borderColor]="isDragging() ? '#2563eb' : '#94a3b8'"
              [style.background]="isDragging() ? '#eff6ff' : '#f8fafc'"
            >
              <input
                #fileInput
                type="file"
                (change)="onFileInputChange($event)"
                accept="image/png,image/jpeg,image/webp"
                style="display: none;"
              />
              <div *ngIf="!receiptPreviewUrl() && !isUploadingReceipt()">
                <div style="font-size: 1.75rem; margin-bottom: 0.25rem;">🧾</div>
                <div style="color: #475569; font-weight: 500;">Kéo thả ảnh hóa đơn vào đây hoặc click để chọn</div>
                <div style="color: #94a3b8; font-size: 0.8rem; margin-top: 0.25rem;">Chấp nhận PNG, JPEG, WEBP (≤ 5MB)</div>
              </div>
              <div *ngIf="isUploadingReceipt()" style="color: #2563eb;">
                <span>Đang tải ảnh lên...</span>
              </div>
              <div *ngIf="receiptPreviewUrl() && !isUploadingReceipt()" style="position: relative; display: inline-block;">
                <img [src]="receiptPreviewUrl()" alt="Xem trước hóa đơn" style="max-height: 120px; border-radius: 4px; box-shadow: 0 1px 4px rgba(0,0,0,0.1);" />
                <button type="button" (click)="removeReceipt($event)" style="margin-top: 0.5rem; display: block; margin-left: auto; margin-right: auto; background: #ef4444; color: white; border: none; padding: 0.2rem 0.6rem; border-radius: 4px; font-size: 0.8rem; cursor: pointer;">Xóa ảnh</button>
              </div>
            </div>
            <div *ngIf="receiptError()" class="invalid-feedback d-block" style="color: #dc2626; font-size: 0.85rem; margin-top: 0.25rem;" data-testid="receipt-error">
              {{ receiptError() }}
            </div>
          </div>

          <!-- Thẻ / Tags (F-05) -->
          <div class="form-group tag-selector-group" style="margin-bottom: 1.5rem;">
            <label style="display: block; font-weight: 500; margin-bottom: 0.5rem; color: #334155;">Thẻ (tối đa 10 thẻ)</label>
            <div style="display: flex; flex-wrap: wrap; gap: 0.5rem; margin-bottom: 0.5rem;">
              <button
                type="button"
                *ngFor="let t of availableTags()"
                (click)="toggleTag(t.id)"
                class="tag-badge"
                [style.background]="isTagSelected(t.id) ? '#2563eb' : '#f1f5f9'"
                [style.color]="isTagSelected(t.id) ? '#ffffff' : '#334155'"
                style="padding: 0.3rem 0.75rem; border-radius: 9999px; border: 1px solid #cbd5e1; font-size: 0.85rem; cursor: pointer; transition: all 0.15s;"
              >
                {{ t.name }} {{ isTagSelected(t.id) ? '✓' : '+' }}
              </button>
            </div>
            <!-- Inline new tag input -->
            <div style="display: flex; gap: 0.5rem; max-width: 320px;">
              <input
                type="text"
                [value]="newTagName()"
                (input)="onNewTagInput($event)"
                placeholder="Tên thẻ mới..."
                maxlength="50"
                class="form-control"
                style="flex: 1; padding: 0.4rem 0.6rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 0.85rem;"
              />
              <button
                type="button"
                (click)="addInlineTag()"
                [disabled]="!newTagName().trim() || selectedTagIds().length >= 10"
                style="padding: 0.4rem 0.8rem; background: #059669; color: white; border: none; border-radius: 6px; font-size: 0.85rem; cursor: pointer;"
              >
                + Thêm
              </button>
            </div>
            <div *ngIf="tagError()" style="color: #dc2626; font-size: 0.85rem; margin-top: 0.25rem;" data-testid="tag-error">
              {{ tagError() }}
            </div>
          </div>

          <!-- Buttons -->
          <div style="display: flex; gap: 0.75rem; justify-content: flex-end; flex-wrap: wrap;">
            <a routerLink="/transactions" style="padding: 0.6rem 1.25rem; border: 1px solid #cbd5e1; border-radius: 6px; text-decoration: none; color: #475569; font-weight: 500;">
              Hủy
            </a>
            <button
              type="submit"
              [disabled]="form.invalid || isSubmitting()"
              style="padding: 0.6rem 1.5rem; background: #2563eb; color: white; border: none; border-radius: 6px; font-weight: 600; cursor: pointer;"
            >
              {{ isSubmitting() ? 'Đang lưu...' : (isEditMode() ? 'Cập nhật' : 'Thêm giao dịch') }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class TransactionFormComponent implements OnInit {
  private fb = inject(FormBuilder);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private transactionService = inject(TransactionService);
  private walletService = inject(WalletService);
  private categoryService = inject(CategoryService);

  isEditMode = signal(false);
  transactionId = signal<string | null>(null);
  currentRowVersion = signal<string | null>(null);
  isSubmitting = signal(false);
  errorMessage = signal<string | null>(null);

  wallets = signal<WalletDto[]>([]);
  allCategories = signal<CategoryDto[]>([]);
  filteredCategories = signal<CategoryDto[]>([]);

  // Receipt upload signals (F-04)
  isDragging = signal(false);
  isUploadingReceipt = signal(false);
  receiptFileId = signal<string | null>(null);
  receiptPreviewUrl = signal<string | null>(null);
  receiptError = signal<string | null>(null);

  // Tag signals (F-05)
  availableTags = signal<TagDto[]>([]);
  selectedTagIds = signal<string[]>([]);
  newTagName = signal<string>('');
  tagError = signal<string | null>(null);

  form: FormGroup = this.fb.group({
    walletId: ['', [Validators.required]],
    categoryId: [null],
    type: [1, [Validators.required]],
    amount: [null, [Validators.required, Validators.min(0.01), Validators.max(1_000_000_000_000)]],
    transactionDate: [this.formatDateForInput(new Date()), [Validators.required]],
    note: ['', [Validators.maxLength(1000)]]
  });

  get f() {
    return this.form.controls;
  }

  ngOnInit(): void {
    this.loadWallets();
    this.loadCategories();
    this.loadTags();

    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode.set(true);
      this.transactionId.set(id);
      this.loadTransaction(id);
    }
  }

  loadWallets(): void {
    this.walletService.getWallets().subscribe({
      next: (list) => {
        this.wallets.set(list);
        if (!this.isEditMode() && list.length > 0 && !this.form.value.walletId) {
          const defaultWallet = list.find(w => w.isDefault) || list[0];
          this.form.patchValue({ walletId: defaultWallet.id });
        }
      },
      error: () => this.errorMessage.set('Không thể tải danh sách ví')
    });
  }

  loadCategories(): void {
    this.categoryService.getCategories().subscribe({
      next: (tree) => {
        const flat: CategoryDto[] = [];
        const flatten = (cats: CategoryDto[]) => {
          for (const c of cats) {
            flat.push(c);
            if (c.children?.length) {
              flatten(c.children);
            }
          }
        };
        flatten(tree);
        this.allCategories.set(flat);
        this.filterCategoriesByType();
      },
      error: () => this.errorMessage.set('Không thể tải danh sách danh mục')
    });
  }

  loadTags(): void {
    this.transactionService.getTags().subscribe({
      next: (tags) => this.availableTags.set(tags),
      error: () => { /* optional load tags */ }
    });
  }

  onTypeChange(): void {
    this.filterCategoriesByType();
    const currentCatId = this.form.value.categoryId;
    if (currentCatId) {
      const cat = this.allCategories().find(c => c.id === currentCatId);
      if (cat && cat.type !== this.form.value.type) {
        this.form.patchValue({ categoryId: null });
      }
    }
  }

  filterCategoriesByType(): void {
    const type = this.form.value.type;
    const filtered = this.allCategories().filter(c => c.type === type);
    this.filteredCategories.set(filtered);
  }

  loadTransaction(id: string): void {
    this.transactionService.getTransaction(id).subscribe({
      next: (tx) => {
        this.currentRowVersion.set(tx.rowVersion);
        this.form.patchValue({
          walletId: tx.walletId,
          categoryId: tx.categoryId,
          type: tx.type,
          amount: tx.amount,
          transactionDate: this.formatDateForInput(new Date(tx.transactionDate)),
          note: tx.note
        });
        if (tx.receiptFileId) {
          this.receiptFileId.set(tx.receiptFileId);
          this.receiptPreviewUrl.set(`/api/files/${tx.receiptFileId}`);
        }
        if (tx.tagIds?.length) {
          this.selectedTagIds.set(tx.tagIds);
        }
        this.filterCategoriesByType();
      },
      error: () => this.errorMessage.set('Không thể tải chi tiết giao dịch')
    });
  }

  // Drag and drop handlers (F-04)
  onDragOver(e: DragEvent): void {
    e.preventDefault();
    e.stopPropagation();
    this.isDragging.set(true);
  }

  onDragLeave(e: DragEvent): void {
    e.preventDefault();
    e.stopPropagation();
    this.isDragging.set(false);
  }

  onFileDrop(e: DragEvent): void {
    e.preventDefault();
    e.stopPropagation();
    this.isDragging.set(false);
    if (e.dataTransfer?.files?.length) {
      this.handleReceiptFile(e.dataTransfer.files[0]);
    }
  }

  onFileInputChange(e: Event): void {
    const input = e.target as HTMLInputElement;
    if (input.files?.length) {
      this.handleReceiptFile(input.files[0]);
    }
  }

  handleReceiptFile(file: File): void {
    const maxBytes = 5 * 1024 * 1024; // 5 MB
    if (file.size > maxBytes) {
      this.receiptError.set('Dung lượng file vượt quá giới hạn 5MB.');
      this.receiptFileId.set(null);
      this.receiptPreviewUrl.set(null);
      return;
    }

    this.receiptError.set(null);
    const reader = new FileReader();
    reader.onload = () => {
      this.receiptPreviewUrl.set(reader.result as string);
    };
    reader.readAsDataURL(file);

    this.isUploadingReceipt.set(true);
    this.transactionService.uploadReceipt(file).subscribe({
      next: (res) => {
        this.receiptFileId.set(res.id);
        this.isUploadingReceipt.set(false);
      },
      error: (err) => {
        this.receiptError.set(err.error?.message || 'Không thể tải ảnh hóa đơn lên');
        this.isUploadingReceipt.set(false);
      }
    });
  }

  removeReceipt(e: Event): void {
    e.stopPropagation();
    this.receiptFileId.set(null);
    this.receiptPreviewUrl.set(null);
    this.receiptError.set(null);
  }

  // Tag handlers (F-05)
  isTagSelected(tagId: string): boolean {
    return this.selectedTagIds().includes(tagId);
  }

  toggleTag(tagId: string): void {
    this.tagError.set(null);
    const current = this.selectedTagIds();
    if (current.includes(tagId)) {
      this.selectedTagIds.set(current.filter(id => id !== tagId));
    } else {
      if (current.length >= 10) {
        this.tagError.set('Tối đa 10 thẻ cho một giao dịch');
        return;
      }
      this.selectedTagIds.set([...current, tagId]);
    }
  }

  onNewTagInput(e: Event): void {
    const val = (e.target as HTMLInputElement).value;
    this.newTagName.set(val);
  }

  addInlineTag(): void {
    const name = this.newTagName().trim();
    if (!name) return;

    if (this.selectedTagIds().length >= 10) {
      this.tagError.set('Tối đa 10 thẻ cho một giao dịch');
      return;
    }

    this.transactionService.createTag(name).subscribe({
      next: (tag) => {
        this.availableTags.set([...this.availableTags(), tag]);
        this.selectedTagIds.set([...this.selectedTagIds(), tag.id]);
        this.newTagName.set('');
        this.tagError.set(null);
      },
      error: (err) => {
        this.tagError.set(err.error?.message || 'Không thể tạo thẻ mới');
      }
    });
  }

  onSubmit(): void {
    if (this.form.invalid) return;

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const val = this.form.value;
    const catId = val.categoryId && val.categoryId !== '' ? val.categoryId : null;

    if (this.isEditMode() && this.transactionId()) {
      const updateReq: UpdateTransactionRequest = {
        walletId: val.walletId,
        categoryId: catId,
        type: val.type,
        amount: Number(val.amount),
        transactionDate: new Date(val.transactionDate).toISOString(),
        note: val.note?.trim() || null,
        receiptFileId: this.receiptFileId(),
        tagIds: this.selectedTagIds(),
        rowVersion: this.currentRowVersion()
      };

      this.transactionService.updateTransaction(this.transactionId()!, updateReq).subscribe({
        next: () => {
          this.isSubmitting.set(false);
          this.router.navigate(['/transactions']);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          if (err.status === 409) {
            this.errorMessage.set('Giao dịch đã bị thay đổi bởi phiên làm việc khác, vui lòng tải lại trang.');
          } else {
            this.errorMessage.set(err.error?.message || 'Lỗi khi cập nhật giao dịch');
          }
        }
      });
    } else {
      const createReq: CreateTransactionRequest = {
        walletId: val.walletId,
        categoryId: catId,
        type: val.type,
        amount: Number(val.amount),
        transactionDate: new Date(val.transactionDate).toISOString(),
        note: val.note?.trim() || null,
        receiptFileId: this.receiptFileId(),
        tagIds: this.selectedTagIds()
      };

      this.transactionService.createTransaction(createReq).subscribe({
        next: () => {
          this.isSubmitting.set(false);
          this.router.navigate(['/transactions']);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err.error?.message || 'Lỗi khi tạo giao dịch');
        }
      });
    }
  }

  private formatDateForInput(date: Date): string {
    const tzOffset = date.getTimezoneOffset() * 60000;
    const localISOTime = (new Date(date.getTime() - tzOffset)).toISOString().slice(0, 16);
    return localISOTime;
  }
}
