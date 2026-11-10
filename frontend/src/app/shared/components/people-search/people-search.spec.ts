import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { PeopleSearchComponent } from './people-search';
import { PeopleSearchState } from './people-search-state';

@Component({ template: '' })
class EmptyPage {}

describe('Header people search', () => {
  it('keeps a draft on other screens, submits encoded queries and hides the icon only while typing', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'games', component: EmptyPage },
          { path: 'people', component: EmptyPage },
        ]),
      ],
    });
    await RouterTestingHarness.create('/games?q=Hades');
    const fixture = TestBed.createComponent(PeopleSearchComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const input = root.querySelector('input')!;
    const router = TestBed.inject(Router);
    expect(input.value).toBe('');
    expect(root.querySelector('svg')).not.toBeNull();
    input.value = 'be&a';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(root.querySelector('svg')).toBeNull();
    expect(router.url).toBe('/games?q=Hades');
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();
    expect(router.url).toBe('/people?q=be%26a');
    input.value = '';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(root.querySelector('svg')).not.toBeNull();
    fixture.destroy();
  });

  it('updates the icon when the results page restores a URL query without emitting input', () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(PeopleSearchComponent);
    fixture.detectChanges();
    const state = TestBed.inject(PeopleSearchState);
    state.restore('bea');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('input').value).toBe('bea');
    expect(fixture.nativeElement.querySelector('svg')).toBeNull();
    state.restore('');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('svg')).not.toBeNull();
    fixture.destroy();
  });

  it('blocks short submissions and clears the invalid state when editing', () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(PeopleSearchComponent);
    const state = TestBed.inject(PeopleSearchState);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate');
    fixture.detectChanges();
    state.control.setValue('b');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('input').getAttribute('aria-invalid')).toBe('true');
    state.control.setValue('be');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('input').getAttribute('aria-invalid')).toBe('false');
    fixture.destroy();
  });
});
