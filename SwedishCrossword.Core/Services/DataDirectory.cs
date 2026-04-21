namespace SwedishCrossword.Services;

/// <summary>
/// Provides a shared method to locate the SwedishCrossword project's Data directory.
/// Works from output directories, bin folders, project directories, and published apps.
/// </summary>
public static class DataDirectory
{
    /// <summary>
    /// Gets the path to the Data directory, working from either the output directory or project directory.
    /// This ensures all applications (main, tests, tools) use the same Data folder.
    /// </summary>
    public static string GetPath()
    {
        // Walk up the directory tree looking for the SwedishCrossword project's Data folder
        var currentDir = AppContext.BaseDirectory;

        while (!string.IsNullOrEmpty(currentDir))
        {
            // Look for SwedishCrossword/Data (the source project's data folder)
            var projectDataPath = Path.Combine(currentDir, "SwedishCrossword", "Data");
            if (Directory.Exists(projectDataPath))
                return projectDataPath;

            // Check if we're directly in a bin folder of SwedishCrossword project
            // e.g., SwedishCrossword/bin/Debug/net10.0/
            if (currentDir.Contains(Path.Combine("SwedishCrossword", "bin")))
            {
                // Walk up to find the SwedishCrossword project root
                var parts = currentDir.Split(Path.DirectorySeparatorChar);
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    if (parts[i] == "SwedishCrossword" && i > 0)
                    {
                        var projectRoot = string.Join(Path.DirectorySeparatorChar.ToString(), parts.Take(i + 1));
                        var dataPath = Path.Combine(projectRoot, "Data");
                        if (Directory.Exists(dataPath))
                            return dataPath;
                    }
                }
            }

            // Check if we're in the SwedishCrossword directory directly (has the csproj)
            var csprojPath = Path.Combine(currentDir, "SwedishCrossword.csproj");
            var directDataPath = Path.Combine(currentDir, "Data");
            if (File.Exists(csprojPath) && Directory.Exists(directDataPath))
                return directDataPath;

            var parent = Directory.GetParent(currentDir);
            if (parent == null) break;
            currentDir = parent.FullName;
        }

        // Last resort: check if output directory has Data (for published apps)
        var outputDataPath = Path.Combine(AppContext.BaseDirectory, "Data");
        if (Directory.Exists(outputDataPath))
            return outputDataPath;

        // Fallback: Use the SwedishCrossword project's Data folder, creating it if needed
        var solutionDir = FindSolutionDirectory();
        if (solutionDir != null)
        {
            var projectData = Path.Combine(solutionDir, "SwedishCrossword", "Data");
            Directory.CreateDirectory(projectData);
            return projectData;
        }

        // Ultimate fallback: create in output directory
        Directory.CreateDirectory(outputDataPath);
        return outputDataPath;
    }

    /// <summary>
    /// Finds the solution directory by looking for a .sln file.
    /// </summary>
    private static string? FindSolutionDirectory()
    {
        var currentDir = AppContext.BaseDirectory;

        while (!string.IsNullOrEmpty(currentDir))
        {
            var slnFiles = Directory.GetFiles(currentDir, "*.sln");
            if (slnFiles.Length > 0)
                return currentDir;

            var parent = Directory.GetParent(currentDir);
            if (parent == null) break;
            currentDir = parent.FullName;
        }

        return null;
    }
}
