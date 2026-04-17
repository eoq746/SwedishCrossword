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
record AnalyticsSummary(int TotalCompletions, int UniquePlayers, int DaysWithData, double AverageTime, double BestTime, double HintRate, double WordHintRate);
record DailyAnalytics(string Date, int Completions, int UniquePlayers, double AverageTime, double BestTime, double HintRate);
record TopPlayer(string Name, int GamesPlayed, double AverageTime, double BestTime, double HintRate);

// Personal stats for authenticated users
record UserStatsResponse(int TotalSolved, double AverageTime, double BestTime, int CurrentStreak, int BestStreak, List<UserSolveRecord> RecentSolves);
record UserSolveRecord(string Date, double Time, string? PuzzleSize, int HintsUsed, int WordHintsUsed);

// Puzzle check/hint request models
record PuzzleCheckRequest(string Token, string PuzzleDate, Dictionary<string, string> Cells, string? Size = null);
record PuzzleHintRequest(string Token, string PuzzleDate, int[][] Cells, string? Size = null);

// Friends
record FriendRequestDto(string Alias);
record FriendInfo(string Alias, string FriendId);
record FriendRequestInfo(string Id, string FromAlias, string ToAlias, string Direction, string Status, long CreatedAt);
record FriendsLeaderboardEntry(string Name, double Time, long? Timestamp, string? PuzzleHash, int HintsUsed = 0, int WordHintsUsed = 0);
