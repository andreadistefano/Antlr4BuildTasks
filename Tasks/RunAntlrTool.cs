﻿// Forked and highly modified from sources at https://github.com/tunnelvisionlabs/antlr4cs/tree/master/runtime/CSharp/Antlr4BuildTasks
// Copyright 2022 Ken Domino, MIT License.

// Copyright (c) Terence Parr, Sam Harwell. All Rights Reserved.
// Licensed under the BSD License. See LICENSE.txt in the project root for license information.

namespace Antlr4.Build.Tasks
{
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using Directory = System.IO.Directory;
    using File = System.IO.File;
    using Path = System.IO.Path;
    using StringBuilder = System.Text.StringBuilder;

    public class RunAntlrTool : Task
    {
        private const string DefaultGeneratedSourceExtension = "g4";
        private List<string> _generatedCodeFiles = new List<string>();
        private List<string> _allGeneratedFiles = new List<string>();

        public RunAntlrTool()
        {
            this.GeneratedSourceExtension = DefaultGeneratedSourceExtension;
        }

        [Output] public ITaskItem[] AllGeneratedFiles
        {
            get { return this._allGeneratedFiles.Select(t => new TaskItem(t)).ToArray(); }
            set { this._allGeneratedFiles = value.Select(t => t.ItemSpec).ToList(); }
        }
        public string AntOutDir { get; set; }
        public List<string> AntlrProbePath
        {
            get;
            set;
        } = new List<string>()
        {
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.9.3/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.9.2/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.9.1/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.9/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.8-1/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.8/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.7.2/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.7.1/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.7/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.6/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.5.3/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.5.2-1/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.5.2/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.5.1-1/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.5.1/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.5/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.3/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.2.2/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.2.1/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.2/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.1/",
            "https://repo1.maven.org/maven2/org/antlr/antlr4/4.0/",
        };
        public string DOptions { get; set; }
        public string Encoding { get; set; }
        public bool Error { get; set; }
        public bool ForceAtn { get; set; }
        public bool GAtn { get; set; }
        [Output] public ITaskItem[] GeneratedCodeFiles
        {
            get
            {
                return this._generatedCodeFiles.Select(t => new TaskItem(t)).ToArray();
            }
        }
        public string GeneratedSourceExtension { get; set; }
        [Required] public string IntermediateOutputPath { get; set; }
        public string JavaExec { get; set; }
        public string LibPath { get; set; }
        public bool Listener { get; set; }
        public string Package { get; set; }
        public List<string> OtherSourceCodeFiles { get; set; }
        [Required] public ITaskItem[] PackageReference { get; set; }
        [Required] public ITaskItem[] PackageVersion { get; set; }
        [Required] public ITaskItem[] SourceCodeFiles { get; set; }
        public string TargetFrameworkVersion { get; set; }
        public ITaskItem[] TokensFiles { get; set; }
        public string ToolPath { get; set; }
        public string Version { get; set; }
        public bool Visitor { get; set; }

        public override bool Execute()
        {
            bool success = false;
            //System.Threading.Thread.Sleep(20000);
            try
            {
                MessageQueue.EnqueueMessage(Message.BuildInfoMessage("Starting Antlr4 Build Tasks."));
                if (IntermediateOutputPath != null) IntermediateOutputPath = Path.GetFullPath(IntermediateOutputPath);
                if (AntOutDir != null) AntOutDir = Path.GetFullPath(AntOutDir);
                if (AntOutDir == null || AntOutDir == "")
                {
                    MessageQueue.EnqueueMessage(Message.BuildInfoMessage("Placing generated files in IntermediateOutputPath " + IntermediateOutputPath));
                    AntOutDir = IntermediateOutputPath;
                }
                Directory.CreateDirectory(AntOutDir);
                JavaExec = SetupJava();
                if (!File.Exists(JavaExec))
                    throw new Exception("Cannot find Java executable, currently set to " + "'" + JavaExec + "'");
                MessageQueue.EnqueueMessage(Message.BuildInfoMessage("JavaExec is \"" + JavaExec + "\""));

                ToolPath = SetupAntlrJar();
                if (!File.Exists(JavaExec))
                    throw new Exception("Cannot find Antlr jar, currently set to " + "'" + ToolPath + "'");
                MessageQueue.EnqueueMessage(Message.BuildInfoMessage("ToolPath is \"" + ToolPath + "\""));

                success = GetGeneratedFileNameList(JavaExec)
                    && GenerateFiles(JavaExec, out success);
            }
            catch (Exception exception)
            {
                ProcessExceptionAsBuildMessage(exception);
                success = false;
            }
            finally
            {
                if (!success)
                {
                    MessageQueue.EnqueueMessage(Message.BuildErrorMessage("The Antlr4 tool failed."));
                    MessageQueue.MutateToError();
                    _generatedCodeFiles.Clear();
                    _allGeneratedFiles.Clear();
                }
                MessageQueue.EmptyMessageQueue(Log);
            }
            return success;
        }

