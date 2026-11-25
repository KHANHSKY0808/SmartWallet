import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReportsDashboardComponent } from './reports-dashboard.component';
import { ReportService } from '../../../core/services/report.service';
import { of, throwError } from 'rxjs';
import { provideCharts, withDefaultRegisterables } from 'ng2-charts';
import { vi } from 'vitest';

describe('ReportsDashboardComponent (F-01, F-02, F-03, F-04)', () => {
  let component: ReportsDashboardComponent;
  let fixture: ComponentFixture<ReportsDashboardComponent>;
  let mockReportService: any;

  beforeEach(async () => {
    mockReportService = {
      getDashboard: vi.fn().mockReturnValue(of({
        month: 11,
        year: 2026,
        totalIncome: 5000000,
        totalExpense: 2000000,
        net: 3000000,
        byCategory: [{ categoryId: 'c1', categoryName: 'Ăn uống', amount: 2000000, percent: 100 }],
        topExpenses: [],
        budgets: [],
        budgetsUnavailable: false,
        excludedCurrencies: []
      })),
      getAnalysis: vi.fn().mockReturnValue(of({
        from: '2026-01-01',
        to: '2026-12-31',
        groupBy: 'month',
        points: [{ period: '2026-11', income: 5000000, expense: 2000000, net: 3000000 }],
        totals: { income: 5000000, expense: 2000000, net: 3000000 },
        previous: null,
        change: { income: null, expense: 20.0, net: null },
        excludedCurrencies: []
      })),
      listExports: vi.fn().mockReturnValue(of([])),
      createExport: vi.fn(),
      getExport: vi.fn(),
      downloadExportFile: vi.fn()
    };

    await TestBed.configureTestingModule({
      imports: [ReportsDashboardComponent],
      providers: [
        { provide: ReportService, useValue: mockReportService },
        provideCharts(withDefaultRegisterables())
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ReportsDashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('F-01: should render dashboard summary and chart data', () => {
    expect(component.dashboard()?.totalIncome).toBe(5000000);
    expect(component.pieChartData.labels).toContain('Ăn uống');
    expect(component.barChartData.labels).toContain('2026-11');
  });

  it('F-02: should call getDashboard with updated month and year on filter change', () => {
    component.selectedMonth = 10;
    component.selectedYear = 2026;
    component.onPeriodChange();
    expect(mockReportService.getDashboard).toHaveBeenCalledWith(10, 2026);
  });

  it('F-03: should format null change as dash instead of NaN or Infinity', () => {
    expect(component.formatChange(null)).toBe('—');
    expect(component.formatChange(Number.NaN)).toBe('—');
    expect(component.formatChange(Number.POSITIVE_INFINITY)).toBe('—');
    expect(component.formatChange(20.0)).toBe('+20.0%');
  });

  it('F-04: should show friendly error message on 429 EXPORT_LIMIT', () => {
    mockReportService.createExport.mockReturnValue(throwError(() => ({ status: 429, error: { code: 'EXPORT_LIMIT' } })));
    component.triggerExport();
    expect(component.exportError()).toContain('tối đa 3');
  });
});

