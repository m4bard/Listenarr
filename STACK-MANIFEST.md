# Stack manifest

This build is NOT a stock release. It is upstream canary plus unmerged patches.

    base:        61117b81345b7c3b03d5e0fafc16e6032bec9b51
    base short:  61117b813
    patches:     661
    version:     1.5.11+m4bard.661

## Patches, oldest first

    c73e31b23  #105  fix(naming): don't lose a series position that isn't a plain number
    56ae37717  #105  fix(naming): derive both series-position fields from one source
    03307a0a8  #106  fix(metadata): populate SeriesAsin from the Audnexus series record
    b602d7496  #106  fix(metadata): carry the Audnexus series list, with identifiers, through the product lookup
    9a70a787f  #106  test(metadata): pin what a blank Audnexus series ASIN stores
    e67bdfa9c  #43   fix(ffmpeg): point macOS at evermeet's ffprobe archive, not its ffmpeg one
    ff9808406  #23   fix(qbittorrent): one unreadable torrent should not truncate the queue poll
    a9e116809  #23   fix(qbittorrent): let a client that stops answering fail the poll
    767c9abe2  #23   fix(qbittorrent): quieten the per-torrent skip and escape the hash
    b8613c1a9  #24   fix(import): actually embed the ASIN after an import
    bde8e3786  #24   comment: drop the em dash from the read-write stream note
    020ea0fe5  #24   tests: drive ApplyAsinTag through a stub TagLib.File
    db06977ac  #24   tests: save and reopen a real m4b for the ASIN writer
    54eb9ac49  #24   feat(settings): show the File Metadata Enrichment options, off and unavailable
    ee398c953  #24   chore: exempt only the generated m4b fixture from the media ignore rules
    e6dabe884  #24   fix(import): do not write the ASIN tag through a hardlink to the source
    d5db7515b  #24   tests: pin that the hardlink case really shares the source's inode
    efdbab817  #22   Group chapter files indexed as "N of M" into one unmatched-scan item
    f7ee97cdc  #22   test(scan): make the bare "N of M" case able to fail
    70a8cbb05  #22   fix(scan): leave a Book or Volume index alone when stripping "N of M"
    8d97452ba  #17   Library import: keep every series membership, not just the first
    258dc327b  #17   fix(library-import): build series memberships from the enriched product and fix the unmatched-files mapping
    0cccefc32  #17   fix(library-import): require the B0 prefix before keeping a series ASIN
    7b44c31d3  #11   fix(scan): stop the Linux descriptor path leaking into metadata and size
    24210e77b  #11   tests: follow the scan's metadata read onto the two-part file source
    8db769409  #11   fix(scan): narrow to the metadata boundary, leave the size to #901
    23f93c618  #29   fix(download-clients): one circuit breaker per client, not one for all of them
    fef9b295c  #29   test(download-clients): say what the isolation test does not cover
    95348829f  #42   fix(manual-import): match the rename naming table on casing and missing tokens
    2570ba944  #42   test(manual-import): assert path segments, not substrings
    3bd4f5dc5  #42   fix(manual-import): build every naming key the rename table builds
    edf0ab511  #32   fix(prowlarr-import): build proxy URLs from the base Prowlarr answered on
    c3f7f6a55  #32   fix(prowlarr-import): only adopt a discovery base on the origin that was asked
    9700820a0  #34   Serve under a URL sub-path by honouring the UrlBase that already exists
    8c38bbf8e  #34   feat(notifications): give notifications their own ApplicationUrl
    cad04daba  #34   feat(notifications): read LISTENARR_PUBLIC_URL as the notification base
    373cdbd22  #34   fix(api): set the path base from UrlBase instead of appending to it
    e6603714a  #35   Record a failed grab in history when the download client rejects it
    4883277e1  #35   test(downloads): assert the failure record on the blank-identifier path
    61e7a8593  #35   fix(downloads): sanitize the client message written to history
    40fa6cf9b  #36   fix(metadata): order series positions with an invariant parse
    4899d2e6b  #36   tests: bring the series-position test up to the convention #717 added
    27b9f4b74  #36   fix(metadata): reject a grouped series position instead of misreading it
    931b27587  #36   fix(parsing): pin machine-format number parses to the invariant culture
    0dc1ca156  #36   fix(parsing): match size units and reject non machine-format numbers
    160fb40f2  #35   fix(qbittorrent): tell a refused release apart from a failed submission
    2ec7d1f22  #35   test(downloads): pin the controller half of the 409 change
    a23bf2c38  #35   fix(downloads): answer a refused release with 409 on the manual send path
    89209a6be  #160  fix(config): overlay a posted startup config onto the file instead of replacing it
    cb9ee82fd  #160  fix(fe): do not post a startup config the settings view never loaded
    ec3ce8afd  #64   feat(settings): add a URL Base field to General settings
    101d7151f  #64   feat(settings): tie the URL Base warning to the field it is about
    dfa1a7969  #64   feat(settings): show an empty URL Base box when the stored value is the site root
    b21093919  #64   feat(settings): add an Application URL field beside URL Base
    519a6d660  #64   feat(api): reject a UrlBase that is a full URL
    a0ba57abe  #33   fix(audible): tell a failed catalog lookup apart from a confirmed zero-match
    2ba5181e3  #33   test(audible): cover the other ways the catalog lookup fails, and the 503 itself
    be6706f66  #33   test(audible): give the new controller class the repository's test conventions
    2a0f87029  #33   fix(audible): declare the search endpoint's responses, including the new 503
    02b86105d  #25   feat(import): embed cover art into imported files, behind a setting
    60339ada4  #25   docs: attach the new doc comments to the members they describe
    f391a63a5  #25   tests: pin that cover art replaces the existing artwork rather than appending
    1ec6aea68  #25   fix(import): embed cover art for a book that has artwork but no ASIN
    79cb09ba0  #25   tests: keep this branch's migration out of the shared expected list
    33fcee29e  #25   refactor: keep the cover art code clear of PR 843's edits to the same file
    a996df43d  #25   fix(import): extend the hardlink guard to cover-art embedding, with its test
    2871e1c15  #60   fix(filesystem): record why a file mutation failed, not only that it did
    efc9793a3  #60   test(downloads): name the no-inner-cause case for what it now does
    a6a315dd4  #60   refactor(downloads): format the failure cause where both writers can reach it
    651320aff  #61   fix(downloads): make retry-import actually requeue the import
    8690765d9  #61   fix(downloads): survive two retry requests racing on the same download
    1cc1e42c5  #51   feat(downloads): blocklist a release that failed, so the retry stops
    857936240  #51   fix(downloads): key a blocked release on something that survives a re-grab
    fcbcb928f  #51   fix(search): consult the blocklist on the path that actually re-grabs
    9d6aa1b58  #51   fix(downloads): work out a release identity once, at the grab, and store it
    87b461c82  #51   chore(downloads): drop the using directives this branch duplicated globally
    03eb4fa5d  #51   chore(downloads): drop the using directives the identity fix restated globally
    2d3b74795  #51   fix(downloads): treat a losing blocklist insert as already blocked
    79eab2a40  #51   feat(downloads): give a blocked release a way back out
    2e76b3327  #51   fix(downloads): remove a book's blocklist entries when the book is deleted
    abd06afd9  #51   fix(downloads): block a release only when failed-download handling is on
    c65f8e1fe  #51   fix(downloads): satisfy the three architecture rules the new surface broke
    331860fb6  #51   test(downloads): pin the release identity wire format with golden vectors
    2bf99a951  #51   test(downloads): fail the build when a second place works out a release key
    c7c488a8c  #51   fix(downloads): order the blocklist migration after the newest released one
    cd1c70344  #51   fix(downloads): order the blocklist migration after the newest one again
    c839fe52c  #62   fix(downloads): give the three download-finalization settings a reader again
    3ed1c6b1e  #62   test(downloads): pin that the processor reads the configured retry delay
    66745baeb  #62   fix(downloads): stop the retry backoff doubling past a day
    a398038e7  #62   fix(downloads): hold only a first transition into Completed
    032ad37e5  #62   test(downloads): scope the completion-stability save to the one test that needs it
    02772182c  #65   fix(quality-profiles): stop hiding Maximum Age, and load a profile's qualities
    9e9c99026  #46   fix(search): read Indexer.MinimumAge and Indexer.MaximumSize, and measure age in UTC
    34f52417d  #46   refactor(search): split the scorer, and test the UTC date parse directly
    31cc75016  #46   test(search): pin that the indexer size ceiling applies to Usenet too
    01febf1f2  #46   test(search): make the new parse tests follow the repository conventions
    9a92770b1  #46   fix(scoring): resolve indexers once per batch, not once per result in parallel
    550fe3a0e  #46   test(scoring): assert the pre-resolved indexer actually reaches the scorer
    dbd7978b0  #46   fix(search): compute the profile size gates in long, not int
    332b38cc2  #47   fix(download-clients): make the Priority dropdown speak the planners' vocabulary
    5135f77ab  #47   test(download-clients): drop two usings the global usings already cover
    724be9677  #47   fix(download-clients): read a stored legacy priority back as Normal
    1bc3b1a53  #47   fix(download-clients): say what Default does per client, and name Force's band
    c609ae768  #47   fix(download-clients): lower the priority in the addurl path too
    4448ee940  #44   test(images): assert that a cached image is served, not that something happened
    812d9bab6  #83   feat(library): render the list view when grouping by author or series
    6e96a5ca3  #83   fix(library): fetch author covers and remember the mode in the grouped list
    c0971efe7  #45   fix(qbittorrent): send the four Advanced Settings to the client
    d63c19ada  #45   fix(qbittorrent): do not fail a submission the client already accepted
    ca3242c7b  #68   fix(filesystem): say why a source file could not be pinned
    fda0e9ce9  #68   fix(filesystem): sanitize the pinning cause before it reaches the log
    9194cdd94  #68   test(filesystem): fail the refusal test when no cause is captured
    df416d8f6  #68   fix(filesystem): lead the refusal with the cause, through the shared formatter
    f93c69f9e  #68   fix(filesystem): do not blame a configured root's link for an unrelated refusal
    456488803  #68   fix(filesystem): name no link when the source overlaps an unavailable root
    76026b622  #68   fix(filesystem): name the outermost link as a possible cause, not a certain one
    579edf68b  #68   test(filesystem): make the nested-link guard able to fail
    19dbe00bb  #72   fix(imports): retry a failed file import instead of blocking on the first attempt
    640d03f42  #72   fix(imports): retry a failed import only when every file in it failed
    ba5ff6069  #80   fix(quality): let a real encoder bitrate reach the rung it was encoded for
    18d925901  #80   test(quality): pin the bitrate tolerance from both sides, not just the floor
    92afac293  #102  fix(docker): give the image a healthcheck it can actually run
    78d89c937  #31   fix(notifications): dispatch webhooks for the triggers the settings screen offers
    c4a1eb611  #49   Make the frontend follow the UrlBase by injecting a <base href> into the shell
    ce52964dc  #49   fix(spa): anchor the shell at the site root even with no UrlBase
    3b1022ac8  #109  stack: let a local build stamp its own version
    a76cbda44  #109  stack: publish the patched build to GHCR from the fork
    5acc91be3  #109  stack: publish a moving current tag beside the immutable commit tag
    c435f5dcb  #108  fix(files): physical-generation snapshots reject database-loaded observation timestamps
    5a5c5aa80  #108  fix(persistence): restore the UTC contract for PhysicalIdentityObservedAtUtc at EF materialization
    a00379e3b  #73   feat(wanted): add a cutoff-unmet bucket to the Wanted page
    6a0eafa44  #74   fix(collection): show an author's books under their membership series
    ff6ca08dd  #75   feat(library): count the distinct series an author appears in
    9c48d1c05  #76   feat(downloads): implement the three reprocess endpoints instead of returning success
    e22793121  #76   fix(downloads): refuse an ineligible reprocess instead of throwing at the caller
    ac905904f  #76   fix(downloads): drop the unused using and the seam-adjacent whitespace
    efd1bc94a  #83   feat(activity): sort the queue, and show when an item was added
    e7a3a192e  #86   fix(collection): apply the language preference to library books, not just suggestions
    dc1adb871  #94   fix(search): the minimum seeders gate never fired on a real torrent
    717fbd4f0  #110  fix(import): match blacklisted extensions without regard to case
    a0cbd0af4  #112  fix(ui): match System recent-log severity classes to the stylesheet
    ba6b02eb7  #20   fix(search): derive one indexer query title, shared by both search paths
    1789db5cb  #20   fix(search): leave the series out of the indexer query, as Readarr does
    989cb5942  #66   fix(downloads): give the queue a control that removes a terminal download
    c907406bd  #66   fix(downloads): tie a processing job's retention to the download it explains
    c38776189  #99   fix(authors): stop attributing one author's ASIN to every co-author
    628541a28  #100  fix(naming): spell an author's initials one way in folder names
    590d12cea  #100  test(naming): pin byline order on two co-authors, not on a translator
    1c3e92a4a  #113  fix(system): stop inventing log entries when no log file exists
    93e5c1807  #114  fix(library): stop announcing the list-view status badge as a button
    1dd2150fc  #115  fix(settings): stop a failed clipboard write from reporting a failed regeneration
    988f42a39  #95   fix(api): the authentication gate ignored requests that varied the path casing
    20d0e4a96  #68   fix(filesystem): resolve a symlinked source path instead of refusing it
    0b370e14f  #68   docs(filesystem): stop two comments claiming what the symlink commit retracted
    e4d6af02b  #98   fix(logging): give silent catch blocks a real log call or a stated reason
    ced35eac4  #108  fix(persistence): keep a persisted UTC timestamp UTC when it is read back
    902b0aaef  #78   test(scoring): pin the four hand-copied quality ladders to each other
    088555cc8  #69   test(metadata): count the attempts the Audible retry policy actually makes
    98736fa2b  #69   test(metadata): stop the retry policy tests racing the caller's clock
    d49a6922d  #136  NZBGet: warn once per failed history entry, and only for monitored entries in polls
    3c4a51c67  #136  fix(nzbget): keep the warn-once keys case-insensitive after a scoped read
    6bbb6d9aa  #92   refactor(collection): lift shared series and text helpers out of the collection view
    c73f937dd  #92   feat(collection): show an author's series as a section on the author page
    a06b1b347  #141  fix(monitoring): keep every series membership and its ASIN when a monitored author or series adds a book
    ac5e63bc2  #141  fix(library): let a legacy-only metadata payload carry its series identifier
    854de2331  #141  fix(monitoring): keep every series and its ASIN through the author catalog cache
    5d99e112d  #141  test(monitoring): use catalog books with real identifiers in the series fixtures
    e3cf0fcda  #141  fix(monitoring): refetch an author catalog cached before series entries existed
    e252e4be5  #145  feat(activity): queue selection that survives the poll instead of being reset by it
    0507f78b1  #145  feat(activity): sequential bulk runner with per-id outcomes and one summary line
    b780c44df  #145  feat(api): keep the queue-management API tests, drop the methods #917 and #927 already added
    e0e491e31  #145  feat(activity): a queue toolbar whose verbs say what they will do before doing it
    fd68646f5  #145  fix(activity): the select cell reasserts its props after a click that changed nothing
    6a4ac677f  #145  feat(downloads): a Retry button that appears only where the endpoint will accept it
    19313f5df  #145  feat(activity): select queue rows and act on them together
    0d6f1dc8d  #145  fix(activity): the filter is a viewport, so selection controls act on the rows in view
    c0e7aa6e7  #145  feat(downloads): the Retry button does the retry, instead of promising one later
    7de887629  #145  fix(activity): the retry button dresses itself and select all goes quiet with nothing to select
    dce9e1d3f  #145  fix(activity): the toolbar drops a confirmation the selection outlived and rests while busy
    21f2b5645  #145  fix(queue): overlay import status onto queue rows the client cannot represent
    be701f91b  #145  fix(activity): disclose the true count before an unbounded sweep
    0829c9021  #144  metadata: name the two nothings a provider can answer with
    f25839859  #144  metadata: answer the ASIN endpoints with a status, not a stack trace
    df9da552d  #144  images: keep serving the placeholder when the provider does not answer
    fc90a0bde  #144  search: move the Audible series endpoints into a partial
    71a964cec  #144  search: say the provider did not answer, rather than returning nothing
    510acfc83  #144  metadata: walk every source, then report the fault rather than a miss
    230a2c0b5  #144  metadata: let the Audible client raise when the provider did not answer
    3adf313a0  #144  metadata: hold the Audnexus book lookup to the same division
    94ac9f8ef  #144  library: record when a book's provider metadata was last refreshed
    bc88cd559  #144  settings: the refresh interval, staleness age and budget are operator-set
    e863837e6  #144  metadata: a request budget with an hourly refill and a spacing floor
    49b7486e0  #144  metadata: lift the per-book rescan out of its HTTP wrapper
    992cc2a3a  #144  metadata: one gate, one run, one budget across every refresh entry point
    3b943937c  #144  metadata: walk the stalest books on an interval, and let an operator ask
    f2af72f2d  #144  fe: a Metadata Refresh section in the settings screen
    3a1e5aa78  #144  metadata: provider 503s answer as a ProblemDetails the filter keeps
    2ce4346a6  #151  fix(logging): sanitize ASIN log arguments, rebased onto the metadata-refresh chain
    23112e553  #153  Attach download history rows to the audiobook they belong to
    2e6aacbc3  #153  fix(history): record the protocol a download actually used
    bbf4b21e3  #153  fix(history): resolve the protocol for the remaining event types too
    4a3b81d4e  #153  test(history): cover the audiobook key and the protocol on one call
    45ae40700  #154  test(downloads): match the int? audiobook id in the grab history verifies
    9f6de7044  #155  fix(imports): classify failures at the boundary instead of leaking them to History
    de0ec3ecc  #156  fix(naming): key the remaining naming tables case insensitively
    a9a1b5b50  #158  fix(qbittorrent): a 200 carrying "Fails." is a refusal, not a grab
    c8db254c0  #159  fix(search): stop reporting a series name as the series identifier
    4feadbf3b  #161  Record a download client timeout as a failed grab, not a shutdown
    6542a3c4d  #163  fix(notifications): sanitize webhook URLs before logging them
    f047a1a49  #163  test(notifications): cover SanitizeWebhookUrl and pin the webhook log call sites
    b702db8f9  #166  feat(search): tell an indexer that answered with nothing apart from one that never answered
    afc525103  #166  fix(search): give the per-indexer timeout its own catch, correctly typed
    323299ad4  #166  test(search): match the fake provider to the observation-returning interface
    dfdb12748  #166  search: isolate a per-indexer HTTP timeout from the multi-indexer fan-out
    7aa523f54  #166  search: bound indexer fan-out concurrency to 4 (tracker #119)
    2e9ac4ffd  #166  search: separate a rate limit and a refused credential from any other non-2xx
    7e131f75f  #166  search: persist a per-indexer failure backoff ladder
    fa6604e7d  #166  search: skip an indexer that is in failure backoff, and record what each one answered
    0ff06f493  #166  search: do not stamp LastSearchTime on a book whose indexer set was degraded
    34a4690c6  #166  indexers: show on the card when an indexer is in failure backoff
    4f7c6cd9a  #166  feat(search): try a second form of a search before saying the book does not exist
    4b7e8f019  #166  search: cover what the query ladder and indexer backoff do to each other
    a8581ba6d  #166  search: drop the bare series-name query rung
    85de13514  #166  test(search): thread the cancellation token through item 107's fake provider
    1a9b75a2a  #172  172: fix Abridged substring match stamping unabridged books abridged
    dfae86cb5  #172  172: fix Abridged substring match on the frontend too
    79e0dd6ae  #173  173c: expose the Listenarr.Backend meter through a listener and an endpoint
    3146c1bb8  #170  search: make the indexer fan-out ceiling an operator setting, shipped at 4
    c5845d8ca  #170  settings: expose the indexer search concurrency ceiling in Search Settings
    58ec46dff  #123  notifications(#126): make EnableNotifications actually gate delivery
    b7392a512  #123  notifications(#127): dispatch webhooks by stored Type, not URL sniffing
    7d5cd89dc  #123  fix(notifications): route webhook Type through the central dispatcher, not per-callsite loops
    60f172c71  #126  notifications(#126): drop EnableNotifications, matching the family
    19fd8198b  #123  search(#123): wire up EnableAmazonSearch/EnableAudibleSearch, dead since introduction
    bb66d43d5  #123  notifications(#128): remove the unreachable legacy /notifications/test endpoint
    845184c4e  #123  settings(#131): add a UI control for ExtractArchives, backend-only until now
    11d7ea581  #123  downloads(#132): fix RemoveCompletedDownloads for Transmission, SABnzbd, NZBGet
    836532f83  #124  fix: wire AllowedFileExtensions into FileUtils.IsAudioFile
    886d78a95  #124  cleanup: remove two orphaned private AudioExtensions arrays
    42ec3e2d0  #129  Remove vestigial Indexer.Tags field
    e51911c00  #133  history: add retention setting UI and a daily cleanup worker
    c5aedeeba  #138  Preserve series ASIN when mapping monitored author/series books
    a8b4fb498  #138  test(monitoring): resolve the series-monitoring root through identity, not a raw builder
    436b938aa  #199  Fix author name normalization to merge spaced-out initials
    cdf94f4fa  #199  test(domain): bring StringUtilsNormalizeAuthorNameTests up to convention
    fd9b195ca  #199  fix(authors): dedupe author-cache methods reintroduced against fix/72's own copy
    a7d34e3a9  #151  fix(authors): skip the SQL narrowing pre-filter for non-ASCII author names
    b3a498e7e  #151  fix: complete the NormalizeAuthorName redirect this branch left half-done
    9157b62af  #201  Finish the author-normalizer consolidation and re-derive the keys it left behind
    a1493c1dc  #201  Keep the canonical author spelling the ASIN lookup already returned
    2061f0e91  #201  Converge existing books onto one spelling per author at startup
    301a1a268  #201  fix(library): normalize author grouping and include every co-author
    b7ec62189  #200  Pin the author-identity behaviour these fixes are meant to change
    beeb318ec  #200  Stop reading author identity out of a list that never recorded it
    f687fc162  #200  Refuse to rename a cached author when an ASIN is already somebody else's
    e30b5ddd1  #200  Confirm an author ASIN with Audible before binding it as an identity
    071d9000f  #200  Pin what stops a renamed cache row reaching stored author names
    d4017548b  #200  fix(authors): dedupe author-cache methods a third time, keeping the collision-safe version
    5a522101c  #167  Fix: manual import path traversal check trips on legitimate paths (#167)
    b7caa007e  #167  test(manual-import): bring the path-traversal test class up to convention
    9ff2b3f1c  #122  search: add ReleaseShapeDetector, which tells a bundle from a single edition
    0c2da916a  #122  search: a per-profile preference between bundle and single-book releases
    eb7ef0c60  #122  fe: the release shape control, and a Bundle badge on search results
    cebc1894f  #172  fix(search): word-boundary word filters, and any-of required words
    c003a4419  #172  fix(search): route PreferredWords through the same word-boundary matcher
    a9e9ccd79  #172  fix(search): make indexer priority a genuine tie-break instead of dead weight or dominant term
    b8e203209  #172  search: give the word-boundary term matcher its own file
    66429a4bf  #172  search: pin the precedence between the three scorer concerns
    c12963569  #173  fix(system): stop fabricating download-client connectivity
    44cdcc854  #173  tests(system-service-logs): pass the download-client status cache the constructor now requires
    b1f903102  #169  fix(nav): dismiss the header search panel when the route changes
    07bd614b9  #169  fix(library): Select All reached past the filter into the whole library
    b06b502a3  #165  feat(detail): add Automatic Search action to audiobook detail toolbar
    a2e6f7afd  #165  feat(library): per-item automatic search icon on the audiobooks list
    50c74f06d  #165  feat(library): bulk automatic search on the audiobooks index toolbar
    cc5fa0bef  #165  feat(calendar): search for the missing books in the window on screen
    d11d699ad  #202  feat(ui): multi-row selection on Wanted and Downloads
    97d1cf5f8  #289  tests: catch up DownloadsView.selection and WantedView.selection with items 73, 169 and 145
    66dbd54ae  #174  fix(history): style the events that happen, not four that cannot
    f41f25f94  #174  fix(history): keep what a grab knew, and name the download client
    888fe9948  #174  feat(history): a global History page over the API that already existed
    7a54b0e63  #174  tests(download-service): match RecordDownloadFailedAsync's protocol parameter in the sanitize test
    8c05d7b86  #209  fix(security): an IPv4 address in its mapped form never matched the private ranges
    d7efec088  #190  fix(myanonamouse): spend the freeleech wedge on the download, not the search
    7ebf06c70  #190  refactor(search): give SearchResultConverters its own file
    7a5b0fa3e  #190  feat(indexers): parse the release flags trackers advertise
    d2b484e67  #183  Add a task surface over the periodic workers, with a manual trigger
    d767f1b4d  #183  scheduled tasks: an allowlist for manual runs, not a deny-list
    6ee6e0829  #183  scheduled tasks: answer the trigger with the row it started, and keep stopped workers
    5da64cf24  #183  scheduled tasks: say which request started the cycle, and stop 404ing a listed task
    1ce23a21b  #183  scheduled tasks: release the gate under the lock that ends the cycle
    172fc95cc  #183  scheduled tasks: publish the interval in whole seconds, and refuse what will not fit
    040fc55ea  #183  scheduled tasks: put the interval refusal on the row, and correct two claims made for it
    49aead1d8  #183  scheduled tasks: make the interval refusal audible, and stop overclaiming what it buys
    847e6a8d2  #183  tasks ui: put the task surface under System, where the family keeps it
    e2a5acece  #183  tasks ui: test the run control against the refusals the API actually answers
    ffa2bd9b6  #183  tasks ui: fix two in-flight defects, make the refusal reachable, and cover the poll
    dd9809d47  #183  housekeeping: the frame for a daily retention sweep, with no table in it yet
    be3bc7d3d  #183  housekeeping: three tables, and the two that were left out on purpose
    7922d1176  #183  housekeeping: move jobs and their three child tables, with the cascade measured
    0be9527b3  #183  housekeeping: the supersede that never stamped its terminal timestamp, and four comments that overclaimed
    a38f52cf4  #285  settings: a Housekeeping section for the two switches item 183 shipped API-only
    ecc40ded2  #281  housekeeping: the two deferred populations, and the one slice of each that is provably unreachable
    6fcefe488  #281  housekeeping: say what the compatibility sweep's query actually costs
    d1bdce7f3  #185  notifications: a subscriber model and a Custom Script provider
    964ed3e0a  #185  configuration: keep stored custom scripts when a save omits them
    038bb99f3  #185  notifications: let the EnableNotifications switch gate the subscriber fan-out
    2c1ce93c9  #189  feat(calendar): add the calendar endpoint and iCalendar feed
    22eac6e3f  #189  feat(calendar): give the calendar page a feed subscribe URL
    97f49ec79  #189  fix(calendar): correct the status precedence and test the two seams
    addff4a9c  #189  fix(calendar): accept Readarr's tag spelling, and test the rest of the review
    aea8d1530  #189  fix(calendar): close the rest of the parse/query seam, and stop citing what cannot be opened
    e1c02af4a  #189  test(calendar): the case-variant feed is refused by the enforcer now, not the attribute
    31b47a565  #189  feat(calendar): resolve per-event status in Readarr/Sonarr precedence order
    673986b5c  #189  feat(calendar): add a first-day-of-week preference, defaulted from locale
    8c39e972e  #189  feat(calendar): add a legend component for the event status colours
    eb8c19741  #189  feat(calendar): colour events by status, add a legend, and a week-start option
    9770d19fc  #189  test(calendar): reconcile search-missing spec with item 189's status classes
    a163d1f96  #79   fix(status): stop reporting a non-preferred format as below cutoff
    1a90149a8  #79   test(status): make the agreement test read the real projection, and pin the widened cases
    4ab0c675e  #252  WIP fix(quality): a blank cutoff means satisfied, not "search forever"
    80f342cbb  #252  test: bring the new cutoff tests up to the repository's test conventions
    efe67c7ab  #252  test: flag the blank-cutoff consequence for the not-yet-landed upgrades flag
    4b1c6904c  #257  fix(quality): resolve the cutoff through QualityMatcher, changing two answers
    f8134bd82  #254  torznab: a bitrate has to be its own token, not digits inside one
    11cb49935  #254  torznab: tests for the x264 mislabel, each with its control
    7d6310710  #254  torznab: a number is a bitrate only where the text says it is
    0ee723cfb  #254  torznab: recover the bracketed bitrate the tightening had lost
    6639c54cc  #255  fix(search): let a quality profile's Allowed flag actually gate
    c73b9f1b5  #255  fix(search): correct the claims around the quality gate, and pin what it does not decide
    d61597a85  #255  fix(search): refuse a quality label the gate cannot place
    11283cda1  #255  fix(search): answer the review of the quality gate fix
    3586afc46  #253  Break release selection ties deterministically instead of by indexer order
    0c765dae5  #253  Make the consuming-site tests actually discriminate their own site
    8771be457  #253  Restore Readarr's protocol step, without which the chain is not transitive
    fd9d770ed  #253  Close the second intransitivity, and three more review findings
    563f70a4a  #253  Rank once per site instead of twice, to stay inside the focus budget
    702524939  #253  Put the same-group invariant in the code, and close the last apparatus gap
    56b820c88  #256  quality profile: refuse a cutoff the profile does not allow
    e914751f4  #256  tests: cover the cutoff rule at the API and at the attribute
    5a07423ab  #256  quality profile: make the cutoff rule agree with the code that reads it
    34ee482a3  #256  quality profile: give it the UpgradeAllowed flag the family has
    5f617a597  #256  quality profile: carry upgrades-off across the upgrade, in a startup repair
    6f89e699b  #256  tests: pin both directions of the upgrade flag, each against its control
    beb0cee61  #256  status evaluator: drop the upgrades-off guard, it was never the reason
    df4795c38  #256  tests: pin the new migration id, the backfill gate depends on it
    2125bf56c  #256  quality profile: decide "blank cutoff" the way the readers decide it
    39bd99883  #256  comments: correct three claims a reviewer could check and find false
    160bcd23b  #261  search: skip the bare-title rung when the title cannot carry a query alone
    070a3c658  #261  search: stop the stem and series rungs unanchoring themselves when there is no author
    65824d264  #262  Require indexer categories, the way the rest of the *arr family does
    1240f18a5  #262  tests: pin that the draft test is gated too, since it binds the same entity
    ce5901fe9  #262  Make the indexer form usable now that categories are required
    599641432  #262  Move the category default off the entity, where it rewrote stored values
    0eb626974  #262  tests: pin that an explicit null category list is not a way past the rule
    49e1e5b03  #262  Validate the merged indexer on update, not just the request body
    8ef245536  #190  fix(myanonamouse): send the torrent filter as tor[searchType]
    bdda07322  #190  fix(myanonamouse): send the page size as perpage, not tor[perpage]
    4237e10f6  #190  fix(myanonamouse): send the language the caller asked for
    924f811b2  #190  fix(myanonamouse): stop asking the search for a freeleech wedge
    c0dfc7854  #268  import: give each companion file the source root it actually came from
    6af0570ad  #268  import: resolve the companion source roots once per batch, not per file
    0b9e21a2f  #269  manual-import: place companions by the selected files' structure, not the caller's path
    1eb977476  #269  manual-import: a companion goes where the file it accompanies went, and nowhere else
    b459d4e99  #269  import: match the companion's neighbour by path identity, and name the rule the manual path uses
    de7364b8b  #269  import: say why the neighbour comparison is safe for the reason that is actually true
    7082d1bcc  #21   tests: give the companion placement pass the root folder this branch now requires
    170eb9c3f  #269  fix(manual-import): log the companion files TopDirectoryOnly drops silently
    a61831b0a  #269  fix(manual-import): guard the nested-companion scan against a hostile tree
    ca9ba25d4  #273  cache: make the author and series cache upserts survive a lost race
    418ae60e8  #273  cache: do not retry a collision that re-reading cannot resolve
    75e37a3e8  #273  cache: the author upsert cannot reach that collision on this build, so pin what it does instead
    ab8763ea2  #273  cache: say only once that a rebind was refused, and describe the collision that can actually happen
    f267ad905  #273  cache: a refusal that first happens on a retry must still be reported
    be0f9f9f2  #273  cache: the comment on the test still described the guard that was removed
    ebcab6be7  #275  fix(metadata): spell a contributor's name without their job title
    eed6fb105  #275  fix(metadata): store the author first, and keep an edition out of the byline
    7d7c6af67  #275  fix(metadata): strip contributor role suffixes on rescan too
    efbfa23b4  #275  test(metadata): use catalogue values for contributor role test data
    32e385395  #275  fix(metadata): finish the contributor role cleanup on scan, dedupe and the singular author
    5cb81f9e9  #275  test(metadata): cite catalogue credits as the catalogue lists them
    6fce24307  #275  fix(metadata): keep matching an author folder that still carries its role
    428bd858b  #276  fix(metadata): bind an author's ASIN only to a row that names them
    cb70415a4  #276  metadata: lease the shared request budget to callers that are not a refresh run
    0f1708d45  #276  repair: a scheduled pass that corrects author identities already written
    66df31603  #276  repair: remove contributor roles from the credits already stored
    d6fc39107  #276  test: give the two new test classes the conventions the suite enforces
    10e23caa9  #276  repair: a provider that did not answer is not evidence that an author has no ASIN
    c9d872732  #276  repair: answer the fresh review, including a starvation defect nothing was checking
    92c48f580  #270  filesystem: let a raced pinned hardlink fall back to a copy, and say so
    89539dd42  #270  filesystem: narrow the hardlink race fallback, because the first attempt claimed too much
    1e9baa4e2  #277  Fix the fail-open identifier check in DownloadRemovalWorkflow
    6ca485faa  #277  Route the downloads delete endpoints through the client removal workflow
    309a5f31d  #277  Add tests for the client-removing delete and the identifier check
    afafd1c4d  #277  Make a disabled client deletable again, and fail closed when no client id is known
    e343a3763  #277  Correct the outer-catch mechanism described in a test comment
    64854c7df  #277  Cover the disabled client, the unmapped record, and deleteFiles on the delete path
    efa948bc6  #277  Let the delete control say whether the download client is involved
    eb66d6eff  #277  Make the record-only way out actually work, and stop it claiming too much
    ce9205ea4  #277  qbittorrent: check presence before delete, fail closed on idempotent-success
    6f7cb71ae  #277  qbittorrent: cover the unparseable-presence-check-body fail-closed path
    30048e684  #277  qbittorrent: treat a torrent confirmed absent from the client as removed
    8fdbca039  #277  tests: give the qBittorrent cleanup composition tests their Name and Category traits
    74aaa7dfc  #277  fe(activity): the bulk remove path also defaulted to removing from the client
    078ef3446  #276  fe: an Author Identity Repair section in the settings screen
    d7caa5824  #276  fe: the preview card promised a run button the frontend does not have
    2efd04a26  #276  fe: the review's findings, one of which the UI could not have shown
    ffaf0fa42  #278  notifications: give the Custom Script provider a settings surface
    d010136c5  #286  notifications: an Email provider, the case a webhook type cannot express
    2800eb64d  #286  notifications: give the Email provider a settings surface
    a15857499  #286  notifications: assert both providers are actually wired into the container
    3dc2fb727  #286  notifications: act on review of the Email provider
    0fb3a8afa  #286  notifications: cover the sentinel guard on a database with no settings row
    e81253dbe  #286  configuration: move the email members into their own partial
    c3b36fcf6  #286  notifications: exercise the SMTP transport against a socket, not a mock
    b7869922e  #286  notifications: act on the second review of the Email provider
    6a7b12f73  #286  configuration: split startup config out of ConfigurationService, so the file is under the cap again
    934942ea6  #177  feat(download-clients): one selector, with a priority the operator sets
    033ed7c58  #177  test(download-clients): pin the selection policy and the migration default
    256582dc0  #177  settings(download-clients): a Client Priority control, so the field is reachable
    8df0c50b4  #177  test(download-clients): pin the rotation cursor's lifetime, with a control that moves
    0cb0e5576  #177  fix(download-clients): four things a fresh review found
    ee6f8106e  #177  settings(download-clients): give Client Priority its own section, keep queue priority usenet-only
    bee41f6fa  #177  test(indexers): pin per-indexer download client binding, failing first
    51215e727  #177  indexers: give an indexer a nullable download client binding (shape only)
    64df9474b  #177  indexers: route a bound indexer's grabs to its client, and fail loudly
    1d46f7cc7  #177  test(download-clients): pin persisted client failure status, failing first
    c6869b5b2  #177  download-clients: persisted client failure status, shape only
    5ae075775  #177  download-clients: record client failures, back off, and route around a blocked client
    6d18369c0  #177  download-clients: record status in a gateway subclass, not a wrapper
    842ff90c1  #177  stack: make item 177's two migration Designers the cumulative model at their position
    c0467f18c  #179  backup: take a copy of the database before migrations run
    a60c4b80f  #179  backup: the settings column, and two defects the suite caught
    e8c05f5c9  #179  backup: tests, each paired with a case that comes out differently
    c939dcdc6  #179  backup: the operator surface, so this is reachable without a shell
    a79abda1b  #179  docs: describe the backups directory and the startup override
    d7f0beb6b  #179  backup: take the copy before the startup path's own repair writes
    d85a5c0ce  #179  backup: cover what the review found had no test
    2499ecb0d  #179  wip: retarget the auth test
    06cf18885  #179  backup: close the gaps the review found in the code and in its tests
    af2e5fd46  #179  backup: fix what the second review found, and rethink the manual limit
    142a732b7  #179  backup: pin the negative retention case
    bb9f0eb4c  #180  feat(indexers): per-indexer seed ratio and seed time
    115e803f0  #180  fix(indexers): close review findings on seed criteria
    5703f3ea4  #181  search: the release score has no ceiling, so a preference can still decide
    04355ff6e  #181  tests: SearchResultScorerCeilingTests follows the repository test conventions
    9c35f957d  #181  settings: the minimum score threshold is no longer capped at 100
    06d92f03c  #181  search: correct what this branch claims, after review
    aec245173  #264  search: the profile's size and quality gates apply to Usenet results too
    e318fbcb7  #264  search: size bounds above 2047 MB, and three corrections to the commit before this
    3d9a7f6b9  #264  search: reconcile the NZB gate hoist with the tests that pinned the exemption
    13afe67d4  #263  search: release selection reads the quality profile, not the hardcoded ladder
    b0f54bc2c  #263  search: take the best candidate that is an upgrade, and fix two things in the commit before this
    183b81ff8  #181  search: split the rejection gates out of the scorer, so the file is under the cap again
    c2e22bfc6  #181  search: two warnings, one of them a control this stack no longer has
    485badaf3  #182  naming(#182): render the settings naming preview through the real backend renderer
    3e85cd56a  #182  naming(#182): wire the settings naming preview to the backend renderer
    c25315646  #182  naming(#182): fix real divergences a fresh review found from GenerateFilePathAsync
    218d7925a  #182  naming(#182): flip the lowercase-token preview test now that the case fix is in the tree
    18ed45f5c  #184  tests(notifications): pin the item-184 gaps blocked on PR #943 (WIP)
    fd15a910f  #184  tests(notifications): tighten item-184 gap pins after fresh review
    7584068c1  #187  feat(#187): free-space check before import (G7)
    8be9f71e2  #187  feat(#187): free-space on root folders (G9), tests, and settings UI (G7/G9)
    889731027  #187  fix(#187): the free-space guard silently never fired under the default naming pattern
    08e679074  #187  test(#187): prove required bytes are the real file size, not a stand-in like #821's 64
    88c76ac91  #284  Add failing test for metadata repair guard missing Entries include
    aa0c1339d  #284  Fix metadata repair guard to load MoveJob Entries and CreatedDirectories
    6af9a821b  #283  tests: add failing control for ProcessExecutionLogs table drop
    b7349273c  #283  Drop the dead ProcessExecutionLogs store, repository, entity, and table
    4979b6253  #237  fix(search): reconcile the query ladder with fakes and tests from three unrelated items
    1b0e45d27  #237  test(startup): mock the metadata-refresh backfill this test's strict repository was missing
    118d2eca9  #237  style: run prettier over item 201's two touched files
    dc94fe9e0  #237  fix(search): reconcile the scorer 3-way with fakes and tests from unrelated items, plus two more real bugs found by the suite
    eade88b45  #237  stack: reconcile the manual-run allowlist with the stacked tree
    40c4fe35a  #237  tests: read the observation's results, the way its sibling case already does
    554f14392  #237  tests: point the tiebreak harness at the signatures the stack actually has
    9aeaa4780  #237  tests: move the blank-cutoff pin to the answer fix/blank-cutoff-search-loop gave it
    781134c27  #237  downloads: split the client-name projection out, so the controller is under the ceiling again
    58084f655  #237  tests: guard that every migration Designer is its predecessor plus its own Up
    96cd5e1b0  #237  stack: make every composed migration Designer the cumulative model at its position
    355fe88c1  #212  tests: pin the no-audio-candidate import block from #890
    d1ef0bee2  #212  fix(import): admit audio-only .mp4 via a probed ambiguous-container tier
    7388bfb06  #212  fix(import): let manual import admit an audio-only .mp4
    ec1710ae2  #212  tests: move the #890 cases out of a file PR 993 is rewriting
    edfaf1619  #212  refactor(files): move the content gate into its own partial, so it fits beside #901
    3e0983f06  #191  qbittorrent: failing spec for postImportCategory form field
    51b3bd4a3  #191  qbittorrent: add Post-Import Category field to the client form
    91efe8205  #288  symlink-chain: fail-first tests for a two-or-more-hop chain
    35c1cd67c  #288  symlink-chain: walk the whole chain, not just the last segment
    26f04f309  #237  stack: make the three Designers after item 170's migration the cumulative model again
    0fce8b0f7  #293  fix: grid containers shrink to content instead of forcing viewport height
    b7bb24339  #293  fix(activity): virtual row stride must not be shorter than the rendered row
    c21e38cb0  #293  fix(activity,wanted): measure the virtual row stride from a rendered row
    2ac68a71c  #279  metadata-refresh: allow the scheduled walk onto the task surface's Run button
    e87c49972  #146  manual-import: reject items missing a matched audiobook id with 400
    d6111a2af  #146  tests: follow the repository test-class conventions
    1c6327fe0  #215  fix(import): refuse a manual import on its extension before publishing it
    c8dc119df  #215  fix(import): say that extensionless sources are refused too, and pin it
    be9224f4f  #292  search: WIP failing tests for the requested-book release check
    51fddde08  #292  search: drop releases that are plainly for a different book before an auto-grab
    ceb9de292  #292  test(search): give the blocklist wiring fixture a release for its own book
    b852c0494  #292  search: fix the requested-book check's author and short-title rules from review
    3cdd92ff3  #292  search: a bare number after the title is a different volume, and only a year is packaging
    e59115661  #310  test(search): series-entry cases for a release honestly titled for a different volume
    40d5f9d16  #310  search: reject a release whose own series position disagrees with the catalog's
    82122f183  #310  test(search): a release honestly for a fractional series entry, caught in review
    4026ce14e  #310  search: parse a release's fractional series position instead of dropping it
    97979b28d  #310  test(search): pin the thousands-separator exclusion on the series-position parse
    d4007ec1b  #356  test(search): pin item 356's whole-number-only series-position tolerance
    80168f62f  #356  search: fail open on series-position comparison when either side is fractional
    f90046160  #139  fix(library): identify a series by its SeriesAsin, not its display name
    1c3404871  #139  fix(library): series identity fixes from review: non-Latin names, author guard, stale links
    f948aa2ab  #139  fix(library): series author guard joins on a shared author, script-preserving
    b3d227093  #291  fix(search): give the indexer backoff write a context of its own
    ba27e2cb6  #291  test(search): assert which context the backoff write used, not whether it collided
    15a881c92  #215  import: move the tag enrichment out of ProcessItem, so the file is under the cap again
    92851826f  #295  health: report indexers in failure backoff, and separately those failing for more than six hours
    951167d9c  #295  search: tell an indexer that could not be asked from one that had nothing
    0ccb9335b  #295  ui: count indexer health checks on the sidebar's System entry
    c5f36790d  #295  search: no default for the outcome search, and tidier could-not-search reasons
    396e77bbd  #295  ui: show the System health badge with authentication off, and re-read it every minute
    617072315  #81   settings: add a stalled-download timeout, off by default
    5ccad2001  #81   downloads: fail a torrent that has made no progress for the stalled-download timeout
    b37e8a3ac  #81   ui: stalled download timeout field under failed download handling
    ffedbafb5  #81   test: a pause shorter than the gap cap still clears the stall clock
    d833a1cae  #223  search: one quality ladder instead of four copies
    ef12126ea  #223  test(scoring): pin the ladder's rung order and every rung literal
    48f4eed19  #63   search: make Prefer newer releases a real ranking bonus
    f37b9e45c  #63   ui: say what Prefer newer releases now does, and pin Maximum Age across a toggle
    506afc87c  #63   search: cap the newer-release bonus below a preferred word, and keep it off rejected releases
    4368569f2  #63   ui: describe the smaller newer-release bonus, and check Maximum Age is visible
    08ae9c6a3  #63   search: default PreferNewerReleases to false, matching the settings modal
    7ec9f3450  #218  fix(scan): report a scan whose files were all declined as CompletedNoFilesAccepted
    6d12a8cfa  #218  fix(ui): show a scan that accepted no files as its own outcome
    f8cbbaf17  #218  fix(scan): record a diagnostic when a candidate's metadata is read and declined
    c50648ba9  #218  fix(metadata): name the file in the ffprobe-failure warning
    397e4c09b  #218  fix(scan): count only files this book could claim, and skip other books' files
    041167a1e  #218  fix(ui): label a no-files-accepted scan on the detail view's Files tab
    cc4e3cb51  #218  fix(scan): probe other books' files again so the metadata conflict guard is unchanged
    1a5ea26f9  #218  fix(scan): decide no-files-accepted on durable ownership, not attribution
    661b4d327  #218  test(scan): reproduce a folder-attributed file whose tags name another book (red)
    4805fd148  #218  fix(scan): decline folder-attributed files whose tags name another book
    558f00cd8  #218  test(scan): update probe-count and swap-timing expectations for the new pass
    d90a4707a  #218  test(scan): give the tracked-file guard a real identity and a Times.Never
    2d6c416ee  #3    791 option D (NuGet ffprobe) composed onto the stack, for validation on the install
    a6a0f7499  #309  test: assert FailedDownloadAutoSearch defaults to true
    256d5a5e7  #309  fix: default FailedDownloadAutoSearch to true, matching the *arr family
    bc997b79a  #298  test(redaction): reproduce ffprobe JSON corruption from RedactText fallback
    a3ff797aa  #298  fix(redaction): drop generic fallback that corrupted unrelated text
    96aa81f78  #299  fix(library): reject an unrecognized refresh-metadata field instead of silently upgrading to whole-library (#299)
    eecbee77a  #299  fix(library): catch the deserialize JsonException so a wrong-typed field 400s, not 500s
    bed7afba6  #69   test(downloads): extend the seed-limit removal gate tests to the full matrix
    13a456ddb  #69   fix(downloads): make CanRemove reach the seed-limit evaluator on the queue-poll path
    f19d106c6  #69   test(downloads): cover the exact ratio == limit boundary for both clients
    b1d3c5a97  #69   test(downloads): pin the fail-closed seed policy on the queue-poll path
    37aa54a48  #317  fix(imports): retry a failed file import instead of blocking at once
    37c4fb7a9  #317  fix(imports): use the configured retry delay on the file-import retry
    75fd6407e  #317  fix(downloads): finalize a held completion whose client item is gone
    f6f718fa8  #324  fix(downloads): isolate OnDownloadFailed's blocklist/removal/search steps so one exception doesn't abort the rest
    655101e6d  #326  test(qbittorrent): add numeric-tolerance coverage for the response mapper helpers (#1008)
    81ff3f3b4  #326  fix(qbittorrent): tolerate every numeric JSON shape in the response mapper (#1008)
    e515de0a6  #326  test(qbittorrent): malform state instead of downloaded in the unreadable-torrent fixtures (#1008)
    008e56ec9  #218  fix(scan): log per-file MetadataContradictsPath diagnostics on CompletedNoFilesAccepted
    ccf04ba24  #325  fix(downloads): record History entry when orphan cleanup removes a download
    74406046c  #340  fix(downloads): record History entry when DownloadRemovalWorkflow removes a download
    f581a67cf  #367  fix(downloads): a successful Removed History row no longer carries a non-null Error
    2c20ce834  #333  fix(nzbget): classify DeleteStatus-family history entries as Failed, not Ignored
    951df0df7  #333  fix(nzbget): correct Sonarr/Readarr citation line numbers in comments
    77e4983c2  #335  feat(blocklist): add a list-everything endpoint for the frontend page
    ae8f62899  #335  feat(blocklist): build the Blocklist page (list, remove, nav entry)
    3f5d5564d  #336  settings: add RejectClearlyMusicReleases to ApplicationSettings
    2491e524e  #336  search: gate clearly-music releases in SearchResultScorer when enabled
    1071dd92d  #336  fe: add "Reject releases that are clearly music" search setting toggle
    fdc6454b6  #336  test: extend the post-canary migration allow-list for the new migration
    7ffaef9db  #336  fix: Audiobooks id must override even alongside another Audio-range id
    e26da6dad  #353  fix(blocklist): require confirmation before removing a blocklist entry
    7a0aa839a  #361  search: strengthen MusicReleaseClassifier with weighted signals (tracker #361)
    5cf20ca60  #361  search: fix 6 false positives found by independent review (tracker #361)
    abd2a697e  #361  search: fix 6 more false positives, round 2 of independent review (tracker #361)
    49f47908b  #361  search: fix 4 more false positives, round 3 of independent review (tracker #361)
    256a34d8f  #361  search: fix 4 more false positives, round 4 of independent review (tracker #361)
    321de20ad  #361  search: gate the bitrate/FLAC signal on a music-specific corroborator (tracker #361)
    e504c18d9  #361  search: drop Artist/Album consistency from the bitrate gate (tracker #361)
    81d824434  #361  search: require two-signal convergence to gate bitrate/FLAC (tracker #361)
    a79cf1a8b  #361  search: gate bitrate/FLAC on an actual music category only (tracker #361)
    36e2398c3  #361  search: remove the bitrate/FLAC/kbps signal as dead code (tracker #361)
    9725fdca8  #371  fix(library-import): decouple manual-search ASIN detection
    b225520f5  #371  chore: replace non-corpus identifiers in comments and tests with placeholders
    40c16bb91  #371  test(metadata): use catalogue values for contributor role test data
    c505d215c  #371  test: take series and diacritics test data from the catalogue
    fae2ca2a4  #366  fix(metadata): bare-anywhere-wins for author/contributor classification
    27e10d597  #366  fix(metadata): close the author-credit vocabulary gap and the Introduction asymmetry
    a14b20d60  #366  feat(metadata): add a standalone organization-as-author detector
    e5a51f05b  #366  fix(metadata): apply the author-credit classifier at the 3 missing call sites
    7b21aa04c  #357  fix(nzbget): bound the unbounded history fetch, item 357
    00af5a677  #357  fix(settings): reject DownloadClientHistoryLimit below 1, item 357 follow-up
    732dd8cda  #237  test(downloads): count mapping reads per queue poll and pin the per-client cache contract
    e7c73a5ed  #237  fix(downloads): serve a client's path mappings from the cache it already invalidates
    09884485d  #237  test(downloads): cover a write racing a cold mapping read and a mapping moved between clients
    e463ab3b8  #237  fix(downloads): invalidate a client's cached mappings under the fill lock and clear both owners on a move
    d81e76a6c  #237  fix(downloads): skip the failure row for a qBittorrent duplicate
    e7912d859  #237  fix(qbittorrent): send an explicit false for Initial State Start
    9cb5ed9f9  #237  test: use neutral book titles in the fixtures this branch adds
    82208af9e  #237  test: use public-domain corpus titles in the fixtures this branch adds
    c8f58d2ff  #237  fix(qbittorrent): a skipped torrent marks the snapshot incomplete
    c2e10f17d  #237  fix(qbittorrent): an open circuit breaker still fails the poll
    13c6fea9c  #237  test(qbittorrent): drive both fixes through the monitor cycle
    8e47180c0  #237  test(qbittorrent): use host-valid save paths in the gateway-level fixtures
    61a43857b  #237  stack: #1021 take the head's comment wording for per-client-type HTTP handlers
    9a527a96e  #237  refactor(quality): move QualityMatcher's helpers to a partial file
    90ff45870  #237  fix(search): keep refusing a codec the ladder has no rung for, as canary did
    3336c6685  #237  stack: #1023 take the head's wording for two scorer comments
    b172b43d8  #237  fix(notifications): redact discordapp.com and pushbullet:// webhook URLs
    5531dd36d  #237  fix(logging): keep known env-secret masking in SanitizeWebhookUrl
    1e26f3c20  #237  test(logging): serialize env-mutating redaction tests
    27f53d6ff  #237  fix(manual-import): authorize companion files against the root folder
    7d05d0a77  #237  test: use neutral book titles in the fixtures this branch adds
    0fdbe4988  #237  test: use public-domain corpus titles in the fixtures this branch adds
    4a629383e  #237  stack: #1022 give the hardlink ASIN-tag test a metadata-writing lease on a real publication
    02c777c1f  #237  stack: reconcile the stalled-download timeout tests with the settings controller
    ab52e85d1  #237  stack: reconcile the history-limit settings test with the settings controller
    855b0a4ee  #237  stack: reconcile the NZBGet history-entry test helper with required members
    941180811  #237  stack: reconcile two scan test files with the scan command upstream reshaped
    ddd6b7bc0  #237  stack: reconcile the Designer-chain guard's sentinels without the recycle bin
    b43bf859f  #237  stack: reconcile the pinned migration list without the recycle-bin migration
    e4c683147  #237  stack: reconcile upstream's released-schema upgrade tests with this stack's columns
    3293b810f  #237  stack: reconcile #914's import-tag tests with leases that withhold metadata writes
    5f5b7abc8  #237  stack: reconcile the hardlink fallback tests with upstream's publication catch
    21dd23e00  #237  stack: reconcile #902's refresh regression test comment with #993's snapshot
    4ede49cf5  #237  stack: reconcile the symlinked-source tests with content-only source proofs
    c6b64b4cb  #237  stack: reconcile the Wanted selection specs with upstream's Search All confirmation flow
    f1e032952  #237  stack: reconcile the WantedView spec formatting after the row stride test
    ab6594459  #237  stack: reconcile the detail view spec's signalRService import
    bfe7aad07  #237  stack: reconcile the download settings spec with the stalled download timeout input
    1b1b8496c  #237  stack: reconcile upstream's empty-send download test with the search query plan
    26e5f16e1  #237  stack: reconcile the companion log-sanitization test with the provider it rebuilds
    9dbc98cec  #237  stack: reconcile DownloadMonitorService with the 500-line cap
    67860065b  #237  stack: reconcile RootFoldersController with the 500-line cap
    42a96f309  #237  stack: reconcile AudiobookFileService with the 500-line cap
    df6e67f77  #237  stack: reconcile upstream's SQLite-reload refresh test with the stack's UTC read converter
    99956a5f4  #68   fix(filesystem): resolve a linked source ancestor only when no link in its chain sits inside a configured root
    0f35b460f  #68   test(filesystem): pin symlinked source ancestors against the configured roots
    60d7856ec  #109  stack-image: fail the build if the version stamp is stale, not just missing
    d3f11d09e  #109  stack-image: anchor the version/patches grep to the manifest header
    b14a79cc6  #383  fix(delete): keep the recursive folder delete unless every tracked file is proven (#1071)
    8ca1be569  #383  fix(delete): keep the recursive delete for durably owned folders (#1071)
    5b94e8e23  #382  Validate quality profile ids on library update and bulk edit
    4941caced  #237  stack: import AuthorCredits where the rescan role-suffix fix was ported
    d4670f6e1  #237  stack: give the upgrade-allowed and embed-cover-art migrations IDs after the upstream frontier
    8fe4742c5  #237  stack: point upstream's empty-query and fallback-query tests at this stack's search surface
    e4a93cde7  #237  stack: rebuild the migration Designer chain for v1.5.9's order
    9559b3e6a  #237  stack: reconcile two files with the 500-line cap after v1.5.9
    07c06ffc7  #237  stack: reconcile #1021's and upstream's new tests with this stack's search and queue shapes
    304298cdc  #237  stack: let the refresh-cycle spend test walk two regions under #1043's rule
    fa468bc55  #237  stack: order line 267's bare-anywhere-wins tests by bare credit, as #1013 now does
    e91fc1955  #237  stack: queue reconciliation tests mock the snapshot API the poller now calls
    5b30ea849  #68   stack: adapt the nested-symlink source test to item 68's behaviour
    3096e8267  #237  stack: pin System.Security.Cryptography.Pkcs to 10.0.13, the first version outside two published advisories
    558655eef  #-   stack: merge upstream canary 1.5.11 into the published stack
    782a3aa12  #-   style(fe): run prettier over the 12 stack files that drifted from it

