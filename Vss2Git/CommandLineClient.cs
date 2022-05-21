using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Hpdi.VssLogicalLib;

namespace Hpdi.Vss2Git
{
    class CommandLineClient
    {
        private readonly Dictionary<int, EncodingInfo> codePages = new Dictionary<int, EncodingInfo>();
        private readonly WorkQueue workQueue = new WorkQueue(1);
        private Logger logger = Logger.Null;
        private RevisionAnalyzer revisionAnalyzer;
        private ChangesetBuilder changesetBuilder;

        private string vssPath;
        private string vssProject;
        private string outputDir;
        private string logFile;

        public CommandLineClient(string[] args)
        {
            if (args.Length < 3)
                throw new ArgumentOutOfRangeException();

            // mandatory args
            vssPath = args[0];
            vssProject = args[1];
            outputDir = args[2];

            // optional args
            logFile = args.Length > 3 ? args[3] : null;
        }

        public bool Start()
        {
            try
            {
                OpenLog(logFile);

                logger.WriteLine("VSS2Git version {0}", Assembly.GetExecutingAssembly().GetName().Version);

                Encoding encoding = Encoding.GetEncoding(1252);

                logger.WriteLine("VSS encoding: {0} (CP: {1}, IANA: {2})",
                    encoding.EncodingName, encoding.CodePage, encoding.WebName);
                logger.WriteLine("Comment transcoding: {0}",
                    true ? "enabled" : "disabled");
                logger.WriteLine("Ignore errors: {0}",
                    false ? "enabled" : "disabled");

                var df = new VssDatabaseFactory(vssPath);
                df.Encoding = encoding;
                var db = df.Open();

                var path = vssProject;
                VssItem item;
                try
                {
                    item = db.GetItem(path);
                }
                catch (VssPathException ex)
                {
                    logger.WriteLine("ERR: Invalid project path");
                    logger.WriteLine(ex.Message);
                    return false;
                }

                var project = item as VssProject;
                if (project == null)
                {
                    logger.WriteLine(path + " is not a project", "Invalid project path");
                    return false;
                }

                revisionAnalyzer = new RevisionAnalyzer(workQueue, logger, db);
                revisionAnalyzer.AddItem(project);

                changesetBuilder = new ChangesetBuilder(workQueue, logger, revisionAnalyzer);
                changesetBuilder.AnyCommentThreshold = TimeSpan.FromSeconds(30);
                changesetBuilder.SameCommentThreshold = TimeSpan.FromSeconds(600);
                changesetBuilder.BuildChangesets();

                if (!string.IsNullOrEmpty(outputDir))
                {
                    var gitExporter = new GitExporter(workQueue, logger,
                        revisionAnalyzer, changesetBuilder);
                    gitExporter.EmailDomain = "localhost";
                    gitExporter.ExportToGit(outputDir);
                }

                workQueue.WaitIdle();

                var exceptions = workQueue.FetchExceptions();
                if (exceptions != null)
                {
                    foreach (var ex in workQueue.FetchExceptions())
                    {
                        logger.WriteLine(ex);
                    }
                }

                return exceptions == null;
            }
            catch (Exception ex)
            {
                logger.Dispose();
                logger = Logger.Null;
                Console.Error.WriteLine(ex);
                return false;
            }
        }
        private void OpenLog(string filename)
        {
            if (String.IsNullOrEmpty(filename))
            {
                logger = new Logger(Console.OpenStandardOutput());
            }
            else
            {
                logger = new Logger(filename);
            }
        }
    }
}
