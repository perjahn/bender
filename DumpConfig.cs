using Microsoft.Win32;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Bender
{
    class DumpConfig
    {
        private const string SubKey = @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps";
        private const string DumpLocation = @"c:\dumps";

        public static void Enable()
        {
            var key = Registry.LocalMachine.CreateSubKey(SubKey);
            key.SetValue("DumpCount", 10, RegistryValueKind.DWord);
            key.SetValue("DumpType", 2, RegistryValueKind.DWord);
            key.SetValue("CustomDumpFlags", 0, RegistryValueKind.DWord);
            key.SetValue("DumpFolder", DumpLocation, RegistryValueKind.String);
            Directory.CreateDirectory(DumpLocation);
            DirectorySecurity acl = Directory.GetAccessControl(DumpLocation);
            SecurityIdentifier everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            acl.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.FullControl | FileSystemRights.Synchronize, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(DumpLocation, acl);
        }

        public static void Disable()
        {
            Registry.LocalMachine.DeleteSubKeyTree(SubKey);
        }

        public static void Enable(bool flag)
        {
            if (flag) Enable();
            else Disable();
        }
    }
}
