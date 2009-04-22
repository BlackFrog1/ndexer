using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Data.SQLite;
using Squared.Task;
using Squared.Task.Data;
using System.Text.RegularExpressions;
using Squared.Task.IO;
using System.Reflection;

namespace Ndexer {
    public partial class FindInFilesDialog : Form {
        public class SearchQuery {
            public readonly string Text;
            public readonly Regex Regex;
            public readonly string[] SearchWords;

            public SearchQuery(string regex) {
                Text = regex;
                Regex = new Regex(regex, RegexOptions.Compiled | RegexOptions.ExplicitCapture);

                // Ph'nglui Mglw'nafh Regex R'lyeh wgah'nagl fhtagn
                var tempRe = new Regex(regex, RegexOptions.ExplicitCapture);
                var recode = tempRe.GetType().GetField("code", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tempRe);
                var words = (string[])(recode.GetType().GetField("_strings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(recode));
                var result = new List<string>();
                foreach (var word in words) {
                    if (word.Contains('\0'))
                        continue;

                    result.Add(word);
                }
                SearchWords = result.ToArray();
            }
        }

        struct LineEntry {
            public string Text;
            public int LineNumber;
        }

        struct SearchResult {
            public string Context;
            public string Filename;
            public long LineNumber;
        }

        static Color ErrorColor = Color.FromArgb(255, 220, 220);

        ConnectionWrapper Connection = null;
        Future ActiveSearch = null;
        Future ActiveQueue = null;
        SearchQuery ActiveSearchQuery = null;
        SearchQuery PendingSearchQuery = null;
        ListViewItem DefaultListItem = new ListViewItem(new string[2]);
        IList<SearchResult> SearchResults = new List<SearchResult>();

        float LineNumberWidth;
        float LineHeight;

        public const int SearchBufferSize = 8192;

        public FindInFilesDialog () {
            InitializeComponent();

            string temp = "AaBbIiJjQqYyZz";
            temp = String.Join("\r\n", new string[] { temp, temp, temp, temp });
            lbResults.ItemHeight = TextRenderer.MeasureText(temp, lbResults.Font).Height + 3;
            Size size = TextRenderer.MeasureText("00000", lbResults.Font);
            LineNumberWidth = size.Width;
            LineHeight = size.Height;

            this.Enabled = false;
            this.UseWaitCursor = true;
        }

        public void SetConnection(ConnectionWrapper connection) {
            Connection = connection;
            this.Enabled = true;
            this.UseWaitCursor = false;
        }

        private void SetSearchResults (IList<SearchResult> items) {
            SearchResults = items;

            object o = new object();

            lbResults.BeginUpdate();

            while (lbResults.Items.Count > items.Count)
                lbResults.Items.RemoveAt(lbResults.Items.Count - 1);

            while (lbResults.Items.Count < items.Count)
                lbResults.Items.Add(o);

            lbResults.EndUpdate();
        }

        private string FilterChars (string text, Func<char, bool> filter) {
            char[] chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!filter(chars[i]))
                    chars[i] = ' ';
            return new string(chars);
        }

        private DbTaskIterator BuildQuery(SearchQuery search) {
            var query = Connection.BuildQuery(
                @"SELECT SourceFiles_Path FROM FullText, SourceFiles WHERE " +
                @"FullText.FileText MATCH ? AND " +
                @"FullText.SourceFiles_ID = SourceFiles.SourceFiles_ID"
            );

            var sb = new StringBuilder();
            foreach (var word in search.SearchWords) {
                if (sb.Length > 0)
                    sb.Append(" ");

                var filteredWord = FilterChars(word, (ch) => char.IsLetterOrDigit(ch));
                sb.Append("*");
                sb.Append(filteredWord);
                sb.Append("*");
            }

            return new DbTaskIterator(query, sb.ToString());
        }

        Encoding DetectEncoding (System.IO.Stream stream) {
            var reader = new System.IO.StreamReader(stream, true);
            var buffer = new char[256];
            reader.ReadBlock(buffer, 0, (int)Math.Min(buffer.Length, stream.Length));
            var result = reader.CurrentEncoding;
            stream.Seek(0, System.IO.SeekOrigin.Begin);
            return result;
        }

