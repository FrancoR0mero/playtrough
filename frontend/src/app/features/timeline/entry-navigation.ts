import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';

type EntryLink = 'comments' | 'compose';
export const entryLinkId = (id: number, link: EntryLink) => `entry-${id}-${link}`;

export interface EntryOrigin {
  url: string;
  id: number;
  link: EntryLink;
  pages: number;
}

@Injectable({ providedIn: 'root' })
export class EntryNavigation {
  private readonly document = inject(DOCUMENT);
  private readonly router = inject(Router);
  private pending?: EntryOrigin;

  prepareReturn(origin: EntryOrigin): void {
    this.pending = { ...origin };
  }

  restore(hasMore: boolean, more: () => void, fallback: () => void): void {
    const origin = this.pending;
    if (!origin || origin.url !== this.router.url) return;
    const link = this.document.getElementById(entryLinkId(origin.id, origin.link));
    if (!link && hasMore && origin.pages > 1) {
      origin.pages--;
      more();
      return;
    }
    this.pending = undefined;
    if (link) link.focus();
    else fallback();
  }
}
