using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Flow.Launcher.Plugin.ClipboardSnippets
{
    internal static class ClipboardHelper
    {
        private const int MaxAttempts = 5;

        private const int ReadAttempts = 3;

        // Writing to and verifying the clipboard can transiently fail when it is temporarily locked
        // by another application (for example while copying from a running Office or Explorer
        // window), so each attempt writes and then reads the clipboard back to confirm the content
        // really is there before reporting success.
        public static bool TryCopy(string text, out string error)
        {
            var copyVerified = false;

            void Work()
            {
                var normalized = text ?? string.Empty;
                var delayMs = 60;
                for (var attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    var writeFailed = false;
                    try
                    {
                        Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, normalized), true);
                    }
                    catch (Exception e) when (e is COMException or ExternalException)
                    {
                        writeFailed = true;
                    }

                    if (!writeFailed)
                    {
                        var readBack = ReadBackText();
                        if (readBack != null && string.Equals(readBack, normalized, StringComparison.Ordinal))
                        {
                            copyVerified = true;
                            return;
                        }
                    }

                    if (attempt == MaxAttempts)
                        return;

                    Thread.Sleep(delayMs);
                    delayMs *= 2;
                }
            }

            // The clipboard can only be accessed from an STA thread.
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                Work();
            }
            else
            {
                var staThread = new Thread(Work);
                staThread.SetApartmentState(ApartmentState.STA);
                staThread.Start();
                staThread.Join();
            }

            if (copyVerified)
            {
                error = null;
                return true;
            }

            error = "Failed to copy to the clipboard. The clipboard may be in use by another application, please try again.";
            return false;
        }

        private static string ReadBackText()
        {
            for (var attempt = 1; attempt <= ReadAttempts; attempt++)
            {
                try
                {
                    return Clipboard.GetText(TextDataFormat.UnicodeText) ?? string.Empty;
                }
                catch (Exception e) when (e is COMException or ExternalException)
                {
                    if (attempt == ReadAttempts)
                        return null;

                    Thread.Sleep(80 * attempt);
                }
            }

            return null;
        }
    }
}
