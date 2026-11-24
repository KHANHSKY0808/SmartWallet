import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { TransactionFormComponent } from './transaction-form.component';
import { TransactionService } from '../../../core/services/transaction.service';
import { WalletService } from '../../../core/services/wallet.service';
import { CategoryService } from '../../../core/services/category.service';
import { WalletDto } from '../../../core/models/wallet.models';
import { CategoryDto } from '../../../core/models/category.models';

describe('TransactionFormComponent', () => {
  let component: TransactionFormComponent;
  let fixture: ComponentFixture<TransactionFormComponent>;
  let transactionServiceMock: {
    getTransaction: ReturnType<typeof vi.fn>;
    createTransaction: ReturnType<typeof vi.fn>;
    updateTransaction: ReturnType<typeof vi.fn>;
    uploadReceipt: ReturnType<typeof vi.fn>;
    getTags: ReturnType<typeof vi.fn>;
    createTag: ReturnType<typeof vi.fn>;
  };
  let walletServiceMock: {
    getWallets: ReturnType<typeof vi.fn>;
  };
  let categoryServiceMock: {
    getCategories: ReturnType<typeof vi.fn>;
  };

  const mockWallets: WalletDto[] = [
    {
      id: 'w1',
      userId: 'u1',
      name: 'Ví Tiền Mặt',
      type: 1,
      balance: 1000000,
      initialBalance: 1000000,
      currency: 'VND',
      isDefault: true,
      isArchived: false,
      createdAt: '2026-11-20',
      updatedAt: '2026-11-20'
    }
  ];

  const mockCategories: CategoryDto[] = [
    {
      id: 'c1',
      name: 'Ăn uống',
      type: 1,
      icon: '🍔',
      color: '#ef4444',
      isDefault: true,
      isActive: true,
      sortOrder: 1,
      children: []
    },
    {
      id: 'c2',
      name: 'Lương',
      type: 2,
      icon: '💵',
      color: '#22c55e',
      isDefault: true,
      isActive: true,
      sortOrder: 2,
      children: []
    }
  ];

  beforeEach(async () => {
    transactionServiceMock = {
      getTransaction: vi.fn(),
      createTransaction: vi.fn().mockReturnValue(of({ id: 'tx1', amount: 50000 })),
      updateTransaction: vi.fn().mockReturnValue(of({ id: 'tx1', amount: 70000 })),
      uploadReceipt: vi.fn().mockReturnValue(of({ id: 'f-rec-1' })),
      getTags: vi.fn().mockReturnValue(of([{ id: 't1', name: 'Công tác' }])),
      createTag: vi.fn().mockImplementation((name) => of({ id: 't-new', name }))
    };

    walletServiceMock = {
      getWallets: vi.fn().mockReturnValue(of(mockWallets))
    };

    categoryServiceMock = {
      getCategories: vi.fn().mockReturnValue(of(mockCategories))
    };

    await TestBed.configureTestingModule({
      imports: [TransactionFormComponent],
      providers: [
        provideRouter([
          { path: 'transactions', component: TransactionFormComponent }
        ]),
        { provide: TransactionService, useValue: transactionServiceMock },
        { provide: WalletService, useValue: walletServiceMock },
        { provide: CategoryService, useValue: categoryServiceMock }
      ]
    }).compileComponents();
  });

  it('FIN-37: should initialize form and select default wallet', () => {
    fixture = TestBed.createComponent(TransactionFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.form).toBeTruthy();
    expect(component.form.value.type).toBe(1);
    expect(component.form.value.walletId).toBe('w1');
  });

  it('FIN-37: should require valid amount > 0', () => {
    fixture = TestBed.createComponent(TransactionFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    component.form.patchValue({ amount: 0 });
    expect(component.form.controls['amount'].invalid).toBe(true);

    component.form.patchValue({ amount: 50000 });
    expect(component.form.controls['amount'].valid).toBe(true);
  });

  it('FIN-37: should call createTransaction when submitting valid form', () => {
    fixture = TestBed.createComponent(TransactionFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    component.form.patchValue({
      walletId: 'w1',
      categoryId: 'c1',
      type: 1,
      amount: 50000,
      note: 'Ăn trưa'
    });

    component.onSubmit();
    expect(transactionServiceMock.createTransaction).toHaveBeenCalled();
  });

  it('FIN-40: should show 409 conflict message on optimistic concurrency conflict', () => {
    fixture = TestBed.createComponent(TransactionFormComponent);
    component = fixture.componentInstance;
    component.isEditMode.set(true);
    component.transactionId.set('tx1');
    component.currentRowVersion.set('old-version');

    transactionServiceMock.updateTransaction.mockReturnValue(
      throwError(() => ({ status: 409, error: { message: 'Concurrency conflict' } }))
    );

    fixture.detectChanges();

    component.form.patchValue({
      walletId: 'w1',
      type: 1,
      amount: 70000
    });

    component.onSubmit();

    expect(component.errorMessage()).toContain('phiên làm việc khác');
  });

  it('F-04: should reject receipt files larger than 5MB immediately at client without upload call', () => {
    fixture = TestBed.createComponent(TransactionFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    const largeFile = new File([new ArrayBuffer(5 * 1024 * 1024 + 100)], 'large.png', { type: 'image/png' });
    component.handleReceiptFile(largeFile);

    expect(component.receiptError()).toContain('5MB');
    expect(component.receiptFileId()).toBeNull();
    expect(transactionServiceMock.uploadReceipt).not.toHaveBeenCalled();
  });

  it('F-05: should limit tags to maximum 10 and support inline tag creation', () => {
    fixture = TestBed.createComponent(TransactionFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    // Set 10 selected tags
    const tenTags = Array.from({ length: 10 }, (_, i) => `tag-${i}`);
    component.selectedTagIds.set(tenTags);

    // Try to toggle an 11th tag
    component.toggleTag('tag-11');
    expect(component.tagError()).toContain('Tối đa 10 thẻ');
    expect(component.selectedTagIds().length).toBe(10);

    // Try inline add
    component.newTagName.set('Tag mới');
    component.addInlineTag();
    expect(transactionServiceMock.createTag).not.toHaveBeenCalled();

    // Remove one tag and add new inline tag
    component.toggleTag('tag-0'); // deselect
    expect(component.selectedTagIds().length).toBe(9);

    component.newTagName.set('Tag mới');
    component.addInlineTag();
    expect(transactionServiceMock.createTag).toHaveBeenCalledWith('Tag mới');
    expect(component.selectedTagIds()).toContain('t-new');
  });

  it('F-06: should be responsive without horizontal overflow', () => {
    fixture = TestBed.createComponent(TransactionFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const container = compiled.querySelector('.transaction-form-container') as HTMLElement;
    expect(container).toBeTruthy();
    expect(container.style.maxWidth).toBe('100%');
    expect(container.style.overflowX).toBe('hidden');
  });
});
