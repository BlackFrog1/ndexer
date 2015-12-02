

# Configuration #

## Configuring file formats ##
By default, NDexer will not index any file formats. You can add formats to index using the **Add** button in the configuration dialog, right under the _File Types_ list.

![http://ndexer.googlecode.com/svn/wiki/screenshots/ndexer_configuration_add_filter_button.png](http://ndexer.googlecode.com/svn/wiki/screenshots/ndexer_configuration_add_filter_button.png)

Clicking it will bring up this dialog box:

![http://ndexer.googlecode.com/svn/wiki/screenshots/ndexer_add_filter.png](http://ndexer.googlecode.com/svn/wiki/screenshots/ndexer_add_filter.png)

The _Language_ dropdown lists all the programming languages and file formats supported by ctags. Selecting one will automatically populate the _Filter_ box, below, with ctags' default filters for that language.

To add a file format, just select the language you want it to be indexed as, and then type one or more filters that will match the format in the box below. You can separate multiple filters with spaces or semicolons. After you're done, hit OK, and the file format(s) will be added to the list in the configuration dialog. Once you close out the configuration dialog with an **OK**, your index will be rebuilt to include the new file format(s).

You can also remove existing file formats from the list by selecting them and clicking **Remove**.

![http://ndexer.googlecode.com/svn/wiki/screenshots/ndexer_configuration_remove_filter_button.png](http://ndexer.googlecode.com/svn/wiki/screenshots/ndexer_configuration_remove_filter_button.png)

Removing a file format removes any matching files from your index automatically.

## Configuring folders ##
NDexer will only add files to its index that live in the folders you specify to index. Folders you specify will also be monitored for changes (instead of monitoring your entire hard disk, which would be ridiculously inefficient).

You can add and remove folders to index using the **Add** and **Remove** buttons in the _Folders_ section of the configuration dialog. Once you apply your changes, your index will automatically be rebuilt to include any new folders and exclude old ones.

## Configuring text editors ##
NDexer has built-in support for integrating with a few different text editors. You can select the text editor you wish to use from the configuration dialog's _Text Editor_ section.

![http://ndexer.googlecode.com/svn/wiki/screenshots/ndexer_configuration_text_editor.png](http://ndexer.googlecode.com/svn/wiki/screenshots/ndexer_configuration_text_editor.png)

Just select the editor of your choice from the dropdown, and then make sure the text box contains the path to the selected editor's executable file (so NDexer can launch it for you as needed).

# Searching #
## Finding an identifier that starts with a word or phrase ##
Open the **Search** dialog, either by selecting **Search Tags** from the context menu, or double-clicking the NDexer icon. Select **Find Tags**, and type the word/phrase into the search field.
## Finding an identifier that ends with a word or phrase ##
You can find identifiers that **end** with a word or phrase by prepending an asterisk (`*`) onto the search text.
## Finding the identifiers defined in a class or function ##
Open the **Search** dialog, select **Tags in Context**, and type the full name of the class or function.
## Finding the identifiers defined in a specific file ##
Open the **Search** dialog, select **Tags in File** and type the name of the file. Any files that have paths ending with the name you enter will be checked. You can use asterisks to expand your search, as well.
## Getting the name of files from the index ##
Open the **Search** dialog, select **Find Files**, and type in part or all of the name of the file. Any files that have paths containing the phrase you enter will be returned.
## Searching for a phrase or identifier in your source code ##
More advanced searches can be performed by opening the **Search Files** dialog from the context menu. Type in the phrase or code snippet you want to search for and any matching lines from your indexed files will be shown, along with surrounding lines for context.