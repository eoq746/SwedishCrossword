import { fetchWithTimeout } from './http';

export type AppNotificationType =
  | 'friend-request'
  | 'challenge-invite'
  | 'challenge-response'
  | 'challenge-result'
  | 'achievement';

export interface AppNotification {
  id: string;
  type: AppNotificationType;
  title: string;
  description: string;
  href: string;
  createdAt: number;
}

const DEFAULT_TIMEOUT_MS = 20_000;
const OPT: RequestInit = { credentials: 'same-origin' };

async function apiFetch<T>(input: string, init?: RequestInit): Promise<T> {
  const res = await fetchWithTimeout(input, {
    ...OPT,
    ...init,
    timeoutMs: DEFAULT_TIMEOUT_MS,
    retries: 1,
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({})) as Record<string, unknown>;
    throw new Error((body['error'] as string | undefined) ?? `HTTP ${res.status}`);
  }
  return res.json() as Promise<T>;
}

export function fetchUnreadNotifications(): Promise<AppNotification[]> {
  return apiFetch('/api/notifications/');
}

export function markNotificationRead(notificationId: string): Promise<{ ok: boolean }> {
  return apiFetch(`/api/notifications/${encodeURIComponent(notificationId)}/read`, { method: 'POST' });
}

export function markNotificationsRead(notificationIds: string[]): Promise<{ ok: boolean; changed: number }> {
  return apiFetch('/api/notifications/read', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ notificationIds }),
  });
}
