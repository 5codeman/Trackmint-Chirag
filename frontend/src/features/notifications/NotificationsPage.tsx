import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bell, CheckCheck } from "lucide-react";
import { AppShell } from "../../components/layout/AppShell";
import { Card } from "../../components/common/Card";
import { useToast } from "../../components/common/ToastProvider";
import { api } from "../../services/api/client";
import type { Notification } from "../../types/models";

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en-IN", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}

function typeLabel(type: string) {
  return type
    .split("_")
    .filter(Boolean)
    .map((part) => part[0]?.toUpperCase() + part.slice(1))
    .join(" ");
}

export function NotificationsPage() {
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const { data = [], isLoading } = useQuery({
    queryKey: ["notifications"],
    queryFn: async () => (await api.get<Notification[]>("/notifications")).data,
  });

  const markReadMutation = useMutation({
    mutationFn: async (id: string) => (await api.put<Notification>(`/notifications/${id}/read`)).data,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["notifications"] });
      queryClient.invalidateQueries({ queryKey: ["notifications-unread-count"] });
      showToast("Notification marked as read.");
    },
    onError: () => showToast("Could not update notification.", "error"),
  });

  const markAllReadMutation = useMutation({
    mutationFn: async () => (await api.put<{ updated: number }>("/notifications/read-all")).data,
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ["notifications"] });
      queryClient.invalidateQueries({ queryKey: ["notifications-unread-count"] });
      showToast(result.updated > 0 ? "All notifications marked as read." : "No unread notifications.");
    },
    onError: () => showToast("Could not update notifications.", "error"),
  });

  const unreadCount = data.filter((item) => !item.isRead).length;

  return (
    <AppShell title="Notifications">
      <Card
        title="Notification Center"
        subtitle="Event-driven alerts created by TrackMint services."
        actions={
          <button
            type="button"
            className="ghost-button"
            disabled={unreadCount === 0 || markAllReadMutation.isPending}
            onClick={() => markAllReadMutation.mutate()}
          >
            <CheckCheck size={16} />
            <span>Mark all read</span>
          </button>
        }
      >
        <div className="notification-summary">
          <div>
            <span>Unread</span>
            <strong>{unreadCount}</strong>
          </div>
          <div>
            <span>Total</span>
            <strong>{data.length}</strong>
          </div>
        </div>

        <div className="notification-list">
          {data.map((notification) => (
            <article
              key={notification.id}
              className={`notification-item ${notification.isRead ? "notification-item--read" : "notification-item--unread"}`.trim()}
            >
              <div className="notification-item__icon">
                <Bell size={18} />
              </div>
              <div className="notification-item__content">
                <div className="notification-item__header">
                  <div>
                    <span className="status-badge status-badge--active">{typeLabel(notification.type)}</span>
                    <h3>{notification.title}</h3>
                  </div>
                  <time>{formatDate(notification.createdAtUtc)}</time>
                </div>
                <p>{notification.message}</p>
              </div>
              {!notification.isRead && (
                <button
                  type="button"
                  className="ghost-button"
                  disabled={markReadMutation.isPending}
                  onClick={() => markReadMutation.mutate(notification.id)}
                >
                  Mark read
                </button>
              )}
            </article>
          ))}

          {!isLoading && data.length === 0 && (
            <div className="empty-state">
              Notifications from budget alerts, completed goals, recurring transactions, and account events will appear here.
            </div>
          )}

          {isLoading && <p className="muted-copy">Loading notifications...</p>}
        </div>
      </Card>
    </AppShell>
  );
}
