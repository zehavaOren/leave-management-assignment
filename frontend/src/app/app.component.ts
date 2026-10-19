import { Component, LOCALE_ID, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { Title } from '@angular/platform-browser';
import { LeaveRequestsComponent } from './leave-requests/leave-requests.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [LeaveRequestsComponent],
  template: `
    <h1 i18n="@@appTitle">Leave Management</h1>
    <app-leave-requests></app-leave-requests>
  `
})
export class AppComponent {
  constructor() {
    const document = inject(DOCUMENT);
    const locale = inject(LOCALE_ID);
    document.documentElement.lang = locale;
    document.documentElement.dir = locale.startsWith('he') ? 'rtl' : 'ltr';
    inject(Title).setTitle($localize`:@@appTitle:Leave Management`);
  }
}
