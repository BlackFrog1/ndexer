﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Data.SQLite;
using Squared.Task;

namespace Ndexer {
    public partial class SearchDialog : Form {
        enum SearchMode {
            None = -1,
            FindTags = 0,
            FindFiles = 1
        }

        class ColumnInfo {
            public Func<int, int> CalculateWidth;
            public Func<SearchResult, string> GetColumnValue;
        }

        struct SearchResult {
            public string Name;
            public string Filename;
            public long LineNumber;
        }

        TagDatabase Tags;
        ConnectionWrapper Connection;
        Future ActiveSearch = null;
        Future ActiveQueue = null;
        string ActiveSearchText = null;
        SearchMode ActiveSearchMode = SearchMode.None;
        string PendingSearchText = null;
        SearchMode PendingSearchMode = SearchMode.None;
        ListViewItem DefaultListItem = new ListViewItem(new string[3]);
        SearchResult[] SearchResults = new SearchResult[0];
        SearchMode DisplayedSearchMode = SearchMode.None;

        public SearchDialog (TagDatabase tags) {
            Tags = tags;
            Connection = tags.OpenReadConnection();

            InitializeComponent();

            lvResults_SizeChanged(lvResults, EventArgs.Empty);
        }

        private int CalculateLineNumberSize() {
            return TextRenderer.MeasureText("00000", lvResults.Font).Width;
        }

        private ColumnHeader[] GetColumnsForMode (SearchMode searchMode) {
            switch (searchMode) {
                case SearchMode.FindTags:
                    return new ColumnHeader[] {
                        new ColumnHeader() { 
                            Text = "Tag Name", 
                            Tag = new ColumnInfo() { 
                                CalculateWidth = (totalWidth) => ((totalWidth - CalculateLineNumberSize()) * 3 / 7),
                                GetColumnValue = (sr) => (sr.Name)
                            }
                        },
                        new ColumnHeader() { 
                            Text = "File Name", 
                            Tag = new ColumnInfo() { 
                                CalculateWidth = (totalWidth) => ((totalWidth - CalculateLineNumberSize()) * 4 / 7),
                                GetColumnValue = (sr) => (sr.Filename)
                            }
                        },
                        new ColumnHeader() { 
                            Text = "Line #", 
                            Tag = new ColumnInfo() { 
                                CalculateWidth = (totalWidth) => (CalculateLineNumberSize()),
                                GetColumnValue = (sr) => (sr.LineNumber.ToString())
                            }
                        },
                    };
                case SearchMode.FindFiles:
                    return new ColumnHeader[] {
                        new ColumnHeader() { 
                            Text = "File Name", 
                            Tag = new ColumnInfo() { 
                                CalculateWidth = (totalWidth) => (totalWidth),
                                GetColumnValue = (sr) => (sr.Filename)
                            }
                        }
                    };
            }

            return new ColumnHeader[0];
        }

        private void SetSearchResults (SearchMode searchMode, SearchResult[] items) {
            SearchResults = items;

            if (searchMode != DisplayedSearchMode) {
                var columns = GetColumnsForMode(searchMode);
                DisplayedSearchMode = searchMode;
                lvResults.VirtualListSize = 0;
                lvResults.Columns.Clear();
                lvResults.Columns.AddRange(columns);
                lvResults_SizeChanged(null, EventArgs.Empty);
            }
            
            lvResults.VirtualListSize = items.Length;

            if ((lvResults.SelectedIndices.Count == 0) && (items.Length > 0))
                lvResults.SelectedIndices.Add(0);
        }

