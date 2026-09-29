# Stack manifest

This build is NOT a stock release. It is upstream canary plus unmerged patches.

    base:        a630572e983614a52ea409a23da52a99e3b8b91b
    base short:  a630572e9
    patches:     508
    version:     1.3.4+m4bard.508

## Patches, oldest first

    6fa991527  #105  fix(naming): don't lose a series position that isn't a plain number
    da0309da7  #105  fix(naming): derive both series-position fields from one source
    73f05a5c8  #106  fix(metadata): populate SeriesAsin from the Audnexus series record
    29cdf7a24  #106  fix(metadata): carry the Audnexus series list, with identifiers, through the product lookup
    10a055580  #106  test(metadata): pin what a blank Audnexus series ASIN stores
    64afcd046  #43   fix(ffmpeg): point macOS at evermeet's ffprobe archive, not its ffmpeg one
    d962ecdbe  #23   fix(qbittorrent): one unreadable torrent should not truncate the queue poll
    3cf1bc0e2  #23   fix(qbittorrent): let a client that stops answering fail the poll
    6bab0f9e2  #23   fix(qbittorrent): quieten the per-torrent skip and escape the hash
    2924fd7ee  #21   manual-import: authorize companion files against the root folder, not the book folder
    50909777e  #21   tests: pin the managed boundary the companion pass hands the ownership store
    b1df400d2  #21   tests: adapt the boundary test to the publication path #864 introduced
    67e3cb8e8  #21   tests: give the out-of-root case the roots the pass now needs
    91f61c83e  #21   fix(manual-import): mark the companion pass failed when a boundary is missing
    50ae7628a  #21   tests: assert a companion file reaches its destination under the new boundary
    7ce92430e  #24   fix(import): actually embed the ASIN after an import
    bfea50751  #24   comment: drop the em dash from the read-write stream note
    be0f6e817  #24   tests: drive ApplyAsinTag through a stub TagLib.File
    84d3fe1bf  #22   Group chapter files indexed as "N of M" into one unmatched-scan item
    ddcef5b20  #22   test(scan): make the bare "N of M" case able to fail
    fbb36689e  #22   fix(scan): leave a Book or Volume index alone when stripping "N of M"
    98a47d766  #17   Library import: keep every series membership, not just the first
    a25d29c34  #17   fix(library-import): build series memberships from the enriched product and fix the unmatched-files mapping
    47ad7d3c8  #17   fix(library-import): require the B0 prefix before keeping a series ASIN
    b1308a57d  #11   fix(scan): stop the Linux descriptor path leaking into metadata and size
    18a68b160  #11   tests: follow the scan's metadata read onto the two-part file source
    8ebd0c54a  #11   fix(scan): narrow to the metadata boundary, leave the size to #901
    cacff2c5d  #30   fix(recovery): name every legacy journal blocking startup, not just the first
    cd5c3ce46  #30   test(recovery): cover the capped listing branch of the legacy-journal message
    dc7a39b32  #30   fix(recovery): name the state each mutation reached, and log the whole set
    19417c3ce  #29   fix(download-clients): one circuit breaker per client, not one for all of them
    0cb1bf18e  #29   test(download-clients): say what the isolation test does not cover
    f3dd1f4a8  #42   fix(manual-import): match the rename naming table on casing and missing tokens
    ec6b3984d  #42   test(manual-import): assert path segments, not substrings
    6301ef48a  #42   fix(manual-import): build every naming key the rename table builds
    99d350cb4  #37   fix(downloads): resolve a client's path mappings once per batch, not per item
    03dc56950  #37   fix(downloads): translate a queue item's source files from the resolved mappings too
    7d2f498fb  #37   fix(downloads): skip the mapping lookup when the queue is empty
    77a73a2ad  #37   fix(downloads): serve a client's path mappings from the cache it already invalidates
    4fe4c57b1  #37   test(downloads): create mappings through the service so the cache sees them
    91684e9bb  #37   test(downloads): hold the new cache tests to the repository's test conventions
    25c7e75ed  #32   fix(prowlarr-import): build proxy URLs from the base Prowlarr answered on
    8f657d743  #32   fix(prowlarr-import): only adopt a discovery base on the origin that was asked
    c5017b184  #34   Serve under a URL sub-path by honouring the UrlBase that already exists
    ec8e2b3bf  #34   feat(notifications): give notifications their own ApplicationUrl
    fb7d183eb  #34   feat(notifications): read LISTENARR_PUBLIC_URL as the notification base
    c9c4d6262  #34   fix(api): set the path base from UrlBase instead of appending to it
    fcfedd7c0  #35   Record a failed grab in history when the download client rejects it
    ddd10440a  #35   test(downloads): assert the failure record on the blank-identifier path
    05ee3cec4  #35   fix(downloads): sanitize the client message written to history
    fe3c5b474  #36   fix(metadata): order series positions with an invariant parse
    c15b74082  #36   tests: bring the series-position test up to the convention #717 added
    d4afb1d92  #36   fix(metadata): reject a grouped series position instead of misreading it
    855520512  #36   fix(parsing): pin machine-format number parses to the invariant culture
    cd3c0eefe  #36   fix(parsing): match size units and reject non machine-format numbers
    0a90be59d  #35   fix(qbittorrent): tell a refused release apart from a failed submission
    8433ffd13  #35   test(downloads): pin the controller half of the 409 change
    e8c615fc6  #35   fix(downloads): answer a refused release with 409 on the manual send path
    e42304ddb  #160  fix(config): overlay a posted startup config onto the file instead of replacing it
    d06afb7d4  #160  fix(fe): do not post a startup config the settings view never loaded
    e37395aed  #64   feat(settings): add a URL Base field to General settings
    feb76775f  #64   feat(settings): tie the URL Base warning to the field it is about
    5c4db4073  #64   feat(settings): show an empty URL Base box when the stored value is the site root
    20d5515b8  #64   feat(settings): add an Application URL field beside URL Base
    e536009a1  #64   feat(api): reject a UrlBase that is a full URL
    9bcfe8d3d  #33   fix(audible): tell a failed catalog lookup apart from a confirmed zero-match
    d3c064ac2  #33   test(audible): cover the other ways the catalog lookup fails, and the 503 itself
    911123b20  #33   test(audible): give the new controller class the repository's test conventions
    68f1bbd2b  #33   fix(audible): declare the search endpoint's responses, including the new 503
    6270e85cd  #25   feat(import): embed cover art into imported files, behind a setting
    826afb0e2  #25   docs: attach the new doc comments to the members they describe
    eb10e1367  #25   tests: pin that cover art replaces the existing artwork rather than appending
    a64f6e3fc  #25   fix(import): embed cover art for a book that has artwork but no ASIN
    fdbd688d3  #25   tests: keep this branch's migration out of the shared expected list
    5e414520d  #25   refactor: keep the cover art code clear of PR 843's edits to the same file
    44940c493  #60   fix(filesystem): record why a file mutation failed, not only that it did
    9ba6fa6b8  #60   test(downloads): name the no-inner-cause case for what it now does
    b1db0f7c0  #60   refactor(downloads): format the failure cause where both writers can reach it
    7eb717edd  #61   fix(downloads): make retry-import actually requeue the import
    a66b75235  #61   fix(downloads): survive two retry requests racing on the same download
    743858a48  #51   feat(downloads): blocklist a release that failed, so the retry stops
    01f171f02  #51   fix(downloads): key a blocked release on something that survives a re-grab
    d46338be5  #51   fix(search): consult the blocklist on the path that actually re-grabs
    a5d98f248  #51   fix(downloads): work out a release identity once, at the grab, and store it
    ed58c9cfd  #51   chore(downloads): drop the using directives this branch duplicated globally
    d6d35b0c7  #51   chore(downloads): drop the using directives the identity fix restated globally
    10b2d10ab  #51   fix(downloads): treat a losing blocklist insert as already blocked
    7d08694bc  #51   feat(downloads): give a blocked release a way back out
    aaff3d431  #51   fix(downloads): remove a book's blocklist entries when the book is deleted
    047117d9f  #51   fix(downloads): block a release only when failed-download handling is on
    4424e2993  #51   fix(downloads): satisfy the three architecture rules the new surface broke
    a39ca8df2  #51   test(downloads): pin the release identity wire format with golden vectors
    4db9da1f8  #51   test(downloads): fail the build when a second place works out a release key
    665086d17  #62   fix(downloads): give the three download-finalization settings a reader again
    e57422a3a  #62   test(downloads): pin that the processor reads the configured retry delay
    867d9582d  #62   fix(downloads): stop the retry backoff doubling past a day
    89545b589  #62   fix(downloads): hold only a first transition into Completed
    3c1b3dcc7  #62   test(downloads): scope the completion-stability save to the one test that needs it
    14eea8f5a  #65   fix(quality-profiles): stop hiding Maximum Age, and load a profile's qualities
    e8a78b536  #46   fix(search): read Indexer.MinimumAge and Indexer.MaximumSize, and measure age in UTC
    ac33fae67  #46   refactor(search): split the scorer, and test the UTC date parse directly
    e99e04751  #46   test(search): pin that the indexer size ceiling applies to Usenet too
    0a752e7ae  #46   test(search): make the new parse tests follow the repository conventions
    b8c1d279b  #46   fix(scoring): resolve indexers once per batch, not once per result in parallel
    65cb854f0  #46   test(scoring): assert the pre-resolved indexer actually reaches the scorer
    1576188ec  #46   fix(search): compute the profile size gates in long, not int
    ccd270b89  #47   fix(download-clients): make the Priority dropdown speak the planners' vocabulary
    ecdc85a0d  #47   test(download-clients): drop two usings the global usings already cover
    64ccf1d37  #47   fix(download-clients): read a stored legacy priority back as Normal
    30911649e  #47   fix(download-clients): say what Default does per client, and name Force's band
    5bbb44d50  #47   fix(download-clients): lower the priority in the addurl path too
    0c9bb8623  #44   test(images): assert that a cached image is served, not that something happened
    06a3f0deb  #83   feat(library): render the list view when grouping by author or series
    ac0533936  #83   fix(library): fetch author covers and remember the mode in the grouped list
    70562b68a  #45   fix(qbittorrent): send the four Advanced Settings to the client
    57e5f7f3b  #45   fix(qbittorrent): do not fail a submission the client already accepted
    ce1a5341c  #68   fix(filesystem): say why a source file could not be pinned
    ed151d2bc  #68   fix(filesystem): sanitize the pinning cause before it reaches the log
    97cd7059e  #68   test(filesystem): fail the refusal test when no cause is captured
    b919a0fd4  #68   fix(filesystem): lead the refusal with the cause, through the shared formatter
    821d86524  #72   fix(imports): retry a failed file import instead of blocking on the first attempt
    c6abeb36e  #72   fix(imports): retry a failed import only when every file in it failed
    7b202ddc4  #80   fix(quality): let a real encoder bitrate reach the rung it was encoded for
    facfaaad9  #80   test(quality): pin the bitrate tolerance from both sides, not just the floor
    e843e372e  #102  fix(docker): give the image a healthcheck it can actually run
    c9a05b923  #31   fix(notifications): dispatch webhooks for the triggers the settings screen offers
    70d458275  #49   Make the frontend follow the UrlBase by injecting a <base href> into the shell
    68e5edf84  #49   fix(spa): anchor the shell at the site root even with no UrlBase
    ad067cfa6  #109  stack: let a local build stamp its own version
    94dca3cbd  #109  stack: publish the patched build to GHCR from the fork
    716ea3ec9  #109  stack: publish a moving current tag beside the immutable commit tag
    847fb00d3  #107  fix(search): one indexer timeout no longer discards every other indexer's results
    d92ea6b08  #108  fix(files): physical-generation snapshots reject database-loaded observation timestamps
    0f9a2232c  #108  fix(persistence): restore the UTC contract for PhysicalIdentityObservedAtUtc at EF materialization
    631ecdaec  #73   feat(wanted): add a cutoff-unmet bucket to the Wanted page
    e4c81433d  #74   fix(collection): show an author's books under their membership series
    beae335a7  #75   feat(library): count the distinct series an author appears in
    87e745907  #76   feat(downloads): implement the three reprocess endpoints instead of returning success
    11e4b1926  #76   fix(downloads): refuse an ineligible reprocess instead of throwing at the caller
    69a1e2d7f  #76   fix(downloads): drop the unused using and the seam-adjacent whitespace
    26b66bf5d  #83   feat(activity): sort the queue, and show when an item was added
    19eb0d210  #86   fix(collection): apply the language preference to library books, not just suggestions
    96fa820ab  #94   fix(search): the minimum seeders gate never fired on a real torrent
    f0eef9932  #110  fix(import): match blacklisted extensions without regard to case
    d57a52a69  #111  fix(qbittorrent): map 4.x paused torrent states alongside 5.x stopped states
    9f0ad4a49  #112  fix(ui): match System recent-log severity classes to the stylesheet
    a0202dec6  #20   fix(search): derive one indexer query title, shared by both search paths
    2094225b9  #66   fix(downloads): give the queue a control that removes a terminal download
    ad299c9c2  #66   fix(downloads): tie a processing job's retention to the download it explains
    4e585cb8c  #99   fix(authors): stop attributing one author's ASIN to every co-author
    6e67d6fb8  #100  fix(naming): spell an author's initials one way in folder names
    504a46d0d  #100  test(naming): pin byline order on two co-authors, not on a translator
    c1866a46f  #113  fix(system): stop inventing log entries when no log file exists
    6ada3d4d7  #114  fix(library): stop announcing the list-view status badge as a button
    543878825  #115  fix(settings): stop a failed clipboard write from reporting a failed regeneration
    10438d905  #95   fix(api): the authentication gate ignored requests that varied the path casing
    0b3c8908d  #68   fix(filesystem): resolve a symlinked source path instead of refusing it
    65711fb54  #68   docs(filesystem): stop two comments claiming what the symlink commit retracted
    cb000e4ee  #98   fix(logging): give silent catch blocks a real log call or a stated reason
    e118b702e  #108  fix(persistence): keep a persisted UTC timestamp UTC when it is read back
    6771c3413  #78   test(scoring): pin the four hand-copied quality ladders to each other
    0d7979f3a  #69   test(metadata): count the attempts the Audible retry policy actually makes
    851716a1f  #69   test(metadata): stop the retry policy tests racing the caller's clock
    241bcc952  #136  NZBGet: warn once per failed history entry, and only for monitored entries in polls
    329c3ca47  #136  fix(nzbget): keep the warn-once keys case-insensitive after a scoped read
    4d444cfb1  #92   refactor(collection): lift shared series and text helpers out of the collection view
    c811a7a08  #92   feat(collection): show an author's series as a section on the author page
    966f593d5  #141  fix(monitoring): keep every series membership and its ASIN when a monitored author or series adds a book
    815c4d6ed  #141  fix(library): let a legacy-only metadata payload carry its series identifier
    469dddd60  #145  feat(activity): queue selection that survives the poll instead of being reset by it
    3a668d7bd  #145  feat(activity): sequential bulk runner with per-id outcomes and one summary line
    ee2313048  #145  feat(api): keep the queue-management API tests, drop the methods #917 and #927 already added
    1f18197c5  #145  feat(activity): a queue toolbar whose verbs say what they will do before doing it
    aba53efc8  #145  fix(activity): the select cell reasserts its props after a click that changed nothing
    1321f8295  #145  feat(downloads): a Retry button that appears only where the endpoint will accept it
    162e78a91  #145  feat(activity): select queue rows and act on them together
    969ddaa2f  #145  fix(activity): the filter is a viewport, so selection controls act on the rows in view
    1c9305386  #145  feat(downloads): the Retry button does the retry, instead of promising one later
    2d63c5612  #145  fix(activity): the retry button dresses itself and select all goes quiet with nothing to select
    7b5e1c8b0  #145  fix(activity): the toolbar drops a confirmation the selection outlived and rests while busy
    14a17a1d7  #145  fix(queue): overlay import status onto queue rows the client cannot represent
    522064bff  #145  fix(activity): disclose the true count before an unbounded sweep
    80092ab50  #144  metadata: name the two nothings a provider can answer with
    f61ad83db  #144  metadata: answer the ASIN endpoints with a status, not a stack trace
    9c618b170  #144  images: keep serving the placeholder when the provider does not answer
    c82f74aee  #144  search: move the Audible series endpoints into a partial
    bc8cd2c75  #144  search: say the provider did not answer, rather than returning nothing
    a0361bcd5  #144  metadata: walk every source, then report the fault rather than a miss
    66685b3f6  #144  metadata: let the Audible client raise when the provider did not answer
    baa75d039  #144  metadata: hold the Audnexus book lookup to the same division
    4fd0e3c38  #144  library: record when a book's provider metadata was last refreshed
    325b6f507  #144  settings: the refresh interval, staleness age and budget are operator-set
    4d0ab6a8d  #144  metadata: a request budget with an hourly refill and a spacing floor
    4f019f10b  #144  metadata: lift the per-book rescan out of its HTTP wrapper
    ffba8460d  #144  metadata: one gate, one run, one budget across every refresh entry point
    d2824a0ca  #144  metadata: walk the stalest books on an interval, and let an operator ask
    938d62fae  #144  fe: a Metadata Refresh section in the settings screen
    1b7a4abfe  #151  fix(logging): sanitize ASIN log arguments, rebased onto the metadata-refresh chain
    9657e4103  #153  Attach download history rows to the audiobook they belong to
    31c61dad4  #153  fix(history): record the protocol a download actually used
    792bf699e  #153  fix(history): resolve the protocol for the remaining event types too
    846bd4447  #153  test(history): cover the audiobook key and the protocol on one call
    7a42e5ba6  #154  test(downloads): match the int? audiobook id in the grab history verifies
    0e48c6a22  #155  fix(imports): classify failures at the boundary instead of leaking them to History
    a02817c61  #156  fix(naming): key the remaining naming tables case insensitively
    ca40761c8  #158  fix(qbittorrent): a 200 carrying "Fails." is a refusal, not a grab
    ace3729a9  #159  fix(search): stop reporting a series name as the series identifier
    3b60fc615  #161  Record a download client timeout as a failed grab, not a shutdown
    ad994c4ce  #163  fix(notifications): sanitize webhook URLs before logging them
    d56a69895  #163  test(notifications): cover SanitizeWebhookUrl and pin the webhook log call sites
    7f723d9f5  #166  feat(search): tell an indexer that answered with nothing apart from one that never answered
    49fb3508d  #166  fix(search): give the per-indexer timeout its own catch, correctly typed
    9d13f6068  #166  test(search): match the fake provider to the observation-returning interface
    95f2838a3  #166  search: isolate a per-indexer HTTP timeout from the multi-indexer fan-out
    6f990945b  #166  search: bound indexer fan-out concurrency to 4 (tracker #119)
    8ce4fec1e  #166  search: separate a rate limit and a refused credential from any other non-2xx
    bbd5cb327  #166  search: persist a per-indexer failure backoff ladder
    efca0e213  #166  search: skip an indexer that is in failure backoff, and record what each one answered
    43f1c21ca  #166  search: do not stamp LastSearchTime on a book whose indexer set was degraded
    1caebcd2c  #166  indexers: show on the card when an indexer is in failure backoff
    2e5634988  #166  feat(search): try a second form of a search before saying the book does not exist
    730b45a00  #166  search: cover what the query ladder and indexer backoff do to each other
    d4914239c  #166  search: drop the bare series-name query rung
    b4c2dd716  #166  test(search): thread the cancellation token through item 107's fake provider
    bf2a51d68  #172  172: fix Abridged substring match stamping unabridged books abridged
    824a53f60  #172  172: fix Abridged substring match on the frontend too
    a61dffa83  #173  173c: expose the Listenarr.Backend meter through a listener and an endpoint
    e09f40f90  #170  search: make the indexer fan-out ceiling an operator setting, shipped at 4
    36fc5bff5  #170  settings: expose the indexer search concurrency ceiling in Search Settings
    df5fc2c2c  #123  notifications(#126): make EnableNotifications actually gate delivery
    2bc55b915  #123  notifications(#127): dispatch webhooks by stored Type, not URL sniffing
    afc65e793  #123  fix(notifications): route webhook Type through the central dispatcher, not per-callsite loops
    bd8b26561  #126  notifications(#126): drop EnableNotifications, matching the family
    137f78521  #123  search(#123): wire up EnableAmazonSearch/EnableAudibleSearch, dead since introduction
    cb9063bb1  #123  notifications(#128): remove the unreachable legacy /notifications/test endpoint
    2a7dbc56b  #123  settings(#131): add a UI control for ExtractArchives, backend-only until now
    efa06ed57  #123  downloads(#132): fix RemoveCompletedDownloads for Transmission, SABnzbd, NZBGet
    1ffea2be7  #124  fix: wire AllowedFileExtensions into FileUtils.IsAudioFile
    972f07cf8  #124  cleanup: remove two orphaned private AudioExtensions arrays
    c22226132  #129  Remove vestigial Indexer.Tags field
    cc67fec6f  #133  history: add retention setting UI and a daily cleanup worker
    5fb1bd0bf  #138  Preserve series ASIN when mapping monitored author/series books
    af691a49a  #138  test(monitoring): resolve the series-monitoring root through identity, not a raw builder
    6a6e5ed52  #199  Fix author name normalization to merge spaced-out initials
    a340972f1  #199  test(domain): bring StringUtilsNormalizeAuthorNameTests up to convention
    2145cae03  #199  fix(authors): dedupe author-cache methods reintroduced against fix/72's own copy
    c61c8681c  #151  fix(authors): skip the SQL narrowing pre-filter for non-ASCII author names
    538b57c85  #151  fix: complete the NormalizeAuthorName redirect this branch left half-done
    88d8b9305  #201  Finish the author-normalizer consolidation and re-derive the keys it left behind
    d5b8af932  #201  Keep the canonical author spelling the ASIN lookup already returned
    5dffcd6ba  #201  Converge existing books onto one spelling per author at startup
    7ab076c95  #201  fix(library): normalize author grouping and include every co-author
    9e6e66feb  #200  Pin the author-identity behaviour these fixes are meant to change
    61c192a4d  #200  Stop reading author identity out of a list that never recorded it
    4d4f517a0  #200  Refuse to rename a cached author when an ASIN is already somebody else's
    e9b6f3b39  #200  Confirm an author ASIN with Audible before binding it as an identity
    22f7350ea  #200  Pin what stops a renamed cache row reaching stored author names
    91b6de481  #200  fix(authors): dedupe author-cache methods a third time, keeping the collision-safe version
    c74d3994b  #167  Fix: manual import path traversal check trips on legitimate paths (#167)
    22b49fb06  #167  test(manual-import): bring the path-traversal test class up to convention
    9960feefa  #122  search: add ReleaseShapeDetector, which tells a bundle from a single edition
    0b86a843b  #122  search: a per-profile preference between bundle and single-book releases
    1df74fa16  #122  fe: the release shape control, and a Bundle badge on search results
    95c8f30cc  #172  fix(search): word-boundary word filters, and any-of required words
    a192c30c8  #172  fix(search): route PreferredWords through the same word-boundary matcher
    0d9357067  #172  fix(search): make indexer priority a genuine tie-break instead of dead weight or dominant term
    16b4067e7  #172  search: give the word-boundary term matcher its own file
    a433d2a37  #172  search: pin the precedence between the three scorer concerns
    32c0440d4  #173  fix(system): stop fabricating download-client connectivity
    77f3be0bf  #173  tests(system-service-logs): pass the download-client status cache the constructor now requires
    f2aac338a  #169  fix(wanted): Search All acted on the unfiltered list, not the one on screen
    ef38e1081  #169  fix(nav): dismiss the header search panel when the route changes
    ea13c598a  #169  fix(library): Select All reached past the filter into the whole library
    15ada2a79  #165  feat(detail): add Automatic Search action to audiobook detail toolbar
    c55f77c32  #165  feat(library): per-item automatic search icon on the audiobooks list
    ce3039f1a  #165  feat(library): bulk automatic search on the audiobooks index toolbar
    ff502a4d5  #165  feat(calendar): search for the missing books in the window on screen
    14ebdbbd0  #202  feat(ui): multi-row selection on Wanted and Downloads
    68622d207  #289  tests: catch up DownloadsView.selection and WantedView.selection with items 73, 169 and 145
    fd7b536ab  #174  fix(history): style the events that happen, not four that cannot
    390a599ec  #174  fix(history): keep what a grab knew, and name the download client
    d3bdf7227  #174  feat(history): a global History page over the API that already existed
    8c3d1991c  #174  tests(download-service): match RecordDownloadFailedAsync's protocol parameter in the sanitize test
    717e377b6  #205  Keep stored secrets when a save carries the redaction sentinel
    ac9535fe0  #209  fix(security): an IPv4 address in its mapped form never matched the private ranges
    fdb126d5d  #190  fix(myanonamouse): spend the freeleech wedge on the download, not the search
    50b86d1d3  #190  refactor(search): give SearchResultConverters its own file
    7e57a4ca7  #190  feat(indexers): parse the release flags trackers advertise
    cc305048a  #183  Add a task surface over the periodic workers, with a manual trigger
    f0945727b  #183  scheduled tasks: an allowlist for manual runs, not a deny-list
    7001ffe39  #183  scheduled tasks: answer the trigger with the row it started, and keep stopped workers
    c170acbd6  #183  scheduled tasks: say which request started the cycle, and stop 404ing a listed task
    d9a5befa4  #183  scheduled tasks: release the gate under the lock that ends the cycle
    0a8cacb95  #183  scheduled tasks: publish the interval in whole seconds, and refuse what will not fit
    45f9c9433  #183  scheduled tasks: put the interval refusal on the row, and correct two claims made for it
    ae556d325  #183  scheduled tasks: make the interval refusal audible, and stop overclaiming what it buys
    517e0adea  #183  tasks ui: put the task surface under System, where the family keeps it
    6d61da120  #183  tasks ui: test the run control against the refusals the API actually answers
    4df54ec99  #183  tasks ui: fix two in-flight defects, make the refusal reachable, and cover the poll
    b604412f8  #183  housekeeping: the frame for a daily retention sweep, with no table in it yet
    44309891e  #183  housekeeping: three tables, and the two that were left out on purpose
    26ee81728  #183  housekeeping: move jobs and their three child tables, with the cascade measured
    f12e1b7f7  #183  housekeeping: the supersede that never stamped its terminal timestamp, and four comments that overclaimed
    23db6cc27  #285  settings: a Housekeeping section for the two switches item 183 shipped API-only
    5a21fea44  #281  housekeeping: the two deferred populations, and the one slice of each that is provably unreachable
    44b0c34e0  #281  housekeeping: say what the compatibility sweep's query actually costs
    2b4ef3354  #185  notifications: a subscriber model and a Custom Script provider
    b3ff07597  #185  configuration: keep stored custom scripts when a save omits them
    3fdddd5ec  #185  notifications: let the EnableNotifications switch gate the subscriber fan-out
    5c99b9587  #189  feat(calendar): add the calendar endpoint and iCalendar feed
    dde8ab896  #189  feat(calendar): give the calendar page a feed subscribe URL
    73a8fa1a7  #189  fix(calendar): correct the status precedence and test the two seams
    c9d985945  #189  fix(calendar): accept Readarr's tag spelling, and test the rest of the review
    ea5712e98  #189  fix(calendar): close the rest of the parse/query seam, and stop citing what cannot be opened
    b9c64be95  #189  test(calendar): the case-variant feed is refused by the enforcer now, not the attribute
    05b446092  #189  feat(calendar): resolve per-event status in Readarr/Sonarr precedence order
    0ca58289b  #189  feat(calendar): add a first-day-of-week preference, defaulted from locale
    b82e2a4f3  #189  feat(calendar): add a legend component for the event status colours
    c17445a0e  #189  feat(calendar): colour events by status, add a legend, and a week-start option
    0439eb6a3  #189  test(calendar): reconcile search-missing spec with item 189's status classes
    9f9726528  #79   fix(status): stop reporting a non-preferred format as below cutoff
    b411669a3  #79   test(status): make the agreement test read the real projection, and pin the widened cases
    03baddded  #252  WIP fix(quality): a blank cutoff means satisfied, not "search forever"
    406748301  #252  test: bring the new cutoff tests up to the repository's test conventions
    b2ebee5d6  #252  test: flag the blank-cutoff consequence for the not-yet-landed upgrades flag
    b9b42882c  #257  fix(quality): resolve the cutoff through QualityMatcher, changing two answers
    665677bb0  #254  torznab: a bitrate has to be its own token, not digits inside one
    55c2d7d20  #254  torznab: tests for the x264 mislabel, each with its control
    4a2eae414  #254  torznab: a number is a bitrate only where the text says it is
    ada35f19c  #254  torznab: recover the bracketed bitrate the tightening had lost
    e5c1040c6  #255  fix(search): let a quality profile's Allowed flag actually gate
    553f95240  #255  fix(search): correct the claims around the quality gate, and pin what it does not decide
    993015c7b  #255  fix(search): refuse a quality label the gate cannot place
    9ccf58b2f  #255  fix(search): answer the review of the quality gate fix
    e1c2b5bd8  #253  Break release selection ties deterministically instead of by indexer order
    cc5a31a96  #253  Make the consuming-site tests actually discriminate their own site
    4ee807a87  #253  Restore Readarr's protocol step, without which the chain is not transitive
    ecba65f73  #253  Close the second intransitivity, and three more review findings
    ae4e63e6e  #253  Rank once per site instead of twice, to stay inside the focus budget
    2e57b8cb8  #253  Put the same-group invariant in the code, and close the last apparatus gap
    44a4e35a6  #256  quality profile: refuse a cutoff the profile does not allow
    d493b8957  #256  tests: cover the cutoff rule at the API and at the attribute
    bfb2614be  #256  quality profile: make the cutoff rule agree with the code that reads it
    4b2761527  #256  quality profile: give it the UpgradeAllowed flag the family has
    ecf8b6bf0  #256  quality profile: carry upgrades-off across the upgrade, in a startup repair
    a596c6822  #256  tests: pin both directions of the upgrade flag, each against its control
    536b1129e  #256  status evaluator: drop the upgrades-off guard, it was never the reason
    a3f34de6f  #256  tests: pin the new migration id, the backfill gate depends on it
    e3f2ae0a2  #256  quality profile: decide "blank cutoff" the way the readers decide it
    050ad5d36  #256  comments: correct three claims a reviewer could check and find false
    0cc8d302e  #261  search: skip the bare-title rung when the title cannot carry a query alone
    4e24ada5e  #261  search: stop the stem and series rungs unanchoring themselves when there is no author
    108f8b307  #262  Require indexer categories, the way the rest of the *arr family does
    aad580163  #262  tests: pin that the draft test is gated too, since it binds the same entity
    a94c4469d  #262  Make the indexer form usable now that categories are required
    ebc5cefc6  #262  Move the category default off the entity, where it rewrote stored values
    6e8990aae  #262  tests: pin that an explicit null category list is not a way past the rule
    d5d573cc5  #262  Validate the merged indexer on update, not just the request body
    abfdec51a  #190  fix(myanonamouse): send the torrent filter as tor[searchType]
    c10bb19e9  #190  fix(myanonamouse): send the page size as perpage, not tor[perpage]
    bedb48d11  #190  fix(myanonamouse): send the language the caller asked for
    ccc189438  #190  fix(myanonamouse): stop asking the search for a freeleech wedge
    5981893d4  #268  import: give each companion file the source root it actually came from
    3e6cda5c1  #268  import: resolve the companion source roots once per batch, not per file
    0af72ffe1  #269  manual-import: place companions by the selected files' structure, not the caller's path
    aff8609ef  #269  manual-import: a companion goes where the file it accompanies went, and nowhere else
    d6ef73098  #269  import: match the companion's neighbour by path identity, and name the rule the manual path uses
    61820f757  #269  import: say why the neighbour comparison is safe for the reason that is actually true
    ca92a678d  #21   tests: give the companion placement pass the root folder this branch now requires
    b9160d65d  #258  fix(recovery): one unrecoverable deletion intent must not disable the whole filesystem gate
    666fcc836  #273  cache: make the author and series cache upserts survive a lost race
    7154bf5be  #273  cache: do not retry a collision that re-reading cannot resolve
    284dd322f  #273  cache: the author upsert cannot reach that collision on this build, so pin what it does instead
    f098b8cee  #273  cache: say only once that a rebind was refused, and describe the collision that can actually happen
    22d729e81  #273  cache: a refusal that first happens on a retry must still be reported
    0a44b777c  #273  cache: the comment on the test still described the guard that was removed
    8cf36a214  #275  fix(metadata): spell a contributor's name without their job title
    294e82780  #275  fix(metadata): store the author first, and keep an edition out of the byline
    da7505bdd  #276  fix(metadata): bind an author's ASIN only to a row that names them
    95e149d40  #276  metadata: lease the shared request budget to callers that are not a refresh run
    52984a94a  #276  repair: a scheduled pass that corrects author identities already written
    8194fa4b2  #276  repair: remove contributor roles from the credits already stored
    ac3e38461  #276  test: give the two new test classes the conventions the suite enforces
    61f83232c  #276  repair: a provider that did not answer is not evidence that an author has no ASIN
    7deaadca1  #276  repair: answer the fresh review, including a starvation defect nothing was checking
    b43c088f6  #270  filesystem: let a raced pinned hardlink fall back to a copy, and say so
    83e881867  #270  filesystem: narrow the hardlink race fallback, because the first attempt claimed too much
    b4d36c703  #277  Fix the fail-open identifier check in DownloadRemovalWorkflow
    20b3084c6  #277  Route the downloads delete endpoints through the client removal workflow
    dceb4512e  #277  Add tests for the client-removing delete and the identifier check
    cf67650f8  #277  Make a disabled client deletable again, and fail closed when no client id is known
    1fa0bd485  #277  Correct the outer-catch mechanism described in a test comment
    c1c2de5c7  #277  Cover the disabled client, the unmapped record, and deleteFiles on the delete path
    a3e3fa475  #277  Let the delete control say whether the download client is involved
    667adc80d  #277  Make the record-only way out actually work, and stop it claiming too much
    de60e6915  #276  fe: an Author Identity Repair section in the settings screen
    345d14d33  #276  fe: the preview card promised a run button the frontend does not have
    441c027b0  #276  fe: the review's findings, one of which the UI could not have shown
    6f8125d9a  #278  notifications: give the Custom Script provider a settings surface
    6fbbbfa0a  #286  notifications: an Email provider, the case a webhook type cannot express
    63c1454e6  #286  notifications: give the Email provider a settings surface
    fb10b8773  #286  notifications: assert both providers are actually wired into the container
    e18a89367  #286  notifications: act on review of the Email provider
    9ad5d1dff  #286  notifications: cover the sentinel guard on a database with no settings row
    31fec9887  #286  configuration: move the email members into their own partial
    3ea44d753  #286  notifications: exercise the SMTP transport against a socket, not a mock
    876430186  #286  notifications: act on the second review of the Email provider
    ae8f8ad67  #286  configuration: split startup config out of ConfigurationService, so the file is under the cap again
    1c5cfe615  #176  settings: a recycle bin path and retention, off by default
    0ceb81468  #176  fs: recycle a library file instead of unlinking it
    e23f3a9fc  #176  library: send tracked file deletes to the bin when one is configured
    8ca63e0b3  #176  recyclebin: validation, an empty control, and a retention sweep
    e6a2e31b7  #176  tests: the recycle primitive, each case with a control that differs
    e974aea98  #176  tests: pin down that an import never displaces an existing tracked file
    b36f295f1  #176  workers: a daily recycle bin retention sweep
    148bc0392  #176  tests: recycle bin validation and the retention sweep
    44cbdeb38  #176  fe: a Recycle Bin settings section, and two holes in its spec
    3c3100f10  #176  tests: AGPL headers on the two new recycle bin test files
    360cb60e4  #176  library: an explicit flag instead of a sentinel string for an unreadable config
    9004d0c7e  #176  recyclebin: the sweep followed directory links out of the bin
    67945589f  #176  fs: stamp the recycle time before the rename, not after
    2aed8a6c9  #176  recyclebin: the bin-inside-a-root check was ordinal, so case folding slipped past it
    4a6fb96bd  #176  recyclebin: a bin path of / validated clean and the sweep would have walked the disk
    b8f960003  #176  tests: the delete wiring had no test at all
    612abd023  #176  recyclebin: refuse a bin path that goes through a symbolic link, at save time
    8cd4a01ac  #176  fs: map the native rename errors off Linux, and cover the stamp ordering
    8faf7787a  #176  recyclebin: stamping is best effort, and the delete path validates the bin too
    95a13144a  #176  tests: the recursive folder delete ignores the recycle bin
    a2c7c3974  #176  tests: the folder recycle's overlap and cross-filesystem guards, at the primitive
    f46238c79  #176  library: send the recursive folder delete to the recycle bin too
    a1acab072  #177  feat(download-clients): one selector, with a priority the operator sets
    c58e3c327  #177  test(download-clients): pin the selection policy and the migration default
    6cb973a77  #177  settings(download-clients): a Client Priority control, so the field is reachable
    a7e8b754f  #177  test(download-clients): pin the rotation cursor's lifetime, with a control that moves
    5e26e49cc  #177  fix(download-clients): four things a fresh review found
    67c3e9b47  #177  settings(download-clients): give Client Priority its own section, keep queue priority usenet-only
    58aa128ea  #177  test(indexers): pin per-indexer download client binding, failing first
    7bf746109  #177  indexers: give an indexer a nullable download client binding (shape only)
    5862e682e  #177  indexers: route a bound indexer's grabs to its client, and fail loudly
    f69c93335  #177  test(download-clients): pin persisted client failure status, failing first
    e700e0393  #177  download-clients: persisted client failure status, shape only
    0bca6e738  #177  download-clients: record client failures, back off, and route around a blocked client
    770a10a85  #177  download-clients: record status in a gateway subclass, not a wrapper
    c13be059b  #177  stack: make item 177's two migration Designers the cumulative model at their position
    2d2bcf263  #179  backup: take a copy of the database before migrations run
    86ea54643  #179  backup: the settings column, and two defects the suite caught
    79c715954  #179  backup: tests, each paired with a case that comes out differently
    7e7066c26  #179  backup: the operator surface, so this is reachable without a shell
    550291bea  #179  docs: describe the backups directory and the startup override
    579d694bf  #179  backup: take the copy before the startup path's own repair writes
    ac5558774  #179  backup: cover what the review found had no test
    882e370cc  #179  wip: retarget the auth test
    10ddf045a  #179  backup: close the gaps the review found in the code and in its tests
    e2c6164ec  #179  backup: fix what the second review found, and rethink the manual limit
    25cdd58d3  #179  backup: pin the negative retention case
    eb0d41569  #180  feat(indexers): per-indexer seed ratio and seed time
    211a622ec  #180  fix(indexers): close review findings on seed criteria
    1e943a0d6  #181  search: the release score has no ceiling, so a preference can still decide
    1260f450e  #181  tests: SearchResultScorerCeilingTests follows the repository test conventions
    fbc2c791a  #181  settings: the minimum score threshold is no longer capped at 100
    b6b842253  #181  search: correct what this branch claims, after review
    c15691508  #264  search: the profile's size and quality gates apply to Usenet results too
    8e874ff79  #264  search: size bounds above 2047 MB, and three corrections to the commit before this
    162f689c2  #264  search: reconcile the NZB gate hoist with the tests that pinned the exemption
    2e9e96a65  #263  search: release selection reads the quality profile, not the hardcoded ladder
    8f0190d87  #263  search: take the best candidate that is an upgrade, and fix two things in the commit before this
    877d0a802  #181  search: split the rejection gates out of the scorer, so the file is under the cap again
    8e62686e1  #181  search: two warnings, one of them a control this stack no longer has
    b4ef69b6a  #182  naming(#182): render the settings naming preview through the real backend renderer
    56e40601a  #182  naming(#182): wire the settings naming preview to the backend renderer
    45f600971  #182  naming(#182): fix real divergences a fresh review found from GenerateFilePathAsync
    7fe9ea2ac  #182  naming(#182): flip the lowercase-token preview test now that the case fix is in the tree
    3055e0234  #184  tests(notifications): pin the item-184 gaps blocked on PR #943 (WIP)
    2208d0e78  #184  tests(notifications): tighten item-184 gap pins after fresh review
    26f1328d9  #187  feat(#187): free-space check before import (G7)
    c92c14258  #187  feat(#187): free-space on root folders (G9), tests, and settings UI (G7/G9)
    f9660b1c9  #187  fix(#187): the free-space guard silently never fired under the default naming pattern
    f6271ff13  #187  test(#187): prove required bytes are the real file size, not a stand-in like #821's 64
    ba2915663  #284  Add failing test for metadata repair guard missing Entries include
    2c61ff5ed  #284  Fix metadata repair guard to load MoveJob Entries and CreatedDirectories
    c64f993bd  #283  tests: add failing control for ProcessExecutionLogs table drop
    8a73f6ecc  #283  Drop the dead ProcessExecutionLogs store, repository, entity, and table
    a11bed7a7  #237  fix(search): reconcile the query ladder with fakes and tests from three unrelated items
    a62cbe345  #237  test(startup): mock the metadata-refresh backfill this test's strict repository was missing
    4da425d5c  #237  style: run prettier over item 201's two touched files
    e006aa45f  #237  fix(search): reconcile the scorer 3-way with fakes and tests from unrelated items, plus two more real bugs found by the suite
    8d5ebb796  #237  style: run prettier over the two files item 165's resolution touched
    629f879d4  #237  fix(wanted): compose the restored multi-select with items 73, 169 and 145
    257e97b48  #237  stack: reconcile the manual-run allowlist with the stacked tree
    22fa17f64  #237  tests: read the observation's results, the way its sibling case already does
    dfb6e9bc6  #237  tests: point the tiebreak harness at the signatures the stack actually has
    b5e43f841  #237  tests: move the blank-cutoff pin to the answer fix/blank-cutoff-search-loop gave it
    d74037287  #237  downloads: split the client-name projection out, so the controller is under the ceiling again
    5fca832a3  #237  quality: split QualityMatcher's internals out, so the file is under the ceiling again
    e4088ec20  #237  tests: guard that every migration Designer is its predecessor plus its own Up
    b9ad07b8b  #237  stack: make every composed migration Designer the cumulative model at its position
    76104730d  #212  tests: pin the no-audio-candidate import block from #890
    8977976a9  #212  fix(import): admit audio-only .mp4 via a probed ambiguous-container tier
    a607c6f83  #212  fix(import): let manual import admit an audio-only .mp4
    d72dfb9fb  #212  tests: move the #890 cases out of a file PR 993 is rewriting
    e93c3d632  #212  refactor(files): move the content gate into its own partial, so it fits beside #901
    285f76d76  #91   Wanted: skip books with an active download in the bulk search
    1b9bc3a43  #191  qbittorrent: failing spec for postImportCategory form field
    c3d837fc1  #191  qbittorrent: add Post-Import Category field to the client form
    95679aa52  #288  symlink-chain: fail-first tests for a two-or-more-hop chain
    bdedcb2c8  #288  symlink-chain: walk the whole chain, not just the last segment
    bcf570ac7  #237  stack: make the three Designers after item 170's migration the cumulative model again
    de71fd991  #293  fix: grid containers shrink to content instead of forcing viewport height

