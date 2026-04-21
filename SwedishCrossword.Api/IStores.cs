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
    Task<string?> GetAliasAsync(string userId);
    Task<bool> IsAliasAvailableAsync(string alias, string? excludeUserId = null);
    Task<bool> SetAliasAsync(string userId, string alias);
    Task<string?> GetUserIdByAliasAsync(string alias);
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
