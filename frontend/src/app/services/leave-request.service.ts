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
  conflict: 'The request changed. Refresh to see its latest status and try again.',
  invalid: 'Some request fields are missing or invalid. Review the form.', missing: 'The employee or request no longer exists.',
  invalidEmployee: 'Choose a valid employee.', invalidLeaveType: 'The selected leave type is invalid. Choose Vacation, Sick or Unpaid.',
  startDateInvalid: 'Enter a valid start date.', endDateInvalid: 'Enter a valid end date.', invalidDates: 'Enter valid start and end dates.',
  employeeMissing: 'The selected employee no longer exists. Refresh the employee list.',
  requestMissing: 'This leave request no longer exists. Refresh the list.',
  alreadyApproved: 'This request has already been approved. Refresh to see its latest status.',
  requestRejected: 'This request has been rejected and cannot be approved.',
  balanceError: 'There is not enough vacation balance for this request.',
  balanceDetails: 'Not enough vacation balance for {year}: requested {requestedDays} days, available {remainingDays} days.',
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
  conflict: 'הבקשה השתנתה. רעננו לצפייה במצב העדכני ונסו שוב.',
  invalid: 'חלק משדות הבקשה חסרים או אינם תקינים. בדקו את הטופס.', missing: 'העובד או הבקשה אינם קיימים עוד.',
  invalidEmployee: 'בחרו עובד תקין.', invalidLeaveType: 'סוג החופשה אינו תקין. בחרו חופשה, מחלה או ללא תשלום.',
  startDateInvalid: 'הזינו תאריך התחלה תקין.', endDateInvalid: 'הזינו תאריך סיום תקין.', invalidDates: 'הזינו תאריכי התחלה וסיום תקינים.',
  employeeMissing: 'העובד שנבחר אינו קיים עוד. רעננו את רשימת העובדים.',
  requestMissing: 'בקשת החופשה אינה קיימת עוד. רעננו את הרשימה.',
  alreadyApproved: 'הבקשה כבר אושרה. רעננו לצפייה במצב העדכני.',
  requestRejected: 'הבקשה נדחתה ולא ניתן לאשר אותה.',
  balanceError: 'אין יתרת חופשה מספקת עבור בקשה זו.',
  balanceDetails: 'אין יתרת חופשה מספקת לשנת {year}: התבקשו {requestedDays} ימים, וזמינים {remainingDays} ימים.',
  created: 'הבקשה נשלחה בהצלחה.', approvedSuccess: 'הבקשה אושרה בהצלחה.', refresh: 'רענון',
  unknown: 'לא ידוע', employeeFallback: 'עובד מספר ', formInvalid: 'יש לתקן את השדות המסומנים.'
};

// Small local translation boundary: no new dependency is needed by this starter.
@Injectable({ providedIn: 'root' })
export class TranslateService {
  private readonly messages = document.documentElement.lang.startsWith('he') ? hebrew : english;
  text(key: TranslationKey): string { return this.messages[key]; }
  format(key: TranslationKey, values: Readonly<Record<string, number>>): string {
    return this.text(key).replace(/\{(\w+)\}/g, (token: string, name: string) =>
      Object.prototype.hasOwnProperty.call(values, name) ? String(values[name]) : token);
  }
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
    if (!(error instanceof HttpErrorResponse)) return this.translations.text('requestError');
    if (error.status === 0) return this.translations.text('networkError');
    if (![400, 404, 409].includes(error.status)) return this.translations.text('requestError');

    // Treat the response body as untrusted data; never display a raw server title or stack trace.
    const problem: unknown = error.error;
    if (isRecord(problem)) {
      const code = problem['code'];
      if (code === 'insufficient_balance') return this.balanceMessage(problem);
      const messages: Readonly<Record<string, TranslationKey>> = {
        invalid_employee: 'invalidEmployee', leave_type_required: 'chooseType', invalid_leave_type: 'invalidLeaveType',
        start_date_required: 'startDateInvalid', end_date_required: 'endDateInvalid', invalid_date_range: 'dateOrder',
        employee_not_found: 'employeeMissing', request_not_found: 'requestMissing',
        request_already_approved: 'alreadyApproved', request_rejected: 'requestRejected'
      };
      if (typeof code === 'string' && Object.prototype.hasOwnProperty.call(messages, code))
        return this.translations.text(messages[code]);

      if (error.status === 400 && isRecord(problem['errors'])) {
        const fields = Object.keys(problem['errors']);
        const keys = new Set<TranslationKey>();
        for (const field of fields) {
          const name = field.toLowerCase();
          if (name.includes('startdate')) keys.add('startDateInvalid');
          else if (name.includes('enddate')) keys.add('endDateInvalid');
          else if (name.includes('type')) keys.add('invalidLeaveType');
          else if (name.includes('employeeid')) keys.add('invalidEmployee');
        }
        if (keys.size > 0) return [...keys].map(key => this.translations.text(key)).join(' ');
      }
    }
    return this.translations.text(error.status === 400 ? 'invalid' : error.status === 404 ? 'missing' : 'conflict');
  }

  private balanceMessage(problem: Record<string, unknown>): string {
    const year = problem['year'];
    const requestedDays = problem['requestedDays'];
    const remainingDays = problem['remainingDays'];
    if (typeof year === 'number' && Number.isInteger(year) && year >= 1 && year <= 9999 &&
        typeof requestedDays === 'number' && Number.isInteger(requestedDays) && requestedDays > 0 &&
        typeof remainingDays === 'number' && Number.isInteger(remainingDays) && remainingDays >= 0)
      return this.translations.format('balanceDetails', { year, requestedDays, remainingDays });
    return this.translations.text('balanceError');
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