## Items, in application order

    105  fix/series-position-not-lost                 2 patches
    106  fix/767-series-asin                          3 patches
    43   fix/777-macos-ffprobe-url                    1 patch
    23   local/rb-bug24-queue-guard-1314              3 patches
    24   fix/bug11-taglib-writestream                 6 patches
    24   local/rb-843-hardlink-guard-1314-base        0 patches
    24   local/rb-843-hardlink-guard-1314             2 patches
    22   fix/bug4-n-of-m-chapter-stem                 3 patches
    17   local/rb-bug5-library-import-series-memberships-159 3 patches
    11   local/rb-818-descriptor-path-leaks-1314      3 patches
    29   fix/bug25-shared-circuit-breaker             2 patches
    42   fix/bug13-manual-import-naming-variables     3 patches
    32   fix/bug1-prowlarr-urlbase                    2 patches
    34   fix/bug28-urlbase-subpath                    4 patches
    35   fix/bug19-duplicate-release-guard            3 patches
    36   fix/795-series-order-culture                 3 patches
    36   fix/796-culture-parse                        2 patches
    35   fix/qbittorrent-409-rejected-release         3 patches
    160  local/rb-startupconfig-merge-159             2 patches
    64   local/rb-980-with-urlbase-validation-159     5 patches
    33   fix/audible-timeout-not-zero-match           4 patches
    25   local/rb-914-on-843-r2-159-base              0 patches
    25   local/rb-914-on-843-r2-159                   7 patches
    60   local/rb-surface-file-mutation-cause-1314    3 patches
    61   fix/retry-import-requeues                    2 patches
    51   local/rb-release-blocklist-159-base          0 patches
    51   local/rb-release-blocklist-159               15 patches
    62   fix/894-download-finalization-settings       5 patches
    65   fix/895-maximum-age-visibility               1 patch
    46   consolidate/921-863-985                      7 patches
    47   fix/900-download-client-priority             5 patches
    44   fix/897-image-serving-assertions             1 patch
    83   feat/595-grouped-list-view                   2 patches
    45   fix/898-qbittorrent-advanced-settings        2 patches
    68   local/rb-source-capability-cause-159         8 patches
    72   fix/bug31-stranded-import-retry              2 patches
    80   fix/quality-bitrate-tolerance                2 patches
    102  fix/72-container-healthcheck                 1 patch
    31   872/option-2-dispatch-learns-ui-names        1 patch
    49   feat/urlbase-fe-basehref                     2 patches
    109  local/patch-version-stamp                    1 patch
    109  local/patch-publish-workflow                 2 patches
    108  local/rb-pr-902-1314-base                    0 patches
    108  local/rb-pr-902-1314                         2 patches
    73   local/rb-wanted-cutoff-unmet-159             1 patch
    74   fix/author-page-series-memberships           1 patch
    75   feat/authors-series-count                    1 patch
    76   local/rb-implement-reprocess-1314-base       0 patches
    76   local/rb-implement-reprocess-1314            3 patches
    83   feat/activity-queue-sorting                  1 patch
    86   fix/author-page-language-filter              1 patch
    94   fix/minimum-seeders-case                     1 patch
    110  fix/import-blacklist-extension-casing        1 patch
    112  fix/system-log-level-casing                  1 patch
    20   fix/848-search-query-title                   2 patches
    66   fix/927-removal-control                      1 patch
    66   927/option-1-couple-job-lifetime             1 patch
    99   fix/72-author-asin-collision                 1 patch
    100  fix/72-author-folder-canonical               2 patches
    113  fix/system-logs-no-fabrication               1 patch
    114  fix/collection-status-badge-role             1 patch
    115  fix/api-key-clipboard-failure                1 patch
    95   fix/auth-enforcer-path-casing                1 patch
    68   local/rb-resolve-symlinked-source-path-resolved-159 2 patches
    98   fix/928-silent-catch-blocks                  1 patch
    108  fix/persisted-utc-kind                       1 patch
    78   check/bug45-quality-ladder-parity            1 patch
    69   fix/bug6-audible-timeout-retry               2 patches
    136  fix/136-nzbget-history-warn-once             2 patches
    92   local/rb-author-series-section-159-base      0 patches
    92   local/rb-author-series-section-159           2 patches
    141  fix/monitoring-series-memberships            5 patches
    145  feat/queue-management-1                      13 patches
    144  fix/provider-silence-is-not-absence          8 patches
    144  local/rb-metadata-refresh-foundation-159-base 0 patches
    144  local/rb-metadata-refresh-foundation-159     4 patches
    144  local/rb-metadata-refresh-scheduled-159-base 0 patches
    144  local/rb-metadata-refresh-scheduled-159      4 patches
    151  local/rb-971-on-144-159-base                 0 patches
    151  local/rb-971-on-144-159                      1 patch
    153  local/rb-973-on-974-159-base                 0 patches
    153  local/rb-973-on-974-159                      4 patches
    154  local/35-tests-on-154-signature              1 patch
    155  local/rb-975-resolved-base                   0 patches
    155  local/rb-975-resolved-1314-base              0 patches
    155  local/rb-975-resolved-1314                   1 patch
    156  fix/naming-table-casing                      1 patch
    158  fold/158-qbittorrent-fails-body              1 patch
    159  fix/search-fallback-series-asin              1 patch
    161  local/rb-161-on-153-159-base                 0 patches
    161  local/rb-161-on-153-159                      1 patch
    163  fix/webhook-url-sanitize                     2 patches
    166  local/rb-166b-failure-reason-159-base        0 patches
    166  local/rb-166b-failure-reason-159             3 patches
    166  local/rb-166-170-on-166b-159-base            0 patches
    166  local/rb-166-170-on-166b-159                 11 patches
    172  local/rb-abridged-flag-word-boundary-0924-base 0 patches
    172  local/rb-abridged-flag-word-boundary-0924    2 patches
    173  feat/expose-app-metrics                      1 patch
    170  local/rb-search-request-budget-159-base      0 patches
    170  local/rb-search-request-budget-159           2 patches
    123  local/123-on-872-v2                          3 patches
    126  local/126-drop-enable-notifications          1 patch
    123  local/rb-dead-settings-batch-159-base        0 patches
    123  local/rb-dead-settings-batch-159             4 patches
    124  local/rb-124-allowed-file-extensions-159-base 0 patches
    124  local/rb-124-allowed-file-extensions-159     2 patches
    129  local/129-remove-indexer-tags                1 patch
    133  local/rb-133-on-980-159-base                 0 patches
    133  local/rb-133-on-980-159                      1 patch
    138  local/rb-138-on-141-159-base                 0 patches
    138  local/rb-138-on-141-159                      2 patches
    199  local/rb-199-on-shim-159-base                0 patches
    199  local/rb-199-on-shim-159                     2 patches
    199  local/rb-199-dedupe-on-shim-159-base         0 patches
    199  local/rb-199-dedupe-on-shim-159              1 patch
    151  local/rb-151-refresh-narrowing-fix-159       2 patches
    201  local/rb-201-on-shim-159-base                0 patches
    201  local/rb-201-on-shim-159                     3 patches
    201  local/rb-author-grouping-normalize-159-base  0 patches
    201  local/rb-author-grouping-normalize-159       1 patch
    200  local/rb-200-on-shim-159-base                0 patches
    200  local/rb-200-on-shim-159                     6 patches
    167  fix/167-manual-import-path-traversal         2 patches
    122  local/rb-122-bundle-individual-preference-159-base 0 patches
    122  local/rb-122-bundle-individual-preference-159 3 patches
    172  local/rb-scorer-3way-reconciled-159-base     0 patches
    172  local/rb-scorer-3way-reconciled-159          5 patches
    173  local/173-on-113                             2 patches
    169  local/rb-169-on-73-159-base                  0 patches
    169  local/rb-169-on-73-159                       2 patches
    165  local/rb-165-detail-on-169-159               1 patch
    165  local/rb-165-toolbar-on-detail-159           2 patches
    165  local/rb-165-calendar-on-detail-159          1 patch
    202  local/rb-wanted-downloads-multi-select-159-base 0 patches
    202  local/rb-wanted-downloads-multi-select-159   1 patch
    289  local/rb-selection-spec-reconcile-159        1 patch
    174  local/rb-174-on-154-159-base                 0 patches
    174  local/rb-174-on-154-159                      3 patches
    174  local/174-signature-fix-on-154               1 patch
    205  fix/redacted-sentinel-round-trip             0 patches
    209  fix/private-address-ipv4-mapped              1 patch
    190  fix/indexer-flags                            3 patches
    183  feat/183-task-scheduler                      8 patches
    183  feat/183-tasks-ui                            3 patches
    183  local/rb-183-housekeeping-retention-159-base 0 patches
    183  local/rb-183-housekeeping-retention-159      4 patches
    285  local/rb-housekeeping-settings-ui-159-base   0 patches
    285  local/rb-housekeeping-settings-ui-159        1 patch
    281  local/rb-housekeeping-deferred-populations-159 2 patches
    185  local/rb-185-on-123-159-base                 0 patches
    185  local/rb-185-on-123-159                      3 patches
    189  local/rb-189-with-ui-base                    0 patches
    189  local/rb-189-with-ui                         6 patches
    189  feat/calendar-status-and-week-start          4 patches
    189  local/rb-165-189-status-test-reconcile-159   1 patch
    79   fix/format-preference-is-not-a-quality-mismatch 2 patches
    252  fix/blank-cutoff-search-loop                 3 patches
    257  local/257-on-252-v2                          1 patch
    254  local/rb-torznab-quality-substring-match-159-base 0 patches
    254  local/rb-torznab-quality-substring-match-159 4 patches
    255  local/rb-quality-gate-respects-allowed-base  0 patches
    255  local/rb-quality-gate-respects-allowed       4 patches
    253  local/rb-deterministic-release-tiebreak-159-base 0 patches
    253  local/rb-deterministic-release-tiebreak-159  6 patches
    256  fix/validate-quality-profile-cutoff          3 patches
    256  local/rb-quality-profile-upgrade-allowed-159-base 0 patches
    256  local/rb-quality-profile-upgrade-allowed-159 7 patches
    261  local/rb-gate-bare-title-on-166-159          2 patches
    262  fix/require-indexer-categories               6 patches
    190  fix/mam-ignored-search-parameters            4 patches
    268  fix/companion-relative-path-escape           2 patches
    269  fix/manual-companion-relative-path           4 patches
    21   local/21-companion-tests-on-269              1 patch
    269  fix/269-silent-drop-companion-log            2 patches
    273  local/rb-273-on-shim-159                     6 patches
    275  local/rb-strip-role-suffix-from-credits-159-base 0 patches
    275  local/rb-strip-role-suffix-from-credits-159  7 patches
    276  local/rb-276-on-shim-159-base                0 patches
    276  local/rb-276-on-shim-159                     7 patches
    270  local/rb-hardlink-falls-back-to-copy-1314    2 patches
    277  local/rb-delete-removes-from-client-0924-base 0 patches
    277  local/rb-delete-removes-from-client-0924     12 patches
    277  local/rb-277-bulk-remove-defaults-client-removal-1314-base 0 patches
    277  local/rb-277-bulk-remove-defaults-client-removal-1314 1 patch
    276  local/rb-276-ui-on-shim-159-base             0 patches
    276  local/rb-276-ui-on-shim-159                  3 patches
    278  local/rb-custom-script-settings-ui-159-base  0 patches
    278  local/rb-custom-script-settings-ui-159       1 patch
    286  local/rb-email-notification-159-base         0 patches
    286  local/rb-email-notification-159              8 patches
    286  local/rb-configservice-split-on-email-notification-159 1 patch
    177  local/rb-download-client-priority-159-base   0 patches
    177  local/rb-download-client-priority-159        6 patches
    177  local/rb-indexer-download-client-binding-159-base 0 patches
    177  local/rb-indexer-download-client-binding-159 3 patches
    177  local/rb-download-client-status-159-base     0 patches
    177  local/rb-download-client-status-159          4 patches
    177  local/rb-download-client-status-designers-159 1 patch
    179  local/rb-backup-service-159-base             0 patches
    179  local/rb-backup-service-159                  11 patches
    180  local/rb-180-per-indexer-seed-criteria-159-base 0 patches
    180  local/rb-180-per-indexer-seed-criteria-159   2 patches
    181  fix/release-score-ceiling                    4 patches
    264  local/rb-264-on-shim-base                    0 patches
    264  local/rb-264-on-shim                         3 patches
    263  local/rb-263-on-shim-159-base                0 patches
    263  local/rb-263-on-shim-159                     2 patches
    181  local/rb-scorer-split-on-263-159             2 patches
    182  local/rb-182-naming-preview-server-rendered-159-base 0 patches
    182  local/rb-182-naming-preview-server-rendered-159 4 patches
    184  check/184-notification-event-gaps            2 patches
    187  local/rb-import-free-space-check-159-base    0 patches
    187  local/rb-import-free-space-check-159         4 patches
    284  fix/metadata-repair-guard-includes           2 patches
    283  local/rb-drop-dead-process-execution-store-159-base 0 patches
    283  local/rb-drop-dead-process-execution-store-159 2 patches
    237  local/rb-stack-reconcile-159-base            0 patches
    237  local/rb-stack-reconcile-159                 9 patches
    237  local/rb-migration-designers-159-base        0 patches
    237  local/rb-migration-designers-159             2 patches
    212  local/rb-mp4-audio-import-159-base           0 patches
    212  local/rb-mp4-audio-import-159                5 patches
    191  local/rb-qbittorrent-post-import-category-control-1314-base 0 patches
    191  local/rb-qbittorrent-post-import-category-control-1314 2 patches
    288  local/rb-symlink-chain-walks-every-hop-159   2 patches
    237  local/rb-designer-chain-0924-base            0 patches
    237  local/rb-designer-chain-0924                 1 patch
    293  fix/grid-container-height-shrinks-to-content 1 patch
    293  local/rb-grid-container-height-row-stride-159-base 0 patches
    293  local/rb-grid-container-height-row-stride-159 2 patches
    279  local/rb-279-metadata-refresh-manual-trigger-base 0 patches
    279  local/rb-279-metadata-refresh-manual-trigger 1 patch
    146  local/rb-146-manual-import-requires-matched-book-159-base 0 patches
    146  local/rb-146-manual-import-requires-matched-book-159 2 patches
    215  local/rb-215-refused-manual-import-rolls-back-1314-base 0 patches
    215  local/rb-215-refused-manual-import-rolls-back-1314 2 patches
    292  local/rb-292-release-matches-requested-book-159-base 0 patches
    292  local/rb-292-release-matches-requested-book-159 5 patches
    310  local/rb-310-grab-time-series-position-159   5 patches
    356  local/rb-356-series-position-fractional-tolerance-159 2 patches
    139  local/rb-953-series-identity-not-display-name-base 0 patches
    139  local/rb-953-series-identity-not-display-name 3 patches
    291  local/rb-291-indexer-backoff-own-dbcontext-base 0 patches
    291  local/rb-291-indexer-backoff-own-dbcontext   2 patches
    215  local/rb-manual-import-processitem-split-1314 1 patch
    295  local/rb-295-indexer-health-surfacing-159-base 0 patches
    295  local/rb-295-indexer-health-surfacing-159    5 patches
    81   local/rb-81-stalled-downloads-159-base       0 patches
    81   local/rb-81-stalled-downloads-159            4 patches
    223  local/rb-896-quality-ladder-one-copy-159-base 0 patches
    223  local/rb-896-quality-ladder-one-copy-159     2 patches
    63   local/rb-895-prefer-newer-bonus-base         0 patches
    63   local/rb-895-prefer-newer-bonus              5 patches
    218  local/rb-822-scan-no-files-accepted-159-base 0 patches
    218  local/rb-822-scan-no-files-accepted-159      12 patches
    3    local/rb-3-791-option-d-nuget-ffprobe-159-base 0 patches
    3    local/rb-3-791-option-d-nuget-ffprobe-159    1 patch
    309  local/rb-failed-download-autosearch-default-true-159-base 0 patches
    309  local/rb-failed-download-autosearch-default-true-159 2 patches
    298  fix/298-redaction-fallback                   2 patches
    299  local/rb-299-metadata-refresh-unknown-scope-key-159-base 0 patches
    299  local/rb-299-metadata-refresh-unknown-scope-key-159 2 patches
    69   local/rb-69-canremove-seed-gate-159-base     0 patches
    69   local/rb-69-canremove-seed-gate-159          4 patches
    317  local/rb-download-pipeline-159-base          0 patches
    317  local/rb-download-pipeline-159               3 patches
    324  local/rb-324-download-failure-step-isolation-159-base 0 patches
    324  local/rb-324-download-failure-step-isolation-159 1 patch
    326  local/rb-1008-qbit-numeric-tolerance-stacked-159-base 0 patches
    326  local/rb-1008-qbit-numeric-tolerance-stacked-159 3 patches
    218  local/rb-218-scanjobprocessor-diagnostic-logging-159 1 patch
    325  local/rb-325-orphan-cleanup-history-row-1314-base 0 patches
    325  local/rb-325-orphan-cleanup-history-row-1314 1 patch
    340  local/rb-340-removal-workflow-history-row-1314-base 0 patches
    340  local/rb-340-removal-workflow-history-row-1314 1 patch
    367  local/rb-367-removed-row-error-field-1314-base 0 patches
    367  local/rb-367-removed-row-error-field-1314    1 patch
    333  local/rb-333-nzbget-history-delete-status-159-base 0 patches
    333  local/rb-333-nzbget-history-delete-status-159 2 patches
    335  local/rb-918-blocklist-frontend-159-base     0 patches
    335  local/rb-918-blocklist-frontend-159          2 patches
    336  local/rb-336-music-category-gate-159-base    0 patches
    336  local/rb-336-music-category-gate-159         5 patches
    353  local/rb-335-blocklist-remove-confirm-159    1 patch
    361  local/rb-336-strengthen-classifier-159       10 patches
    371  local/rb-series-and-metadata-159-base        0 patches
    371  local/rb-series-and-metadata-159             4 patches
    366  local/rb-341-author-credit-classification-159-base 0 patches
    366  local/rb-341-author-credit-classification-159 4 patches
    357  local/rb-357-nzbget-history-limit-159-base   0 patches
    357  local/rb-357-nzbget-history-limit-159        2 patches
    237  local/rb-pr-first-substitution-159-base      0 patches
    237  local/rb-pr-first-substitution-159           23 patches
    237  local/rb-stack-reconcile-after-prfirst-159-base 0 patches
    237  local/rb-stack-reconcile-after-prfirst-159   21 patches
    68   local/rb-item68-option-b3-159-base           0 patches
    68   local/rb-item68-option-b3-159                2 patches
    109  local/rb-publish-workflow-stamp-check-1314-base 0 patches
    109  local/rb-publish-workflow-stamp-check-1314   2 patches
    383  fix/1071-delete-gate-live-proof              2 patches
    382  fix/1056-1057-quality-profile-validation     1 patch
    237  local/rb-stack-reconcile-after-rebase-159-base 0 patches
    237  local/rb-stack-reconcile-after-rebase-159    7 patches
    237  local/rb-stack-reconcile-after-resync-159-base 0 patches
    237  local/rb-stack-reconcile-after-resync-159    1 patch
    237  local/rb-queue-snapshot-mock-fix-159-base    0 patches
    237  local/rb-queue-snapshot-mock-fix-159         1 patch
    68   local/rb-item68-nested-symlink-tests-159-base 0 patches
    68   local/rb-item68-nested-symlink-tests-159     1 patch
    237  local/rb-pkcs-transitive-pin-159-base        0 patches
    237  local/rb-pkcs-transitive-pin-159             1 patch


Regenerate with tools/local_stack.sh in the tracker repo.

This head is the published round 27 stack (3096e8267 plus its manifest commit e8d8e52bf) with upstream canary
61117b813 merged in, so it is a fast-forward of the branch it replaces and no published sha changed. It is not
a linear stack on the base. The patch list above is the round 27 list, still accurate for every patch it names
except three, a06b1b347, ac5e63bc2 and 5d99e112d, which upstream 3b7e0fbff (#961) now supplies; they stay in
history and are listed for continuity. The two entries added at the end are the merge and a stack-only prettier
commit. The patch count is `git rev-list --count base..HEAD` taken before this manifest commit, so it counts the
round 27 manifest commit, the merge and the three superseded commits, and overstates the distinct patches
(about 656). The next `local_stack.sh refresh` re-linearizes the stack.
