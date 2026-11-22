import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { CategoryTreeComponent } from './category-tree.component';
import { CategoryService } from '../../../core/services/category.service';
import { AuthService } from '../../../core/services/auth.service';
import { CategoryDto } from '../../../core/models/category.models';

describe('CategoryTreeComponent', () => {
  let component: CategoryTreeComponent;
  let fixture: ComponentFixture<CategoryTreeComponent>;
  let categoryServiceMock: {
    getCategories: ReturnType<typeof vi.fn>;
    createCategory: ReturnType<typeof vi.fn>;
    updateCategory: ReturnType<typeof vi.fn>;
    deleteCategory: ReturnType<typeof vi.fn>;
  };
  let authServiceMock: {
    getUserRole: ReturnType<typeof vi.fn>;
  };

  const mockCategories: CategoryDto[] = [
    {
      id: 'cat-root-1',
      name: 'Ăn uống',
      type: 1,
      parentId: null,
      icon: '🍔',
      color: '#ef4444',
      isDefault: true,
      isActive: true,
      sortOrder: 1,
      children: [
        {
          id: 'cat-child-1',
          name: 'Cà phê',
          type: 1,
          parentId: 'cat-root-1',
          icon: '☕',
          color: '#854d0e',
          isDefault: true,
          isActive: true,
          sortOrder: 1,
          children: []
        }
      ]
    },
    {
      id: 'cat-root-2',
      name: 'Thú cưng',
      type: 1,
      parentId: null,
      icon: '🐱',
      color: '#10b981',
      isDefault: false,
      isActive: true,
      sortOrder: 2,
      children: [
        {
          id: 'cat-child-2',
          name: 'Hạt mèo',
          type: 1,
          parentId: 'cat-root-2',
          icon: '🐟',
          color: '#059669',
          isDefault: false,
          isActive: true,
          sortOrder: 1,
          children: []
        }
      ]
    }
  ];

  beforeEach(async () => {
    categoryServiceMock = {
      getCategories: vi.fn().mockReturnValue(of(mockCategories)),
      createCategory: vi.fn().mockReturnValue(of({} as CategoryDto)),
      updateCategory: vi.fn().mockReturnValue(of({} as CategoryDto)),
      deleteCategory: vi.fn().mockReturnValue(of({ message: 'Deleted' }))
    };

    authServiceMock = {
      getUserRole: vi.fn().mockReturnValue('User')
    };

    await TestBed.configureTestingModule({
      imports: [CategoryTreeComponent],
      providers: [
        { provide: CategoryService, useValue: categoryServiceMock },
        { provide: AuthService, useValue: authServiceMock }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(CategoryTreeComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  // F-05: Cây danh mục: Danh mục mặc định có nhãn riêng và ẩn nút sửa/xóa. Không cho chọn cấp 3 làm cha.
  it('F-05: should display "Mặc định" badge on default categories', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    const defaultBadges = compiled.querySelectorAll('.badge-default');
    expect(defaultBadges.length).toBeGreaterThan(0);
    expect(defaultBadges[0].textContent?.trim()).toBe('Mặc định');
  });

  it('F-05: should hide edit and delete buttons for default categories when user is regular User', () => {
    // For default category (Ăn uống, cat-root-1), canModify should be false
    const defaultCat = mockCategories[0];
    expect(component.canModify(defaultCat)).toBe(false);

    // For custom category (Thú cưng, cat-root-2), canModify should be true
    const customCat = mockCategories[1];
    expect(component.canModify(customCat)).toBe(true);

    // If role is Admin, canModify should be true even for default
    authServiceMock.getUserRole.mockReturnValue('Admin');
    expect(component.canModify(defaultCat)).toBe(true);
  });

  it('F-05: should only include root categories (Level 1) in parent selector to prevent level 3', () => {
    // Open create modal
    component.openCreateModal();
    fixture.detectChanges();

    const rootOnly = component.rootCategoriesOnly();
    // Only cat-root-1 and cat-root-2 should be in rootCategoriesOnly
    expect(rootOnly.length).toBe(2);
    expect(rootOnly.every(c => !c.parentId)).toBe(true);

    // Level 2 categories like 'cat-child-1' ('Cà phê') or 'cat-child-2' ('Hạt mèo') MUST NOT be selectable as parent
    expect(rootOnly.some(c => c.id === 'cat-child-1')).toBe(false);
    expect(rootOnly.some(c => c.id === 'cat-child-2')).toBe(false);
  });
});

