import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { BudgetListComponent } from './budget-list.component';
import { BudgetService } from '../../../core/services/budget.service';
import { CategoryService } from '../../../core/services/category.service';
import { BudgetDto } from '../../../core/models/budget.models';
import { CategoryDto } from '../../../core/models/category.models';

describe('BudgetListComponent (F-01, F-03)', () => {
  let component: BudgetListComponent;
  let fixture: ComponentFixture<BudgetListComponent>;
  let budgetServiceMock: {
    getBudgets: ReturnType<typeof vi.fn>;
    deleteBudget: ReturnType<typeof vi.fn>;
  };
  let categoryServiceMock: {
    getCategories: ReturnType<typeof vi.fn>;
  };

  const mockBudgets: BudgetDto[] = [
    {
      id: 'b1',
      userId: 'u1',
      categoryId: 'c1',
      amount: 2000000,
      month: 11,
      year: 2026,
      currentSpent: 1598000,
      percent: 79.9,
      status: 'normal',
      isAlertSent80: false,
      isAlertSent100: false,
      createdAt: '2026-11-20',
      updatedAt: '2026-11-20'
    },
    {
      id: 'b2',
      userId: 'u1',
      categoryId: 'c2',
      amount: 1000000,
      month: 11,
      year: 2026,
      currentSpent: 800000,
      percent: 80.0,
      status: 'warning',
      isAlertSent80: true,
      isAlertSent100: false,
      createdAt: '2026-11-20',
      updatedAt: '2026-11-20'
    },
    {
      id: 'b3',
      userId: 'u1',
      categoryId: 'c3',
      amount: 1000000,
      month: 11,
      year: 2026,
      currentSpent: 1300000,
      percent: 130.0,
      status: 'exceeded',
      isAlertSent80: true,
      isAlertSent100: true,
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
    }
  ];

  beforeEach(async () => {
    budgetServiceMock = {
      getBudgets: vi.fn().mockReturnValue(of(mockBudgets)),
      deleteBudget: vi.fn().mockReturnValue(of({ message: 'Deleted' }))
    };

    categoryServiceMock = {
      getCategories: vi.fn().mockReturnValue(of(mockCategories))
    };

    await TestBed.configureTestingModule({
      imports: [BudgetListComponent],
      providers: [
        provideRouter([]),
        { provide: BudgetService, useValue: budgetServiceMock },
        { provide: CategoryService, useValue: categoryServiceMock }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(BudgetListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('F-01: Progress bar colors should follow green (<80%), yellow (80-99.9%), red (>=100%)', () => {
    expect(component.getProgressColor(79.9)).toBe('#22c55e'); // Green
    expect(component.getProgressColor(80.0)).toBe('#eab308'); // Yellow
    expect(component.getProgressColor(99.9)).toBe('#eab308'); // Yellow
    expect(component.getProgressColor(100.0)).toBe('#ef4444'); // Red
    expect(component.getProgressColor(130.0)).toBe('#ef4444'); // Red
  });

  it('F-01: Progress bar width must NOT overflow 100%', () => {
    expect(component.getBarWidth(0)).toBe(0);
    expect(component.getBarWidth(50)).toBe(50);
    expect(component.getBarWidth(79.9)).toBe(79.9);
    expect(component.getBarWidth(80)).toBe(80);
    expect(component.getBarWidth(100)).toBe(100);
    expect(component.getBarWidth(130)).toBe(100);
    expect(component.getBarWidth(250)).toBe(100);
  });

  it('F-03: Container should have max-width and render budget list without horizontal overflow', () => {
    const el = fixture.nativeElement as HTMLElement;
    const container = el.querySelector('.budget-container') as HTMLElement;
    expect(container).toBeTruthy();
    expect(container.style.maxWidth).toBe('900px');
  });
});

