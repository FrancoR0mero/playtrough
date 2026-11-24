import { inject, Injectable } from '@angular/core';
import { HubConnection } from '@microsoft/signalr';
import { RealtimeConnection } from '../../core/realtime/realtime-connection';

export interface ChatMessage {
  id: string;
  groupId: number;
  authorId: number;
  username: string;
  text: string | null;
  createdAt: string;
  removedAt?: string | null;
  delivered?: boolean;
  clientMessageId?: string | null;
}

export interface ChatParticipant {
  userId: number;
  username: string;
  isTyping: boolean;
}

@Injectable({ providedIn: 'root' })
export class GroupChatConnection {
  private readonly realtime = inject(RealtimeConnection);

  create(): HubConnection {
    return this.realtime.create('/hubs/chat');
  }
}
