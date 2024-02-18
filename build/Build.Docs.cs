using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Npm;

using Serilog;

using static Nuke.Common.Tools.Npm.NpmTasks;

public partial class Build
{
    Target DocsBuild => _ => _
        .Executes(() =>
        {
            if (IsServerBuild)
            {
                NpmCi();
            }
            else
            {
                NpmInstall();
            }

            // https://stackoverflow.com/a/69699772/111604
            var nodeVersion = ProcessTasks.StartProcess("node", "--version").AssertWaitForExit().Output.FirstOrDefault().Text.Trim();
            var major = Convert.ToInt32(Regex.Match(nodeVersion, "^v(\\d+)").Groups[1].Captures[0].Value);

            Log.Information("Detected Node.js major version {Version}", major);

            NpmRun(_ => _
                .SetCommand("docs:build")
            );
        });

    Target ApiDoc => _ => _
        .Executes(() =>
        {
            var headerContent = File.ReadAllText("doc/header.template");
            var footerContent = File.ReadAllText("doc/footer.template");

            var docsDirectory = RootDirectory / "build" / "apidoc";

            foreach (var file in docsDirectory.GlobFiles("**/*.htm", "**/*.html"))
            {
                var contents = File.ReadAllText(file);
                contents = contents.Replace("@HEADER@", headerContent);
                contents = contents.Replace("@FOOTER@", footerContent);
                File.WriteAllText(file, contents);
            }
        });
}