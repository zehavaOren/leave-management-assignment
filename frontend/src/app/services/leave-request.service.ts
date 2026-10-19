import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { forkJoin } from 'rxjs';
import { CreateLeaveRequest, Employee, LeaveRequest, LeaveStatus } from '../models/leave-request.model';
import { leaveRequestErrorMessage } from '../i18n/leave-request-errors';

@Injectable({ providedIn: 'root' })
export class LeaveRequestService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = 'http://localhost:5080/api';

  load() {
    return forkJoin({
      requests: this.http.get<LeaveRequest[]>(`${this.apiUrl}/leave-requests`),
      employees: this.http.get<Employee[]>(`${this.apiUrl}/employees`)
    });
  }
  create(request: CreateLeaveRequest) {
    return this.http.post<LeaveRequest>(`${this.apiUrl}/leave-requests`, request);
  }
  approve(id: number) {
    return this.http.post<LeaveRequest>(`${this.apiUrl}/leave-requests/${id}/approve`, {});
  }
  errorMessage(error: unknown): string {
    return leaveRequestErrorMessage(error);
  }
  // A 409 on approve can tell us the request's real status, so the row can be corrected without a reload.
  statusAfterFailedApproval(error: unknown): LeaveStatus | null {
    if (!(error instanceof HttpErrorResponse) || error.status !== 409) return null;
    const code: unknown = (error.error as { code?: unknown } | null)?.code;
    if (code === 'request_already_approved') return LeaveStatus.Approved;
    if (code === 'request_rejected') return LeaveStatus.Rejected;
    return null;
  }
}
