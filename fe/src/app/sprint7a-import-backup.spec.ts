import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TransactionImportComponent } from './features/transactions/transaction-import/transaction-import.component';
import { AdminBackupsComponent } from './features/admin/admin-backups/admin-backups.component';

describe('Sprint 7a Frontend (F-01..F-04: CSV/Excel Import & Admin Backups)', () => {
  it('F-01: Blocks files > 5 MB on client before upload and previews up to 20 CSV rows', () => {
    TestBed.configureTestingModule({
      imports: [TransactionImportComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    });

    const fixture = TestBed.createComponent(TransactionImportComponent);
    const comp = fixture.componentInstance;

    const oversized = new File(['x'], 'big.csv', { type: 'text/csv' });
    Object.defineProperty(oversized, 'size', { value: 5 * 1024 * 1024 + 1 });
    comp.handleFile(oversized);
    fixture.detectChanges();

    expect(comp.clientError()).toContain('5 MB');
    expect(comp.selectedFile()).toBeNull();

    const rows = Array.from({ length: 25 }, (_, i) => `15/11/2026,${1000 + i},Chi,Ăn uống,Ví chính,note ${i}`).join('\n');
    comp.parseCsvPreview(`Ngày;Số tiền;Loại;Danh mục;Ví;Ghi chú\n${rows}`);
    fixture.detectChanges();

    expect(comp.previewRows().length).toBe(20);
  });

  it('F-02 & F-03: Suggests column mapping, allows edit, uploads, shows result and errors.csv button', () => {
    TestBed.configureTestingModule({
      imports: [TransactionImportComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    });

    const httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TransactionImportComponent);
    const comp = fixture.componentInstance;

    comp.parseCsvPreview('Ngày,Số tiền,Loại,Danh mục,Ví,Ghi chú\n01/11/2026,1000,Chi,Ăn uống,Ví chính,a');
    expect(comp.mapping.date).toBe('Ngày');
    expect(comp.mapping.amount).toBe('Số tiền');

    comp.mapping.note = 'Ghi chú';
    const smallFile = new File(['Ngay,SoTien,Loai,DanhMuc,Vi\n01/11/2026,1000,Chi,Ăn uống,Ví chính'], 'ok.csv', { type: 'text/csv' });
    comp.selectedFile.set(smallFile);
    comp.submitImport();

    const postReq = httpMock.expectOne('/api/transactions/imports');
    expect(postReq.request.method).toBe('POST');
    postReq.flush({ jobId: 'job-1', status: 'Queued' }, { status: 202, statusText: 'Accepted' });

    const statusReq = httpMock.expectOne('/api/transactions/imports/job-1');
    statusReq.flush({
      id: 'job-1',
      jobId: 'job-1',
      fileName: 'ok.csv',
      status: 'Completed',
      totalRows: 10,
      succeeded: 8,
      failed: 2,
      errors: [{ row: 3, reason: 'Ngày sai' }],
      createdAt: '2026-11-26T00:00:00Z'
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Completed');
    expect(text).toContain('Ngày sai');
    expect(fixture.nativeElement.querySelector('.btn-download-errors')).toBeTruthy();

    comp.ngOnDestroy();
    httpMock.verify();
  });

  it('F-04: AdminBackupsComponent renders history, service statuses and triggers manual backup', () => {
    TestBed.configureTestingModule({
      imports: [AdminBackupsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });

    const httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AdminBackupsComponent);
    fixture.detectChanges();

    const getReq = httpMock.expectOne('/api/admin/backups');
    getReq.flush([
      {
        backupId: '20261126-010000-sched',
        startedAt: '2026-11-25T18:00:00Z',
        completedAt: '2026-11-25T18:00:04Z',
        status: 'Completed',
        files: [{ service: 'IdentityService', fileName: 'identity.db', sizeBytes: 4096, sha256: 'a'.repeat(64) }],
        failedServices: []
      }
    ]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('20261126-010000-sched');
    expect(fixture.nativeElement.textContent).toContain('IdentityService');

    const btn = fixture.nativeElement.querySelector('.btn-backup-now') as HTMLButtonElement;
    btn.click();

    const postReq = httpMock.expectOne('/api/admin/backups');
    expect(postReq.request.method).toBe('POST');
    postReq.flush({ backupId: '20261126-120000-abc123' }, { status: 202, statusText: 'Accepted' });

    const refreshReq = httpMock.expectOne('/api/admin/backups');
    refreshReq.flush([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('20261126-120000-abc123');
    httpMock.verify();
  });
});
