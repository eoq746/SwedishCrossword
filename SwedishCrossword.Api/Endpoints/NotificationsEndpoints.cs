using System.Security.Claims;

namespace SwedishCrossword.Api.Endpoints;

internal static class NotificationsEndpoints
{
    internal static WebApplication MapNotificationsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization().RequireRateLimiting("friends");

        group.MapGet("/", async (ClaimsPrincipal user, INotificationStore notificationStore, IUserProfileStore profileStore) =>
        {
            var userId = await AuthEndpoints.ResolveUserIdAsync(user, profileStore);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var notifications = await notificationStore.GetUnreadNotificationsAsync(userId);
            return Results.Ok(notifications);
        });

        group.MapPost("/{notificationId}/read", async (string notificationId, ClaimsPrincipal user, INotificationStore notificationStore, IUserProfileStore profileStore) =>
        {
            var userId = await AuthEndpoints.ResolveUserIdAsync(user, profileStore);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            if (string.IsNullOrWhiteSpace(notificationId))
                return Results.BadRequest(new ErrorResponse("Notis-id saknas"));

            await notificationStore.MarkNotificationReadAsync(userId, notificationId);
            return Results.Ok(new { ok = true });
        });

        group.MapPost("/read", async (NotificationsMarkReadRequest body, ClaimsPrincipal user, INotificationStore notificationStore, IUserProfileStore profileStore) =>
        {
            var userId = await AuthEndpoints.ResolveUserIdAsync(user, profileStore);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            if (body.NotificationIds is null || body.NotificationIds.Length == 0)
                return Results.BadRequest(new ErrorResponse("Inga notis-id skickades"));

            var changed = await notificationStore.MarkNotificationsReadAsync(userId, body.NotificationIds);
            return Results.Ok(new { ok = true, changed });
        });

        return app;
    }
}
