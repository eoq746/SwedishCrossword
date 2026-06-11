namespace SwedishCrossword.Api;

/// <summary>
/// Score (per-puzzle leaderboard) read/write operations.
/// </summary>
internal interface IScoreStore
{
    Task<string> GetCurrentAsync();
    Task<List<ScoreRecord>> AppendScoreAsync(string leaderboardKey, ScoreRecord entry);
    Task PruneOldEntriesAsync();
}

/// <summary>
/// Historical solve archive operations.
/// </summary>
internal interface IHistoryStore
{
    Task AppendHistoryAsync(string date, HistoryRecord record);
    Task<Dictionary<string, List<HistoryRecord>>> GetHistoryAsync(int days);
}

/// <summary>
/// Authenticated user profile, alias, stats and GDPR data operations.
/// </summary>
internal interface IUserProfileStore
{
    /// <summary>
    /// Resolves the caller to a canonical user ID and migrates any legacy rows.
    /// </summary>
    Task<string> ResolveCanonicalUserIdAsync(string canonicalUserId, string? legacyUserId);
    Task<string?> GetAliasAsync(string userId);
    Task<bool> IsAliasAvailableAsync(string alias, string? excludeUserId = null);
    Task<bool> SetAliasAsync(string userId, string alias);
    Task<string?> GetUserIdByAliasAsync(string alias);
    Task<List<AdminUserSearchResult>> SearchUsersByAliasAsync(string query, int limit = 10);
    Task<UserStatsResponse> GetUserStatsAsync(string userId);
    Task<UserDataExport> ExportUserDataAsync(string userId);
    Task DeleteUserDataAsync(string userId);
}

/// <summary>
/// Friend graph and friends-only leaderboard operations.
/// </summary>
internal interface IFriendStore
{
    Task<(bool Success, string Error)> SendFriendRequestAsync(string fromUserId, string toUserId);
    Task<bool> AcceptFriendRequestAsync(string requestId, string currentUserId);
    Task<bool> DeclineFriendRequestAsync(string requestId, string currentUserId);
    Task<bool> RemoveFriendAsync(string currentUserId, string friendshipId);
    Task<List<FriendInfo>> GetFriendsAsync(string userId);
    Task<List<FriendRequestInfo>> GetPendingRequestsAsync(string userId);
    Task<List<FriendsLeaderboardEntry>> GetFriendsLeaderboardAsync(string userId, string date, string? puzzleHash = null);
    Task<(bool Success, string Error)> CreateChallengeAsync(string fromUserId, string friendRequestId, string date, string puzzleSize);
    Task<FriendChallengesCreateResponse> CreateChallengesAsync(string fromUserId, IReadOnlyCollection<string> friendRequestIds, string date, string puzzleSize);
    Task<List<FriendChallengeInfo>> GetChallengesAsync(string userId, bool expiredOnly = false);
    Task<bool> RespondToChallengeAsync(string challengeId, string userId, bool accepted);
}

/// <summary>
/// Aggregate analytics queries (admin-only).
/// </summary>
internal interface IAnalyticsStore
{
    Task<AnalyticsSummary> GetAnalyticsSummaryAsync();
    Task<List<DailyAnalytics>> GetDailyAnalyticsAsync(int days);
    Task<List<TopPlayer>> GetTopPlayersAsync(int limit);
}

/// <summary>
/// Dynamic admin grant operations. Config-based admins are checked separately.
/// </summary>
internal interface IAdminStore
{
    Task<bool> IsAdminAsync(string userId);
    Task GrantAdminAsync(string userId, string grantedByUserId);
    Task RevokeAdminAsync(string userId);
    Task<List<AdminGrantInfo>> ListGrantedAdminsAsync();
}

/// <summary>
/// Clue quality flag queue operations.
/// </summary>
internal interface IClueFlagStore
{
    Task<string> CreateClueFlagAsync(ClueFlagCreateRequest request, string? createdByUserId);
    Task<List<ClueFlagInfo>> ListPendingClueFlagsAsync(int limit);
    Task<ClueFlagInfo?> GetClueFlagAsync(string id);
    Task<bool> ResolveClueFlagAsync(string id, string status, string? updatedClue, string? adminNote, string resolvedByUserId);
}

/// <summary>
/// Per-user unread notifications operations.
/// </summary>
internal interface INotificationStore
{
    Task<List<AppNotification>> GetUnreadNotificationsAsync(string userId);
    Task<bool> MarkNotificationReadAsync(string userId, string notificationId);
    Task<int> MarkNotificationsReadAsync(string userId, IReadOnlyCollection<string> notificationIds);
}
