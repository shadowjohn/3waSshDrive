using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ThreeWa.SshDrive.App.Diagnostics
{
    internal static class SelfCheckConsole
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint processId);

        internal static void AttachParent()
        {
            const uint AttachParentProcess = 0xffffffff;
            AttachConsole(AttachParentProcess);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput())
            {
                AutoFlush = true
            });
            Console.SetError(new StreamWriter(Console.OpenStandardError())
            {
                AutoFlush = true
            });
        }
    }
}
