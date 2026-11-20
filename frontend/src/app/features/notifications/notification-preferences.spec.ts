import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, Subject, throwError } from 'rxjs';
import { NotificationPreferencesComponent } from './notification-preferences';
import { NotificationPreference, NotificationsApi } from './notifications-api';

const groups = { category: 'groups', enabled: true, editable: true };
const settings = [
  { category: 'friends', enabled: false, editable: true },
  groups,
  { category: 'account', enabled: true, editable: false },
  { category: 'moderation', enabled: true, editable: false },
];

describe('NotificationPreferencesComponent', () => {
  function setup(initial: Observable<NotificationPreference[]> = of(settings)) {
    const preferences = vi.fn().mockReturnValue(initial);
    const setPreference = vi.fn();
    TestBed.configureTestingModule({
      imports: [NotificationPreferencesComponent],
      providers: [
        provideRouter([]),
        { provide: NotificationsApi, useValue: { preferences, setPreference } },
      ],
    });
    const fixture = TestBed.createComponent(NotificationPreferencesComponent);
    const root = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    const groupsInput = () => root.querySelector<HTMLInputElement>('input[aria-label="Grupos"]')!;
    return { fixture, root, preferences, setPreference, groupsInput };
  }

  it('shows editable social preferences, explains essential categories and saves a change', () => {
    const { fixture, root, setPreference, groupsInput } = setup();
    expect(root.querySelectorAll('input[type="checkbox"]')).toHaveLength(2);
    expect(root.textContent).toContain('Los avisos de cuenta y moderación están siempre activos');
    expect(root.querySelector('input[aria-label="Moderación"]')).toBeNull();
    expect(root.querySelector('a')?.getAttribute('href')).toBe('/notifications');
    const response = new Subject<NotificationPreference>();
    setPreference.mockReturnValue(response);

    groupsInput().click();
    fixture.detectChanges();
    expect(setPreference).toHaveBeenCalledWith('groups', false);
    expect(root.querySelector('fieldset')?.disabled).toBe(true);
    response.next({ ...groups, enabled: false });
    response.complete();
    fixture.detectChanges();

    expect(groupsInput().checked).toBe(false);
    expect(root.querySelector('fieldset')?.disabled).toBe(false);
    expect(root.textContent).toContain('Avisos de grupos desactivados.');
  });

  it('restores the checkbox after a failure and lets the user retry the change', () => {
    const { fixture, root, setPreference, groupsInput } = setup();
    setPreference
      .mockReturnValueOnce(throwError(() => new Error('offline')))
      .mockReturnValueOnce(of({ ...groups, enabled: false }));
    groupsInput().click();
    fixture.detectChanges();
    expect(groupsInput().checked).toBe(true);
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('No pudimos guardar');

    groupsInput().click();
    fixture.detectChanges();
    expect(setPreference).toHaveBeenCalledTimes(2);
    expect(groupsInput().checked).toBe(false);
    expect(root.querySelector('[role="alert"]')).toBeNull();
  });

  it('can recover from an initial load failure', () => {
    const { fixture, root, preferences, groupsInput } = setup(
      throwError(() => new Error('offline')),
    );
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('No pudimos cargar');
    preferences.mockReturnValueOnce(of(settings));
    root.querySelector('button')!.click();
    fixture.detectChanges();
    expect(groupsInput().checked).toBe(true);
    expect(root.querySelector('[role="alert"]')).toBeNull();
  });
});
