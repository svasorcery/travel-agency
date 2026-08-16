import { Component } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { TravelButton } from './button';

@Component({
  imports: [TravelButton],
  template: `
    <button travelButton>Search</button>
    <button travelButton type="submit">Book</button>
    <button travelButton disabled>Unavailable</button>
  `,
})
class ButtonHost {}

describe('TravelButton', () => {
  let fixture: ComponentFixture<ButtonHost>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ButtonHost] }).compileComponents();
    fixture = TestBed.createComponent(ButtonHost);
    fixture.detectChanges();
  });

  it('defaults to a non-submitting button without overriding an explicit type', () => {
    const buttons = fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>;
    const [search, book] = Array.from(buttons);

    expect(search.type).toBe('button');
    expect(book.type).toBe('submit');
  });

  it('applies the owned Helm recipe metadata and styles', () => {
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;

    expect(button.getAttribute('data-slot')).toBe('button');
    expect(button.classList).toContain('bg-primary');
    expect(button.classList).toContain('focus-visible:ring-3');
  });

  it('exposes Spartan Brain disabled behavior through the Travel facade', () => {
    const buttons = fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>;
    const unavailable = buttons.item(2);

    expect(unavailable.disabled).toBe(true);
    expect(unavailable.getAttribute('data-disabled')).toBe('true');
    expect(unavailable.tabIndex).toBe(-1);
  });
});
