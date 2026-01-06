using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace AutoEdit.Media;

/// <summary>
/// Parser för PotPlayer bookmark-filer (.pbf).
/// PotPlayer sparar bokmärken i INI-format med tidsstämplar.
/// </summary>
public static class PotPlayerBookmarkParser
{
    public static List<double> ParseBookmarkFile(string videoPath)
    {
        // PotPlayer bookmark-filen har samma namn som videofilen men med .pbf-tillägg
        string bookmarkPath = videoPath + ".pbf";
        
        if (!File.Exists(bookmarkPath))
            return new List<double>();
        
        return ParseBookmarks(bookmarkPath);
    }
    
    public static List<double> ParseBookmarks(string pbfFilePath)
    {
        var timestamps = new List<double>();
        
        try
        {
            string[] lines = File.ReadAllLines(pbfFilePath);
            
            // Format: 0=00:01:23.456*Bookmark Name
            // eller:  1=123.456*Another Bookmark
            // eller:  2=65568*Example*<snapshot data>
            var timeRegex = new Regex(@"^\d+=(.+?)\*", RegexOptions.Compiled);
            
            foreach (string line in lines)
            {
                var match = timeRegex.Match(line);
                if (match.Success)
                {
                    string timeStr = match.Groups[1].Value.Trim();
                    double seconds = ParseTimeString(timeStr);
                    
                    if (seconds >= 0)
                        timestamps.Add(seconds);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse PotPlayer bookmarks: {ex.Message}");
        }
        
        timestamps.Sort();
        return timestamps;
    }
    
    private static double ParseTimeString(string timeStr)
    {
        var trimmed = timeStr.Trim();

        // PotPlayer lagrar ibland tid i millisekunder som heltal (t.ex. "65568").
        if (Regex.IsMatch(trimmed, @"^\d+$"))
        {
            if (long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var millis))
                return millis / 1000.0;
        }

        // Försök med direkt sekunder (t.ex. "123.456")
        if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out double seconds))
            return seconds;
        
        // Försök med HH:MM:SS.mmm format
        var timeSpanRegex = new Regex(@"^(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?$");
        var match = timeSpanRegex.Match(trimmed);
        
        if (match.Success)
        {
            int hours = int.Parse(match.Groups[1].Value);
            int minutes = int.Parse(match.Groups[2].Value);
            int secs = int.Parse(match.Groups[3].Value);
            int millis = match.Groups[4].Success ? int.Parse(match.Groups[4].Value.PadRight(3, '0')) : 0;
            
            return hours * 3600 + minutes * 60 + secs + millis / 1000.0;
        }
        
        return -1;
    }
}
