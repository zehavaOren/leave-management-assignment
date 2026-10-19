import { clearTranslations, loadTranslations } from '@angular/localize';
import { HttpErrorResponse } from '@angular/common/http';
import { leaveRequestErrorMessage } from './leave-request-errors';

describe('Native Angular i18n error interpolation', () => {
  afterEach(() => clearTranslations());

  it('translates balance details and preserves named placeholders when their order changes', () => {
    loadTranslations({
      balanceDetails: 'לשנת {$year} זמינים {$remainingDays} ימים; התבקשו {$requestedDays} ימים.'
    });
    const message = leaveRequestErrorMessage(new HttpErrorResponse({ status: 400,
      error: { code: 'insufficient_balance', year: 2026, requestedDays: 3, remainingDays: 2 } }));
    expect(message).toBe('לשנת 2026 זמינים 2 ימים; התבקשו 3 ימים.');
  });
});
