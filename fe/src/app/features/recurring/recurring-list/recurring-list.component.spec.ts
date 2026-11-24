import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RecurringListComponent } from './recurring-list.component';
import { RecurringService } from '../../../core/services/recurring.service';
import { WalletService } from '../../../core/services/wallet.service';
import { CategoryService } from '../../../core/services/category.service';
import { of } from 'rxjs';

describe('RecurringListComponent (F-01)', () => {
  let component: RecurringListComponent;
  let fixture: ComponentFixture<RecurringListComponent>;
  let mockRecurringService: any;
  let mockWalletService: any;
  let mockCategoryService: any;

  beforeEach(async () => {
    mockRecurringService = {
      getRules: () => of([]),
      previewNextOccurrences: (date: string, freq: number, count: number) => ['2027-02-28', '2027-03-31', '2027-04-30'],
      createRule: () => of({}),
      updateRule: () => of({}),
      deleteRule: () => of({})
    };
    mockWalletService = {
      getWallets: () => of([])
    };
    mockCategoryService = {
      getCategories: () => of([])
    };

    await TestBed.configureTestingModule({
      imports: [RecurringListComponent],
      providers: [
        { provide: RecurringService, useValue: mockRecurringService },
        { provide: WalletService, useValue: mockWalletService },
        { provide: CategoryService, useValue: mockCategoryService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(RecurringListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('F-01: should preview 3 next occurrences with anchor day logic', () => {
    component.newRule.startDate = '2027-01-31';
    component.newRule.frequency = 3; // Monthly
    component.updatePreview();

    expect(component.previewOccurrences.length).toBe(3);
    expect(component.previewOccurrences[0]).toBe('2027-02-28');
  });
});

