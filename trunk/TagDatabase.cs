﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.SQLite;
using Squared.Task;
using System.Data;

namespace Ndexer {
    public class TagDatabase : IDisposable {
        public struct Filter {
            public long ID;
            public string Pattern;
            public string Language;
        }

        public struct Change {
            public string Filename;
            public bool Deleted;
        }

        public struct Folder {
            public long ID;
            public string Path;
        }

        public struct SourceFile {
            public long ID;
            public string Path;
            public long Timestamp;
        }

        private Query
            _GetContextID,
            _GetKindID,
            _GetLanguageID,
            _GetSourceFileID,
            _GetSourceFileTimestamp,
            _GetPreference,
            _SetPreference,
            _MakeContextID,
            _MakeKindID,
            _MakeLanguageID,
            _MakeSourceFileID,
            _DeleteTagsForFile,
            _DeleteSourceFile,
            _DeleteTagsForFolder,
            _DeleteSourceFilesForFolder,
            _LastInsertID,
            _InsertTag;

        private Dictionary<string, Dictionary<string, object>> _MemoizationCache = new Dictionary<string, Dictionary<string, object>>();

        public TaskScheduler Scheduler;
        public SQLiteConnection NativeConnection;
        public ConnectionWrapper Connection;

        public TagDatabase (TaskScheduler scheduler, string filename) {
            Scheduler = scheduler;

            string connectionString = String.Format("Data Source={0}", filename);
            NativeConnection = new SQLiteConnection(connectionString);
            NativeConnection.Open();
            Connection = new ConnectionWrapper(scheduler, NativeConnection);

            CompileQueries();
        }

        private void CompileQueries () {
            _GetContextID = Connection.BuildQuery(@"SELECT TagContexts_ID FROM TagContexts WHERE TagContexts_Text = ?");
            _GetKindID = Connection.BuildQuery(@"SELECT TagKinds_ID FROM TagKinds WHERE TagKinds_Name = ?");
            _GetLanguageID = Connection.BuildQuery(@"SELECT TagLanguages_ID FROM TagLanguages WHERE TagLanguages_Name = ?");
            _GetSourceFileID = Connection.BuildQuery(@"SELECT SourceFiles_ID FROM SourceFiles WHERE SourceFiles_Path = ?");
            _GetSourceFileTimestamp = Connection.BuildQuery(@"SELECT SourceFiles_Timestamp FROM SourceFiles WHERE SourceFiles_Path = ?");
            _GetPreference = Connection.BuildQuery(@"SELECT Preferences_Value FROM Preferences WHERE Preferences_Name = ?");
            _SetPreference = Connection.BuildQuery(@"INSERT OR REPLACE INTO Preferences (Preferences_Name, Preferences_Value) VALUES (?, ?)");
            _LastInsertID = Connection.BuildQuery(@"SELECT last_insert_rowid()");
            _MakeContextID = Connection.BuildQuery(
                @"INSERT INTO TagContexts (TagContexts_Text) VALUES (?);" +
                @"SELECT last_insert_rowid()"
            );
            _MakeKindID = Connection.BuildQuery(
                @"INSERT INTO TagKinds (TagKinds_Name) VALUES (?);" +
                @"SELECT last_insert_rowid()"
            );
            _MakeLanguageID = Connection.BuildQuery(
                @"INSERT INTO TagLanguages (TagLanguages_Name) VALUES (?);" +
                @"SELECT last_insert_rowid()"
            );
            _MakeSourceFileID = Connection.BuildQuery(
                @"INSERT OR REPLACE INTO SourceFiles (SourceFiles_Path, SourceFiles_Timestamp) VALUES (?, ?);" +
                @"SELECT last_insert_rowid()"
            );
            _DeleteTagsForFile = Connection.BuildQuery(@"DELETE FROM Tags WHERE SourceFiles_ID = ?");
            _DeleteSourceFile = Connection.BuildQuery(@"DELETE FROM SourceFiles WHERE SourceFiles_ID = ?");
            _DeleteTagsForFolder = Connection.BuildQuery(
                @"DELETE FROM Tags WHERE " +
                @"Tags.SourceFiles_ID IN ( " +
                @"SELECT SourceFiles_ID FROM SourceFiles WHERE " +
                @"SourceFiles.SourceFiles_Path LIKE ? )"
            );
            _DeleteSourceFilesForFolder = Connection.BuildQuery(@"DELETE FROM SourceFiles WHERE SourceFiles_Path LIKE ?");
            _InsertTag = Connection.BuildQuery(
                @"INSERT INTO Tags (" +
                @"Tags_Name, SourceFiles_ID, Tags_LineNumber, TagKinds_ID, TagContexts_ID, TagLanguages_ID" +
                @") VALUES (" +
                @"?, ?, ?, ?, ?, ?" +
                @");" +
                @"SELECT last_insert_rowid()"
            );
        }

