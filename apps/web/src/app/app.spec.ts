import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('offers flight search as the main path and keeps system status available', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    const searchLink = compiled.querySelector<HTMLAnchorElement>('a[href="/flights"]');
    const statusLink = compiled.querySelector<HTMLAnchorElement>('a[href="/status"]');

    expect(searchLink?.textContent).toContain('Travel Platform');
    expect(statusLink?.textContent).toContain('Статус');
    expect(compiled.querySelector('router-outlet')).not.toBeNull();
    expect(compiled.querySelector('app-nx-welcome')).toBeNull();
    expect(compiled.textContent).not.toContain('Welcome web');
  });
});
