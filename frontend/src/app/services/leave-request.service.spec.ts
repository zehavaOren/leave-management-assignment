import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { LeaveRequestService } from './leave-request.service';

describe('LeaveRequestService error messages', () => {
  let service: LeaveRequestService;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    service = TestBed.inject(LeaveRequestService);
  });
  it('distinguishes date errors from leave type errors', () => {
    expect(service.errorMessage(new HttpErrorResponse({ status: 400, error: { code: 'invalid_date_range' } })))
      .toBe('End date must be on or after start date.');
    expect(service.errorMessage(new HttpErrorResponse({ status: 400, error: { code: 'invalid_leave_type' } })))
      .toBe('The selected leave type is invalid. Choose Vacation, Sick or Unpaid.');
  });
  for (const status of [400, 409]) {
    it(`shows requested and available days for a ${status} balance failure`, () => {
      expect(service.errorMessage(new HttpErrorResponse({ status,
        error: { code: 'insufficient_balance', year: 2026, requestedDays: 3, remainingDays: 2 } })))
        .toBe('Not enough vacation balance for 2026: requested 3 days, available 2 days.');
    });
  }
  it('distinguishes already approved and rejected requests', () => {
    expect(service.errorMessage(new HttpErrorResponse({ status: 409, error: { code: 'request_already_approved' } })))
      .toContain('already been approved');
    expect(service.errorMessage(new HttpErrorResponse({ status: 409, error: { code: 'request_rejected' } })))
      .toBe('This request has been rejected and cannot be approved.');
  });
  it('maps automatic JSON validation to the affected fields', () => {
    const message = service.errorMessage(new HttpErrorResponse({ status: 400,
      error: { errors: { '$.startDate': ['internal parser details'], Type: ['validation details'] } } }));
    expect(message).toContain('Enter a valid start date.');
    expect(message).toContain('The selected leave type is invalid.');
    expect(message).not.toContain('internal');
  });
  it('keeps zero remaining days in the detailed balance message', () => {
    expect(service.errorMessage(new HttpErrorResponse({ status: 409,
      error: { code: 'insufficient_balance', year: 2027, requestedDays: 1, remainingDays: 0 } })))
      .toBe('Not enough vacation balance for 2027: requested 1 days, available 0 days.');
  });
  it('does not render malformed balance details or raw server exceptions', () => {
    expect(service.errorMessage(new HttpErrorResponse({ status: 400,
      error: { code: 'insufficient_balance', year: 2026, requestedDays: 3, remainingDays: '<script>' } })))
      .toBe('There is not enough vacation balance for this request.');
    expect(service.errorMessage(new HttpErrorResponse({ status: 500,
      error: { code: 'invalid_date_range', title: 'secret exception' } })))
      .toBe('The request failed. Please retry.');
  });
  it('explains overlapping leave dates', () => {
    expect(service.errorMessage(new HttpErrorResponse({ status: 409, error: { code: 'overlapping_request' } })))
      .toBe('These dates overlap another leave request for this employee. Choose different dates.');
  });
  it('explains rate limiting without blaming the request fields', () => {
    expect(service.errorMessage(new HttpErrorResponse({ status: 429 })))
      .toBe('Too many requests. Wait a minute and try again.');
  });
});
