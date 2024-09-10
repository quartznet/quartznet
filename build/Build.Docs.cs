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
            const string Version = "4.0";

            AbsolutePath docDirectory = RootDirectory / "doc";
            var headerContent = File.ReadAllText(docDirectory / "header.template");
            var footerContent = File.ReadAllText(docDirectory / "footer.template");

            var docsDirectory = ArtifactsDirectory / "apidoc";

            foreach (var file in docsDirectory.GlobFiles("**/*.htm", "**/*.html"))
            {
                var contents = File.ReadAllText(file);
                contents = contents.Replace("@HEADER@", headerContent);
                contents = contents.Replace("@FOOTER@", footerContent);
                File.WriteAllText(file, contents);
            }

            (docDirectory / "html-redirect.php").Copy(docsDirectory / Version / "html" / "index.php", ExistsPolicy.FileOverwrite);

            docsDirectory.ZipTo(ArtifactsDirectory / $"apidoc-{Version}.zip", fileMode: FileMode.Create);
        });
}