        public IEnumerator<object> Compact () {
            yield return Connection.ExecuteSQL(
                @"DELETE FROM TagContexts WHERE (" +
                @"SELECT COUNT(*) FROM Tags WHERE " +
                @"TagContexts.TagContexts_ID = Tags.TagContexts_ID ) < 1"
            );

            yield return Connection.ExecuteSQL(
                @"VACUUM"
            );
        }

        public ConnectionWrapper OpenReadConnection () {
            var conn = new SQLiteConnection(NativeConnection.ConnectionString + ";Read Only=True");
            conn.Open();
            return new ConnectionWrapper(
                Scheduler,
                conn
            );
        }

        public IEnumerator<object> GetFilters () {
            var filter = new Filter();

            using (var conn = OpenReadConnection())
            using (var query = conn.BuildQuery(@"SELECT Filters_ID, Filters_Pattern, Filters_Language FROM Filters"))
            using (var iter = new DbTaskIterator(query)) {
                yield return iter.Start();

                while (!iter.Disposed) {
                    var item = iter.Current;

                    filter.ID = item.GetInt64(0);
                    filter.Pattern = item.GetString(1);
                    filter.Language = item.GetString(2);
                    yield return new NextValue(filter);

                    yield return iter.MoveNext();
                }
            }
        }

        public IEnumerator<object> GetFolders () {
            var folder = new Folder();

            using (var conn = OpenReadConnection())
            using (var query = conn.BuildQuery(@"SELECT Folders_ID, Folders_Path FROM Folders"))
            using (var iter = new DbTaskIterator(query)) {
                yield return iter.Start();

                while (!iter.Disposed) {
                    var item = iter.Current;

                    folder.ID = item.GetInt64(0);
                    folder.Path = item.GetString(1);
                    yield return new NextValue(folder);

                    yield return iter.MoveNext();
                }
            }
        }

        public IEnumerator<object> GetSourceFiles () {
            var sf = new SourceFile();

            using (var conn = OpenReadConnection())
            using (var query = conn.BuildQuery(@"SELECT SourceFiles_ID, SourceFiles_Path, SourceFiles_Timestamp FROM SourceFiles"))
            using (var iter = new DbTaskIterator(query)) {
                yield return iter.Start();

                while (!iter.Disposed) {
                    var item = iter.Current;

                    sf.ID = item.GetInt64(0);
                    sf.Path = item.GetString(1);
                    sf.Timestamp = item.GetInt64(2);
                    yield return new NextValue(sf);

                    yield return iter.MoveNext();
                }
            }
        }

        public IEnumerator<object> GetPreference (string name) {
            var f = _GetPreference.ExecuteScalar(name);
            yield return f;
            yield return new Result(f.Result);
        }

        public IEnumerator<object> SetPreference (string name, string value) {
            var f = _SetPreference.ExecuteScalar(name, value);
            yield return f;
        }

