using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Flow.Launcher.Plugin.ClipboardSnippets
{
    internal static class ClipboardHelper
    {
        private const int MaxAttempts = 5;

        private const int RetryDelayMs = 50;

        // Writing to the clipboard can transiently fail when it is temporarily locked
        // by another application (for example while copying from a running Office or
        // Explorer window), so each failed attempt is retried with a short delay.
        public static bool TryCopy(string text, out string error)
        {
            var copySucceeded = false;

            void Work()
            {
                var normalized = text ?? string.Empty;
                for (var attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    try
                    {
                        Clipboard.SetText(normalized);
                        copySucceeded = true;
                        return;
                    }
                    catch (Exception e) when (e is COMException or ExternalException)
                    {
                        if (attempt == MaxAttempts)
                            return;

                        Thread.Sleep(RetryDelayMs);
                    }
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

            if (copySucceeded)
            {
                error = null;
                return true;
            }

            error = "Failed to copy to the clipboard. The clipboard may be in use by another application, please try again.";
            return false;
        }
    }
}
