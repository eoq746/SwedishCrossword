import { useCallback, useEffect, useState } from 'react';
import {
  fetchUnreadNotifications,
  markNotificationRead,
  markNotificationsRead,
  type AppNotification,
} from '../api/notifications';

interface UseNotificationsResult {
  notifications: AppNotification[];
  unreadNotifications: AppNotification[];
  unreadCount: number;
  loading: boolean;
  markAsRead: (notificationId: string) => void;
  markAllAsRead: () => void;
  refresh: () => Promise<void>;
}

export function useNotifications(userId: string | null): UseNotificationsResult {
  const [notifications, setNotifications] = useState<AppNotification[]>([]);
  const [loading, setLoading] = useState(false);

  const refresh = useCallback(async () => {
    if (!userId) {
      setNotifications([]);
      setLoading(false);
      return;
    }

    setLoading(true);
    try {
      const unread = await fetchUnreadNotifications();
      setNotifications(unread);
    } catch {
      setNotifications([]);
    } finally {
      setLoading(false);
    }
  }, [userId]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const markAsRead = useCallback((notificationId: string) => {
    if (!userId || !notificationId) return;

    setNotifications(prev => prev.filter(notification => notification.id !== notificationId));
    void markNotificationRead(notificationId).catch(() => {
      void refresh();
    });
  }, [refresh, userId]);

  const markAllAsRead = useCallback(() => {
    if (!userId || notifications.length === 0) return;

    const ids = notifications.map(notification => notification.id);
    setNotifications([]);
    void markNotificationsRead(ids).catch(() => {
      void refresh();
    });
  }, [notifications, refresh, userId]);

  return {
    notifications,
    unreadNotifications: notifications,
    unreadCount: notifications.length,
    loading,
    markAsRead,
    markAllAsRead,
    refresh,
  };
}