## Items, in application order

    105  fix/series-position-not-lost                 2 patches
    106  fix/767-series-asin                          3 patches
    43   fix/777-macos-ffprobe-url                    1 patch
    23   fix/bug24-queue-guard                        3 patches
    21   local/21-boundary-only                       6 patches
    24   fix/bug11-taglib-writestream                 3 patches
    22   fix/bug4-n-of-m-chapter-stem                 3 patches
    17   fix/bug5-library-import-series-memberships   3 patches
    11   fix/818-descriptor-path-leaks                3 patches
    30   fix/bug26-journal-repair-visibility          3 patches
    29   fix/bug25-shared-circuit-breaker             2 patches
    42   fix/bug13-manual-import-naming-variables     3 patches
    37   fix/gateway-path-mapping-concurrency         6 patches
    32   fix/bug1-prowlarr-urlbase                    2 patches
    34   fix/bug28-urlbase-subpath                    4 patches
    35   fix/bug19-duplicate-release-guard            3 patches
    36   fix/795-series-order-culture                 3 patches
    36   fix/796-culture-parse                        2 patches
    35   fix/qbittorrent-409-rejected-release         3 patches
    160  fix/startupconfig-merge                      2 patches
    64   local/980-with-urlbase-validation            5 patches
    33   fix/audible-timeout-not-zero-match           4 patches
    25   prreview/914-on-843                          6 patches
    60   fix/surface-file-mutation-cause              3 patches
    61   fix/retry-import-requeues                    2 patches
    51   feat/release-blocklist                       13 patches
    62   fix/894-download-finalization-settings       5 patches
    65   fix/895-maximum-age-visibility               1 patch
    46   consolidate/921-863-985                      7 patches
    47   fix/900-download-client-priority             5 patches
    44   fix/897-image-serving-assertions             1 patch
    83   feat/595-grouped-list-view                   2 patches
    45   fix/898-qbittorrent-advanced-settings        2 patches
    68   fix/source-capability-cause                  4 patches
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
    76   feat/implement-reprocess                     3 patches
    83   feat/activity-queue-sorting                  1 patch
    86   fix/author-page-language-filter              1 patch
    94   fix/minimum-seeders-case                     1 patch
    110  fix/import-blacklist-extension-casing        1 patch
    111  fix/qbittorrent-paused-states                1 patch
    112  fix/system-log-level-casing                  1 patch
    20   fix/848-search-query-title                   1 patch
    66   fix/927-removal-control                      1 patch
    66   927/option-1-couple-job-lifetime             1 patch
    99   fix/72-author-asin-collision                 1 patch
    100  fix/72-author-folder-canonical               2 patches
    113  fix/system-logs-no-fabrication               1 patch
    114  fix/collection-status-badge-role             1 patch
    115  fix/api-key-clipboard-failure                1 patch
    95   fix/auth-enforcer-path-casing                1 patch
    68   local/rb-resolve-symlinked-source-path-resolved 2 patches
    98   fix/928-silent-catch-blocks                  1 patch
    108  fix/persisted-utc-kind                       1 patch
    78   check/bug45-quality-ladder-parity            1 patch
    69   fix/bug6-audible-timeout-retry               2 patches
    136  fix/136-nzbget-history-warn-once             2 patches
    92   feat/author-series-section                   2 patches
    141  fix/monitoring-series-memberships            2 patches
    145  feat/queue-management-1                      13 patches
    144  fix/provider-silence-is-not-absence          8 patches
    144  feat/metadata-refresh-foundation             4 patches
    144  feat/metadata-refresh-scheduled              3 patches
    151  local/rb-971-on-144-base                     0 patches
    151  local/rb-971-on-144                          1 patch
    153  local/973-on-974-v2                          4 patches
    154  local/35-tests-on-154-signature              1 patch
    155  local/rb-975-resolved-base                   0 patches
    155  local/rb-975-resolved                        1 patch
    156  fix/naming-table-casing                      1 patch
    158  fold/158-qbittorrent-fails-body              1 patch
    159  fix/search-fallback-series-asin              1 patch
    161  local/rb-161-on-153-base                     0 patches
    161  local/rb-161-on-153                          1 patch
    163  fix/webhook-url-sanitize                     2 patches
    166  local/166b-failure-reason-v2                 3 patches
    166  local/rb-166-170-on-166b-base                0 patches
    166  local/rb-166-170-on-166b                     11 patches
    172  local/rb-abridged-flag-word-boundary-0924-base 0 patches
    172  local/rb-abridged-flag-word-boundary-0924    2 patches
    173  feat/expose-app-metrics                      1 patch
    170  feat/search-request-budget                   2 patches
    123  local/123-on-872-v2                          3 patches
    126  local/126-drop-enable-notifications          1 patch
    123  local/rb-dead-settings-batch-0924-base       0 patches
    123  local/rb-dead-settings-batch-0924-no7df      4 patches
    124  local/124-allowed-file-extensions            2 patches
    129  local/129-remove-indexer-tags                1 patch
    133  local/133-on-980                             1 patch
    138  local/138-on-141                             2 patches
    199  local/199-base-shim                          0 patches
    199  local/199-on-shim                            2 patches
    199  local/199-dedupe-on-shim                     1 patch
    151  local/rb-151-refresh-narrowing-fix           2 patches
    201  local/201-on-shim                            3 patches
    201  fix/author-grouping-normalize-v4             1 patch
    200  local/200-on-shim                            6 patches
    167  fix/167-manual-import-path-traversal         2 patches
    122  local/rb-122-bundle-individual-preference-0924-base 0 patches
    122  local/rb-122-bundle-individual-preference-0924 3 patches
    172  local/rb-scorer-3way-reconciled-0924-base    0 patches
    172  local/rb-scorer-3way-reconciled-0924         5 patches
    173  local/173-on-113                             2 patches
    169  local/169-on-73                              3 patches
    165  local/165-detail-on-169                      1 patch
    165  local/165-toolbar-on-detail                  2 patches
    165  local/165-calendar-on-detail                 1 patch
    202  local/rb-wanted-downloads-multi-select-base  0 patches
    202  local/rb-wanted-downloads-multi-select       1 patch
    289  local/rb-selection-spec-reconcile-v2         1 patch
    174  local/rb-174-on-154-0924-base                0 patches
    174  local/rb-174-on-154-0924                     3 patches
    174  local/174-signature-fix-on-154               1 patch
    205  fix/redacted-sentinel-round-trip             1 patch
    209  fix/private-address-ipv4-mapped              1 patch
    190  fix/indexer-flags                            3 patches
    183  feat/183-task-scheduler                      8 patches
    183  feat/183-tasks-ui                            3 patches
    183  local/rb-183-housekeeping-retention-0924-base 0 patches
    183  local/rb-183-housekeeping-retention-0924     4 patches
    285  local/rb-housekeeping-settings-ui-0924-base  0 patches
    285  local/rb-housekeeping-settings-ui-0924       1 patch
    281  local/rb-housekeeping-deferred-populations-0924 2 patches
    185  local/rb-185-on-123-0924-base                0 patches
    185  local/rb-185-on-123-0924                     3 patches
    189  local/rb-189-with-ui-base                    0 patches
    189  local/rb-189-with-ui                         6 patches
    189  feat/calendar-status-and-week-start          4 patches
    189  local/165-189-status-test-reconcile-v2       1 patch
    79   fix/format-preference-is-not-a-quality-mismatch 2 patches
    252  fix/blank-cutoff-search-loop                 3 patches
    257  local/257-on-252-v2                          1 patch
    254  fix/torznab-quality-substring-match          4 patches
    255  local/rb-quality-gate-respects-allowed-base  0 patches
    255  local/rb-quality-gate-respects-allowed       4 patches
    253  local/rb-deterministic-release-tiebreak-base 0 patches
    253  local/rb-deterministic-release-tiebreak      6 patches
    256  fix/validate-quality-profile-cutoff          3 patches
    256  local/rb-quality-profile-upgrade-allowed-0924-base 0 patches
    256  local/rb-quality-profile-upgrade-allowed-0924 7 patches
    261  local/rb-gate-bare-title-on-166              2 patches
    262  fix/require-indexer-categories               6 patches
    190  fix/mam-ignored-search-parameters            4 patches
    268  fix/companion-relative-path-escape           2 patches
    269  fix/manual-companion-relative-path           4 patches
    21   local/21-companion-tests-on-269              1 patch
    258  fix/deletion-intent-does-not-brick-startup   1 patch
    273  local/273-on-shim                            6 patches
    275  local/rb-strip-role-suffix-from-credits-base 0 patches
    275  local/rb-strip-role-suffix-from-credits      2 patches
    276  local/rb-276-on-shim-0924-base               0 patches
    276  local/rb-276-on-shim-0924                    7 patches
    270  fix/hardlink-falls-back-to-copy              2 patches
    277  local/rb-delete-removes-from-client-0924-base 0 patches
    277  local/rb-delete-removes-from-client-0924     8 patches
    276  local/rb-276-ui-on-shim-0924-base            0 patches
    276  local/rb-276-ui-on-shim-0924                 3 patches
    278  local/rb-custom-script-settings-ui-0924-base 0 patches
    278  local/rb-custom-script-settings-ui-0924      1 patch
    286  local/rb-email-notification-0924-base        0 patches
    286  local/rb-email-notification-0924             8 patches
    286  local/rb-configservice-split-on-email-notification-0924 1 patch
    176  local/rb-recycle-bin-0924-base               0 patches
    176  local/rb-recycle-bin-0924                    19 patches
    176  local/rb-recycle-bin-folders-0924            3 patches
    177  local/rb-download-client-priority-0924-base  0 patches
    177  local/rb-download-client-priority-0924       6 patches
    177  local/rb-indexer-download-client-binding-0924-base 0 patches
    177  local/rb-indexer-download-client-binding-0924 3 patches
    177  local/rb-download-client-status-0924-base    0 patches
    177  local/rb-download-client-status-0924         4 patches
    177  local/rb-download-client-status-designers-0924 1 patch
    179  local/rb-backup-service-0924-base            0 patches
    179  local/rb-backup-service-0924                 11 patches
    180  local/rb-180-per-indexer-seed-criteria-0924-base 0 patches
    180  local/rb-180-per-indexer-seed-criteria-0924  2 patches
    181  fix/release-score-ceiling                    4 patches
    264  local/rb-264-on-shim-base                    0 patches
    264  local/rb-264-on-shim                         3 patches
    263  local/rb-263-on-shim-0924-base               0 patches
    263  local/rb-263-on-shim-0924                    2 patches
    181  local/rb-scorer-split-on-263-0924            2 patches
    182  local/rb-182-naming-preview-server-rendered-0924-base 0 patches
    182  local/rb-182-naming-preview-server-rendered-0924 4 patches
    184  check/184-notification-event-gaps            2 patches
    187  local/rb-import-free-space-check-0924-base   0 patches
    187  local/rb-import-free-space-check-0924        4 patches
    284  fix/metadata-repair-guard-includes           2 patches
    283  local/rb-drop-dead-process-execution-store-0924-base 0 patches
    283  local/rb-drop-dead-process-execution-store-0924 2 patches
    237  local/rb-stack-reconcile-0924-base           0 patches
    237  local/rb-stack-reconcile-0924                12 patches
    237  local/rb-migration-designers-base            0 patches
    237  local/rb-migration-designers                 2 patches
    212  local/rb-mp4-audio-import-0924-base          0 patches
    212  local/rb-mp4-audio-import-0924               5 patches
    91   local/rb-936-wanted-search-skips-active-downloads-0924-base 0 patches
    91   local/rb-936-wanted-search-skips-active-downloads-0924 1 patch
    191  local/rb-qbittorrent-post-import-category-control-0924-base 0 patches
    191  local/rb-qbittorrent-post-import-category-control-0924 2 patches
    288  fix/symlink-chain-walks-every-hop            2 patches
    237  local/rb-designer-chain-0924-base            0 patches
    237  local/rb-designer-chain-0924                 1 patch
    293  fix/grid-container-height-shrinks-to-content 1 patch

Regenerate with tools/local_stack.sh in the tracker repo.