        public IEnumerator<object> DeleteSourceFile (string filename) {
            FlushMemoizedIDs();

            var f = _GetSourceFileID.ExecuteScalar(filename);
            yield return f;

            if (f.Result is long) {
                yield return _DeleteTagsForFile.ExecuteNonQuery(f.Result);
                yield return _DeleteSourceFile.ExecuteNonQuery(f.Result);
            }
        }

        public IEnumerator<object> DeleteTagsForFile (string filename) {
            FlushMemoizedIDs();

            var f = _GetSourceFileID.ExecuteScalar(filename);
            yield return f;

            if (f.Result is long)
                yield return _DeleteTagsForFile.ExecuteNonQuery(f.Result);
        }

        public IEnumerator<object> DeleteSourceFileOrFolder (string filename) {
            FlushMemoizedIDs();

            var f = _GetSourceFileID.ExecuteScalar(filename);
            yield return f;

            if (f.Result is long) {
                yield return _DeleteTagsForFile.ExecuteNonQuery(f.Result);
                yield return _DeleteSourceFile.ExecuteNonQuery(f.Result);
            } else {
                if (!filename.EndsWith("\\"))
                    filename += "\\";
                filename += "%";

                yield return _DeleteTagsForFolder.ExecuteNonQuery(filename);
                yield return _DeleteSourceFilesForFolder.ExecuteNonQuery(filename);
            }
        }

        public IEnumerator<object> GetFilterPatterns () {
            var iter = new TaskIterator<Filter>(GetFilters());
            yield return iter.Start();
            var f = iter.ToArray();

            yield return f;

            string[] filters = (from _ in (Filter[])f.Result select _.Pattern).ToArray();

            yield return new Result(filters);
        }

        public IEnumerator<object> GetFolderPaths () {
            var iter = new TaskIterator<Folder>(GetFolders());
            yield return iter.Start();
            var f = iter.ToArray();

            yield return f;

            string[] folders = (from _ in (Folder[])f.Result select _.Path).ToArray();

            yield return new Result(folders);
        }

        public IEnumerator<object> UpdateFileListAndGetChangeSet () {
            var rtc = new RunToCompletion(GetFilterPatterns());
            yield return rtc;
            var filters = String.Join(";", (string[])rtc.Result);

            rtc = new RunToCompletion(GetFolderPaths());
            yield return rtc;
            var folders = (string[])rtc.Result;

            SourceFile[] sourceFiles;

            using (var iterator = new TaskIterator<SourceFile>(GetSourceFiles())) {
                yield return iterator.Start();
                var ta = iterator.ToArray();
                yield return ta;
                sourceFiles = (SourceFile[])ta.Result;
            }

            foreach (var file in sourceFiles) {
                bool validFolder = false;
                foreach (var folder in folders) {
                    if (file.Path.StartsWith(folder)) {
                        validFolder = true;
                        break;
                    }
                }

                if (!validFolder || !System.IO.File.Exists(file.Path))
                    yield return new NextValue(
                        new Change { Filename = file.Path, Deleted = true }
                    );
            }

            foreach (var folder in folders) {
                var enumerator = Squared.Util.IO.EnumDirectoryEntries(
                    folder, filters, true, Squared.Util.IO.IsFile
                );

                var dirEntries = enumerator.GetTaskIterator();
                yield return dirEntries.Start();

                while (!dirEntries.Disposed) {
                    var entry = dirEntries.Current;

                    long newTimestamp = entry.LastWritten;
                    long oldTimestamp = 0;

                    rtc = new RunToCompletion(GetSourceFileTimestamp(entry.Name));
                    yield return rtc;
                    if (rtc.Result is long)
                        oldTimestamp = (long)rtc.Result;

                    if (newTimestamp > oldTimestamp)
                        yield return new NextValue(
                            new Change { Filename = entry.Name, Deleted = false }
                        );

                    yield return dirEntries.MoveNext();
                }
            }

            yield break;
        }

        public IEnumerator<object> GetSourceFileTimestamp (string path) {
            var f = _GetSourceFileTimestamp.ExecuteScalar(path);
            yield return f;
            yield return new Result(f.Result);
        }

