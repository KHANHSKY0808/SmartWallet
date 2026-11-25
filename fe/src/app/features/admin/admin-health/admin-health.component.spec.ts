import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminHealthComponent } from './admin-health.component';
import { ReportService } from '../../../core/services/report.service';
import { of } from 'rxjs';
import { vi } from 'vitest';

describe('AdminHealthComponent (F-05)', () => {
  let component: AdminHealthComponent;
  let fixture: ComponentFixture<AdminHealthComponent>;
  let mockReportService: any;

  beforeEach(async () => {
    mockReportService = {
      getAdminHealth: vi.fn().mockReturnValue(of([
        { service: 'IdentityService', status: 'Healthy', latencyMs: 12, checkedAt: '2026-11-25T10:00:00Z' },
        { service: 'FileService', status: 'Unhealthy', latencyMs: 2001, checkedAt: '2026-11-25T10:00:00Z' }
      ]))
    };

    await TestBed.configureTestingModule({
      imports: [AdminHealthComponent],
      providers: [
        { provide: ReportService, useValue: mockReportService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AdminHealthComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('F-05: should display service health statuses with appropriate colors', () => {
    expect(component.services().length).toBe(2);
    expect(component.getStatusColor('Healthy')).toBe('#166534');
    expect(component.getStatusColor('Unhealthy')).toBe('#991b1b');
    expect(component.getStatusColor('Degraded')).toBe('#92400e');
  });
});

