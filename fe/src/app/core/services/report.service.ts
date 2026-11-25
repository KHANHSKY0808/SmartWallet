import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AnalysisResponse,
  CreateExportRequest,
  DashboardResponse,
  ExportJobDto,
  ServiceHealthItem
} from '../models/report.models';

@Injectable({
  providedIn: 'root'
})
export class ReportService {
  private readonly reportsUrl = '/api/reports';
  private readonly adminHealthUrl = '/api/admin/health';

  constructor(private http: HttpClient) {}

  getDashboard(month?: number, year?: number): Observable<DashboardResponse> {
    let params = new HttpParams();
    if (month !== undefined) params = params.set('month', month.toString());
    if (year !== undefined) params = params.set('year', year.toString());
    return this.http.get<DashboardResponse>(`${this.reportsUrl}/dashboard`, { params });
  }

  getAnalysis(
    from: string,
    to: string,
    groupBy: 'day' | 'week' | 'month' | 'year' = 'month',
    comparePrevious = true
  ): Observable<AnalysisResponse> {
    let params = new HttpParams()
      .set('from', from)
      .set('to', to)
      .set('groupBy', groupBy);
    if (comparePrevious) {
      params = params.set('compare', 'previous');
    }
    return this.http.get<AnalysisResponse>(`${this.reportsUrl}/analysis`, { params });
  }

  createExport(req: CreateExportRequest): Observable<ExportJobDto> {
    return this.http.post<ExportJobDto>(`${this.reportsUrl}/exports`, req);
  }

  listExports(): Observable<ExportJobDto[]> {
    return this.http.get<ExportJobDto[]>(`${this.reportsUrl}/exports`);
  }

  getExport(jobId: string): Observable<ExportJobDto> {
    return this.http.get<ExportJobDto>(`${this.reportsUrl}/exports/${jobId}`);
  }

  downloadExportFile(jobId: string): Observable<Blob> {
    return this.http.get(`${this.reportsUrl}/exports/${jobId}/file`, { responseType: 'blob' });
  }

  getAdminHealth(): Observable<ServiceHealthItem[]> {
    return this.http.get<ServiceHealthItem[]>(this.adminHealthUrl);
  }
}

