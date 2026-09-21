# Stack manifest

This build is NOT a stock release. It is upstream canary plus unmerged patches.

    base:        a630572e983614a52ea409a23da52a99e3b8b91b
    base short:  a630572e9
    patches:     356
    version:     1.3.4+m4bard.356

## Patches, oldest first

    683a4513a  #105  fix(naming): don't lose a series position that isn't a plain number
    85a9303b8  #105  fix(naming): derive both series-position fields from one source
    18615696f  #106  fix(metadata): populate SeriesAsin from the Audnexus series record
    d77b45fed  #-    fix(metadata): carry the Audnexus series list, with identifiers, through the product lookup
    e70432268  #-    test(metadata): pin what a blank Audnexus series ASIN stores
    b09bed0fe  #43   fix(ffmpeg): point macOS at evermeet's ffprobe archive, not its ffmpeg one
    9e5ea7583  #23   fix(qbittorrent): one unreadable torrent should not truncate the queue poll
    b113d93c2  #23   fix(qbittorrent): let a client that stops answering fail the poll
    aabbb224b  #23   fix(qbittorrent): quieten the per-torrent skip and escape the hash
    625d9f726  #21   manual-import: authorize companion files against the root folder, not the book folder
    73f3e163e  #21   tests: pin the managed boundary the companion pass hands the ownership store
    0bef7a9e6  #21   tests: adapt the boundary test to the publication path #864 introduced
    c1ab1266e  #21   tests: give the out-of-root case the roots the pass now needs
    43c193cf0  #-    fix(manual-import): mark the companion pass failed when a boundary is missing
    e2474f492  #-    tests: assert a companion file reaches its destination under the new boundary
    3447876ba  #24   fix(import): actually embed the ASIN after an import
    46901f608  #24   comment: drop the em dash from the read-write stream note
    6ea2205bd  #-    tests: drive ApplyAsinTag through a stub TagLib.File
    5a107e346  #22   Group chapter files indexed as "N of M" into one unmatched-scan item
    bd1aa6395  #-    test(scan): make the bare "N of M" case able to fail
    26bab6fcd  #-    fix(scan): leave a Book or Volume index alone when stripping "N of M"
    e84f708e0  #17   Library import: keep every series membership, not just the first
    fb69e9fcb  #-    fix(library-import): build series memberships from the enriched product and fix the unmatched-files mapping
    b927a32f6  #-    fix(library-import): require the B0 prefix before keeping a series ASIN
    a852fd903  #11   fix(scan): stop the Linux descriptor path leaking into metadata and size
    69a16794e  #11   tests: follow the scan's metadata read onto the two-part file source
    4a9dcf0dd  #11   fix(scan): narrow to the metadata boundary, leave the size to #901
    06bf713b6  #27   fix(scoring): resolve indexers once per batch, not once per result in parallel
    bbd531a12  #27   test(scoring): assert the pre-resolved indexer actually reaches the scorer
    f6a003b3c  #30   fix(recovery): name every legacy journal blocking startup, not just the first
    985377cfe  #30   test(recovery): cover the capped listing branch of the legacy-journal message
    d7ea8f578  #-    fix(recovery): name the state each mutation reached, and log the whole set
    a8c499f3f  #29   fix(download-clients): one circuit breaker per client, not one for all of them
    87a6a58b4  #29   test(download-clients): say what the isolation test does not cover
    0e323042d  #42   fix(manual-import): match the rename naming table on casing and missing tokens
    b3c095f23  #42   test(manual-import): assert path segments, not substrings
    1ddc3a8b3  #-    fix(manual-import): build every naming key the rename table builds
    e5c82954f  #37   fix(downloads): resolve a client's path mappings once per batch, not per item
    05a6313ce  #37   fix(downloads): translate a queue item's source files from the resolved mappings too
    183a3eec1  #37   fix(downloads): skip the mapping lookup when the queue is empty
    801b86077  #37   fix(downloads): serve a client's path mappings from the cache it already invalidates
    feae3db6a  #37   test(downloads): create mappings through the service so the cache sees them
    e0bf28899  #37   test(downloads): hold the new cache tests to the repository's test conventions
    634559d49  #32   fix(prowlarr-import): build proxy URLs from the base Prowlarr answered on
    2b06a1388  #32   fix(prowlarr-import): only adopt a discovery base on the origin that was asked
    243f371d5  #34   Serve under a URL sub-path by honouring the UrlBase that already exists
    956d4a86e  #34   feat(notifications): give notifications their own ApplicationUrl
    211aba993  #-    feat(notifications): read LISTENARR_PUBLIC_URL as the notification base
    5b51a783e  #-    fix(api): set the path base from UrlBase instead of appending to it
    81ad19c23  #35   Record a failed grab in history when the download client rejects it
    82613cac0  #35   test(downloads): assert the failure record on the blank-identifier path
    5d73b735e  #35   fix(downloads): sanitize the client message written to history
    92bccc576  #36   fix(metadata): order series positions with an invariant parse
    3c11ea579  #36   tests: bring the series-position test up to the convention #717 added
    e2e3b27cb  #36   fix(metadata): reject a grouped series position instead of misreading it
    5bc20e783  #36   fix(parsing): pin machine-format number parses to the invariant culture
    3b62a3b6c  #36   fix(parsing): match size units and reject non machine-format numbers
    a1e1b90a6  #35   fix(qbittorrent): tell a refused release apart from a failed submission
    da290c11c  #-    test(downloads): pin the controller half of the 409 change
    308c96692  #-    fix(downloads): answer a refused release with 409 on the manual send path
    abd523016  #-    fix(config): overlay a posted startup config onto the file instead of replacing it
    728627221  #-    fix(fe): do not post a startup config the settings view never loaded
    c29b4875f  #-    feat(settings): add a URL Base field to General settings
    526d5658d  #-    feat(settings): tie the URL Base warning to the field it is about
    9f483cdeb  #-    feat(settings): show an empty URL Base box when the stored value is the site root
    3e6178afd  #-    feat(settings): add an Application URL field beside URL Base
    0df2beddf  #-    feat(api): reject a UrlBase that is a full URL
    38fd00e4b  #33   fix(audible): tell a failed catalog lookup apart from a confirmed zero-match
    32d393c44  #33   test(audible): cover the other ways the catalog lookup fails, and the 503 itself
    9649ce32f  #33   test(audible): give the new controller class the repository's test conventions
    21abb7a33  #33   fix(audible): declare the search endpoint's responses, including the new 503
    ed018017a  #-    feat(import): embed cover art into imported files, behind a setting
    4ca2d5091  #-    docs: attach the new doc comments to the members they describe
    b8f8a16ab  #-    tests: pin that cover art replaces the existing artwork rather than appending
    b60522f81  #-    fix(import): embed cover art for a book that has artwork but no ASIN
    2d92fe173  #-    tests: keep this branch's migration out of the shared expected list
    6b5d1f9dc  #-    refactor: keep the cover art code clear of PR 843's edits to the same file
    46ef515ba  #60   fix(filesystem): record why a file mutation failed, not only that it did
    0d1b51eaa  #60   test(downloads): name the no-inner-cause case for what it now does
    438903123  #-    refactor(downloads): format the failure cause where both writers can reach it
    ced6fdd83  #-    fix(downloads): make retry-import actually requeue the import
    84fb1f45f  #-    fix(downloads): survive two retry requests racing on the same download
    3bdc7b35d  #51   feat(downloads): blocklist a release that failed, so the retry stops
    ce835637c  #51   fix(downloads): key a blocked release on something that survives a re-grab
    2e9c88812  #51   fix(search): consult the blocklist on the path that actually re-grabs
    57f086b46  #-    fix(downloads): work out a release identity once, at the grab, and store it
    60de1aae7  #-    chore(downloads): drop the using directives this branch duplicated globally
    22aa1d3df  #-    chore(downloads): drop the using directives the identity fix restated globally
    6ffb1c142  #-    fix(downloads): treat a losing blocklist insert as already blocked
    2ab76b337  #-    feat(downloads): give a blocked release a way back out
    1212028ac  #-    fix(downloads): remove a book's blocklist entries when the book is deleted
    f29c60a35  #-    fix(downloads): block a release only when failed-download handling is on
    1d463852d  #-    fix(downloads): satisfy the three architecture rules the new surface broke
    d5fe98155  #-    fix(downloads): give the three download-finalization settings a reader again
    57879c53a  #-    test(downloads): pin that the processor reads the configured retry delay
    16271fbcb  #-    fix(downloads): stop the retry backoff doubling past a day
    1b4edf44b  #-    fix(downloads): hold only a first transition into Completed
    cd6f173b9  #-    test(downloads): scope the completion-stability save to the one test that needs it
    a220574b1  #-    fix(quality-profiles): stop hiding Maximum Age, and load a profile's qualities
    374a1cd5d  #-    fix(search): read Indexer.MinimumAge and Indexer.MaximumSize, and measure age in UTC
    baebbd3f0  #-    refactor(search): split the scorer, and test the UTC date parse directly
    79f0f5585  #-    test(search): pin that the indexer size ceiling applies to Usenet too
    94ff3d43c  #-    test(search): make the new parse tests follow the repository conventions
    251496b17  #-    fix(download-clients): make the Priority dropdown speak the planners' vocabulary
    e7ebf12f2  #-    test(download-clients): drop two usings the global usings already cover
    4b094ecbe  #-    fix(download-clients): read a stored legacy priority back as Normal
    89bc9f577  #-    fix(download-clients): say what Default does per client, and name Force's band
    7be6eb096  #-    fix(download-clients): lower the priority in the addurl path too
    1114dff56  #-    test(images): assert that a cached image is served, not that something happened
    7ad657525  #83   feat(library): render the list view when grouping by author or series
    5f8d513c1  #83   fix(library): fetch author covers and remember the mode in the grouped list
    810f7ee8e  #-    fix(qbittorrent): send the four Advanced Settings to the client
    50d31181d  #-    fix(qbittorrent): do not fail a submission the client already accepted
    c6658357f  #-    fix(filesystem): say why a source file could not be pinned
    5de448b1d  #-    fix(filesystem): sanitize the pinning cause before it reaches the log
    0d43bb11b  #-    test(filesystem): fail the refusal test when no cause is captured
    6138be5e0  #-    fix(filesystem): lead the refusal with the cause, through the shared formatter
    adaa923c9  #72   fix(imports): retry a failed file import instead of blocking on the first attempt
    8e4869c8f  #72   fix(imports): retry a failed import only when every file in it failed
    78cb79019  #80   fix(quality): let a real encoder bitrate reach the rung it was encoded for
    2dcf698e2  #80   test(quality): pin the bitrate tolerance from both sides, not just the floor
    cc4b2af3c  #102  fix(docker): give the image a healthcheck it can actually run
    0d52c1527  #31   fix(notifications): dispatch webhooks for the triggers the settings screen offers
    5e4fd44a4  #49   Make the frontend follow the UrlBase by injecting a <base href> into the shell
    5babf3a4a  #49   fix(spa): anchor the shell at the site root even with no UrlBase
    c4d00be89  #109  stack: let a local build stamp its own version
    eb9f98cfb  #109  stack: publish the patched build to GHCR from the fork
    637d734be  #107  fix(search): one indexer timeout no longer discards every other indexer's results
    309b2df77  #108  fix(files): physical-generation snapshots reject database-loaded observation timestamps
    a0fa5f7dc  #108  fix(persistence): restore the UTC contract for PhysicalIdentityObservedAtUtc at EF materialization
    985798648  #73   feat(wanted): add a cutoff-unmet bucket to the Wanted page
    3d6bfcf4a  #74   fix(collection): show an author's books under their membership series
    835895583  #75   feat(library): count the distinct series an author appears in
    e635f2d07  #76   feat(downloads): implement the three reprocess endpoints instead of returning success
    25a7ef514  #76   fix(downloads): refuse an ineligible reprocess instead of throwing at the caller
    8e94dc7af  #-    fix(downloads): drop the unused using and the seam-adjacent whitespace
    125d0875d  #83   feat(activity): sort the queue, and show when an item was added
    7b5fb562d  #-    fix(collection): apply the language preference to library books, not just suggestions
    dbba26ab1  #94   fix(search): the minimum seeders gate never fired on a real torrent
    1e6d9021e  #110  fix(import): match blacklisted extensions without regard to case
    f5f3c7dbd  #111  fix(qbittorrent): map 4.x paused torrent states alongside 5.x stopped states
    ecfffacf7  #112  fix(ui): match System recent-log severity classes to the stylesheet
    47c6c2f24  #20   fix(search): derive one indexer query title, shared by both search paths
    d1c9f40f9  #-    fix(downloads): give the queue a control that removes a terminal download
    921a26153  #-    fix(downloads): tie a processing job's retention to the download it explains
    f3e04b5b5  #99   fix(authors): stop attributing one author's ASIN to every co-author
    569b8344d  #100  fix(naming): spell an author's initials one way in folder names
    f55394320  #113  fix(system): stop inventing log entries when no log file exists
    c3f26ee38  #114  fix(library): stop announcing the list-view status badge as a button
    48679b947  #115  fix(settings): stop a failed clipboard write from reporting a failed regeneration
    46df44b3a  #95   fix(api): the authentication gate ignored requests that varied the path casing
    7e63d88c3  #-    fix(filesystem): resolve a symlinked source path instead of refusing it
    c2babab24  #98   fix(logging): give silent catch blocks a real log call or a stated reason
    d92d4da15  #108  fix(persistence): keep a persisted UTC timestamp UTC when it is read back
    9d44cade9  #-    test(scoring): pin the four hand-copied quality ladders to each other
    9a16733c5  #69   test(metadata): count the attempts the Audible retry policy actually makes
    d6a6af169  #-    NZBGet: warn once per failed history entry, and only for monitored entries in polls
    5b4e6cd4e  #-    fix(nzbget): keep the warn-once keys case-insensitive after a scoped read
    30b227aee  #-    refactor(collection): lift shared series and text helpers out of the collection view
    ef76b4019  #-    feat(collection): show an author's series as a section on the author page
    b41fc4f7a  #-    fix(monitoring): keep every series membership and its ASIN when a monitored author or series adds a book
    69e9fcc67  #-    fix(library): let a legacy-only metadata payload carry its series identifier
    8aeb84bcf  #-    feat(activity): queue selection that survives the poll instead of being reset by it
    120cebf7b  #-    feat(activity): sequential bulk runner with per-id outcomes and one summary line
    a6d37d93b  #-    feat(api): keep the queue-management API tests, drop the methods #917 and #927 already added
    2757eef23  #-    feat(activity): a queue toolbar whose verbs say what they will do before doing it
    01aa1ca73  #-    fix(activity): the select cell reasserts its props after a click that changed nothing
    43fe2d8a5  #-    feat(downloads): a Retry button that appears only where the endpoint will accept it
    b8c3aaec4  #-    feat(activity): select queue rows and act on them together
    230368957  #-    fix(activity): the filter is a viewport, so selection controls act on the rows in view
    379dafc18  #-    feat(downloads): the Retry button does the retry, instead of promising one later
    e5ed1edea  #-    fix(activity): the retry button dresses itself and select all goes quiet with nothing to select
    fdb2c39c9  #-    fix(activity): the toolbar drops a confirmation the selection outlived and rests while busy
    1ca1e6b27  #-    fix(queue): overlay import status onto queue rows the client cannot represent
    b725cf643  #-    fix(activity): disclose the true count before an unbounded sweep
    e2d317d76  #-    metadata: name the two nothings a provider can answer with
    4c8d65404  #-    metadata: answer the ASIN endpoints with a status, not a stack trace
    05ab58316  #-    images: keep serving the placeholder when the provider does not answer
    4e04d9212  #-    search: move the Audible series endpoints into a partial
    24c268bb6  #-    search: say the provider did not answer, rather than returning nothing
    dd7c33cef  #-    metadata: walk every source, then report the fault rather than a miss
    f32c372bf  #-    metadata: let the Audible client raise when the provider did not answer
    14768bacb  #-    metadata: hold the Audnexus book lookup to the same division
    1b0e0c62c  #-    library: record when a book's provider metadata was last refreshed
    bbf0ca85b  #-    settings: the refresh interval, staleness age and budget are operator-set
    0a189c52f  #-    metadata: a request budget with an hourly refill and a spacing floor
    96a7a6377  #-    metadata: lift the per-book rescan out of its HTTP wrapper
    0bee5b61b  #-    metadata: one gate, one run, one budget across every refresh entry point
    292ff7825  #-    metadata: walk the stalest books on an interval, and let an operator ask
    651ac1b74  #-    fe: a Metadata Refresh section in the settings screen
    136f06206  #-    fix(logging): sanitize ASIN log arguments, rebased onto the metadata-refresh chain
    8022c09a6  #-    test(downloads): fail the build when a second place works out a release key
    92a0df4b3  #-    Attach download history rows to the audiobook they belong to
    4b2e753f0  #-    fix(history): record the protocol a download actually used
    e92c43e66  #-    fix(history): resolve the protocol for the remaining event types too
    71e9020bb  #-    test(history): cover the audiobook key and the protocol on one call
    a4e32b9a6  #-    test(downloads): match the int? audiobook id in the grab history verifies
    053ff705e  #156  fix(naming): key the remaining naming tables case insensitively
    ce26d8e32  #-    fix(search): compute the profile size gates in long, not int
    71e73a47c  #-    fix(qbittorrent): a 200 carrying "Fails." is a refusal, not a grab
    4e0004523  #159  fix(search): stop reporting a series name as the series identifier
    787295b89  #-    Record a download client timeout as a failed grab, not a shutdown
    6bf622e73  #-    fix(notifications): sanitize webhook URLs before logging them
    69a6b6e0d  #-    test(notifications): cover SanitizeWebhookUrl and pin the webhook log call sites
    cb2aae954  #-    feat(search): tell an indexer that answered with nothing apart from one that never answered
    82d656788  #-    fix(search): give the per-indexer timeout its own catch, correctly typed
    4e93447db  #-    test(search): match the fake provider to the observation-returning interface
    f7dcfd549  #-    notifications(#126): make EnableNotifications actually gate delivery
    46b562ae0  #-    notifications(#127): dispatch webhooks by stored Type, not URL sniffing
    6beb13dc7  #-    fix(notifications): route webhook Type through the central dispatcher, not per-callsite loops
    4e3db987c  #-    search(#123): wire up EnableAmazonSearch/EnableAudibleSearch, dead since introduction
    d3601daf6  #-    notifications(#128): remove the unreachable legacy /notifications/test endpoint
    db37d8076  #-    settings(#131): add a UI control for ExtractArchives, backend-only until now
    4128d0aeb  #-    downloads(#132): fix RemoveCompletedDownloads for Transmission, SABnzbd, NZBGet
    8458d8e8f  #-    fix: wire AllowedFileExtensions into FileUtils.IsAudioFile
    81b483c59  #-    cleanup: remove two orphaned private AudioExtensions arrays
    143eff1ad  #-    Remove vestigial Indexer.Tags field
    6af2d2a1a  #-    history: add retention setting UI and a daily cleanup worker
    3ea2b3ed1  #-    Preserve series ASIN when mapping monitored author/series books
    d52cc29fd  #-    test(monitoring): resolve the series-monitoring root through identity, not a raw builder
    1fcc517ec  #-    fix(authors): dedupe author-cache methods reintroduced against fix/72's own copy
    9c9718e82  #-    fix(authors): skip the SQL narrowing pre-filter for non-ASCII author names
    ca2ca5d17  #-    Fix: manual import path traversal check trips on legitimate paths (#167)
    ce894e2aa  #-    test(manual-import): bring the path-traversal test class up to convention
    294f1497f  #-    fix(system): stop fabricating download-client connectivity
    65c52b718  #-    tests(system-service-logs): pass the download-client status cache the constructor now requires
    3b9ab875f  #-    fix(wanted): Search All acted on the unfiltered list, not the one on screen
    5967bcf91  #-    fix(nav): dismiss the header search panel when the route changes
    9a596941c  #-    fix(library): Select All reached past the filter into the whole library
    e15203db3  #-    Fix author name normalization to merge spaced-out initials
    119416752  #-    test(domain): bring StringUtilsNormalizeAuthorNameTests up to convention
    0f34fd5d9  #-    fix: remove literal conflict-marker text accidentally committed during rebuild resolution
    f4102c57d  #-    fix(imports): classify failures at the boundary instead of leaking them to History
    f695eda03  #-    search: isolate a per-indexer HTTP timeout from the multi-indexer fan-out
    f651432f3  #-    search: bound indexer fan-out concurrency to 4 (tracker #119)
    dd455cc37  #-    search: separate a rate limit and a refused credential from any other non-2xx
    ce3b4c488  #-    search: persist a per-indexer failure backoff ladder
    5020529a5  #-    search: skip an indexer that is in failure backoff, and record what each one answered
    f6c04c1dd  #-    search: do not stamp LastSearchTime on a book whose indexer set was degraded
    e492a56aa  #-    indexers: show on the card when an indexer is in failure backoff
    83df8e698  #-    feat(search): try a second form of a search before saying the book does not exist
    1bd237921  #-    search: cover what the query ladder and indexer backoff do to each other
    3a77e8827  #-    fix(search): reconcile the query ladder with fakes and tests from three unrelated items
    30ac80ddd  #-    Finish the author-normalizer consolidation and re-derive the keys it left behind
    4b4dd541a  #-    Keep the canonical author spelling the ASIN lookup already returned
    8d64037ad  #-    Converge existing books onto one spelling per author at startup
    98fef24c1  #-    test(startup): mock the metadata-refresh backfill this test's strict repository was missing
    36470bc6a  #-    fix(library): normalize author grouping and include every co-author
    592f6ca61  #-    style: run prettier over item 201's two touched files
    90209c81c  #-    Pin the author-identity behaviour these fixes are meant to change
    9356d2393  #-    Stop reading author identity out of a list that never recorded it
    2daa58590  #-    Refuse to rename a cached author when an ASIN is already somebody else's
    16a9b54a1  #-    Confirm an author ASIN with Audible before binding it as an identity
    b2e99c115  #-    Pin what stops a renamed cache row reaching stored author names
    c13fdce39  #-    fix(authors): dedupe author-cache methods a third time, keeping the collision-safe version
    b64982d5a  #-    fix: complete the NormalizeAuthorName redirect, fix a recurring rerere-replayed brace bug
    149ea25f8  #-    fix(search): word-boundary word filters, and any-of required words
    dc2c50de5  #-    fix(search): route PreferredWords through the same word-boundary matcher
    0d8ca1dce  #-    fix(search): make indexer priority a genuine tie-break instead of dead weight or dominant term
    efa13ca50  #-    search: add ReleaseShapeDetector, which tells a bundle from a single edition
    263e1173f  #-    search: a per-profile preference between bundle and single-book releases
    e073f9f0a  #-    fe: the release shape control, and a Bundle badge on search results
    8e8b06d03  #-    search: give the word-boundary term matcher its own file
    c1763e747  #-    search: pin the precedence between the three scorer concerns
    c05a40dff  #-    fix(search): reconcile the scorer 3-way with fakes and tests from unrelated items, plus two more real bugs found by the suite
    045e8bc59  #-    feat(detail): add Automatic Search action to audiobook detail toolbar
    ec9f33990  #-    feat(library): per-item automatic search icon on the audiobooks list
    13e9de27c  #-    feat(library): bulk automatic search on the audiobooks index toolbar
    13bbadc93  #-    feat(calendar): search for the missing books in the window on screen
    1828532fa  #-    fix(history): style the events that happen, not four that cannot
    0af62a299  #-    fix(history): keep what a grab knew, and name the download client
    3105a88a5  #-    feat(history): a global History page over the API that already existed
    c94af5784  #-    tests(download-service): match RecordDownloadFailedAsync's protocol parameter in the sanitize test
    73445ed18  #-    style: run prettier over the two files item 165's resolution touched
    5d7d6e5af  #-    test(metadata): stop the retry policy tests racing the caller's clock
    e7bffd4d4  #202  feat(ui): multi-row selection on Wanted and Downloads
    8f7b2b4a1  #-    fix(wanted): compose the restored multi-select with items 73, 169 and 145
    ef557ead7  #109  stack: publish a moving current tag beside the immutable commit tag
    72bde3f9d  #-    stack: manifest for a630572e9 plus 278 patches
    a35568f1e  #-    Keep stored secrets when a save carries the redaction sentinel
    1931d06a1  #-    fix(security): an IPv4 address in its mapped form never matched the private ranges
    7dd19db52  #-    fix(myanonamouse): send the torrent filter as tor[searchType]
    8b7af0da0  #-    fix(myanonamouse): spend the freeleech wedge on the download, not the search
    1f6ac8264  #-    feat(indexers): parse the release flags trackers advertise
    d1b7e074e  #-    Add a task surface over the periodic workers, with a manual trigger
    3e8b882fc  #-    notifications: a subscriber model and a Custom Script provider
    7f4fcba1a  #-    feat(calendar): add the calendar endpoint and iCalendar feed
    fcd551c6b  #-    test(indexers): reach the provider's parse by shape, not by a fixed signature
    38ffef5e2  #-    stack: manifest for a630572e9 plus 288 patches
    074a75875  #-    scheduled tasks: an allowlist for manual runs, not a deny-list
    5d89a0468  #-    notifications: let the EnableNotifications switch gate the subscriber fan-out
    965217c88  #-    configuration: keep stored custom scripts when a save omits them
    2b066e661  #-    stack: reconcile the manual-run allowlist with the stacked tree
    4ea7b2cb1  #-    stack: manifest for a630572e9 plus 293 patches
    7f0cea860  #-    search: drop the bare series-name query rung
    e561fd227  #-    stack: manifest for a630572e9 plus 295 patches
    e57c46a8d  #-    WIP fix(quality): a blank cutoff means satisfied, not "search forever"
    ec4eff278  #-    test: bring the new cutoff tests up to the repository's test conventions
    067894da1  #-    torznab: a bitrate has to be its own token, not digits inside one
    31dc185d4  #-    torznab: tests for the x264 mislabel, each with its control
    df53e2fc3  #-    torznab: a number is a bitrate only where the text says it is
    e1adf03ee  #-    torznab: recover the bracketed bitrate the tightening had lost
    a9b5ae526  #-    fix(search): let a quality profile's Allowed flag actually gate
    356cd2e19  #-    fix(search): correct the claims around the quality gate, and pin what it does not decide
    64f4cc27d  #-    quality profile: refuse a cutoff the profile does not allow
    22244f086  #-    tests: cover the cutoff rule at the API and at the attribute
    4da35c2ea  #-    quality profile: make the cutoff rule agree with the code that reads it
    bee76b55c  #-    WIP fix(quality): one definition of Allowed, not two
    9e72e20ec  #-    quality profile: give it the UpgradeAllowed flag the family has
    a65dec266  #-    quality profile: carry upgrades-off across the upgrade, in a startup repair
    b594df132  #-    tests: pin both directions of the upgrade flag, each against its control
    88c0067f7  #-    status evaluator: drop the upgrades-off guard, it was never the reason
    bac3c51d4  #-    tests: pin the new migration id, the backfill gate depends on it
    cfe9d3377  #-    quality profile: decide "blank cutoff" the way the readers decide it
    92888272b  #-    comments: correct three claims a reviewer could check and find false
    86f8b285c  #-    tests: read the observation's results, the way its sibling case already does
    04ad75a46  #-    Break release selection ties deterministically instead of by indexer order
    10506fb92  #-    Make the consuming-site tests actually discriminate their own site
    ca5c06241  #-    Restore Readarr's protocol step, without which the chain is not transitive
    7160839b3  #-    Close the second intransitivity, and three more review findings
    a1d9c7f9b  #-    Rank once per site instead of twice, to stay inside the focus budget
    7587e01b8  #-    Put the same-group invariant in the code, and close the last apparatus gap
    cbb75246f  #-    tests: point the tiebreak harness at the signatures the stack actually has
    6f62b7cfc  #-    tests: move the blank-cutoff pin to the answer fix/blank-cutoff-search-loop gave it
    fe47095cd  #51   test(downloads): pin the release identity wire format with golden vectors
    be03c5b15  #183  scheduled tasks: answer the trigger with the row it started, and keep stopped workers
    fd1a532df  #183  scheduled tasks: say which request started the cycle, and stop 404ing a listed task
    61b6fff06  #183  scheduled tasks: release the gate under the lock that ends the cycle
    acf34a969  #183  scheduled tasks: publish the interval in whole seconds, and refuse what will not fit
    e80254824  #183  scheduled tasks: put the interval refusal on the row, and correct two claims made for it
    26b213cf5  #183  scheduled tasks: make the interval refusal audible, and stop overclaiming what it buys
    9d17ce8f3  #79   fix(status): stop reporting a non-preferred format as below cutoff
    96447b6c4  #79   test(status): make the agreement test read the real projection, and pin the widened cases
    aed7e0a8b  #252  test: flag the blank-cutoff consequence for the not-yet-landed upgrades flag
    289d53195  #257  fix(quality): resolve the cutoff through QualityMatcher, changing two answers
    10b3850d8  #255  fix(search): refuse a quality label the gate cannot place
    6409ad63b  #255  fix(search): answer the review of the quality gate fix
    f6a644c0b  #261  search: skip the bare-title rung when the title cannot carry a query alone
    b4af09652  #261  search: stop the stem and series rungs unanchoring themselves when there is no author
    e63f2d986  #262  Require indexer categories, the way the rest of the *arr family does
    8360a0909  #262  tests: pin that the draft test is gated too, since it binds the same entity
    4310392ee  #262  Make the indexer form usable now that categories are required
    b39a6c737  #262  Move the category default off the entity, where it rewrote stored values
    451205196  #262  tests: pin that an explicit null category list is not a way past the rule
    48627ac29  #262  Validate the merged indexer on update, not just the request body
    28b2521a0  #190  fix(myanonamouse): send the page size as perpage, not tor[perpage]
    92ff9816d  #190  fix(myanonamouse): send the language the caller asked for
    c3e1dca8b  #268  import: give each companion file the source root it actually came from
    ce8c6ad09  #268  import: resolve the companion source roots once per batch, not per file
    1d17d4fae  #269  manual-import: place companions by the selected files' structure, not the caller's path
    0523c9d92  #269  manual-import: a companion goes where the file it accompanies went, and nowhere else
    cf5322744  #269  import: match the companion's neighbour by path identity, and name the rule the manual path uses
    437b5d3ed  #269  import: say why the neighbour comparison is safe for the reason that is actually true
    c2ece6dce  #21   tests: give the companion placement pass the root folder this branch now requires
    de2de1846  #-    quality: split QualityMatcher's internals out, so the file is under the ceiling again
    a433ec0b2  #258  fix(recovery): one unrecoverable deletion intent must not disable the whole filesystem gate

