namespace SwedishCrossword.Api;

record ScoreSubmissionRequest(string Token, string Name, double Time, string PuzzleHash, string Date, string? PuzzleSize = null, int HintsUsed = 0, int WordHintsUsed = 0);
record ScoreRecord(string Name, double Time, long? Timestamp, string? PuzzleHash, int HintsUsed = 0, int WordHintsUsed = 0);
record LeaderboardHistoryRequest(string Date, LeaderboardEntry Entry, string? Token = null);
record LeaderboardEntry(string Name, double Time, long? Timestamp, string? PuzzleHash, string? PuzzleSize = null, int HintsUsed = 0, int WordHintsUsed = 0);
record HistoryRecord(string Name, double Time, long? Timestamp, string? PuzzleHash, string? PuzzleSize = null, int HintsUsed = 0, int WordHintsUsed = 0);

// Puzzle check/hint request models
record PuzzleCheckRequest(string Token, string PuzzleDate, Dictionary<string, string> Cells, string? Size = null);
record PuzzleHintRequest(string Token, string PuzzleDate, int[][] Cells, string? Size = null);