        IEnumerator<object> SearchInFiles (SearchQuery search, BlockingQueue<string> filenames, Future completionFuture) {
            var searchedFiles = new List<string>();
            var buffer = new List<SearchResult>();
            var sb = new StringBuilder();

            int numFiles = 0;

            while (filenames.Count > 0 || !completionFuture.Completed) {
                var f = filenames.Dequeue();
                yield return f;

                var filename = f.Result as string;

                if (filename == null)
                    continue;
                if (searchedFiles.Contains(filename))
                    continue;

                if (PendingSearchQuery != null)
                    break;

                searchedFiles.Add(filename);

                int lineNumber = 0;
                var lineBuffer = new LineEntry[3];

                var insertResult = (Action)(() => {
                    var item = new SearchResult();
                    item.Filename = filename;
                    item.LineNumber = lineBuffer[1].LineNumber;

                    sb.Remove(0, sb.Length);
                    for (int i = 0; i < 3; i++) {
                        if (lineBuffer[i].Text != null) {
                            var line = lineBuffer[i].Text;
                            if (line.Length > 1024)
                                line = line.Substring(0, 1024);
                            sb.Append(line);
                        }

                        if (i < 2)
                            sb.Append("\r\n");
                    }
                    item.Context = sb.ToString();

                    buffer.Add(item);

                    if ((buffer.Count % 100 == 0) || ((buffer.Count < 20) && (buffer.Count % 5 == 1)))
                        SetSearchResults(buffer);
                });

                var stepSearch = (Action)(() => {
                    string currentLine = lineBuffer[1].Text;

                    if ((currentLine != null) && search.Regex.IsMatch(currentLine))
                        insertResult();
                });

                var insertLine = (Action<LineEntry>)((line) => {
                    lineBuffer[0] = lineBuffer[1];
                    lineBuffer[1] = lineBuffer[2];
                    lineBuffer[2] = line;

                    stepSearch();
                });

                numFiles += 1;
                if (numFiles % 20 == 0) {
                    lblStatus.Text = String.Format("Scanning '{0}'...", filename);
                    if (completionFuture.Completed) {
                        int totalNumFiles = numFiles + filenames.Count;
                        int progress = (numFiles * 1000 / totalNumFiles);

                        if (pbProgress.Value != progress)
                            pbProgress.Value = progress;
                        if (pbProgress.Style != ProgressBarStyle.Continuous)
                            pbProgress.Style = ProgressBarStyle.Continuous;
                    }
                }

                FileDataAdapter adapter = null;
                try {
                    adapter = new FileDataAdapter(filename, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                } catch {
                    if (adapter != null)
                        adapter.Dispose();
                }
                using (adapter) {
                    var encoding = DetectEncoding(adapter.BaseStream);
                    using (var reader = new AsyncTextReader(adapter, encoding, SearchBufferSize)) {
                        while (true) {
                            f = reader.ReadLine();
                            yield return f;

                            lineNumber += 1;
                            string line = f.Result as string;
                            insertLine(new LineEntry { Text = line, LineNumber = lineNumber });

                            if (line == null)
                                break;
                        }
                    }
                }
            }

            SetSearchResults(buffer);
            lblStatus.Text = String.Format("{0} result(s) found.", buffer.Count);
        }

        IEnumerator<object> PerformSearch (SearchQuery search) {
            pbProgress.Style = ProgressBarStyle.Marquee;
            lblStatus.Text = String.Format("Starting search...");
            lbResults.Items.Clear();

            var filenames = new BlockingQueue<string>();
            var completionFuture = new Future();

            using (var fileSearch = Program.Scheduler.Start(
                SearchInFiles(search, filenames, completionFuture),
                TaskExecutionPolicy.RunAsBackgroundTask
            )) {
                using (var iterator = BuildQuery(search)) {
                    var f = Program.Scheduler.Start(iterator.Start());
                    yield return f;

                    if (!f.Failed) {
                        txtSearch.BackColor = SystemColors.Window;

                        while (!iterator.Disposed) {
                            if (PendingSearchQuery != null)
                                break;

                            string filename = iterator.Current.GetString(0);

                            filenames.Enqueue(filename);

                            yield return iterator.MoveNext();
                        }
                    } else {
                        txtSearch.BackColor = ErrorColor;
                    }
                }

                completionFuture.Complete();

                while (filenames.Count < 0)
                    filenames.Enqueue(null);

                yield return fileSearch;
            }

            if (PendingSearchQuery != null) {
                yield return BeginSearch();
            } else {
                pbProgress.Value = 0;
                pbProgress.Style = ProgressBarStyle.Continuous;
            }
        }

        IEnumerator<object> BeginSearch () {
            ActiveSearchQuery = PendingSearchQuery;
            PendingSearchQuery = null;
            ActiveSearch = Program.Scheduler.Start(
                PerformSearch(ActiveSearchQuery),
                TaskExecutionPolicy.RunAsBackgroundTask
            );
            yield break;
        }

        IEnumerator<object> QueueNewSearch (string searchText) {
            if ((searchText ?? "").Trim().Length == 0)
                yield break;

            SearchQuery search = null;
            try {
                search = new SearchQuery(searchText);
                txtSearch.BackColor = SystemColors.Window;
            } catch {
                txtSearch.BackColor = ErrorColor;
                yield break;
            }

            ActiveQueue = null;
            PendingSearchQuery = search;

            if ((ActiveSearch == null) || (ActiveSearch.Completed)) {
                yield return BeginSearch();
            }
        }

        private void txtFilter_TextChanged (object sender, EventArgs e) {
            SearchParametersChanged();
        }

        private void SearchDialog_FormClosing (object sender, FormClosingEventArgs e) {
            if (Connection != null) {
                Connection.Dispose();
                Connection = null;
            }
            if (ActiveSearch != null) {
                ActiveSearch.Dispose();
                ActiveSearch = null;
            }
            if (ActiveQueue != null) {
                ActiveQueue.Dispose();
                ActiveQueue = null;
            }
        }

        private void SearchParametersChanged() {
            string searchText = txtSearch.Text.Trim();

            ActiveQueue = Program.Scheduler.Start(
                QueueNewSearch(searchText),
                TaskExecutionPolicy.RunAsBackgroundTask
            );
        }

        private void txtSearch_KeyDown(object sender, KeyEventArgs e) {
            if (e.KeyCode == Keys.Enter) {
                e.Handled = true;
                e.SuppressKeyPress = true;
                OpenItem(lbResults.SelectedIndex);
            }
        }

        private void SearchDialog_FormClosed (object sender, FormClosedEventArgs e) {
            this.Dispose();
        }

        private void lbResults_DrawItem (object sender, DrawItemEventArgs e) {
            e.DrawBackground();
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            if ((e.Index < 0) || (e.Index >= SearchResults.Count))
                return;

            var search = ActiveSearchQuery;
            if (search == null)
                return;

            using (var backgroundBrush = new SolidBrush(selected ? SystemColors.Highlight : lbResults.BackColor))
            using (var brush = new SolidBrush(selected ? SystemColors.HighlightText : lbResults.ForeColor)) {
                var item = SearchResults[e.Index];
                var format = StringFormat.GenericTypographic;

                format.LineAlignment = StringAlignment.Near;
                format.FormatFlags = StringFormatFlags.NoWrap;

                format.Trimming = StringTrimming.EllipsisCharacter;
                format.Alignment = StringAlignment.Near;
                var rect = new RectangleF(e.Bounds.X, e.Bounds.Y + 1, e.Bounds.Width - LineNumberWidth, LineHeight);
                e.Graphics.DrawString(item.Filename, lbResults.Font, brush, rect, format);

                format.Trimming = StringTrimming.Character;
                format.Alignment = StringAlignment.Far;
                rect = new RectangleF(e.Bounds.X, e.Bounds.Y + 1, e.Bounds.Width, LineHeight);
                e.Graphics.DrawString(item.LineNumber.ToString(), lbResults.Font, brush, rect, format);

                string context = item.Context;
                MatchEvaluator blankEvaluator = (m) => new String(' ', m.Length);
                string cleanContext = search.Regex.Replace(item.Context, blankEvaluator);

                format.Trimming = StringTrimming.None;
                format.Alignment = StringAlignment.Near;
                rect = new RectangleF(e.Bounds.X + 6, e.Bounds.Y + LineHeight + 1, e.Bounds.Width - 6, e.Bounds.Height - LineHeight);

                using (var pen = new Pen(SystemColors.ButtonShadow, 1.0f))
                    e.Graphics.DrawLine(pen, rect.Location, new PointF(rect.Right, rect.Top));

                e.Graphics.DrawString(cleanContext, lbResults.Font, brush, rect, format);

                var matches = search.Regex.Matches(context);
                if (matches.Count > 0) {
                    var ranges = (from m in matches.Cast<Match>() select new CharacterRange(m.Index, m.Length)).ToArray();
                    int blockSize = Math.Min(32, ranges.Length);
                    var temp = new CharacterRange[blockSize];

                    e.Graphics.ResetClip();
                    e.Graphics.ExcludeClip(e.Graphics.Clip);

                    for (int i = 0; i < ranges.Length; i += 32) {
                        Array.Copy(ranges, i, temp, 0, Math.Min(blockSize, ranges.Length - i));
                        format.SetMeasurableCharacterRanges(temp);

                        foreach (var region in e.Graphics.MeasureCharacterRanges(item.Context, lbResults.Font, rect, format))
                            e.Graphics.SetClip(region, System.Drawing.Drawing2D.CombineMode.Union);
                    }

                    using (var highlightBrush = new SolidBrush(SystemColors.Highlight))
                    using (var highlightTextBrush = new SolidBrush(SystemColors.HighlightText)) {
                        e.Graphics.FillRectangle(highlightBrush, rect);
                        e.Graphics.DrawString(context, lbResults.Font, highlightTextBrush, rect, format);
                    }

                    e.Graphics.ResetClip();
                }
            }

            e.DrawFocusRectangle();
        }

        void OpenItem (int index) {
            SearchResult item;
            try {
                item = SearchResults[index];
            } catch {
                return;
            }

            using (var director = Program.GetDirector()) {
                director.OpenFile(item.Filename, item.LineNumber);
                director.BringToFront();
            }
        }

        private void lbResults_MouseDoubleClick (object sender, MouseEventArgs e) {
            int index = lbResults.IndexFromPoint(e.X, e.Y);
            OpenItem(index);
        }
    }
}
