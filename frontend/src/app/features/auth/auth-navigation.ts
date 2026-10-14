import { PRIMARY_OUTLET, Router } from '@angular/router';

export const DEFAULT_AUTHENTICATED_ROUTE = '/library';

export function authDestination(router: Router, returnUrl: string | null): string {
  if (
    !returnUrl ||
    !returnUrl.startsWith('/') ||
    returnUrl.startsWith('//') ||
    /[\\\s]/.test(returnUrl)
  ) {
    return DEFAULT_AUTHENTICATED_ROUTE;
  }
  try {
    const tree = router.parseUrl(returnUrl);
    const outlets = tree.root.children;
    const path = outlets[PRIMARY_OUTLET]?.segments[0]?.path.toLowerCase();
    if (path === 'auth' || Object.keys(outlets).some((outlet) => outlet !== PRIMARY_OUTLET)) {
      return DEFAULT_AUTHENTICATED_ROUTE;
    }
    return router.serializeUrl(tree);
  } catch {
    return DEFAULT_AUTHENTICATED_ROUTE;
  }
}