        private string SetupAntlrJar()
        {
            string result = null;
            // Make sure old crusty Tunnelvision port not being used.
            foreach (var i in PackageReference)
            {
                if (i.ItemSpec == "Antlr4.Runtime")
                {
                    throw new Exception(
                        @"You are referencing Antlr4.Runtime in your .csproj file. This build tool can only reference the NET Standard library https://www.nuget.org/packages/Antlr4.Runtime.Standard/. You can only use either the 'official' Antlr4 or the 'tunnelvision' fork, but not both. You have to choose one.");
                }
            }
            string version = null;
            bool reference_standard_runtime = false;
            foreach (var i in PackageReference)
            {
                if (i.ItemSpec == "Antlr4.Runtime.Standard")
                {
                    reference_standard_runtime = true;
                    version = i.GetMetadata("Version");
                    if (version == null || version.Trim() == "")
                    {
                        foreach (var j in PackageVersion)
                        {
                            if (j.ItemSpec == "Antlr4.Runtime.Standard")
                            {
                                reference_standard_runtime = true;
                                version = j.GetMetadata("Version");
                                break;
                            }
                        }
                    }
                    break;
                }
            }
            if (version == null)
            {
                if (reference_standard_runtime)
                    throw new Exception(
                        @"Antlr4BuildTasks cannot identify the version number you are referencing. Check the Version parameter for Antlr4.Runtime.Standard.");
                else
                    throw new Exception(
                        @"You are not referencing Antlr4.Runtime.Standard in your .csproj file. Antlr4BuildTasks requires a reference to it in order
to identify which version of the Antlr Java tool to run to generate the parser and lexer.");
            }
            else if (version.Trim() == "")
            {
                throw new Exception(
                    @"Antlr4BuildTasks cannot determine the version of Antlr4.Runtime.Standard. It's ''!.
version = '" + version + @"'
PackageReference = '" + PackageReference.ToString() + @"'
PackageVersion = '" + PackageVersion.ToString() + @"
");
            }
            Version = version;
            MessageQueue.EnqueueMessage(Message.BuildInfoMessage(
                @"Antlr4BuildTasks identified that you are looking for version "
                + Version
                + " of the Antlr4 tool jar."));
            if (AntlrProbePath == null || AntlrProbePath.Count == 0)
            {
                throw new Exception(
                    @"Antlr4BuildTasks requires an AntlrProbePath, which contains the list of places to find and download the Antlr .jar file. AntlrProbePath is null.");
            }
            var assembly = this.GetType().Assembly;
            var dir = IntermediateOutputPath;
            dir = dir.Replace("\\", "/");
            if (!dir.EndsWith("/"))
            {
                dir = dir + "/";
            }
            var archive = dir + "antlr4-4.9.3-complete.jar";
            if (!File.Exists(archive))
            {
                System.IO.Directory.CreateDirectory(dir);
                var names = assembly.GetManifestResourceNames();
                var jar = @"Antlr4.Build.Tasks.antlr4-4.9.3-complete.jar";
                Stream contents = this.GetType().Assembly.GetManifestResourceStream(jar);
                var destinationFileStream = new FileStream(archive, FileMode.OpenOrCreate);
                while (contents.Position < contents.Length)
                {
                    destinationFileStream.WriteByte((byte)contents.ReadByte());
                }
                destinationFileStream.Close();
            }

            //string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            //assemblyPath = Path.GetFullPath(assemblyPath + "/../../build/").Replace("\\", "/");
            //string archive_path = "file:///" + assemblyPath;

            var paths = AntlrProbePath;
            var full_path = "file:///" + Path.GetFullPath(IntermediateOutputPath);
            paths.Insert(0, full_path);
            MessageQueue.EnqueueMessage(Message.BuildInfoMessage("Paths to search for Antlr4 jar, in order, are: "
                + String.Join(";", paths)));
            foreach (var probe in paths)
            {
                Regex r2 = new Regex("^(?<TWOVERSION>[0-9]+[.][0-9]+)([.][0-9]*)?$");
                var m2 = r2.Match(Version);
                var v2 = m2.Success && m2.Groups["TWOVERSION"].Length > 0 ? m2.Groups["TWOVERSION"].Value : null;
                Regex r3 = new Regex("^(?<THREEVERSION>[0-9]+[.][0-9]+[.][0-9]+)$");
                var m3 = r3.Match(Version);
                var v3 = m3.Success && m3.Groups["THREEVERSION"].Length > 0 ? m3.Groups["THREEVERSION"].Value : null;
                if (v3 != null && TryProbe(probe, v3, out string where))
                {
                    if (where == null || where == "")
                    {
                        throw new Exception(
                            @"Antlr4BuildTasks is going to return an empty UsingToolPath, but it should never do that.");
                    }
                    else
                    {
                        return where;
                    }
                }
                if (v2 != null && TryProbe(probe, v2, out string w2))
                {
                    if (w2 == null || w2 == "")
                    {
                        throw new Exception(
                            @"Antlr4BuildTasks is going to return an empty UsingToolPath, but it should never do that.");
                    }
                    else
                    {
                        result = w2;
                        break;
                    }
                }
            }
            if (result == null || result == "")
            {
                MessageQueue.EnqueueMessage(Message.BuildErrorMessage(
                    @"Went through the complete probe list looking for an Antlr4 tool jar, but could not find anything. Fail!"));
            }
            if (!File.Exists(result))
                throw new Exception("Cannot find Antlr4 jar file, currently set to "
                                    + "'" + result + "'");
            return result;
        }

        private bool TryProbe(string path, string version, out string where)
        {
            bool result = false;
            where = null;
            path = path.Trim();
            MessageQueue.EnqueueMessage(Message.BuildInfoMessage("path is " + path));
            if (!(path.EndsWith("/") || path.EndsWith("\\"))) path = path + "/";

            var jar = path + @"antlr4-" + version + @"-complete.jar";
            MessageQueue.EnqueueMessage(Message.BuildInfoMessage("Probing " + jar));
            if (jar.StartsWith("file://"))
            {
                try
                {
                    System.Uri uri = new Uri(jar);
                    var local_file = uri.LocalPath;
                    MessageQueue.EnqueueMessage(Message.BuildInfoMessage("Local path " + local_file));
                    if (File.Exists(local_file))
                    {
                        MessageQueue.EnqueueMessage(Message.BuildInfoMessage("Found."));
                        where = local_file;
                        result = true;
                    }
                }
                catch
                {
                }
            }
            else if (jar.StartsWith("https://"))
            {
                try
                {
                    WebClient webClient = new WebClient();
                    System.IO.Directory.CreateDirectory(IntermediateOutputPath);
                    var archive_name = IntermediateOutputPath + System.IO.Path.DirectorySeparatorChar +
                                       System.IO.Path.GetFileName(jar);
                    var jar_dir = IntermediateOutputPath;
                    System.IO.Directory.CreateDirectory(jar_dir);
                    if (!File.Exists(archive_name))
                    {
                        MessageQueue.EnqueueMessage(Message.BuildInfoMessage("Downloading " + jar));
                        webClient.DownloadFile(jar, archive_name);
                    }
                    MessageQueue.EnqueueMessage(Message.BuildInfoMessage("Found. Saving to "
                        + archive_name));
                    where = archive_name;
                    result = true;
                }
                catch
                {
                    MessageQueue.EnqueueMessage(Message.BuildInfoMessage(
                        "Problem downloading or saving probed file."));
                }
            }
            else
            {
                MessageQueue.EnqueueMessage(Message.BuildInfoMessage(
                    @"The AntlrProbePath contains '"
                        + jar
                        + "', which doesn't start with 'file://' or 'https://'. "
                        + @"Edit your .csproj file to make sure the path follows that syntax."));
            }
            return result;
        }

        public string SetupJava()
        {
            // https://download.java.net/java/GA/jdk14.0.1/664493ef4a6946b186ff29eb326336a2/7/GPL/openjdk-14.0.1_windows-x64_bin.zip
            if (System.Environment.OSVersion.Platform == PlatformID.Win32NT
                  || System.Environment.OSVersion.Platform == PlatformID.Win32S
                  || System.Environment.OSVersion.Platform == PlatformID.Win32Windows
                 )
            {
                MessageQueue.EnqueueMessage(Message.BuildInfoMessage(
                    "AntOutDir is \"" + AntOutDir + "\""));
                var assembly = this.GetType().Assembly;
                var zip = @"Antlr4.Build.Tasks.jre.zip";
                var java_dir = AntOutDir;
                java_dir = java_dir.Replace("\\", "/");
                if (!java_dir.EndsWith("/"))
                {
                    java_dir = java_dir + "/";
                }
                java_dir = java_dir + "Java/";
                var archive = java_dir + "jre.zip";
                if (!Directory.Exists(java_dir))
                {
                    System.IO.Directory.CreateDirectory(java_dir);
                    var names = assembly.GetManifestResourceNames();
                    Stream contents = this.GetType().Assembly.GetManifestResourceStream(zip);
                    var destinationFileStream = new FileStream(archive, FileMode.OpenOrCreate);
                    while (contents.Position < contents.Length)
                    {
                        destinationFileStream.WriteByte((byte)contents.ReadByte());
                    }
                    destinationFileStream.Close();
                    System.IO.Compression.ZipFile.ExtractToDirectory(archive, java_dir);
                }
                var result = java_dir + "jre/bin/java.exe";
                return result;
            }
            else if (System.Environment.OSVersion.Platform == PlatformID.Unix
                     || System.Environment.OSVersion.Platform == PlatformID.MacOSX
                    )
            {
                MessageQueue.EnqueueMessage(Message.BuildInfoMessage(
                    "AntOutDir is \"" + AntOutDir + "\""));
                var result = "/usr/bin/java";
                return result;
            }
            else throw new Exception("Which OS??");
        }

        private bool GetGeneratedFileNameList(string java_executable)
        {
            // Because we're using the Java version of the Antlr tool,
            // we're going to execute this command twice: first with the
            // -depend option so as to get the list of generated files,
            // then a second time to actually generate the files.
            // The code that was here probably worked, but only for the C#
            // version of the Antlr tool chain.
            //
            // After collecting the output of the first command, convert the
            // output so as to get a clean list of files generated.
            List<string> arguments = new List<string>();
            arguments.Add("-cp");
            arguments.Add(ToolPath);
            arguments.Add("org.antlr.v4.Tool");
            arguments.Add("-depend");
            arguments.Add("-o");
            arguments.Add(AntOutDir);
            if (!string.IsNullOrEmpty(LibPath))
            {
                var split = LibPath.Split(';');
                foreach (var p in split)
                {
                    if (string.IsNullOrEmpty(p))
                        continue;
                    if (string.IsNullOrWhiteSpace(p))
                        continue;
                    arguments.Add("-lib");
                    arguments.Add(p);
                }
            }
            if (GAtn) arguments.Add("-atn");
            if (!string.IsNullOrEmpty(Encoding))
            {
                arguments.Add("-encoding");
                arguments.Add(Encoding);
            }
            arguments.Add(Listener ? "-listener" : "-no-listener");
            arguments.Add(Visitor ? "-visitor" : "-no-visitor");
            if (!(string.IsNullOrEmpty(Package) || string.IsNullOrWhiteSpace(Package)))
            {
                arguments.Add("-package");
                arguments.Add(Package);
            }
            if (!string.IsNullOrEmpty(DOptions))
            {
                // The Antlr tool can take multiple -D options, but
                // DOptions is just a string. We allow for multiple
                // options by separating each with a semi-colon. At this
                // point, convert each option into separate "-D" option
                // arguments.
                var split = DOptions.Split(';');
                foreach (var p in split)
                {
                    var q = p.Trim();
                    if (string.IsNullOrEmpty(q))
                        continue;
                    if (string.IsNullOrWhiteSpace(q))
                        continue;
                    arguments.Add("-D" + q);
                }
            }
            if (Error) arguments.Add("-Werror");
            if (ForceAtn) arguments.Add("-Xforce-atn");
            if (SourceCodeFiles == null) arguments.AddRange(OtherSourceCodeFiles);
            else arguments.AddRange(SourceCodeFiles?.Select(s => s.ItemSpec));
            ProcessStartInfo startInfo = new ProcessStartInfo(java_executable, JoinArguments(arguments))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            MessageQueue.EnqueueMessage(Message.BuildInfoMessage(
                "Executing command: \"" + startInfo.FileName + "\" " + startInfo.Arguments));
            Process process = new Process();
            process.StartInfo = startInfo;
            process.ErrorDataReceived += HandleStderrDataReceived;
            process.OutputDataReceived += HandleOutputDataReceivedFirstTime;
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            process.StandardInput.Dispose();
            process.WaitForExit();
            MessageQueue.EnqueueMessage(Message.BuildInfoMessage(
                "Finished executing Antlr jar command."));
            MessageQueue.EnqueueMessage(Message.BuildInfoMessage(
                "The generated file list contains " + _generatedCodeFiles.Count() + " items."));
            if (process.ExitCode != 0)
            {
                return false;
            }
            // Add in tokens and interp files since Antlr Tool does not do that.
            var old_list = _generatedCodeFiles.ToList();
            var new_list = new List<string>();
            foreach (var s in old_list)
            {
                if (Path.GetExtension(s) == ".tokens")
                {
                    var interp = s.Replace(Path.GetExtension(s), ".interp");
                    new_list.Append(interp);
                }
                else
                    new_list.Add(s);
            }
            _generatedCodeFiles = new_list.ToList();
            return true;
        }

        private bool GenerateFiles(string java_executable, out bool success)
        {
            List<string> arguments = new List<string>();
            {
                arguments.Add("-cp");
                arguments.Add(ToolPath);
                //arguments.Add("org.antlr.v4.CSharpTool");
                arguments.Add("org.antlr.v4.Tool");
            }
            arguments.Add("-o");
            arguments.Add(AntOutDir);
            if (!string.IsNullOrEmpty(LibPath))
            {
                var split = LibPath.Split(';');
                foreach (var p in split)
                {
                    if (string.IsNullOrEmpty(p))
                        continue;
                    if (string.IsNullOrWhiteSpace(p))
                        continue;
                    arguments.Add("-lib");
                    arguments.Add(p);
                }
            }
            if (GAtn) arguments.Add("-atn");
            if (!string.IsNullOrEmpty(Encoding))
            {
                arguments.Add("-encoding");
                arguments.Add(Encoding);
            }
            arguments.Add(Listener ? "-listener" : "-no-listener");
            arguments.Add(Visitor ? "-visitor" : "-no-visitor");
            if (!(string.IsNullOrEmpty(Package) || string.IsNullOrWhiteSpace(Package)))
            {
                arguments.Add("-package");
                arguments.Add(Package);
            }
            if (!string.IsNullOrEmpty(DOptions))
            {
                // Since the C# target currently produces the same code for all target framework versions, we can
                // avoid bugs with support for newer frameworks by just passing CSharp as the language and allowing
                // the tool to use a default.
                var split = DOptions.Split(';');
                foreach (var p in split)
                {
                    if (string.IsNullOrEmpty(p))
                        continue;
                    if (string.IsNullOrWhiteSpace(p))
                        continue;
                    arguments.Add("-D" + p);
                }
            }
            if (Error) arguments.Add("-Werror");
            if (ForceAtn) arguments.Add("-Xforce-atn");
            if (SourceCodeFiles == null) arguments.AddRange(OtherSourceCodeFiles);
            else arguments.AddRange(SourceCodeFiles?.Select(s => s.ItemSpec));
            ProcessStartInfo startInfo = new ProcessStartInfo(java_executable, JoinArguments(arguments))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            MessageQueue.EnqueueMessage(Message.BuildInfoMessage(
                "Executing command: \"" + startInfo.FileName + "\" " + startInfo.Arguments));
            Process process = new Process();
            process.StartInfo = startInfo;
            process.ErrorDataReceived += HandleStderrDataReceived;
            process.OutputDataReceived += HandleStdoutDataReceived;
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            process.StandardInput.Dispose();
            process.WaitForExit();
            MessageQueue.EnqueueMessage(Message.BuildInfoMessage(
                "Finished executing Antlr jar command."));
            MessageQueue.EnqueueMessage(Message.BuildInfoMessage(
                "The generated file list contains " + _generatedCodeFiles.Count() + " items."));
            foreach (var fn in _generatedCodeFiles)
                MessageQueue.EnqueueMessage(Message.BuildInfoMessage("Generated file " + fn));
            MessageQueue.EnqueueMessage(Message.BuildInfoMessage(
                "Executing command: \"" + startInfo.FileName + "\" " + startInfo.Arguments));
            // At this point, regenerate the entire GeneratedCodeFiles list.
            // This is because (1) it contains duplicates; (2) it contains
            // files that really actually weren't generated. This can happen
            // if the grammar was a Lexer grammar. (Note, I don't think it
            // wise to look at the grammar file to figure out what it is, nor
            // do I think it wise to expose a switch to the user for him to
            // indicate what type of grammar it is.)
            var new_code_list = new List<string>();
            var new_all_list = new List<string>();
            foreach (var fn in _generatedCodeFiles.Distinct().ToList())
            {
                var ext = Path.GetExtension(fn);
                if (File.Exists(fn) && !(ext == ".g4" && ext == ".g4"))
                    new_all_list.Add(fn);
                if ((ext == ".cs" || ext == ".java" || ext == ".cpp" ||
                     ext == ".php" || ext == ".js") && File.Exists(fn))
                    new_code_list.Add(fn);
            }
            foreach (var fn in _allGeneratedFiles.Distinct().ToList())
            {
                var ext = Path.GetExtension(fn);
                if (File.Exists(fn) && !(ext == ".g4" && ext == ".g4"))
                    new_all_list.Add(fn);
                if ((ext == ".cs" || ext == ".java" || ext == ".cpp" ||
                     ext == ".php" || ext == ".js") && File.Exists(fn))
                    new_code_list.Add(fn);
            }
            _allGeneratedFiles = new_all_list.ToList();
            _generatedCodeFiles = new_code_list.ToList();
            MessageQueue.EnqueueMessage(Message.BuildInfoMessage("List of generated files " + String.Join(" ", _allGeneratedFiles)));
            MessageQueue.EnqueueMessage(Message.BuildInfoMessage("List of generated code files " + String.Join(" ", _generatedCodeFiles)));
            success = process.ExitCode == 0;
            return success;
        }

        private void ProcessExceptionAsBuildMessage(Exception exception)
        {
            MessageQueue.EnqueueMessage(Message.BuildCrashMessage(exception.Message
                + exception.StackTrace));
        }

        internal static bool IsFatalException(Exception exception)
        {
            while (exception != null)
            {
                if (exception is OutOfMemoryException)
                {
                    return true;
                }

                if (!(exception is TypeInitializationException) && !(exception is TargetInvocationException))
                {
                    break;
                }

                exception = exception.InnerException;
            }

            return false;
        }

        private static string JoinArguments(IEnumerable<string> arguments)
        {
            if (arguments == null)
                throw new ArgumentNullException("arguments");

            StringBuilder builder = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (builder.Length > 0)
                    builder.Append(' ');

                if (argument.IndexOfAny(new[] { '"', ' ' }) < 0)
                {
                    builder.Append(argument);
                    continue;
                }

                // escape a backslash appearing before a quote
                string arg = argument.Replace("\\\"", "\\\\\"");
                // escape double quotes
                arg = arg.Replace("\"", "\\\"");

                // wrap the argument in outer quotes
                builder.Append('"').Append(arg).Append('"');
            }

            return builder.ToString();
        }

        private static readonly Regex GeneratedFileMessageFormat = new Regex(@"^Generating file '(?<OUTPUT>.*?)' for grammar '(?<GRAMMAR>.*?)'$", RegexOptions.Compiled);

        private void HandleStderrDataReceived(object sender, DataReceivedEventArgs e)
        {
            HandleStderrDataReceived(e.Data);
        }

        bool start = false;
        StringBuilder sb = new StringBuilder();
        private void HandleStderrDataReceived(string data)
        {
            //System.Console.Error.WriteLine("XXX3 " + data);
            if (string.IsNullOrEmpty(data))
                return;
            try
            {
                if (data.Contains("Exception in thread"))
                {
                    start = true;
                    sb.AppendLine(data);
                }
                else if (start)
                {
                    sb.AppendLine(data);
                    if (data.Contains("at org.antlr.v4.Tool.main(Tool.java"))
                    {
                        MessageQueue.EnqueueMessage(Message.BuildErrorMessage(sb.ToString()));
                        sb = new StringBuilder();
                        start = false;
                    }
                }
                else
                    MessageQueue.EnqueueMessage(Message.BuildDefaultMessage(data));
            }
            catch (Exception ex)
            {
                if (RunAntlrTool.IsFatalException(ex))
                    throw;

                MessageQueue.EnqueueMessage(Message.BuildCrashMessage(ex.Message));
            }
        }

        private void HandleOutputDataReceivedFirstTime(object sender, DataReceivedEventArgs e)
        {
            string str = e.Data as string;
            if (string.IsNullOrEmpty(str))
                return;

            MessageQueue.EnqueueMessage(new Message("Yo got " + str + " from Antlr Tool."));

            // There could all kinds of shit coming out of the Antlr Tool, so we need to
            // take care of what to record.
            // Parse the dep string as "file-name1 : file-name2". Strip off the name
            // file-name1 and save it away.
            try
            {
                Regex regex = new Regex(@"^(?<OUTPUT>\S+)\s*:");
                Match match = regex.Match(str);
                if (!match.Success)
                {
                    MessageQueue.EnqueueMessage(new Message("Yo didn't fit pattern!"));
                    return;
                }
                string fn = match.Groups["OUTPUT"].Value;
                var ext = Path.GetExtension(fn);
                if (ext == ".cs" || ext == ".java" || ext == ".cpp" ||
                    ext == ".php" || ext == ".js" || ext == ".tokens" || ext == ".interp" ||
                    ext == ".dot")
                    _generatedCodeFiles.Add(fn);
            }
            catch (Exception ex)
            {
                if (RunAntlrTool.IsFatalException(ex))
                    throw;

                MessageQueue.EnqueueMessage(new Message(ex.Message));
            }
        }

        private void HandleStdoutDataReceived(object sender, DataReceivedEventArgs e)
        {
            HandleStdoutDataReceived(e.Data);
        }

        private void HandleStdoutDataReceived(string data)
        {
            if (string.IsNullOrEmpty(data))
                return;

            MessageQueue.EnqueueMessage(new Message("Yo got " + data + " from Antlr Tool."));

            try
            {
                Match match = GeneratedFileMessageFormat.Match(data);
                if (!match.Success)
                {
                    MessageQueue.EnqueueMessage(new Message(data));
                    return;
                }

                string fileName = match.Groups["OUTPUT"].Value;
                _generatedCodeFiles.Add(match.Groups["OUTPUT"].Value);
            }
            catch (Exception ex)
            {
                MessageQueue.EnqueueMessage(Message.BuildErrorMessage(ex.Message
                                                            + ex.StackTrace));

                if (RunAntlrTool.IsFatalException(ex))
                    throw;
            }
        }
    }
}
