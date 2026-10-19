import { Injectable, Pipe, PipeTransform, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { forkJoin } from 'rxjs';
import { CreateLeaveRequest, Employee, LeaveRequest } from '../models/leave-request.model';

const english = {
  title: 'Leave requests', newRequest: 'New request', employee: 'Employee', chooseEmployee: 'Choose an employee',
  type: 'Leave type', chooseType: 'Choose a leave type', start: 'Start date', end: 'End date', days: 'Calendar days',
  submit: 'Submit request', submitting: 'Submitting…', required: 'This field is required.',
  dateOrder: 'End date must be on or after start date.', vacation: 'Vacation', sick: 'Sick', unpaid: 'Unpaid',
  pending: 'Pending', approved: 'Approved', rejected: 'Rejected', status: 'Status', actions: 'Actions',
  approve: 'Approve', approving: 'Approving…', loading: 'Loading…', retry: 'Retry', empty: 'No leave requests yet.',
  loadError: 'Could not load requests and employees. Please retry.',
  networkError: 'Cannot reach the server. Check the connection and retry.',
  requestError: 'The request failed. Please retry.',
  conflict: 'Approval failed: the request is no longer pending or its vacation balance is insufficient. Refresh to see the latest status.',
  invalid: 'Check the dates, leave type and remaining vacation balance.', missing: 'The employee or request no longer exists.',
  created: 'Request submitted successfully.', approvedSuccess: 'Request approved successfully.', refresh: 'Refresh',
  unknown: 'Unknown', employeeFallback: 'Employee #', formInvalid: 'Please correct the highlighted fields.'
};
export type TranslationKey = keyof typeof english;
const hebrew: Record<TranslationKey, string> = {
  title: 'בקשות חופשה', newRequest: 'בקשה חדשה', employee: 'עובד', chooseEmployee: 'בחרו עובד',
  type: 'סוג חופשה', chooseType: 'בחרו סוג חופשה', start: 'תאריך התחלה', end: 'תאריך סיום', days: 'ימים קלנדריים',
  submit: 'שליחת בקשה', submitting: 'שולח…', required: 'שדה חובה.', dateOrder: 'תאריך הסיום חייב להיות בתאריך ההתחלה או אחריו.',
  vacation: 'חופשה', sick: 'מחלה', unpaid: 'ללא תשלום', pending: 'ממתינה', approved: 'מאושרת', rejected: 'נדחתה',
  status: 'סטטוס', actions: 'פעולות', approve: 'אישור', approving: 'מאשר…', loading: 'טוען…', retry: 'ניסיון חוזר',
  empty: 'אין עדיין בקשות חופשה.', loadError: 'טעינת הבקשות והעובדים נכשלה. נסו שוב.',
  networkError: 'לא ניתן להתחבר לשרת. בדקו את החיבור ונסו שוב.', requestError: 'הפעולה נכשלה. נסו שוב.',
  conflict: 'האישור נכשל: הבקשה כבר טופלה או שאין יתרת חופשה מספקת. רעננו לצפייה במצב העדכני.',
  invalid: 'בדקו את התאריכים, סוג החופשה ויתרת ימי החופשה.', missing: 'העובד או הבקשה אינם קיימים עוד.',
  created: 'הבקשה נשלחה בהצלחה.', approvedSuccess: 'הבקשה אושרה בהצלחה.', refresh: 'רענון',
  unknown: 'לא ידוע', employeeFallback: 'עובד מספר ', formInvalid: 'יש לתקן את השדות המסומנים.'
};

// Small local translation boundary: no new dependency is needed by this starter.
@Injectable({ providedIn: 'root' })
export class TranslateService {
  private readonly messages = document.documentElement.lang.startsWith('he') ? hebrew : english;
  text(key: TranslationKey): string { return this.messages[key]; }
}

@Pipe({ name: 'translate', standalone: true })
export class TranslatePipe implements PipeTransform {
  private readonly translations = inject(TranslateService);
  transform(key: TranslationKey): string { return this.translations.text(key); }
}

@Injectable({ providedIn: 'root' })
export class LeaveRequestService {
  private readonly http = inject(HttpClient);
  private readonly translations = inject(TranslateService);
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
    let key: TranslationKey = 'requestError';
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) key = 'networkError';
      else if (error.status === 409) key = 'conflict';
      else if (error.status === 400) key = 'invalid';
      else if (error.status === 404) key = 'missing';
    }
    return this.translations.text(key);
  }
}
