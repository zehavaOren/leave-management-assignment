import { HttpErrorResponse } from '@angular/common/http';

// Maps API errors to localized user-facing messages ($localize + XLIFF).
export function leaveRequestErrorMessage(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return $localize`:@@requestError:The request failed. Please retry.`;
  if (error.status === 0) return $localize`:@@networkError:Cannot reach the server. Check the connection and retry.`;
  if (error.status === 429) return $localize`:@@tooManyRequests:Too many requests. Wait a minute and try again.`;
  if (![400, 404, 409].includes(error.status)) return $localize`:@@requestError:The request failed. Please retry.`;

  // Treat the response body as untrusted data; never display a raw server title or stack trace.
  const problem: unknown = error.error;
  if (isRecord(problem)) {
    const code = problem['code'];
    if (code === 'insufficient_balance') return balanceMessage(problem);
    const messages: Readonly<Record<string, string>> = {
      overlapping_request: $localize`:@@overlappingRequest:These dates overlap another leave request for this employee. Choose different dates.`,
      invalid_employee: $localize`:@@invalidEmployee:Choose a valid employee.`,
      leave_type_required: $localize`:@@chooseType:Choose a leave type`,
      invalid_leave_type: $localize`:@@invalidLeaveType:The selected leave type is invalid. Choose Vacation, Sick or Unpaid.`,
      start_date_required: $localize`:@@startDateInvalid:Enter a valid start date.`,
      end_date_required: $localize`:@@endDateInvalid:Enter a valid end date.`,
      invalid_date_range: $localize`:@@dateOrder:End date must be on or after start date.`,
      employee_not_found: $localize`:@@employeeMissing:The selected employee no longer exists. Refresh the employee list.`,
      request_not_found: $localize`:@@requestMissing:This leave request no longer exists. Refresh the list.`,
      request_already_approved: $localize`:@@alreadyApproved:This request has already been approved.`,
      request_rejected: $localize`:@@requestRejected:This request has been rejected and cannot be approved.`
    };
    if (typeof code === 'string' && Object.prototype.hasOwnProperty.call(messages, code)) return messages[code];
    if (error.status === 400 && isRecord(problem['errors'])) {
      const messages = new Set<string>();
      for (const field of Object.keys(problem['errors'])) {
        const name = field.toLowerCase();
        if (name.includes('startdate')) messages.add($localize`:@@startDateInvalid:Enter a valid start date.`);
        else if (name.includes('enddate')) messages.add($localize`:@@endDateInvalid:Enter a valid end date.`);
        else if (name.includes('type')) messages.add($localize`:@@invalidLeaveType:The selected leave type is invalid. Choose Vacation, Sick or Unpaid.`);
        else if (name.includes('employeeid')) messages.add($localize`:@@invalidEmployee:Choose a valid employee.`);
      }
      if (messages.size > 0) return [...messages].join(' ');
    }
  }
  if (error.status === 400) return $localize`:@@invalid:Some request fields are missing or invalid. Review the form.`;
  if (error.status === 404) return $localize`:@@missing:The employee or request no longer exists.`;
  return $localize`:@@conflict:The request changed. Refresh to see its latest status and try again.`;
}

function balanceMessage(problem: Record<string, unknown>): string {
  const year = problem['year'];
  const requestedDays = problem['requestedDays'];
  const remainingDays = problem['remainingDays'];
  if (typeof year === 'number' && Number.isInteger(year) && year >= 1 && year <= 9999 &&
      typeof requestedDays === 'number' && Number.isInteger(requestedDays) && requestedDays > 0 &&
      typeof remainingDays === 'number' && Number.isInteger(remainingDays) && remainingDays >= 0)
    return $localize`:@@balanceDetails:Not enough vacation balance for ${year}:year:: requested ${requestedDays}:requestedDays: days, available ${remainingDays}:remainingDays: days.`;
  return $localize`:@@balanceError:There is not enough vacation balance for this request.`;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
