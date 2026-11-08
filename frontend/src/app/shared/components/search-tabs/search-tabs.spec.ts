import { Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { SearchTabsComponent } from './search-tabs';

@Component({
  imports: [SearchTabsComponent],
  template: '<app-search-tabs [active]="active" [query]="query" />',
})
class SearchHost {
  private readonly route = inject(ActivatedRoute);
  readonly active = this.route.snapshot.data['active'] as 'games' | 'people';
  readonly query = this.route.snapshot.queryParamMap.get('q') ?? '';
}

describe('SearchTabs', () => {
  it.each(['games', 'people'] as const)(
    'preserves q when navigating away from %s and marks the current route',
    async (active) => {
      TestBed.configureTestingModule({
        providers: [
          provideRouter([
            { path: 'games', component: SearchHost, data: { active: 'games' } },
            { path: 'people', component: SearchHost, data: { active: 'people' } },
          ]),
        ],
      });
      const harness = await RouterTestingHarness.create(`/${active}?q=ana%20%26%20bea`);
      const links = harness.routeNativeElement!.querySelectorAll<HTMLAnchorElement>('a');
      const index = active === 'games' ? 0 : 1;
      expect(links[index].getAttribute('aria-current')).toBe('page');
      expect(links[1 - index].hasAttribute('aria-current')).toBe(false);
      links[1 - index].click();
      await harness.fixture.whenStable();
      const router = TestBed.inject(Router);
      expect(router.url.split('?')[0]).toBe(active === 'games' ? '/people' : '/games');
      expect(router.parseUrl(router.url).queryParams['q']).toBe('ana & bea');
    },
  );
});
