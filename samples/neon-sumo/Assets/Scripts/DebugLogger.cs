using System;
using System.Globalization;
using System.Text;
using UnityEngine;

public static class DebugLogger
{
    public static bool Enabled { get; set; } = true;
    public static bool IncludeRealtime { get; set; } = true;
    /// <summary>When true, the prefix wall-clock segment uses <see cref="DateTime.UtcNow"/> (cheaper than local <see cref="DateTime.Now"/>).</summary>
    public static bool IncludeWallClock { get; set; } = true;
    public static string WallClockFormat { get; set; } = "HH:mm:ss.fff";
    public static string RealtimeFormat { get; set; } = "0.000";
    public static string Prefix { get; set; } = string.Empty;

    private enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    public static void Log(string message, string tag = null, UnityEngine.Object context = null)
        => Write(LogLevel.Info, message, tag, context);

    public static void Log(Func<string> messageFactory, string tag = null, UnityEngine.Object context = null)
    {
        if (!Enabled || messageFactory == null) return;
        Log(messageFactory(), tag, context);
    }

    public static void LogWarning(string message, string tag = null, UnityEngine.Object context = null)
        => Write(LogLevel.Warning, message, tag, context);

    public static void LogWarning(Func<string> messageFactory, string tag = null, UnityEngine.Object context = null)
    {
        if (!Enabled || messageFactory == null) return;
        LogWarning(messageFactory(), tag, context);
    }

    public static void LogError(string message, string tag = null, UnityEngine.Object context = null)
        => Write(LogLevel.Error, message, tag, context);

    public static void LogError(Func<string> messageFactory, string tag = null, UnityEngine.Object context = null)
    {
        if (!Enabled || messageFactory == null) return;
        LogError(messageFactory(), tag, context);
    }

    public static void LogException(Exception exception, string tag = null, UnityEngine.Object context = null)
    {
        if (!Enabled || exception == null) return;

        // Single entry: Debug.LogException outputs type, message, and stack trace.
        // Tag/prefix are not included since Unity's API doesn't support custom prefixes.
        if (context != null)
            Debug.LogException(exception, context);
        else
            Debug.LogException(exception);
    }

    private static void Write(LogLevel level, string message, string tag, UnityEngine.Object context)
    {
        if (!Enabled) return;

        var fullMessage = BuildPrefix(tag) + message;

        switch (level)
        {
            case LogLevel.Info:
                if (context != null) Debug.Log(fullMessage, context);
                else Debug.Log(fullMessage);
                break;

            case LogLevel.Warning:
                if (context != null) Debug.LogWarning(fullMessage, context);
                else Debug.LogWarning(fullMessage);
                break;

            case LogLevel.Error:
                if (context != null) Debug.LogError(fullMessage, context);
                else Debug.LogError(fullMessage);
                break;
        }
    }

    private static string BuildPrefix(string tag)
    {
        var sb = new StringBuilder(64);
        var hasContent = false;

        if (IncludeRealtime || IncludeWallClock)
        {
            sb.Append('[');

            if (IncludeRealtime)
            {
                sb.Append(Time.realtimeSinceStartup.ToString(RealtimeFormat, CultureInfo.InvariantCulture));
            }

            if (IncludeWallClock)
            {
                if (IncludeRealtime) sb.Append(" | ");
                sb.Append(DateTime.UtcNow.ToString(WallClockFormat, CultureInfo.InvariantCulture));
            }

            sb.Append(']');
            hasContent = true;
        }

        if (!string.IsNullOrEmpty(tag))
        {
            if (hasContent) sb.Append(' ');
            sb.Append('[').Append(tag).Append(']');
            hasContent = true;
        }

        if (!string.IsNullOrEmpty(Prefix))
        {
            if (hasContent) sb.Append(' ');
            sb.Append(Prefix);
            hasContent = true;
        }

        if (hasContent)
            sb.Append(' ');

        return sb.ToString();
    }
}
