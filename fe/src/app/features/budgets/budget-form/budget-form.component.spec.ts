import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { BudgetFormComponent } from './budget-form.component';
import { BudgetService } from '../../../core/services/budget.service';
import { CategoryService } from '../../../core/services/category.service';
import { CategoryDto } from '../../../core/models/category.models';

describe('BudgetFormComponent (F-02)', () => {
  let component: BudgetFormComponent;
  let fixture: ComponentFixture<BudgetFormComponent>;
  let budgetServiceMock: {
    getBudget: ReturnType<typeof vi.fn>;
    createBudget: ReturnType<typeof vi.fn>;
    updateBudget: ReturnType<typeof vi.fn>;
  };
  let categoryServiceMock: {
    getCategories: ReturnType<typeof vi.fn>;
  };

  const mockCategories: CategoryDto[] = [
    {
      id: 'c-expense-1',
      name: 'Ăn uống',
      type: 1, // Expense
      icon: '🍔',
      color: '#ef4444',
      isDefault: true,
      isActive: true,
      sortOrder: 1,
      children: []
    },
    {
      id: 'c-income-1',
      name: 'Tiền lương',
      type: 2, // Income - Phải bị lọc bỏ
      icon: '💵',
      color: '#22c55e',
      isDefault: true,
      isActive: true,
      sortOrder: 2,
      children: []
    }
  ];

  beforeEach(async () => {
    budgetServiceMock = {
      getBudget: vi.fn(),
      createBudget: vi.fn().mockReturnValue(of({ id: 'b1', amount: 2000000 })),
      updateBudget: vi.fn().mockReturnValue(of({ id: 'b1', amount: 2500000 }))
    };

    categoryServiceMock = {
      getCategories: vi.fn().mockReturnValue(of(mockCategories))
    };

    await TestBed.configureTestingModule({
      imports: [BudgetFormComponent],
      providers: [
        provideRouter([]),
        { provide: BudgetService, useValue: budgetServiceMock },
        { provide: CategoryService, useValue: categoryServiceMock }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(BudgetFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('F-02: Category selector should ONLY display Expense categories (type === 1)', () => {
    expect(categoryServiceMock.getCategories).toHaveBeenCalledWith(1);
    expect(component.expenseCategories().length).toBe(1);
    expect(component.expenseCategories()[0].id).toBe('c-expense-1');
    expect(component.expenseCategories()[0].type).toBe(1);
  });

  it('F-02: Amount <= 0 should trigger validation error', () => {
    const amountCtrl = component.budgetForm.get('amount');
    amountCtrl?.setValue(0);
    amountCtrl?.markAsTouched();
    fixture.detectChanges();

    expect(amountCtrl?.invalid).toBe(true);
    expect(amountCtrl?.hasError('min')).toBe(true);

    amountCtrl?.setValue(-50000);
    expect(amountCtrl?.invalid).toBe(true);

    amountCtrl?.setValue(2000000);
    expect(amountCtrl?.valid).toBe(true);
  });

  it('F-02: 409 conflict error should display under the category select field', () => {
    budgetServiceMock.createBudget.mockReturnValue(
      throwError(() => ({
        status: 409,
        error: { code: 'DUPLICATE_BUDGET', message: 'Ngân sách cho danh mục này trong tháng đã tồn tại' }
      }))
    );

    component.budgetForm.patchValue({
      categoryId: 'c-expense-1',
      amount: 2000000,
      month: 11,
      year: 2026
    });

    component.onSubmit();
    fixture.detectChanges();

    expect(component.categoryError()).toBe('Ngân sách cho danh mục này trong tháng đã tồn tại');
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('.category-error');
    expect(errorEl?.textContent).toContain('Ngân sách cho danh mục này trong tháng đã tồn tại');
  });
});