        public IEnumerator<object> GetSourceFileID (string path) {
            var f = _GetSourceFileID.ExecuteScalar(path);
            yield return f;

            if (f.Result is long) {
                yield return new Result(f.Result);
            } else {
                var rtc = new RunToCompletion(MakeSourceFileID(path, 0));
                yield return rtc;
                yield return new Result(rtc.Result);
            }
        }

        internal IEnumerator<object> MakeSourceFileID (string path, long timestamp) {
            var f = _MakeSourceFileID.ExecuteScalar(path, timestamp);
            yield return f;
            yield return new Result(f.Result);
        }

        public IEnumerator<object> GetKindID (string kind) {
            if (kind == null)
                yield return new Result(0);

            var f = _GetKindID.ExecuteScalar(kind);
            yield return f;

            if (f.Result is long) {
                yield return new Result(f.Result);
            } else {
                f = _MakeKindID.ExecuteScalar(kind);
                yield return f;
                yield return new Result(f.Result);
            }
        }

        public IEnumerator<object> GetContextID (string context) {
            if (context == null)
                yield return new Result(0);

            var f = _GetContextID.ExecuteScalar(context);
            yield return f;

            if (f.Result is long) {
                yield return new Result(f.Result);
            } else {
                f = _MakeContextID.ExecuteScalar(context);
                yield return f;
                yield return new Result(f.Result);
            }
        }

        public IEnumerator<object> GetLanguageID (string language) {
            if (language == null)
                yield return new Result(0);

            var f = _GetLanguageID.ExecuteScalar(language);
            yield return f;

            if (f.Result is long) {
                yield return new Result(f.Result);
            } else {
                f = _MakeLanguageID.ExecuteScalar(language);
                yield return f;
                yield return new Result(f.Result);
            }
        }

        public void FlushMemoizedIDs () {
            _MemoizationCache.Clear();
        }

        public IEnumerator<object> MemoizedGetID (Func<string, IEnumerator<object>> task, string argument) {
            if (argument == null)
                yield return new Result(0);

            string taskName = task.Method.Name;
            Dictionary<string, object> resultCache = null;
            if (!_MemoizationCache.TryGetValue(taskName, out resultCache)) {
                resultCache = new Dictionary<string, object>();
                _MemoizationCache[taskName] = resultCache;
            }

            object result = null;
            if (resultCache.TryGetValue(argument, out result)) {
                yield return new Result(result);
            } else {
                var rtc = new RunToCompletion(task(argument));
                yield return rtc;
                result = rtc.Result;
                if (resultCache.Count > 256)
                    resultCache.Clear();

                resultCache[argument] = result;
            }
        }

        public IEnumerator<object> AddTag (Tag tag) {
            var rtc = new RunToCompletion(MemoizedGetID(GetSourceFileID, tag.SourceFile));
            yield return rtc;
            var sourceFileID = Convert.ToInt64(rtc.Result);

            rtc = new RunToCompletion(MemoizedGetID(GetKindID, tag.Kind));
            yield return rtc;
            var kindID = Convert.ToInt64(rtc.Result);

            rtc = new RunToCompletion(MemoizedGetID(GetContextID, tag.Context));
            yield return rtc;
            var contextID = Convert.ToInt64(rtc.Result);

            rtc = new RunToCompletion(MemoizedGetID(GetLanguageID, tag.Language));
            yield return rtc;
            var languageID = Convert.ToInt64(rtc.Result);

            var f = _InsertTag.ExecuteScalar(
                tag.Name, sourceFileID, tag.LineNumber,
                kindID, contextID, languageID
            );
            yield return f;
            yield return new Result(f.Result);
        }

        public IEnumerator<object> Clear () {
            yield return Connection.ExecuteSQL("DELETE FROM Tags");
        }

        public void Dispose () {
            Connection.Dispose();
            NativeConnection.Dispose();
        }
    }
}
