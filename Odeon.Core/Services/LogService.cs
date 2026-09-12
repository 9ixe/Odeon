#nullable enable

using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
namespace Odeon.Core.Services
{
    public static class LogService
    {
        public static void Log(object? message, [CallerMemberName] string? source = default)
        {
            Debug.WriteLine($"[{DateTime.Now.ToString(CultureInfo.CurrentCulture)} - {source}]: {message}");
        }
    }
}