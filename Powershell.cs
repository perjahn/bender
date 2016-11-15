using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Bender
{
    class Powershell
    {
        public void RunPowershellScript(string[] scripts, Stream output)
        {
            foreach (string script in scripts)
            {
                if (!File.Exists(script))
                {
                    Bender.WriteLine($"Powershell script not found: '{script}'", output);
                    continue;
                }

                Bender.WriteLine($"Running Powershell script: {script}", output);

                Process process = new Process()
                {
                    StartInfo = {
                        FileName = "powershell.exe",
                        Arguments = $"-file {script}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true }
                };

                process.Start();
                process.WaitForExit();

                Bender.WriteLine(process.StandardOutput.ReadToEnd(), output);
            }

            return;
        }
    }
}
