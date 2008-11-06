﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Task;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Diagnostics;

namespace Ndexer {
    public class TextPadDirector : Director, IBasicDirector {
        public TextPadDirector (string applicationPath)
            : base (applicationPath) {
        }

        protected override IntPtr FindDirectorWindow () {
            return IntPtr.Zero;
        }

        protected override IntPtr FindEditorWindow () {
            return FindWindow("TextPad4", null);
        }

        public void Launch (string arguments) {
            var info = new ProcessStartInfo(_ApplicationPath, arguments);
            var process = Process.Start(info);
            process.WaitForInputIdle();
            _EditorWindow = FindEditorWindow();
            process.Dispose();
        }

        public void OpenFile (string filename) {
            Launch(filename);
        }

        public void OpenFile (string filename, long initialLineNumber) {
            Launch(String.Format("{0}({1})", filename, initialLineNumber));
        }
    }
}