## Items, in application order

    105  fix/series-position-not-lost                 2 patches
    106  fix/767-series-asin                          1 patch
    43   fix/777-macos-ffprobe-url                    1 patch
    23   fix/bug24-queue-guard                        3 patches
    21   fix/bug12-companion-import-boundary          5 patches
    24   fix/bug11-taglib-writestream                 2 patches
    22   fix/bug4-n-of-m-chapter-stem                 1 patch
    17   fix/bug5-library-import-series-memberships   1 patch
    11   fix/818-descriptor-path-leaks                3 patches
    27   fix/bug17-dbcontext-concurrency              2 patches
    30   fix/bug26-journal-repair-visibility          2 patches
    29   fix/bug25-shared-circuit-breaker             2 patches
    42   fix/bug13-manual-import-naming-variables     2 patches
    37   fix/gateway-path-mapping-concurrency         6 patches
    32   fix/bug1-prowlarr-urlbase                    2 patches
    34   fix/bug28-urlbase-subpath                    2 patches
    35   fix/bug19-duplicate-release-guard            3 patches
    36   fix/795-series-order-culture                 3 patches
    36   fix/796-culture-parse                        2 patches
    35   fix/qbittorrent-409-rejected-release         1 patch
    160  fix/startupconfig-merge                      0 patches
    64   local/980-with-urlbase-validation            0 patches
    33   fix/audible-timeout-not-zero-match           4 patches
    25   prreview/914-on-843                          0 patches
    60   fix/surface-file-mutation-cause              2 patches
    61   fix/retry-import-requeues                    0 patches
    51   feat/release-blocklist                       4 patches
    62   fix/894-download-finalization-settings       0 patches
    65   fix/895-maximum-age-visibility               0 patches
    46   fix/899-indexer-age-and-size                 0 patches
    47   fix/900-download-client-priority             0 patches
    44   fix/897-image-serving-assertions             0 patches
    83   feat/595-grouped-list-view                   2 patches
    45   fix/898-qbittorrent-advanced-settings        0 patches
    68   fix/source-capability-cause                  0 patches
    72   fix/bug31-stranded-import-retry              2 patches
    80   fix/quality-bitrate-tolerance                2 patches
    102  fix/72-container-healthcheck                 1 patch
    31   872/option-2-dispatch-learns-ui-names        1 patch
    49   feat/urlbase-fe-basehref                     2 patches
    109  local/patch-version-stamp                    1 patch
    109  local/patch-publish-workflow                 2 patches
    107  pr-757-rebased                               1 patch
    108  pr-902                                       2 patches
    73   feat/wanted-cutoff-unmet                     1 patch
    74   fix/author-page-series-memberships           1 patch
    75   feat/authors-series-count                    1 patch
    76   feat/implement-reprocess                     2 patches
    83   feat/activity-queue-sorting                  1 patch
    86   fix/author-page-language-filter              0 patches
    94   fix/minimum-seeders-case                     1 patch
    110  fix/import-blacklist-extension-casing        1 patch
    111  fix/qbittorrent-paused-states                1 patch
    112  fix/system-log-level-casing                  1 patch
    20   fix/848-search-query-title                   1 patch
    66   fix/927-removal-control                      0 patches
    66   927/option-1-couple-job-lifetime             0 patches
    99   fix/72-author-asin-collision                 1 patch
    100  fix/72-author-folder-canonical               1 patch
    113  fix/system-logs-no-fabrication               1 patch
    114  fix/collection-status-badge-role             1 patch
    115  fix/api-key-clipboard-failure                1 patch
    95   fix/auth-enforcer-path-casing                1 patch
    68   fix/resolve-symlinked-source-path            0 patches
    98   fix/928-silent-catch-blocks                  1 patch
    108  fix/persisted-utc-kind                       1 patch
    78   check/bug45-quality-ladder-parity            0 patches
    69   fix/bug6-audible-timeout-retry               1 patch
    136  fix/136-nzbget-history-warn-once             0 patches
    92   feat/author-series-section                   0 patches
    141  fix/monitoring-series-memberships            0 patches
    145  feat/queue-management-1                      0 patches
    144  fix/provider-silence-is-not-absence          0 patches
    144  feat/metadata-refresh-foundation             0 patches
    144  feat/metadata-refresh-scheduled              0 patches
    151  local/971-on-144-v4                          0 patches
    153  local/973-on-974-v2                          0 patches
    154  local/35-tests-on-154-signature              0 patches
    155  local/975-resolved-v2                        0 patches
    156  fix/naming-table-casing                      1 patch
    157  fix/profile-size-int-overflow                0 patches
    158  fold/158-qbittorrent-fails-body              0 patches
    159  fix/search-fallback-series-asin              1 patch
    161  local/161-on-153-v4                          0 patches
    163  fix/webhook-url-sanitize                     0 patches
    166  local/166b-failure-reason-v2                 0 patches
    166  local/166-170-flat                           0 patches
    123  local/123-on-872-v2                          0 patches
    123  local/dead-settings-batch-v3                 0 patches
    124  local/124-allowed-file-extensions            0 patches
    129  local/129-remove-indexer-tags                0 patches
    133  local/133-on-980                             0 patches
    138  local/138-on-141                             0 patches
    199  fix/author-name-normalize-whitespace         0 patches
    199  local/199-on-99-v2                           0 patches
    151  local/151-refresh-narrowing-fix              0 patches
    201  feat/author-canonicalization-backend         0 patches
    201  fix/author-grouping-normalize-v2             0 patches
    200  feat/author-asin-identity-matching           0 patches
    167  fix/167-manual-import-path-traversal         0 patches
    172  local/scorer-3way-reconciled                 0 patches
    173  local/173-on-113                             0 patches
    169  local/169-on-73                              0 patches
    165  feat/165-detail-page-automatic-search        0 patches
    165  feat/165-items-2-4-audiobooks-toolbar        0 patches
    165  feat/165-item3-calendar-search               0 patches
    202  feat/wanted-downloads-multi-select           1 patch
    174  local/174-on-154-v2                          0 patches
    205  fix/redacted-sentinel-round-trip             0 patches
    209  fix/private-address-ipv4-mapped              0 patches
    190  fix/indexer-flags                            0 patches
    183  feat/183-task-scheduler                      6 patches
    185  local/185-on-123                             0 patches
    189  local/189-on-95                              0 patches
    79   fix/format-preference-is-not-a-quality-mismatch 2 patches
    252  fix/blank-cutoff-search-loop                 1 patch
    257  fix/cutoff-lookup-respects-allowed           1 patch
    254  fix/torznab-quality-substring-match          0 patches
    255  fix/quality-gate-respects-allowed            2 patches
    253  fix/deterministic-release-tiebreak           0 patches
    256  fix/validate-quality-profile-cutoff          0 patches
    256  fix/quality-profile-upgrade-allowed          0 patches
    261  fix/gate-bare-title-on-166                   2 patches
    262  fix/require-indexer-categories               6 patches
    190  fix/mam-ignored-search-parameters            2 patches
    268  fix/companion-relative-path-escape           2 patches
    269  fix/manual-companion-relative-path           4 patches
    258  fix/deletion-intent-does-not-brick-startup   1 patch
    273  fix/author-cache-upsert-is-atomic            0 patches

Regenerate with tools/local_stack.sh in the tracker repo.
