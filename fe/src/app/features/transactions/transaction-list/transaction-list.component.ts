import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { TransactionService } from '../../../core/services/transaction.service';
import { WalletService } from '../../../core/services/wallet.service';
import { CategoryService } from '../../../core/services/category.service';
import { TransactionDto, TransactionQueryParameters } from '../../../core/models/transaction.models';
import { WalletDto } from '../../../core/models/wallet.models';
import { CategoryDto } from '../../../core/models/category.models';

@Component({
  selector: 'app-transaction-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
    <div class="transaction-list-container" style="max-width: 100%; box-sizing: border-box; padding: 1rem;">
      <!-- Header -->
      <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; flex-wrap: wrap; gap: 1rem;">
        <div>
          <h2 style="margin: 0; font-size: 1.5rem; color: #1e293b;">Lịch sử giao dịch</h2>
          <p style="margin: 0.25rem 0 0 0; color: #64748b; font-size: 0.9rem;">Tra cứu, lọc và quản lý các khoản thu chi</p>
        </div>
        <div style="display: flex; gap: 0.6rem; flex-wrap: wrap;">
          <a routerLink="/transactions/import" style="padding: 0.6rem 1rem; background: #2563eb; color: white; border-radius: 6px; text-decoration: none; font-weight: 600; display: inline-flex; align-items: center; gap: 0.4rem;">
            <span>📥</span> Nhập CSV/Excel
          </a>
          <a routerLink="/transactions/create" style="padding: 0.6rem 1.25rem; background: #16a34a; color: white; border-radius: 6px; text-decoration: none; font-weight: 600; display: inline-flex; align-items: center; gap: 0.4rem;">
            <span>+</span> Ghi giao dịch
          </a>
        </div>
      </div>

      <!-- Filter Panel (FIN-43 / FIN-45) -->
      <div class="filter-card" style="background: white; border-radius: 8px; padding: 1.25rem; margin-bottom: 1.5rem; box-shadow: 0 1px 4px rgba(0,0,0,0.05); border: 1px solid #e2e8f0;">
        <div style="font-weight: 600; color: #334155; margin-bottom: 0.75rem; display: flex; align-items: center; gap: 0.5rem;">
          <span>🔍</span> Bộ lọc tìm kiếm nâng cao
        </div>

        <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 1rem; margin-bottom: 1rem;">
          <!-- Keyword -->
          <div>
            <label style="display: block; font-size: 0.85rem; color: #64748b; margin-bottom: 0.25rem;">Từ khóa</label>
            <input
              type="text"
              [(ngModel)]="filterParams.keyword"
              placeholder="Tìm theo ghi chú..."
              style="width: 100%; padding: 0.45rem 0.6rem; border: 1px solid #cbd5e1; border-radius: 4px; box-sizing: border-box; font-size: 0.9rem;"
            />
          </div>

          <!-- Type -->
          <div>
            <label style="display: block; font-size: 0.85rem; color: #64748b; margin-bottom: 0.25rem;">Loại giao dịch</label>
            <select
              [(ngModel)]="filterParams.type"
              style="width: 100%; padding: 0.45rem 0.6rem; border: 1px solid #cbd5e1; border-radius: 4px; box-sizing: border-box; font-size: 0.9rem;"
            >
              <option [ngValue]="undefined">Tất cả loại</option>
              <option [ngValue]="1">💸 Chi tiêu</option>
              <option [ngValue]="2">💰 Thu nhập</option>
              <option [ngValue]="3">🔄 Chuyển tiền</option>
            </select>
          </div>

          <!-- Wallet -->
          <div>
            <label style="display: block; font-size: 0.85rem; color: #64748b; margin-bottom: 0.25rem;">Ví</label>
            <select
              [(ngModel)]="filterParams.walletId"
              style="width: 100%; padding: 0.45rem 0.6rem; border: 1px solid #cbd5e1; border-radius: 4px; box-sizing: border-box; font-size: 0.9rem;"
            >
              <option [ngValue]="undefined">Tất cả ví</option>
              <option *ngFor="let w of wallets()" [value]="w.id">{{ w.name }}</option>
            </select>
          </div>

          <!-- Category -->
          <div>
            <label style="display: block; font-size: 0.85rem; color: #64748b; margin-bottom: 0.25rem;">Danh mục</label>
            <select
              [(ngModel)]="filterParams.categoryId"
              style="width: 100%; padding: 0.45rem 0.6rem; border: 1px solid #cbd5e1; border-radius: 4px; box-sizing: border-box; font-size: 0.9rem;"
            >
              <option [ngValue]="undefined">Tất cả danh mục</option>
              <option *ngFor="let c of categories()" [value]="c.id">{{ c.icon || '📁' }} {{ c.name }}</option>
            </select>
          </div>

          <!-- Start Date -->
          <div>
            <label style="display: block; font-size: 0.85rem; color: #64748b; margin-bottom: 0.25rem;">Từ ngày</label>
            <input
              type="date"
              [(ngModel)]="filterParams.startDate"
              style="width: 100%; padding: 0.45rem 0.6rem; border: 1px solid #cbd5e1; border-radius: 4px; box-sizing: border-box; font-size: 0.9rem;"
            />
          </div>

          <!-- End Date -->
          <div>
            <label style="display: block; font-size: 0.85rem; color: #64748b; margin-bottom: 0.25rem;">Đến ngày</label>
            <input
              type="date"
              [(ngModel)]="filterParams.endDate"
              style="width: 100%; padding: 0.45rem 0.6rem; border: 1px solid #cbd5e1; border-radius: 4px; box-sizing: border-box; font-size: 0.9rem;"
            />
          </div>

          <!-- Min Amount -->
          <div>
            <label style="display: block; font-size: 0.85rem; color: #64748b; margin-bottom: 0.25rem;">Số tiền tối thiểu</label>
            <input
              type="number"
              [(ngModel)]="filterParams.minAmount"
              placeholder="0"
              style="width: 100%; padding: 0.45rem 0.6rem; border: 1px solid #cbd5e1; border-radius: 4px; box-sizing: border-box; font-size: 0.9rem;"
            />
          </div>

          <!-- Max Amount -->
          <div>
            <label style="display: block; font-size: 0.85rem; color: #64748b; margin-bottom: 0.25rem;">Số tiền tối đa</label>
            <input
              type="number"
              [(ngModel)]="filterParams.maxAmount"
              placeholder="100000000"
              style="width: 100%; padding: 0.45rem 0.6rem; border: 1px solid #cbd5e1; border-radius: 4px; box-sizing: border-box; font-size: 0.9rem;"
            />
          </div>
        </div>

        <div style="display: flex; justify-content: flex-end; gap: 0.75rem;">
          <button (click)="resetFilters()" style="padding: 0.45rem 1rem; border: 1px solid #cbd5e1; background: white; border-radius: 4px; color: #475569; font-size: 0.85rem; cursor: pointer;">
            Đặt lại
          </button>
          <button (click)="applyFilters()" style="padding: 0.45rem 1.25rem; background: #2563eb; color: white; border: none; border-radius: 4px; font-weight: 500; font-size: 0.85rem; cursor: pointer;">
            Áp dụng lọc
          </button>
        </div>
      </div>

      <!-- Loading / Error -->
      <div *ngIf="isLoading()" style="text-align: center; padding: 2rem; color: #64748b;">
        Đang tải danh sách giao dịch...
      </div>

      <div *ngIf="errorMessage()" class="alert alert-danger" style="background: #fef2f2; color: #b91c1c; padding: 0.75rem 1rem; border-radius: 6px; margin-bottom: 1rem;">
        {{ errorMessage() }}
      </div>

      <!-- Empty State -->
      <div *ngIf="!isLoading() && transactions().length === 0" class="empty-state" style="text-align: center; padding: 3rem 1rem; background: white; border-radius: 8px; border: 1px dashed #cbd5e1;">
        <div style="font-size: 3rem; margin-bottom: 0.5rem;">💸</div>
        <h3 style="margin: 0 0 0.5rem 0; color: #334155;">Không tìm thấy giao dịch nào</h3>
        <p style="color: #64748b; font-size: 0.9rem; margin-bottom: 1.5rem;">Hãy thử nới lỏng bộ lọc hoặc ghi thêm giao dịch mới.</p>
        <a routerLink="/transactions/create" style="padding: 0.5rem 1rem; background: #2563eb; color: white; border-radius: 6px; text-decoration: none; font-size: 0.9rem;">
          + Ghi giao dịch ngay
        </a>
      </div>

      <!-- Transactions List Table -->
      <div *ngIf="!isLoading() && transactions().length > 0" style="background: white; border-radius: 8px; box-shadow: 0 1px 4px rgba(0,0,0,0.05); overflow-x: auto; border: 1px solid #e2e8f0;">
        <table style="width: 100%; border-collapse: collapse; text-align: left; font-size: 0.9rem;">
          <thead>
            <tr style="background: #f8fafc; border-bottom: 1px solid #e2e8f0; color: #64748b;">
              <th style="padding: 0.75rem 1rem;">Ngày</th>
              <th style="padding: 0.75rem 1rem;">Ví</th>
              <th style="padding: 0.75rem 1rem;">Danh mục</th>
              <th style="padding: 0.75rem 1rem;">Ghi chú</th>
              <th style="padding: 0.75rem 1rem; text-align: right;">Số tiền</th>
              <th style="padding: 0.75rem 1rem; text-align: center;">Hành động</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let tx of transactions()" style="border-bottom: 1px solid #f1f5f9;">
              <td style="padding: 0.75rem 1rem; white-space: nowrap; color: #475569;">
                {{ tx.transactionDate | date:'dd/MM/yyyy HH:mm' }}
              </td>
              <td style="padding: 0.75rem 1rem; color: #1e293b; font-weight: 500;">
                {{ getWalletName(tx.walletId) }}
              </td>
              <td style="padding: 0.75rem 1rem;">
                <span style="display: inline-flex; align-items: center; gap: 0.35rem; background: #f1f5f9; padding: 0.2rem 0.5rem; border-radius: 4px; font-size: 0.85rem; color: #334155;">
                  {{ getCategoryDisplay(tx.categoryId) }}
                </span>
              </td>
              <td style="padding: 0.75rem 1rem; color: #334155; max-width: 250px;">
                {{ tx.note || '—' }}
              </td>
              <td style="padding: 0.75rem 1rem; text-align: right; font-weight: 700; white-space: nowrap;">
                <span *ngIf="tx.type === 1" style="color: #dc2626;">-{{ tx.amount | number }} ₫</span>
                <span *ngIf="tx.type === 2" style="color: #16a34a;">+{{ tx.amount | number }} ₫</span>
                <span *ngIf="tx.type === 3" style="color: #2563eb;">🔄 {{ tx.amount | number }} ₫</span>
              </td>
              <td style="padding: 0.75rem 1rem; text-align: center; white-space: nowrap;">
                <ng-container *ngIf="tx.type !== 3 && tx.sourceType !== 'Debt' && tx.sourceType !== 'Goal'">
                  <a [routerLink]="['/transactions/edit', tx.id]" style="color: #2563eb; text-decoration: none; margin-right: 0.75rem; font-weight: 500;">Sửa</a>
                  <button (click)="onDelete(tx)" style="background: none; border: none; color: #dc2626; cursor: pointer; font-weight: 500;">Xóa</button>
                </ng-container>
                <ng-container *ngIf="tx.sourceType === 'Debt'">
                  <span style="background: #fef3c7; color: #b45309; padding: 0.15rem 0.4rem; border-radius: 4px; font-size: 0.8rem; font-weight: 500;">Sổ nợ</span>
                </ng-container>
                <ng-container *ngIf="tx.sourceType === 'Goal'">
                  <span style="background: #e0e7ff; color: #4338ca; padding: 0.15rem 0.4rem; border-radius: 4px; font-size: 0.8rem; font-weight: 500;">Mục tiêu</span>
                </ng-container>
                <ng-container *ngIf="tx.type === 3 && tx.sourceType !== 'Goal'">
                  <span style="color: #94a3b8; font-size: 0.8rem; font-style: italic;">Chuyển tiền</span>
                </ng-container>
              </td>
            </tr>
          </tbody>
        </table>

        <!-- Pagination Controls (FIN-44) -->
        <div style="display: flex; justify-content: space-between; align-items: center; padding: 0.75rem 1rem; background: #f8fafc; border-top: 1px solid #e2e8f0; flex-wrap: wrap; gap: 0.5rem;">
          <div style="color: #64748b; font-size: 0.85rem;">
            Trang {{ page() }} / {{ totalPages() }} (Tổng {{ totalItems() }} giao dịch)
          </div>
          <div style="display: flex; gap: 0.5rem; align-items: center;">
            <select [ngModel]="pageSize()" (ngModelChange)="onPageSizeChange($event)" style="padding: 0.3rem 0.5rem; border: 1px solid #cbd5e1; border-radius: 4px; font-size: 0.85rem;">
              <option [ngValue]="10">10 / trang</option>
              <option [ngValue]="20">20 / trang</option>
              <option [ngValue]="50">50 / trang</option>
            </select>
            <button [disabled]="page() <= 1" (click)="goToPage(page() - 1)" style="padding: 0.3rem 0.75rem; border: 1px solid #cbd5e1; background: white; border-radius: 4px; font-size: 0.85rem; cursor: pointer;">
              Trước
            </button>
            <button [disabled]="page() >= totalPages()" (click)="goToPage(page() + 1)" style="padding: 0.3rem 0.75rem; border: 1px solid #cbd5e1; background: white; border-radius: 4px; font-size: 0.85rem; cursor: pointer;">
              Sau
            </button>
          </div>
        </div>
      </div>
    </div>
  `
})
export class TransactionListComponent implements OnInit {
  private transactionService = inject(TransactionService);
  private walletService = inject(WalletService);
  private categoryService = inject(CategoryService);

  transactions = signal<TransactionDto[]>([]);
  wallets = signal<WalletDto[]>([]);
  categories = signal<CategoryDto[]>([]);

  isLoading = signal(false);
  errorMessage = signal<string | null>(null);

  page = signal(1);
  pageSize = signal(20);
  totalItems = signal(0);
  totalPages = signal(0);

  filterParams: TransactionQueryParameters = {
    page: 1,
    pageSize: 20
  };

  ngOnInit(): void {
    this.loadReferenceData();
    this.loadTransactions();
  }

  loadReferenceData(): void {
    this.walletService.getWallets(true).subscribe({
      next: (w) => this.wallets.set(w),
      error: () => {}
    });

    this.categoryService.getCategories().subscribe({
      next: (tree) => {
        const flat: CategoryDto[] = [];
        const flatten = (cats: CategoryDto[]) => {
          for (const c of cats) {
            flat.push(c);
            if (c.children?.length) flatten(c.children);
          }
        };
        flatten(tree);
        this.categories.set(flat);
      },
      error: () => {}
    });
  }

  loadTransactions(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    const query: TransactionQueryParameters = {
      ...this.filterParams,
      page: this.page(),
      pageSize: this.pageSize()
    };

    this.transactionService.getTransactions(query).subscribe({
      next: (result) => {
        this.transactions.set(result.items);
        this.totalItems.set(result.totalItems);
        this.totalPages.set(result.totalPages);
        this.page.set(result.page);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Lỗi khi tải danh sách giao dịch');
        this.isLoading.set(false);
      }
    });
  }

  applyFilters(): void {
    this.page.set(1);
    this.loadTransactions();
  }

  resetFilters(): void {
    this.filterParams = {
      page: 1,
      pageSize: this.pageSize()
    };
    this.page.set(1);
    this.loadTransactions();
  }

  goToPage(p: number): void {
    if (p < 1 || p > this.totalPages()) return;
    this.page.set(p);
    this.loadTransactions();
  }

  onPageSizeChange(newSize: number): void {
    this.pageSize.set(newSize);
    this.page.set(1);
    this.loadTransactions();
  }

  onDelete(tx: TransactionDto): void {
    if (!confirm(`Bạn có chắc chắn muốn xóa giao dịch "${tx.note || 'không ghi chú'}" với số tiền ${tx.amount.toLocaleString()} ₫ không?`)) {
      return;
    }

    this.transactionService.deleteTransaction(tx.id).subscribe({
      next: () => {
        this.loadTransactions();
      },
      error: (err) => {
        alert(err.error?.message || 'Lỗi khi xóa giao dịch');
      }
    });
  }

  getWalletName(walletId: string): string {
    const w = this.wallets().find(item => item.id === walletId);
    return w ? w.name : 'Ví đã xóa';
  }

  getCategoryDisplay(catId?: string | null): string {
    if (!catId) return 'Không phân loại';
    const c = this.categories().find(item => item.id === catId);
    return c ? `${c.icon || '📁'} ${c.name}` : 'Danh mục đã xóa';
  }
}