        private DbTaskIterator BuildQuery (SearchMode searchMode, string searchText) {
            switch (searchMode) {
                case SearchMode.FindTags: {
                        var query = Connection.BuildQuery(
                            @"SELECT Tags_Name, SourceFiles_Path, Tags_LineNumber " +
                            @"FROM Tags_And_SourceFiles WHERE " +
                            @"Tags_Name = ? " +
                            @"UNION ALL " +
                            @"SELECT * FROM (SELECT Tags_Name, SourceFiles_Path, Tags_LineNumber " +
                            @"FROM Tags_And_SourceFiles WHERE " +
                            @"Tags_Name GLOB ? LIMIT 1000)"
                        );
                        return new DbTaskIterator(query, searchText, searchText + "?*");
                    }
                case SearchMode.FindFiles: {
                        var query = Connection.BuildQuery(
                            @"SELECT SourceFiles_Path " +
                            @"FROM SourceFiles WHERE " +
                            @"SourceFiles_Path GLOB ? " +
                            @"UNION ALL " +
                            @"SELECT SourceFiles_Path " +
                            @"FROM SourceFiles WHERE " +
                            @"SourceFiles_Path GLOB ?"
                        );
                        return new DbTaskIterator(query, @"*\" + searchText, @"*\" + searchText + "?*");
                    }
            }

            throw new InvalidOperationException();
        }

        IEnumerator<object> PerformSearch (SearchMode searchMode, string searchText) {
            string[] columnValues = new string[3];

            pbProgress.Style = ProgressBarStyle.Marquee;
            lblStatus.Text = String.Format("Starting search...");

            var buffer = new List<SearchResult>();
            var item = new SearchResult();

            SetSearchResults(searchMode, buffer.ToArray());

            if (searchText.Length > 0) {
                using (var iterator = BuildQuery(searchMode, searchText)) {
                    yield return iterator.Start();

                    while (!iterator.Disposed) {
                        if (PendingSearchText != null)
                            break;

                        switch (searchMode) {
                            case SearchMode.FindFiles:
                                item.Filename = iterator.Current.GetString(0);
                                break;
                            case SearchMode.FindTags:
                                item.Name = iterator.Current.GetString(0);
                                item.Filename = iterator.Current.GetString(1);
                                item.LineNumber = iterator.Current.GetInt64(2);
                                break;
                        }

                        buffer.Add(item);

                        if ((buffer.Count % 50 == 0) || ((buffer.Count < 20) && (buffer.Count % 5 == 1))) {
                            lblStatus.Text = String.Format("{0} result(s) found so far...", buffer.Count);
                            SetSearchResults(searchMode, buffer.ToArray());
                        }

                        yield return iterator.MoveNext();
                    }
                }
            }

            if (PendingSearchText != null) {
                yield return BeginSearch();
            } else {
                SetSearchResults(searchMode, buffer.ToArray());
                lblStatus.Text = String.Format("{0} result(s) found.", buffer.Count);
                pbProgress.Style = ProgressBarStyle.Continuous;
            }
        }

        IEnumerator<object> BeginSearch () {
            ActiveSearchText = PendingSearchText;
            ActiveSearchMode = PendingSearchMode;
            PendingSearchText = null;
            PendingSearchMode = SearchMode.None;
            ActiveSearch = Program.Scheduler.Start(
                PerformSearch(ActiveSearchMode, ActiveSearchText),
                TaskExecutionPolicy.RunWhileFutureLives
            );
            yield break;
        }

        IEnumerator<object> QueueNewSearch (SearchMode searchMode, string searchText) {
            ActiveQueue = null;
            PendingSearchMode = searchMode;
            PendingSearchText = searchText;

            if ((ActiveSearch == null) || (ActiveSearch.Completed)) {
                yield return BeginSearch();
            }
        }

        private void txtFilter_TextChanged (object sender, EventArgs e) {
            SearchParametersChanged();
        }

        private void lvResults_SizeChanged (object sender, EventArgs e) {
            int totalSize = lvResults.ClientSize.Width - 2;
            for (int i = 0; i < lvResults.Columns.Count; i++)
                lvResults.Columns[i].Width = (lvResults.Columns[i].Tag as ColumnInfo).CalculateWidth(totalSize);
        }

        private void lvResults_DoubleClick (object sender, EventArgs e) {
            SearchResult item;
            try {
                item = SearchResults[lvResults.SelectedIndices[0]];
            } catch {
                return;
            }
            try {
                using (var director = new SciTEDirector()) {
                    switch (DisplayedSearchMode) {
                        case SearchMode.FindFiles:
                            director.OpenFile(item.Filename);
                            director.BringToFront();
                            break;
                        case SearchMode.FindTags:
                            director.OpenFile(item.Filename, item.LineNumber);
                            director.FindText(item.Name);
                            director.BringToFront();
                            break;
                    } 
                }
            } catch (SciTENotRunningException) {
                MessageBox.Show(this, "SciTE not running", "Error");
            }
        }

        private void lvResults_DrawSubItem (object sender, DrawListViewSubItemEventArgs e) {
            if (e.ColumnIndex == 0) {
                e.DrawDefault = true;
                return;
            }

            if (e.Item.Selected)
                using (var backgroundBrush = new SolidBrush(lvResults.Focused ? SystemColors.Highlight : SystemColors.ButtonFace))
                    e.Graphics.FillRectangle(backgroundBrush, e.Bounds.X, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height);

            var textColor = lvResults.ForeColor;
            if (e.Item.Selected && lvResults.Focused)
                textColor = SystemColors.HighlightText;

            using (var textBrush = new SolidBrush(textColor)) {
                var textRect = new RectangleF(e.Bounds.Left, e.Bounds.Top, e.Bounds.Width, e.Bounds.Height);
                var stringFormat = StringFormat.GenericDefault;
                stringFormat.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.FitBlackBox;
                stringFormat.Trimming = StringTrimming.EllipsisPath;
                e.Graphics.DrawString(e.SubItem.Text, lvResults.Font, textBrush, textRect, stringFormat);
            }
        }

        private void lvResults_RetrieveVirtualItem (object sender, RetrieveVirtualItemEventArgs e) {
            if ((e.ItemIndex < 0) || (e.ItemIndex >= SearchResults.Length))
                e.Item = DefaultListItem;
            else {
                SearchResult item = SearchResults[e.ItemIndex];
                string[] subitems = new string[lvResults.Columns.Count];
                for (int i = 0; i < lvResults.Columns.Count; i++)
                    subitems[i] = (lvResults.Columns[i].Tag as ColumnInfo).GetColumnValue(item);
                e.Item = new ListViewItem(subitems);
            }
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

        private void tcFilter_SelectedIndexChanged(object sender, EventArgs e) {
            SearchParametersChanged();
        }

        private void SearchParametersChanged() {
            var searchMode = (SearchMode)tcFilter.SelectedIndex;
            string searchText = txtFilter.Text.Trim();

            ActiveQueue = Program.Scheduler.Start(
                QueueNewSearch(searchMode, searchText),
                TaskExecutionPolicy.RunAsBackgroundTask
            );
        }
    }
}