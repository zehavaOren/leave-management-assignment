import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { AppComponent } from './app.component';

// Smoke test proving the test runner is wired up. Feel free to replace it.
describe('AppComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  it('creates the app', () => {
    const fixture = TestBed.createComponent(AppComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });
  it('sets document language and direction for the Hebrew locale', () => {
    TestBed.overrideProvider(LOCALE_ID, { useValue: 'he' });
    const fixture = TestBed.createComponent(AppComponent);
    expect(document.documentElement.lang).toBe('he');
    expect(document.documentElement.dir).toBe('rtl');
    fixture.destroy();
    document.documentElement.lang = 'en-US';
    document.documentElement.dir = 'ltr';
  });
});
