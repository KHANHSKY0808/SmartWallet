import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  CategoryDto,
  CreateCategoryRequest,
  UpdateCategoryRequest
} from '../models/category.models';

@Injectable({
  providedIn: 'root'
})
export class CategoryService {
  private http = inject(HttpClient);
  private apiUrl = '/api/categories';

  getCategories(type?: number): Observable<CategoryDto[]> {
    let params = new HttpParams();
    if (type !== undefined && type !== null) {
      params = params.set('type', type.toString());
    }
    return this.http.get<CategoryDto[]>(this.apiUrl, { params });
  }

  getCategory(id: string): Observable<CategoryDto> {
    return this.http.get<CategoryDto>(`${this.apiUrl}/${id}`);
  }

  createCategory(request: CreateCategoryRequest): Observable<CategoryDto> {
    return this.http.post<CategoryDto>(this.apiUrl, request);
  }

  updateCategory(id: string, request: UpdateCategoryRequest): Observable<CategoryDto> {
    return this.http.put<CategoryDto>(`${this.apiUrl}/${id}`, request);
  }

  deleteCategory(id: string, replacementId?: string): Observable<{ message: string }> {
    let params = new HttpParams();
    if (replacementId) {
      params = params.set('replacementId', replacementId);
    }
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`, { params });
  }

  // Admin Defaults
  createDefaultCategory(request: CreateCategoryRequest): Observable<CategoryDto> {
    return this.http.post<CategoryDto>(`${this.apiUrl}/defaults`, request);
  }

  updateDefaultCategory(id: string, request: UpdateCategoryRequest): Observable<CategoryDto> {
    return this.http.put<CategoryDto>(`${this.apiUrl}/defaults/${id}`, request);
  }

  deleteDefaultCategory(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/defaults/${id}`);
  }
}

