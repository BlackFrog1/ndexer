﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Squared.Task;
using System.Threading;

namespace Ndexer {
    internal struct ChildProcess : IDisposable {
        public Process Process;

        public ChildProcess (Process process) {
            Process = process;
            Process.PriorityClass = Process.GetCurrentProcess().PriorityClass;
        }

        public System.IO.Stream StandardInput {
            get {
                return Process.StandardInput.BaseStream;
            }
        }

        public System.IO.Stream StandardOutput {
            get {
                return Process.StandardOutput.BaseStream;
            }
        }   

        public void Dispose () {
            if (!Process.HasExited) {
                Process.StandardInput.BaseStream.Close();
                Process.WaitForExit(500);
            }
            if (!Process.HasExited)
                Process.Kill();
        }
    }
    
    public class TagGenerator {
        public string ApplicationPath;
        public string Arguments;

        public TagGenerator (string app, string arguments) {
            ApplicationPath = app;
            Arguments = arguments;
        }

        internal IEnumerator<object> WriteFilenames (BlockingQueue<string> sourceFilenames, Func<string, object> onNextFile, Func<string, Future> writeLine) {
            Future pendingLine = null;
            while (true) {
                string filename = null;
                {
                    var f = sourceFilenames.Dequeue();
                    yield return f;
                    filename = (string)f.Result;
                }

                using (new ActiveWorker("Sending filenames to indexer")) {
                    yield return onNextFile(filename);

                    if (pendingLine != null)
                        yield return pendingLine;

                    pendingLine = writeLine(filename);
                }
            }
        }

        public IEnumerator<object> GenerateTags (BlockingQueue<string> sourceFilenames, BlockingQueue<string> outputLines, Func<string, object> onNextFile) {
            var info = new ProcessStartInfo(ApplicationPath, Arguments);
            info.UseShellExecute = false;
            info.RedirectStandardOutput = true;
            info.RedirectStandardInput = true;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;

            var _process = Process.Start(info);
            using (var process = new ChildProcess(_process))
            using (var outputAdapter = new StreamDataAdapter(process.StandardOutput, false))
            using (var inputAdapter = new StreamDataAdapter(process.StandardInput, false))
            using (var stdout = new AsyncTextReader(outputAdapter, Encoding.ASCII))
            using (var stdin = new AsyncTextWriter(inputAdapter, Encoding.ASCII)) {
                var writeLine = (Func<string, Future>)(
                    (fn) => {
                        return Future.RunInThread(
                            (Action<string>)(
                                (fn_) => {
                                    _process.StandardInput.WriteLine(fn_);
                                    process.StandardInput.Flush();
                                }
                            ),
                            fn
                        );
                    }
                );

                Program.Scheduler.Start(
                    WriteFilenames(sourceFilenames, onNextFile, writeLine),
                    TaskExecutionPolicy.RunAsBackgroundTask
                );

                while (!_process.HasExited) {
                    var f = stdout.ReadLine();
                    yield return f;
                    string currentLine = f.Result as string;

                    if (currentLine == null) {
                        break;
                    } else {
                        outputLines.Enqueue(currentLine);
                    }
                }

                if (_process.ExitCode != 0) {
                    System.Windows.Forms.MessageBox.Show(String.Format("ctags terminated with exit code {0}.", _process.ExitCode), "Error");
                }
            }
        }
    }
}