export interface CategoryDto {
  id: string;
  userId?: string | null;
  name: string;
  type: number; // 1=Expense, 2=Income
  parentId?: string | null;
  icon?: string | null;
  color?: string | null;
  isDefault: boolean;
  isActive: boolean;
  sortOrder: number;
  children: CategoryDto[];
}

export interface CreateCategoryRequest {
  name: string;
  type: number;
  parentId?: string | null;
  icon?: string | null;
  color?: string | null;
}

export interface UpdateCategoryRequest {
  name: string;
  parentId?: string | null;
  icon?: string | null;
  color?: string | null;
}

