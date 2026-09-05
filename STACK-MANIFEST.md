# Stack manifest

This build is NOT a stock release. It is upstream canary plus unmerged patches.

    base:        a630572e983614a52ea409a23da52a99e3b8b91b
    base short:  a630572e
    patches:     87
    version:     1.3.4+m4bard.87

## Patches, oldest first

    380789ad  fix(naming): don't lose a series position that isn't a plain number
    36ff11f2  fix(metadata): populate SeriesAsin from the Audnexus series record
    5398b596  fix(ffmpeg): point macOS at evermeet's ffprobe archive, not its ffmpeg one
    997df9bd  fix(qbittorrent): one unreadable torrent should not truncate the queue poll
    2a582a96  manual-import: authorize companion files against the root folder, not the book folder
    fac10d62  tests: pin the managed boundary the companion pass hands the ownership store
    e4d160b9  tests: adapt the boundary test to the publication path #864 introduced
    9078b687  tests: give the out-of-root case the roots the pass now needs
    e45e97d9  fix(import): actually embed the ASIN after an import
    fe01c830  Group chapter files indexed as "N of M" into one unmatched-scan item
    ae3d159f  Library import: keep every series membership, not just the first
    b1d15796  fix(scan): stop the Linux descriptor path leaking into metadata and size
    bae9a510  tests: follow the scan's metadata read onto the two-part file source
    7c2cfde6  fix(scan): narrow to the metadata boundary, leave the size to #901
    e98d243d  fix(scoring): resolve indexers once per batch, not once per result in parallel
    5a7acc28  fix(recovery): name every legacy journal blocking startup, not just the first
    5e59e6b1  fix(download-clients): one circuit breaker per client, not one for all of them
    19a679b6  fix(manual-import): match the rename naming table on casing and missing tokens
    f60b8b3e  fix(downloads): resolve a client's path mappings once per batch, not per item
    b2fe80a8  fix(downloads): translate a queue item's source files from the resolved mappings too
    3aae48e8  fix(prowlarr-import): build proxy URLs from the base Prowlarr answered on
    53e35d9c  Serve under a URL sub-path by honouring the UrlBase that already exists
    fefa6a35  feat(notifications): give notifications their own ApplicationUrl
    c1b547c6  Record a failed grab in history when the download client rejects it
    5f77415b  fix(metadata): order series positions with an invariant parse
    9498e2c7  tests: bring the series-position test up to the convention #717 added
    35369fa6  fix(parsing): pin machine-format number parses to the invariant culture
    afbfc5d5  fix(qbittorrent): tell a refused release apart from a failed submission
    f684beba  feat(settings): add a URL Base field to General settings
    94310fe9  fix(audible): tell a failed catalog lookup apart from a confirmed zero-match
    6aa7a916  feat(import): embed cover art into imported files, behind a setting
    280bd72a  fix(filesystem): record why a file mutation failed, not only that it did
    26154ff5  fix(downloads): make retry-import actually requeue the import
    d09609b4  feat(downloads): blocklist a release that failed, so the retry stops
    641a11df  fix(downloads): key a blocked release on something that survives a re-grab
    947739b3  fix(search): consult the blocklist on the path that actually re-grabs
    fd5ca8d7  fix(downloads): give the three download-finalization settings a reader again
    073a8a86  fix(quality-profiles): stop hiding Maximum Age, and load a profile's qualities
    caf2bea2  fix(search): read Indexer.MinimumAge and Indexer.MaximumSize, and measure age in UTC
    3960cbd7  fix(download-clients): make the Priority dropdown speak the planners' vocabulary
    f0ea6d81  test(images): assert that a cached image is served, not that something happened
    f996bc02  feat(library): render the list view when grouping by author or series
    4379b60e  fix(qbittorrent): send the four Advanced Settings to the client
    dc69d0b7  fix(filesystem): say why a source file could not be pinned
    70c8c7c2  fix(imports): retry a failed file import instead of blocking on the first attempt
    7114ca8f  fix(quality): let a real encoder bitrate reach the rung it was encoded for
    1e892911  fix(docker): give the image a healthcheck it can actually run
    dc2d95c4  Make the frontend follow the UrlBase by injecting a <base href> into the shell
    11349317  fix(spa): anchor the shell at the site root even with no UrlBase
    ded206af  stack: bring the stale #786 test class up to the BaseTests convention
    988d3863  stack: let a local build stamp its own version
    684081fe  stack: publish the patched build to GHCR from the fork
    fad07d9b  fix(search): one indexer timeout no longer discards every other indexer's results
    c8dc71fe  stack: split the indexer context out of SearchResultScorer
    b0827cc7  fix(files): physical-generation snapshots reject database-loaded observation timestamps
    f8316433  fix(persistence): restore the UTC contract for PhysicalIdentityObservedAtUtc at EF materialization
    c6146a97  feat(wanted): add a cutoff-unmet bucket to the Wanted page
    17e9dd73  fix(collection): show an author's books under their membership series
    5a267148  feat(library): count the distinct series an author appears in
    397357eb  feat(downloads): implement the three reprocess endpoints instead of returning success
    37c94c0a  fix(downloads): refuse an ineligible reprocess instead of throwing at the caller
    c97a689f  feat(activity): sort the queue, and show when an item was added
    cf68797f  fix(collection): apply the language preference to library books, not just suggestions
    623e9a2a  fix(downloads): work out a release identity once, at the grab, and store it
    cbfd3c84  test(downloads): fail the build when a second place works out a release key
    d183e914  fix(search): the minimum seeders gate never fired on a real torrent
    fca59212  fix(import): match blacklisted extensions without regard to case
    69488295  fix(qbittorrent): map 4.x paused torrent states alongside 5.x stopped states
    874a09fd  fix(ui): match System recent-log severity classes to the stylesheet
    d5c2514f  fix(notifications): dispatch webhooks for the triggers the settings screen offers
    5cbb27bd  fix(search): derive one indexer query title, shared by both search paths
    8ef01a00  fix(downloads): give the queue a control that removes a terminal download
    228c2ff6  fix(downloads): tie a processing job's retention to the download it explains
    cfff12c0  fix(authors): stop attributing one author's ASIN to every co-author
    a7ac2f1c  fix(naming): spell an author's initials one way in folder names
    946ce8e8  fix(system): stop inventing log entries when no log file exists
    55ace0c1  fix(library): stop announcing the list-view status badge as a button
    81eb1bc8  fix(settings): stop a failed clipboard write from reporting a failed regeneration
    bb43fdf3  fix(api): the authentication gate ignored requests that varied the path casing
    71c695ad  fix(filesystem): resolve a symlinked source path instead of refusing it
    d05ee2c8  fix(logging): give silent catch blocks a real log call or a stated reason
    a7aee431  fix(persistence): keep a persisted UTC timestamp UTC when it is read back
    fe8c2ccf  test(scoring): pin the four hand-copied quality ladders to each other
    bcfe9fb3  test(metadata): count the attempts the Audible retry policy actually makes
    548fb195  feat(ui): multi-row selection on Wanted and Downloads
    6944053e  stack: point the multi-select Wanted spec at the renamed search handler
    96a26293  stack: publish a moving current tag beside the immutable commit tag

Regenerate with tools/local_stack.sh in the tracker repo.
