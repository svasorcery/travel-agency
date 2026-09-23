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

  it('renders the Travel Platform status shell without the Nx starter', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    const statusLink = compiled.querySelector<HTMLAnchorElement>('a[href="/status"]');

    expect(statusLink?.textContent?.trim()).toBe('Travel Platform');
    expect(compiled.querySelector('router-outlet')).not.toBeNull();
    expect(compiled.querySelector('app-nx-welcome')).toBeNull();
    expect(compiled.textContent).not.toContain('Welcome web');
  });
});
