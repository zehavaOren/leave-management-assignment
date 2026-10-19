import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { Employee, LeaveRequest, LeaveStatus, LeaveType } from '../models/leave-request.model';
import { LeaveRequestService, TranslatePipe, TranslateService, TranslationKey } from './leave-request.service';

export function dateRangeValidator(control: AbstractControl): ValidationErrors | null {
  const start: unknown = control.get('startDate')?.value;
  const end: unknown = control.get('endDate')?.value;
  return typeof start === 'string' && typeof end === 'string' && start && end && end < start
    ? { dateOrder: true } : null;
}

// NOTE: This component was written quickly for a POC.
// It talks to the API directly, manages state by hand and uses `any` everywhere.
// Historical POC note above: HTTP and translations now live in services; state is typed.
@Component({
  selector: 'app-leave-requests', standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslatePipe],
  templateUrl: './leave-requests.component.html', styleUrls: ['./leave-requests.component.css']
})
export class LeaveRequestsComponent implements OnInit {
  private readonly api = inject(LeaveRequestService);
  private readonly translations = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);
  readonly requests = signal<LeaveRequest[]>([]);
  readonly employees = signal<Employee[]>([]);
  readonly loading = signal(false);
  readonly submitting = signal(false);
  readonly approving = signal<ReadonlySet<number>>(new Set());
  readonly rowErrors = signal<Record<number, string>>({});
  readonly error = signal('');
  readonly success = signal('');
  readonly pending = LeaveStatus.Pending;
  readonly types: ReadonlyArray<{ value: LeaveType; label: TranslationKey }> = [
    { value: LeaveType.Vacation, label: 'vacation' }, { value: LeaveType.Sick, label: 'sick' },
    { value: LeaveType.Unpaid, label: 'unpaid' }
  ];
  readonly form = new FormGroup({
    employeeId: new FormControl<number | null>(null, Validators.required),
    type: new FormControl<LeaveType | null>(null, Validators.required),
    startDate: new FormControl('', { nonNullable: true, validators: Validators.required }),
    endDate: new FormControl('', { nonNullable: true, validators: Validators.required })
  }, { validators: dateRangeValidator });

  ngOnInit(): void { this.load(); }

  load(): void {
    if (this.loading() || this.submitting() || this.approving().size > 0) return;
    this.loading.set(true);
    this.error.set('');
    this.api.load().pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: data => { this.requests.set(data.requests); this.employees.set(data.employees); this.rowErrors.set({}); },
        error: (error: unknown) => this.error.set(this.api.errorMessage(error))
      });
  }

  get days(): number {
    const { startDate, endDate } = this.form.getRawValue();
    if (!startDate || !endDate || endDate < startDate) return 0;
    // Date-only UTC arithmetic avoids DST making a calendar day 23 or 25 hours.
    const count = (Date.parse(`${endDate}T00:00:00Z`) - Date.parse(`${startDate}T00:00:00Z`)) / 86400000 + 1;
    return Number.isFinite(count) ? count : 0;
  }

  submit(): void {
    if (this.submitting() || this.loading()) return;
    this.form.markAllAsTouched();
    this.error.set(''); this.success.set('');
    const value = this.form.getRawValue();
    if (this.form.invalid || value.employeeId === null || value.type === null || this.days < 1) {
      this.error.set(this.formErrorMessage()); return;
    }
    this.submitting.set(true);
    this.api.create({ ...value, employeeId: value.employeeId, type: value.type })
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.submitting.set(false)))
      .subscribe({
        next: request => {
          this.requests.update(requests => [request, ...requests].sort((a, b) => b.startDate.localeCompare(a.startDate)));
          this.form.reset();
          this.success.set(this.translations.text('created'));
        },
        error: (error: unknown) => this.error.set(this.api.errorMessage(error))
      });
  }

  private formErrorMessage(): string {
    const value = this.form.getRawValue();
    const keys: TranslationKey[] = [];
    if (value.employeeId === null) keys.push('chooseEmployee');
    if (value.type === null) keys.push('chooseType');
    if (!value.startDate) keys.push('startDateInvalid');
    if (!value.endDate) keys.push('endDateInvalid');
    if (this.form.hasError('dateOrder')) keys.push('dateOrder');
    if (keys.length === 0 && this.days < 1) keys.push('invalidDates');
    return keys.map(key => this.translations.text(key)).join(' ') || this.translations.text('formInvalid');
  }

  // Wired up by the candidate as part of the assignment.
  approve(id: number): void {
    // TODO (candidate): call POST /api/leave-requests/{id}/approve
    // and handle loading / error / success without a generic alert.
    // Implemented below; only the returned row is replaced.
    if (this.approving().has(id) || this.loading()) return;
    this.success.set('');
    this.rowErrors.update(errors => ({ ...errors, [id]: '' }));
    this.approving.update(ids => new Set([...ids, id]));
    this.api.approve(id).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => {
      this.approving.update(ids => { const next = new Set(ids); next.delete(id); return next; });
    })).subscribe({
      next: request => {
        this.requests.update(requests => requests.map(row => row.id === request.id ? request : row));
        this.success.set(this.translations.text('approvedSuccess'));
      },
      error: (error: unknown) => this.rowErrors.update(errors => ({ ...errors, [id]: this.api.errorMessage(error) }))
    });
  }

  typeLabel(type: number): string {
    return this.translations.text(this.types.find(item => item.value === type)?.label ?? 'unknown');
  }
  statusLabel(status: number): string {
    const labels: Record<LeaveStatus, TranslationKey> = {
      [LeaveStatus.Pending]: 'pending', [LeaveStatus.Approved]: 'approved', [LeaveStatus.Rejected]: 'rejected'
    };
    return this.translations.text(labels[status as LeaveStatus] ?? 'unknown');
  }
  trackRequest(_index: number, request: LeaveRequest): number { return request.id; }
}
