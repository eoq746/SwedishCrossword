namespace SwedishCrossword.Api;

record ScoreSubmissionRequest(string Token, string Name, double Time, string PuzzleHash, string Date, string? PuzzleSize = null, int HintsUsed = 0, int WordHintsUsed = 0);
record ScoreRecord(string Name, double Time, long? Timestamp, string? PuzzleHash, int HintsUsed = 0, int WordHintsUsed = 0);
record LeaderboardHistoryRequest(string Date, LeaderboardEntry Entry, string? Token = null);
record LeaderboardEntry(string Name, double Time, long? Timestamp, string? PuzzleHash, string? PuzzleSize = null, int HintsUsed = 0, int WordHintsUsed = 0);
record HistoryRecord(string Name, double Time, long? Timestamp, string? PuzzleHash, string? PuzzleSize = null, int HintsUsed = 0, int WordHintsUsed = 0);

// Error response
record ErrorResponse(string Error);

// Analytics response models
record AnalyticsSummary(int TotalCompletions, int UniquePlayers, int DaysWithData, double AverageTime, double BestTime, double HintRate, double WordHintRate);
record DailyAnalytics(string Date, int Completions, int UniquePlayers, double AverageTime, double BestTime, double HintRate);
record TopPlayer(string Name, int GamesPlayed, double AverageTime, double BestTime, double HintRate);

// Puzzle check/hint request models
record PuzzleCheckRequest(string Token, string PuzzleDate, Dictionary<string, string> Cells, string? Size = null);
record PuzzleHintRequest(string Token, string PuzzleDate, int[][] Cells, string? Size = null);
