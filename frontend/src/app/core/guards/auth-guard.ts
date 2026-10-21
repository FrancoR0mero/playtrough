import { CanActivateFn } from '@angular/router';
import { requireSession } from './session-access';

export const authGuard: CanActivateFn = (_route, state) => requireSession(state.url);
