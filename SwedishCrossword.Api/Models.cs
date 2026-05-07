namespace SwedishCrossword.Api;

record ScoreSubmissionRequest(string Token, string Name, double Time, string PuzzleHash, string Date, string? PuzzleSize = null, int HintsUsed = 0, int WordHintsUsed = 0);
record ScoreRecord(string Name, double Time, long? Timestamp, string? PuzzleHash, int HintsUsed = 0, int WordHintsUsed = 0, string? UserId = null);
record LeaderboardHistoryRequest(string Date, LeaderboardEntry Entry, string? Token = null);
record LeaderboardEntry(string Name, double Time, long? Timestamp, string? PuzzleHash, string? PuzzleSize = null, int HintsUsed = 0, int WordHintsUsed = 0);
record HistoryRecord(string Name, double Time, long? Timestamp, string? PuzzleHash, string? PuzzleSize = null, int HintsUsed = 0, int WordHintsUsed = 0, string? UserId = null);

// Error response
record ErrorResponse(string Error);

// Alias request
record AliasRequest(string Alias);

// Analytics response models
record AnalyticsSummary(
    int TotalCompletions, int UniquePlayers, int RegisteredUsers, int CompletionsToday, int ActiveToday,
    double AverageTime, double BestTime, double HintUsageRate,
    Dictionary<string, SizeCompletions> PerSize);
record SizeCompletions(int Completions, double AverageTime);
record DailyAnalytics(string Date, int Completions, int UniquePlayers, double AverageTime, double BestTime);
record TopPlayer(string DisplayName, string RawName, bool Verified, int GamesPlayed, double AverageTime, double BestTime);

// Personal stats for authenticated users
record UserStatsResponse(
    int TotalSolved,
    double AverageTime,
    double BestTime,
    int CurrentStreak,
    int BestStreak,
    List<UserSolveRecord> RecentSolves,
    Dictionary<string, SizeStatsEntry>? PerSize = null,
    List<AchievementBadge>? Badges = null);
record SizeStatsEntry(int Count, double AverageTime, double BestTime, int CurrentStreak, int BestStreak);
record UserSolveRecord(string Date, double Time, string? PuzzleSize, int HintsUsed, int WordHintsUsed);
record AchievementBadge(string Id, string Name, string Description, string Icon, bool Unlocked);

// Puzzle check/hint request models
record PuzzleCheckRequest(string Token, string PuzzleDate, Dictionary<string, string> Cells, string? Size = null);
record PuzzleHintRequest(string Token, string PuzzleDate, int[][] Cells, string? Size = null);

// Friends
record FriendRequestDto(string Alias);
record FriendInfo(string Alias, string FriendId);
record FriendRequestInfo(string Id, string FromAlias, string ToAlias, string Direction, string Status, long CreatedAt);
record FriendsLeaderboardEntry(string Name, double Time, long? Timestamp, string? PuzzleHash, int HintsUsed = 0, int WordHintsUsed = 0);
record FriendChallengeCreateRequest(string FriendId, string Date);
record FriendChallengeRespondRequest(bool Accepted);
record FriendChallengeInfo(string Id, string FriendAlias, string Date, string Status, string Direction, long CreatedAt, long? RespondedAt);

// GDPR data export
record UserDataExport(string UserId, string? Alias, List<UserSolveRecord> History, List<UserScoreExport> Scores, List<string> Friends);
record UserScoreExport(string LeaderboardKey, string Name, double Time, long? Timestamp);

// Admin grant management
record GrantAdminRequest(string UserId);
record AdminGrantInfo(string UserId, string? Alias, long GrantedAt, string? GrantedByAlias);

// Clue quality flags
record ClueFlagCreateRequest(
    string? Word,
    string CurrentClue,
    int[][]? ClueCells,
    string? SuggestedClue,
    string? Reason,
    string? PuzzleDate,
    string? PuzzleSize,
    string? PuzzleHash);

record ClueFlagResolveRequest(
    string Status,
    string? UpdatedClue,
    string? AdminNote,
    string? ExpectedWordListVersion = null,
    bool RemoveClue = false);

record ClueFlagInfo(
    string Id,
    string Word,
    string CurrentClue,
    string? SuggestedClue,
    string? Reason,
    string Status,
    long CreatedAt,
    long? ReviewedAt,
    string? UpdatedClue,
    string? PuzzleDate,
    string? PuzzleSize,
    string? PuzzleHash,
    string? AdminNote,
    int ReportCount = 1);

record CreateCustomClueRequest(
    string Word,
    string Clue,
    string? Category = null,
    string? Difficulty = null);

record BlobWordListSyncRequest(bool DryRun = false);
record BlobWordListSyncConflictDetail(string Word, string Reason, string Resolution);
record BlobWordListSyncFileResult(
    string FileName,
    int Added,
    int Updated,
    int Removed,
    int Conflicts,
    bool Changed,
    List<BlobWordListSyncConflictDetail>? ConflictDetails = null,
    string? Error = null);
record BlobWordListSyncResponse(
    bool DryRun,
    int FilesProcessed,
    int FilesChanged,
    int TotalAdded,
    int TotalUpdated,
    int TotalRemoved,
    int TotalConflicts,
    List<BlobWordListSyncFileResult> Files);

// Admin user search
record AdminUserSearchResult(string UserId, string Alias, bool ExactMatch = false);

// Admin puzzle regeneration scheduler
record PuzzleRegenerationStatusResponse(
    string State,
    int PendingChangeCount,
    long? NotBeforeAt,
    long? LastQueuedAt,
    long? LastStartedAt,
    long? LastCompletedAt,
    string? LastError);
