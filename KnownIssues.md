# Issues #
  * Sometimes a search will take an unusually long time to begin returning results. This may be a bug in NDexer, or a symptom of some internal sqlite structure being paged out of active memory. Occasionally if your search seems to be 'stuck' you can erase the contents of the search field and type your search again to begin getting results.
  * Occasionally a database transaction in NDexer will 'hang', leaving the index database locked and preventing any new search dialogs you open from performing searches. The only reliable fix for this is restarting NDexer.
  * Search dialogs tend to be somewhat unresponsive when NDexer is performing an index update.
  * NDexer sometimes will spend multiple seconds at maximum CPU utilization if a large number of files/folders are deleted from an indexed folder at once.
  * Automatic re-indexing of changed files is not configurable, which means that you may have to wait up to a minute to see the index updated with changes to a file after you modify it.
# Missing features #
  * Find in Files should ideally have options for filtering by file type and folder, in addition to basic search configuration options like case sensitivity.
  * Support for adding non-code files to your index in order to enable using Find Files or Find in Files to search them, without having ctags index them, would be extremely useful.
  * A 'project' window similar to that in Source Insight or Eclipse would be of particular value for many users, especially if it could automatically dock to the side of your text editor's window, like a docking panel.