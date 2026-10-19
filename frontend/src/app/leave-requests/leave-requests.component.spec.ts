import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { LeaveRequestsComponent } from './leave-requests.component';
import { LeaveRequest, LeaveStatus, LeaveType } from '../models/leave-request.model';

const url = 'http://localhost:5080/api';
const employee = { id: 1, name: 'Dana', annualQuota: 20 };
const pending: LeaveRequest = { id: 7, employeeId: 1, employee, type: LeaveType.Vacation,
  startDate: '2026-03-01', endDate: '2026-03-02', days: 2, status: LeaveStatus.Pending };

describe('LeaveRequestsComponent', () => {
  let fixture: ComponentFixture<LeaveRequestsComponent>;
  let component: LeaveRequestsComponent;
  let http: HttpTestingController;
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [LeaveRequestsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()] }).compileComponents();
    fixture = TestBed.createComponent(LeaveRequestsComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });
  afterEach(() => { http.verify(); fixture.destroy(); });
  function loaded(): void {
    http.expectOne(`${url}/leave-requests`).flush([pending]);
    http.expectOne(`${url}/employees`).flush([employee]);
    fixture.detectChanges();
  }
  function validForm(): void {
    component.form.setValue({ employeeId: 1, type: LeaveType.Vacation,
      startDate: '2026-03-01', endDate: '2026-03-02' });
  }
  it('rejects missing leave type and reversed dates without posting', () => {
    loaded(); validForm(); component.form.controls.type.setValue(null); component.submit();
    expect(component.form.controls.type.hasError('required')).toBeTrue();
    component.form.controls.type.setValue(LeaveType.Vacation);
    component.form.controls.endDate.setValue('2026-02-28'); component.submit();
    expect(component.form.hasError('dateOrder')).toBeTrue(); expect(component.days).toBe(0);
    http.expectNone(request => request.method === 'POST');
  });
  it('accepts Vacation zero and appends the returned row without reloading', () => {
    loaded(); validForm(); expect(component.days).toBe(2); component.submit();
    expect(component.submitting()).toBeTrue();
    const request = http.expectOne(`${url}/leave-requests`);
    expect(request.request.body.type).toBe(0); expect(request.request.body.days).toBeUndefined();
    request.flush({ ...pending, id: 8 });
    expect(component.submitting()).toBeFalse(); expect(component.requests().length).toBe(2);
    expect(component.success()).toBeTruthy();
  });
  it('approves once, disables its row and updates it without list reload', () => {
    loaded(); component.approve(7); component.approve(7);
    expect(component.approving().has(7)).toBeTrue();
    http.expectOne(`${url}/leave-requests/7/approve`).flush({ ...pending, status: LeaveStatus.Approved });
    expect(component.approving().has(7)).toBeFalse();
    expect(component.requests()[0].status).toBe(LeaveStatus.Approved);
    http.expectNone(`${url}/leave-requests`);
  });
  it('shows a conflict beside its row and clears the loading state', () => {
    loaded(); component.approve(7);
    http.expectOne(`${url}/leave-requests/7/approve`).flush({}, { status: 409, statusText: 'Conflict' });
    expect(component.approving().size).toBe(0); expect(component.rowErrors()[7]).toBeTruthy();
    expect(component.requests()[0].status).toBe(LeaveStatus.Pending); expect(component.success()).toBe('');
  });
  it('marks the row approved when the server reports it was already approved', () => {
    loaded(); component.approve(7);
    http.expectOne(`${url}/leave-requests/7/approve`)
      .flush({ code: 'request_already_approved' }, { status: 409, statusText: 'Conflict' });
    expect(component.requests()[0].status).toBe(LeaveStatus.Approved);
    expect(component.rowErrors()[7]).toBe('This request has already been approved.');
    http.expectNone(`${url}/leave-requests`);
  });
  it('clears submitting on failure and keeps form values for retry', () => {
    loaded(); validForm(); component.submit();
    http.expectOne(`${url}/leave-requests`).flush({}, { status: 400, statusText: 'Bad Request' });
    expect(component.submitting()).toBeFalse(); expect(component.form.controls.employeeId.value).toBe(1);
    expect(component.error()).toBeTruthy();
  });
  it('clears loading after initial load failure', () => {
    const employees = http.expectOne(`${url}/employees`);
    http.expectOne(`${url}/leave-requests`).flush({}, { status: 500, statusText: 'Server Error' });
    expect(employees.cancelled).toBeTrue(); expect(component.loading()).toBeFalse();
    expect(component.error()).toBeTruthy();
  });
  it('cancels in-flight HTTP on destruction', () => {
    const requests = http.expectOne(`${url}/leave-requests`);
    const employees = http.expectOne(`${url}/employees`);
    fixture.destroy(); expect(requests.cancelled).toBeTrue(); expect(employees.cancelled).toBeTrue();
  });
  it('counts calendar days across a DST change', () => {
    loaded(); validForm(); component.form.patchValue({ startDate: '2026-03-27', endDate: '2026-03-29' });
    expect(component.days).toBe(3);
  });
});
