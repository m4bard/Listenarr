# Stack manifest

This build is NOT a stock release. It is upstream canary plus unmerged patches.

    base:        dc14be034ad80e6a04e4318470f327e246c26971
    base short:  dc14be034
    patches:     766
    version:     1.10.6+m4bard.766

## Patches, oldest first

    da9ca67d3  #105  fix(naming): don't lose a series position that isn't a plain number
    bcb852b9b  #105  fix(naming): derive both series-position fields from one source
    94aeb9723  #106  fix(metadata): populate SeriesAsin from the Audnexus series record
    cfb86cb54  #106  fix(metadata): carry the Audnexus series list, with identifiers, through the product lookup
    eed12c868  #106  test(metadata): pin what a blank Audnexus series ASIN stores
    d48745be7  #43   fix(ffmpeg): point macOS at evermeet's ffprobe archive, not its ffmpeg one [superseded by upstream #786, not carried]
    00ed29c1d  #23   fix(qbittorrent): one unreadable torrent should not truncate the queue poll
    5bfabf8d1  #23   fix(qbittorrent): let a client that stops answering fail the poll
    23f19d2f4  #23   fix(qbittorrent): quieten the per-torrent skip and escape the hash
    240d73a55  #24   fix(import): actually embed the ASIN after an import
    f496cd70f  #24   tests: save and reopen a real m4b for the ASIN writer
    9b99602b5  #24   chore: exempt only the generated m4b fixture from the media ignore rules
    b42b0d337  #24   fix(import): do not write the ASIN tag through a hardlink to the source
    bb5de2508  #24   tests: pin that the hardlink case really shares the source's inode
    d453b60d6  #17   Library import: keep every series membership, not just the first
    5d82de09e  #17   fix(library-import): build series memberships from the enriched product and fix the unmatched-files mapping
    892e0f6fa  #17   fix(library-import): require the B0 prefix before keeping a series ASIN
    74670fce4  #11   fix(scan): stop the Linux descriptor path leaking into metadata and size
    c3e626f32  #11   tests: follow the scan's metadata read onto the two-part file source
    cf80488a9  #11   fix(scan): narrow to the metadata boundary, leave the size to #901
    dc5943042  #29   fix(download-clients): one circuit breaker per client, not one for all of them
    df87a7e57  #29   test(download-clients): say what the isolation test does not cover
    5a167f9f2  #42   fix(manual-import): match the rename naming table on casing and missing tokens
    6b0d150d6  #42   test(manual-import): assert path segments, not substrings
    62249701d  #42   fix(manual-import): build every naming key the rename table builds
    8981649c0  #32   fix(prowlarr-import): build proxy URLs from the base Prowlarr answered on
    3881c60f8  #32   fix(prowlarr-import): only adopt a discovery base on the origin that was asked
    37485fb9f  #34   Serve under a URL sub-path by honouring the UrlBase that already exists
    0be7534b3  #34   feat(notifications): give notifications their own ApplicationUrl
    25f28d78a  #34   feat(notifications): read LISTENARR_PUBLIC_URL as the notification base
    089105d5f  #34   fix(api): set the path base from UrlBase instead of appending to it
    1d2c3cab1  #35   Record a failed grab in history when the download client rejects it
    ba1bfd94f  #35   test(downloads): assert the failure record on the blank-identifier path
    3a9345f51  #35   fix(downloads): sanitize the client message written to history
    722003a88  #36   fix(metadata): order series positions with an invariant parse
    19d46f6b4  #36   tests: bring the series-position test up to the convention #717 added
    a16eb9419  #36   fix(metadata): reject a grouped series position instead of misreading it
    43efeadd0  #36   fix(parsing): pin machine-format number parses to the invariant culture
    f5ac32b83  #36   fix(parsing): match size units and reject non machine-format numbers
    139868a9a  #35   fix(qbittorrent): tell a refused release apart from a failed submission
    19a082130  #35   test(downloads): pin the controller half of the 409 change
    758777ce2  #35   fix(downloads): answer a refused release with 409 on the manual send path
    f8c294773  #160  fix(config): overlay a posted startup config onto the file instead of replacing it [merged upstream as #1119, not carried]
    379c5abda  #160  fix(fe): do not post a startup config the settings view never loaded [merged upstream as #1119, not carried]
    9a7ab43e7  #64   feat(settings): add a URL Base field to General settings
    0abce6bd5  #64   feat(settings): tie the URL Base warning to the field it is about
    94ad62ba3  #64   feat(settings): show an empty URL Base box when the stored value is the site root
    c87f68e27  #64   feat(settings): add an Application URL field beside URL Base
    9e237149b  #64   feat(api): reject a UrlBase that is a full URL
    cf98ea61c  #33   fix(audible): tell a failed catalog lookup apart from a confirmed zero-match
    1b1d9b1a3  #33   test(audible): cover the other ways the catalog lookup fails, and the 503 itself
    e6c5240d7  #33   test(audible): give the new controller class the repository's test conventions
    f8b6d9c1b  #33   fix(audible): declare the search endpoint's responses, including the new 503
    c0e15c84c  #25   feat(import): embed cover art into imported files, behind a setting
    123117a0a  #25   docs: attach the new doc comments to the members they describe
    1d595e1c0  #25   tests: pin that cover art replaces the existing artwork rather than appending
    3567450d0  #25   fix(import): embed cover art for a book that has artwork but no ASIN
    deeebc39b  #25   tests: keep this branch's migration out of the shared expected list
    16460d396  #25   refactor: keep the cover art code clear of PR 843's edits to the same file
    e2a9d1c9a  #25   fix(import): extend the hardlink guard to cover-art embedding, with its test
    bc14bc7a7  #61   fix(downloads): make retry-import actually requeue the import
    9fd183c9e  #61   fix(downloads): survive two retry requests racing on the same download
    e1f42ee8e  #62   fix(downloads): give the three download-finalization settings a reader again
    0b3987a98  #62   test(downloads): pin that the processor reads the configured retry delay
    a33f064a2  #62   fix(downloads): stop the retry backoff doubling past a day
    0e008367a  #62   fix(downloads): hold only a first transition into Completed
    e0ee1f86c  #62   test(downloads): scope the completion-stability save to the one test that needs it
    636dba774  #65   fix(quality-profiles): stop hiding Maximum Age, and load a profile's qualities
    e5c42f8b0  #46   fix(search): read Indexer.MinimumAge and Indexer.MaximumSize, and measure age in UTC [merged upstream as #1019, not carried]
    cdcb3261e  #46   refactor(search): split the scorer, and test the UTC date parse directly [merged upstream as #1019, not carried]
    eecb047cd  #46   test(search): pin that the indexer size ceiling applies to Usenet too [merged upstream as #1019, not carried]
    eb3f3dcec  #46   test(search): make the new parse tests follow the repository conventions [merged upstream as #1019, not carried]
    016c797f5  #46   fix(scoring): resolve indexers once per batch, not once per result in parallel [merged upstream as #1019, not carried]
    3e0edba1b  #46   test(scoring): assert the pre-resolved indexer actually reaches the scorer [merged upstream as #1019, not carried]
    aac7c95fc  #46   fix(search): compute the profile size gates in long, not int [merged upstream as #1019, not carried]
    368e86f94  #47   fix(download-clients): make the Priority dropdown speak the planners' vocabulary
    1e74a4c17  #47   test(download-clients): drop two usings the global usings already cover
    c4a7ffd45  #47   fix(download-clients): read a stored legacy priority back as Normal
    8da58a890  #47   fix(download-clients): say what Default does per client, and name Force's band
    ce9b21651  #47   fix(download-clients): lower the priority in the addurl path too
    60a150e01  #44   test(images): assert that a cached image is served, not that something happened
    d813ad337  #83   feat(library): render the list view when grouping by author or series
    a7718e9cf  #83   fix(library): fetch author covers and remember the mode in the grouped list
    029638b26  #45   fix(qbittorrent): send the four Advanced Settings to the client
    2611eba9a  #45   fix(qbittorrent): do not fail a submission the client already accepted
    178c22c74  #68   fix(filesystem): say why a source file could not be pinned
    b25fae189  #68   fix(filesystem): sanitize the pinning cause before it reaches the log
    9eefc477f  #68   test(filesystem): fail the refusal test when no cause is captured
    0119e5fa5  #68   fix(filesystem): lead the refusal with the cause, through the shared formatter
    d2b81f077  #68   fix(filesystem): do not blame a configured root's link for an unrelated refusal
    e1af40c92  #68   fix(filesystem): name no link when the source overlaps an unavailable root
    7b86de0e1  #68   fix(filesystem): name the outermost link as a possible cause, not a certain one
    274ad7960  #68   test(filesystem): make the nested-link guard able to fail
    4ced94e8f  #72   fix(imports): retry a failed file import instead of blocking on the first attempt
    2a4e865f1  #72   fix(imports): retry a failed import only when every file in it failed
    7cb17e89c  #80   fix(quality): let a real encoder bitrate reach the rung it was encoded for
    636e54894  #80   test(quality): pin the bitrate tolerance from both sides, not just the floor
    febdfd398  #102  fix(docker): give the image a healthcheck it can actually run
    ee2b26cc0  #31   fix(notifications): dispatch webhooks for the triggers the settings screen offers
    12b086367  #49   Make the frontend follow the UrlBase by injecting a <base href> into the shell
    5e46776c1  #49   fix(spa): anchor the shell at the site root even with no UrlBase
    bd9b1181a  #109  stack: let a local build stamp its own version
    3ce1711af  #109  stack: publish the patched build to GHCR from the fork
    147a854e4  #109  stack: publish a moving current tag beside the immutable commit tag
    bf2d3935f  #108  fix(files): physical-generation snapshots reject database-loaded observation timestamps
    e53ba5320  #108  fix(persistence): restore the UTC contract for PhysicalIdentityObservedAtUtc at EF materialization
    966cdfa3a  #73   feat(wanted): add a cutoff-unmet bucket to the Wanted page
    006f103fd  #74   fix(collection): show an author's books under their membership series
    f1c46f2f8  #75   feat(library): count the distinct series an author appears in
    d3bb2e40f  #76   feat(downloads): implement the three reprocess endpoints instead of returning success
    ad11bbcf1  #76   fix(downloads): refuse an ineligible reprocess instead of throwing at the caller
    163e737e1  #76   fix(downloads): drop the unused using and the seam-adjacent whitespace
    7ac7b7c3c  #83   feat(activity): sort the queue, and show when an item was added
    9d5c2a761  #86   fix(collection): apply the language preference to library books, not just suggestions
    3528cf119  #110  fix(import): match blacklisted extensions without regard to case
    dbe603cfc  #112  fix(ui): match System recent-log severity classes to the stylesheet
    104231cfa  #20   fix(search): derive one indexer query title, shared by both search paths [merged upstream as #1019, not carried]
    d79a747cd  #20   fix(search): leave the series out of the indexer query, as Readarr does [merged upstream as #1019, not carried]
    20b6cf36f  #66   fix(downloads): give the queue a control that removes a terminal download
    7f40f23e8  #66   fix(downloads): tie a processing job's retention to the download it explains
    fc4cfbe8e  #99   fix(authors): stop attributing one author's ASIN to every co-author
    febdb334f  #100  fix(naming): spell an author's initials one way in folder names
    d77be23cc  #100  test(naming): pin byline order on two co-authors, not on a translator
    90d68d332  #113  fix(system): stop inventing log entries when no log file exists
    a673075c3  #114  fix(library): stop announcing the list-view status badge as a button
    edee18978  #115  fix(settings): stop a failed clipboard write from reporting a failed regeneration
    2b1a9adb2  #95   fix(api): the authentication gate ignored requests that varied the path casing
    20847ea07  #68   fix(filesystem): resolve a symlinked source path instead of refusing it
    8a2a3d907  #68   docs(filesystem): stop two comments claiming what the symlink commit retracted
    e6c80013a  #98   fix(logging): give silent catch blocks a real log call or a stated reason
    4460e7e39  #108  fix(persistence): keep a persisted UTC timestamp UTC when it is read back
    9a32d8a06  #78   test(scoring): pin the four hand-copied quality ladders to each other
    4135b3953  #69   test(metadata): count the attempts the Audible retry policy actually makes
    4a7f474a5  #69   test(metadata): stop the retry policy tests racing the caller's clock
    c517d7750  #136  NZBGet: warn once per failed history entry, and only for monitored entries in polls
    0ad2dea52  #136  fix(nzbget): keep the warn-once keys case-insensitive after a scoped read
    e20bb1391  #92   refactor(collection): lift shared series and text helpers out of the collection view
    dee16bc33  #92   feat(collection): show an author's series as a section on the author page
    889a8dc2c  #141  fix(monitoring): keep every series membership and its ASIN when a monitored author or series adds a book
    35a0bd229  #141  fix(library): let a legacy-only metadata payload carry its series identifier
    8b0d62347  #141  fix(monitoring): keep every series and its ASIN through the author catalog cache
    05204fc51  #141  test(monitoring): use catalog books with real identifiers in the series fixtures
    c3acace5b  #141  fix(monitoring): refetch an author catalog cached before series entries existed
    e854a0cbe  #145  feat(activity): queue selection that survives the poll instead of being reset by it
    a2c84506e  #145  feat(activity): sequential bulk runner with per-id outcomes and one summary line
    90f773d61  #145  feat(api): keep the queue-management API tests, drop the methods #917 and #927 already added
    39a54aaba  #145  feat(activity): a queue toolbar whose verbs say what they will do before doing it
    9db95dfef  #145  fix(activity): the select cell reasserts its props after a click that changed nothing
    5a3bf70fc  #145  feat(downloads): a Retry button that appears only where the endpoint will accept it
    00d162aca  #145  feat(activity): select queue rows and act on them together
    b8faddeab  #145  fix(activity): the filter is a viewport, so selection controls act on the rows in view
    28c0a12f0  #145  feat(downloads): the Retry button does the retry, instead of promising one later
    70d2a71c0  #145  fix(activity): the retry button dresses itself and select all goes quiet with nothing to select
    161046e44  #145  fix(activity): the toolbar drops a confirmation the selection outlived and rests while busy
    3daf0d850  #145  fix(queue): overlay import status onto queue rows the client cannot represent
    790e4e68e  #145  fix(activity): disclose the true count before an unbounded sweep
    5539c38bc  #144  metadata: name the two nothings a provider can answer with
    dddf4ee40  #144  metadata: answer the ASIN endpoints with a status, not a stack trace
    c3e113f4f  #144  images: keep serving the placeholder when the provider does not answer
    a9f68b787  #144  search: move the Audible series endpoints into a partial
    68b07f5e7  #144  search: say the provider did not answer, rather than returning nothing
    9cad3fb49  #144  metadata: walk every source, then report the fault rather than a miss
    c69327ee1  #144  metadata: let the Audible client raise when the provider did not answer
    f606dde2e  #144  metadata: hold the Audnexus book lookup to the same division
    e8089b9e2  #144  library: record when a book's provider metadata was last refreshed
    9e27e99b9  #144  settings: the refresh interval, staleness age and budget are operator-set
    4c8868ce2  #144  metadata: a request budget with an hourly refill and a spacing floor
    58dda6491  #144  metadata: lift the per-book rescan out of its HTTP wrapper
    a354aea36  #144  metadata: one gate, one run, one budget across every refresh entry point
    d969296f9  #144  metadata: walk the stalest books on an interval, and let an operator ask
    01637f62f  #144  fe: a Metadata Refresh section in the settings screen
    d87e70e87  #144  metadata: provider 503s answer as a ProblemDetails the filter keeps
    426c29c19  #151  fix(logging): sanitize ASIN log arguments, rebased onto the metadata-refresh chain
    a3d3fcb38  #153  Attach download history rows to the audiobook they belong to
    eb0152de3  #153  fix(history): record the protocol a download actually used
    5e493a1fa  #153  fix(history): resolve the protocol for the remaining event types too
    716676276  #153  test(history): cover the audiobook key and the protocol on one call
    86b72178d  #154  test(downloads): match the int? audiobook id in the grab history verifies
    474be1dda  #155  fix(imports): classify failures at the boundary instead of leaking them to History
    cab8591dd  #156  fix(naming): key the remaining naming tables case insensitively
    f81a339ff  #158  fix(qbittorrent): a 200 carrying "Fails." is a refusal, not a grab
    9e409aaf7  #161  Record a download client timeout as a failed grab, not a shutdown
    9465cf546  #163  fix(notifications): sanitize webhook URLs before logging them
    af2580b1e  #163  test(notifications): cover SanitizeWebhookUrl and pin the webhook log call sites
    2649b8c20  #166  feat(search): tell an indexer that answered with nothing apart from one that never answered
    12965e569  #166  fix(search): give the per-indexer timeout its own catch, correctly typed
    32242a34c  #166  test(search): match the fake provider to the observation-returning interface
    5bd989221  #166  search: isolate a per-indexer HTTP timeout from the multi-indexer fan-out
    189969087  #166  search: bound indexer fan-out concurrency to 4 (tracker #119)
    8e4491db6  #166  search: separate a rate limit and a refused credential from any other non-2xx
    dee1aeedc  #166  search: persist a per-indexer failure backoff ladder
    27813f5aa  #166  search: skip an indexer that is in failure backoff, and record what each one answered
    781eb6775  #166  search: do not stamp LastSearchTime on a book whose indexer set was degraded
    77eecf9bc  #166  indexers: show on the card when an indexer is in failure backoff
    9bd516411  #166  feat(search): try a second form of a search before saying the book does not exist
    584b17e37  #166  search: cover what the query ladder and indexer backoff do to each other
    a0e3a144d  #166  search: drop the bare series-name query rung
    d11d648fb  #166  test(search): thread the cancellation token through item 107's fake provider
    d29c474d4  #172  172: fix Abridged substring match stamping unabridged books abridged
    d02287f5c  #172  172: fix Abridged substring match on the frontend too
    2dc8d0e8d  #173  173c: expose the Listenarr.Backend meter through a listener and an endpoint
    098a1ac60  #170  search: make the indexer fan-out ceiling an operator setting, shipped at 4
    80570f6f6  #170  settings: expose the indexer search concurrency ceiling in Search Settings
    d2a2891b1  #123  notifications(#126): make EnableNotifications actually gate delivery
    8dd407d79  #123  notifications(#127): dispatch webhooks by stored Type, not URL sniffing
    2328b6c68  #126  notifications(#126): drop EnableNotifications, matching the family
    fa3518a49  #123  search(#123): wire up EnableAmazonSearch/EnableAudibleSearch, dead since introduction
    169f66c5b  #123  notifications(#128): remove the unreachable legacy /notifications/test endpoint
    96b4dff5b  #123  settings(#131): add a UI control for ExtractArchives, backend-only until now
    8f955c05f  #123  downloads(#132): fix RemoveCompletedDownloads for Transmission, SABnzbd, NZBGet
    1af93217d  #124  fix: wire AllowedFileExtensions into FileUtils.IsAudioFile
    cb28a3d72  #124  cleanup: remove two orphaned private AudioExtensions arrays
    f1cbdf48e  #129  Remove vestigial Indexer.Tags field
    a945623b2  #133  history: add retention setting UI and a daily cleanup worker
    542e13578  #138  Preserve series ASIN when mapping monitored author/series books
    1ba87c7bb  #138  test(monitoring): resolve the series-monitoring root through identity, not a raw builder
    35eb730bc  #199  Fix author name normalization to merge spaced-out initials
    1b3938b45  #199  test(domain): bring StringUtilsNormalizeAuthorNameTests up to convention
    dfd77a745  #199  fix(authors): dedupe author-cache methods reintroduced against fix/72's own copy
    fb2e149a3  #151  fix(authors): skip the SQL narrowing pre-filter for non-ASCII author names
    a774096cf  #151  fix: complete the NormalizeAuthorName redirect this branch left half-done
    6ba1ef40f  #201  Finish the author-normalizer consolidation and re-derive the keys it left behind
    ed5c20b69  #201  Keep the canonical author spelling the ASIN lookup already returned
    3f1814f5a  #201  Converge existing books onto one spelling per author at startup
    75ca2df02  #201  fix(library): normalize author grouping and include every co-author
    ce93c2000  #200  Pin the author-identity behaviour these fixes are meant to change
    d99679d8e  #200  Stop reading author identity out of a list that never recorded it
    22b85d83f  #200  Refuse to rename a cached author when an ASIN is already somebody else's
    a9093e836  #200  Confirm an author ASIN with Audible before binding it as an identity
    037710515  #200  Pin what stops a renamed cache row reaching stored author names
    2cfcdb351  #200  fix(authors): dedupe author-cache methods a third time, keeping the collision-safe version
    1ad83e7e8  #122  search: add ReleaseShapeDetector, which tells a bundle from a single edition
    7e71bce5c  #122  search: a per-profile preference between bundle and single-book releases
    ce9d5083c  #122  fe: the release shape control, and a Bundle badge on search results
    8d5a41439  #172  fix(search): word-boundary word filters, and any-of required words
    a3109c2a8  #172  fix(search): route PreferredWords through the same word-boundary matcher
    567665e70  #172  fix(search): make indexer priority a genuine tie-break instead of dead weight or dominant term
    8c1999ba5  #172  search: give the word-boundary term matcher its own file
    2d220f253  #172  search: pin the precedence between the three scorer concerns
    6f362577f  #173  fix(system): stop fabricating download-client connectivity
    16fbd41f6  #173  tests(system-service-logs): pass the download-client status cache the constructor now requires
    ef8ad4926  #169  fix(nav): dismiss the header search panel when the route changes
    74f8542f9  #169  fix(library): Select All reached past the filter into the whole library
    7fd86b932  #165  feat(detail): add Automatic Search action to audiobook detail toolbar
    e2c79c397  #165  feat(library): per-item automatic search icon on the audiobooks list
    dd7ecf44d  #165  feat(library): bulk automatic search on the audiobooks index toolbar
    2d45833ea  #165  feat(calendar): search for the missing books in the window on screen
    6c3e39cbb  #202  feat(ui): multi-row selection on Wanted and Downloads
    532a7e87c  #289  tests: catch up DownloadsView.selection and WantedView.selection with items 73, 169 and 145
    81cedd73e  #174  fix(history): style the events that happen, not four that cannot
    2992f2980  #174  fix(history): keep what a grab knew, and name the download client
    c0c36e785  #174  feat(history): a global History page over the API that already existed
    0416ecdf5  #174  tests(download-service): match RecordDownloadFailedAsync's protocol parameter in the sanitize test
    56b658736  #209  fix(security): an IPv4 address in its mapped form never matched the private ranges
    57501ad88  #190  fix(myanonamouse): spend the freeleech wedge on the download, not the search
    5e9127f24  #190  feat(indexers): parse the release flags trackers advertise
    01051fa6f  #183  Add a task surface over the periodic workers, with a manual trigger
    2658723e0  #183  scheduled tasks: an allowlist for manual runs, not a deny-list
    58f38af98  #183  scheduled tasks: answer the trigger with the row it started, and keep stopped workers
    b42cabeb7  #183  scheduled tasks: say which request started the cycle, and stop 404ing a listed task
    4facd81dd  #183  scheduled tasks: release the gate under the lock that ends the cycle
    89032ef12  #183  scheduled tasks: publish the interval in whole seconds, and refuse what will not fit
    9898ac1fe  #183  scheduled tasks: put the interval refusal on the row, and correct two claims made for it
    51ce59af6  #183  scheduled tasks: make the interval refusal audible, and stop overclaiming what it buys
    56033c379  #183  tasks ui: put the task surface under System, where the family keeps it
    9e7591d1d  #183  tasks ui: test the run control against the refusals the API actually answers
    944828cc9  #183  tasks ui: fix two in-flight defects, make the refusal reachable, and cover the poll
    a28598a20  #183  housekeeping: the frame for a daily retention sweep, with no table in it yet
    0d06d0e50  #183  housekeeping: three tables, and the two that were left out on purpose
    d214c632f  #183  housekeeping: move jobs and their three child tables, with the cascade measured
    aee55ed36  #183  housekeeping: the supersede that never stamped its terminal timestamp, and four comments that overclaimed
    0e542919a  #285  settings: a Housekeeping section for the two switches item 183 shipped API-only
    f5a381a2b  #281  housekeeping: the two deferred populations, and the one slice of each that is provably unreachable
    d031a0ef2  #281  housekeeping: say what the compatibility sweep's query actually costs
    8799eed83  #185  notifications: a subscriber model and a Custom Script provider
    30f70962a  #185  configuration: keep stored custom scripts when a save omits them
    7ba9edbef  #185  notifications: let the EnableNotifications switch gate the subscriber fan-out
    beb9b79e6  #189  feat(calendar): add the calendar endpoint and iCalendar feed
    275bd587e  #189  feat(calendar): give the calendar page a feed subscribe URL
    cc6303617  #189  fix(calendar): correct the status precedence and test the two seams
    a2cd39a84  #189  fix(calendar): accept Readarr's tag spelling, and test the rest of the review
    2898c0897  #189  fix(calendar): close the rest of the parse/query seam, and stop citing what cannot be opened
    228bdfa5d  #189  test(calendar): the case-variant feed is refused by the enforcer now, not the attribute
    364375178  #189  feat(calendar): resolve per-event status in Readarr/Sonarr precedence order
    30bfa9593  #189  feat(calendar): add a first-day-of-week preference, defaulted from locale
    64a510a23  #189  feat(calendar): add a legend component for the event status colours
    780e9e8b4  #189  feat(calendar): colour events by status, add a legend, and a week-start option
    b3c590904  #189  test(calendar): reconcile search-missing spec with item 189's status classes
    3e4b9370a  #79   fix(status): stop reporting a non-preferred format as below cutoff
    396207734  #79   test(status): make the agreement test read the real projection, and pin the widened cases
    938582274  #252  WIP fix(quality): a blank cutoff means satisfied, not "search forever"
    28bf7c01f  #252  test: bring the new cutoff tests up to the repository's test conventions
    d974521cb  #252  test: flag the blank-cutoff consequence for the not-yet-landed upgrades flag
    65da19ae2  #257  fix(quality): resolve the cutoff through QualityMatcher, changing two answers
    58f5f3666  #254  torznab: a bitrate has to be its own token, not digits inside one
    e079181da  #254  torznab: tests for the x264 mislabel, each with its control
    ef8d840a5  #254  torznab: a number is a bitrate only where the text says it is
    e61104424  #254  torznab: recover the bracketed bitrate the tightening had lost
    e89324f1f  #255  fix(search): let a quality profile's Allowed flag actually gate
    baa1a660e  #255  fix(search): correct the claims around the quality gate, and pin what it does not decide
    f167b8488  #255  fix(search): refuse a quality label the gate cannot place
    3e4ca22bd  #255  fix(search): answer the review of the quality gate fix
    db5fd2c19  #253  Break release selection ties deterministically instead of by indexer order
    98fc19668  #253  Make the consuming-site tests actually discriminate their own site
    2f8715014  #253  Restore Readarr's protocol step, without which the chain is not transitive
    a9909894c  #253  Close the second intransitivity, and three more review findings
    04331b236  #253  Put the same-group invariant in the code, and close the last apparatus gap
    df158c7b3  #256  quality profile: refuse a cutoff the profile does not allow
    0929a25f9  #256  tests: cover the cutoff rule at the API and at the attribute
    9d9348202  #256  quality profile: make the cutoff rule agree with the code that reads it
    1adf1617a  #256  quality profile: give it the UpgradeAllowed flag the family has
    2de87873d  #256  quality profile: carry upgrades-off across the upgrade, in a startup repair
    0c2f0be88  #256  tests: pin both directions of the upgrade flag, each against its control
    29b29dc92  #256  status evaluator: drop the upgrades-off guard, it was never the reason
    8428e285b  #256  tests: pin the new migration id, the backfill gate depends on it
    92d55f510  #256  quality profile: decide "blank cutoff" the way the readers decide it
    fc380ee7b  #256  comments: correct three claims a reviewer could check and find false
    4f60e8c70  #261  search: skip the bare-title rung when the title cannot carry a query alone
    436339e4f  #261  search: stop the stem and series rungs unanchoring themselves when there is no author
    ad170a99b  #262  Require indexer categories, the way the rest of the *arr family does
    f2b8fa851  #262  tests: pin that the draft test is gated too, since it binds the same entity
    b15d388bf  #262  Make the indexer form usable now that categories are required
    0652d888c  #262  Move the category default off the entity, where it rewrote stored values
    812f39de7  #262  tests: pin that an explicit null category list is not a way past the rule
    a4ef6ec02  #262  Validate the merged indexer on update, not just the request body
    f0e38c81a  #190  fix(myanonamouse): send the torrent filter as tor[searchType]
    c89c0de35  #190  fix(myanonamouse): send the page size as perpage, not tor[perpage]
    2530b0a5a  #190  fix(myanonamouse): send the language the caller asked for
    4433ff24e  #190  fix(myanonamouse): stop asking the search for a freeleech wedge
    762ab6f9c  #21   tests: give the companion placement pass the root folder this branch now requires
    77b3863ff  #269  fix(manual-import): log the companion files TopDirectoryOnly drops silently
    0590b23e0  #269  fix(manual-import): guard the nested-companion scan against a hostile tree
    e5f8d2f29  #273  cache: make the author and series cache upserts survive a lost race
    67fff8030  #273  cache: do not retry a collision that re-reading cannot resolve
    bd4e95426  #273  cache: the author upsert cannot reach that collision on this build, so pin what it does instead
    36500971c  #273  cache: say only once that a rebind was refused, and describe the collision that can actually happen
    c449a1af1  #273  cache: a refusal that first happens on a retry must still be reported
    8dedd2e4c  #273  cache: the comment on the test still described the guard that was removed
    8674c70f6  #275  fix(metadata): spell a contributor's name without their job title
    aad6a91c8  #275  fix(metadata): store the author first, and keep an edition out of the byline
    c2bb85046  #275  fix(metadata): strip contributor role suffixes on rescan too
    135d92d5c  #275  test(metadata): use catalogue values for contributor role test data
    e998c21f1  #275  fix(metadata): finish the contributor role cleanup on scan, dedupe and the singular author
    d4bcda50d  #275  test(metadata): cite catalogue credits as the catalogue lists them
    53be7dca9  #275  fix(metadata): keep matching an author folder that still carries its role
    77fc43142  #276  fix(metadata): bind an author's ASIN only to a row that names them
    50a50b53f  #276  metadata: lease the shared request budget to callers that are not a refresh run
    5fb4e3373  #276  repair: a scheduled pass that corrects author identities already written
    984f51660  #276  repair: remove contributor roles from the credits already stored
    0da986b1b  #276  test: give the two new test classes the conventions the suite enforces
    479c32487  #276  repair: a provider that did not answer is not evidence that an author has no ASIN
    8ff4aa3dc  #276  repair: answer the fresh review, including a starvation defect nothing was checking
    495ce6a21  #270  filesystem: let a raced pinned hardlink fall back to a copy, and say so
    eb0752415  #270  filesystem: narrow the hardlink race fallback, because the first attempt claimed too much
    7b695682f  #277  Fix the fail-open identifier check in DownloadRemovalWorkflow
    68df81dcd  #277  Route the downloads delete endpoints through the client removal workflow
    4769c7086  #277  Add tests for the client-removing delete and the identifier check
    14e5b1ac6  #277  Make a disabled client deletable again, and fail closed when no client id is known
    0d911e337  #277  Correct the outer-catch mechanism described in a test comment
    c41586af4  #277  Cover the disabled client, the unmapped record, and deleteFiles on the delete path
    8784de050  #277  Let the delete control say whether the download client is involved
    160c1aa46  #277  Make the record-only way out actually work, and stop it claiming too much
    6c9128999  #277  qbittorrent: check presence before delete, fail closed on idempotent-success
    fbe41e2b1  #277  qbittorrent: cover the unparseable-presence-check-body fail-closed path
    cec3d335b  #277  qbittorrent: treat a torrent confirmed absent from the client as removed
    7f0f7044b  #277  tests: give the qBittorrent cleanup composition tests their Name and Category traits
    72bd955a5  #277  fe(activity): the bulk remove path also defaulted to removing from the client
    7f0792e54  #276  fe: an Author Identity Repair section in the settings screen
    1828ae37c  #276  fe: the preview card promised a run button the frontend does not have
    ea8ce94a2  #276  fe: the review's findings, one of which the UI could not have shown
    76a7dcd36  #278  notifications: give the Custom Script provider a settings surface
    6573106e4  #286  notifications: an Email provider, the case a webhook type cannot express
    7578a77d9  #286  notifications: give the Email provider a settings surface
    cea3bf275  #286  notifications: assert both providers are actually wired into the container
    0f341e72c  #286  notifications: act on review of the Email provider
    484d33467  #286  notifications: cover the sentinel guard on a database with no settings row
    b92eb9d9c  #286  configuration: move the email members into their own partial
    1739af5b7  #286  notifications: exercise the SMTP transport against a socket, not a mock
    84a9be1aa  #286  notifications: act on the second review of the Email provider
    3a65a75fa  #286  configuration: split startup config out of ConfigurationService, so the file is under the cap again
    7726394bf  #177  feat(download-clients): one selector, with a priority the operator sets
    574514fb1  #177  test(download-clients): pin the selection policy and the migration default
    e7d21042d  #177  settings(download-clients): a Client Priority control, so the field is reachable
    e1ff88a41  #177  test(download-clients): pin the rotation cursor's lifetime, with a control that moves
    e5de3c0a4  #177  fix(download-clients): four things a fresh review found
    f723d6b23  #177  settings(download-clients): give Client Priority its own section, keep queue priority usenet-only
    51f943446  #177  test(indexers): pin per-indexer download client binding, failing first
    5ee916c47  #177  indexers: give an indexer a nullable download client binding (shape only)
    60613f367  #177  indexers: route a bound indexer's grabs to its client, and fail loudly
    1eb410739  #177  test(download-clients): pin persisted client failure status, failing first
    dbbfb93ef  #177  download-clients: persisted client failure status, shape only
    1cc419a9b  #177  download-clients: record client failures, back off, and route around a blocked client
    cd6a0ae81  #177  download-clients: record status in a gateway subclass, not a wrapper
    5776a149f  #177  stack: make item 177's two migration Designers the cumulative model at their position
    1f3c81136  #179  backup: take a copy of the database before migrations run
    af425f397  #179  backup: the settings column, and two defects the suite caught
    153914f5c  #179  backup: tests, each paired with a case that comes out differently
    2ae1573e5  #179  backup: the operator surface, so this is reachable without a shell
    ceea50bb9  #179  docs: describe the backups directory and the startup override
    dc05da9c2  #179  backup: take the copy before the startup path's own repair writes
    7b73889d0  #179  backup: cover what the review found had no test
    16397e94c  #179  wip: retarget the auth test
    e630570aa  #179  backup: close the gaps the review found in the code and in its tests
    68ef95f49  #179  backup: fix what the second review found, and rethink the manual limit
    0b1c8655d  #179  backup: pin the negative retention case
    6156baaf0  #180  feat(indexers): per-indexer seed ratio and seed time
    c52c13ec5  #180  fix(indexers): close review findings on seed criteria
    f77d3ab19  #181  search: the release score has no ceiling, so a preference can still decide
    36e13e2f9  #181  tests: SearchResultScorerCeilingTests follows the repository test conventions
    dc1f9662a  #181  settings: the minimum score threshold is no longer capped at 100
    77de2f8aa  #181  search: correct what this branch claims, after review
    1cb28da0d  #264  search: the profile's size and quality gates apply to Usenet results too
    dff600881  #264  search: size bounds above 2047 MB, and three corrections to the commit before this
    8bad28c7a  #264  search: reconcile the NZB gate hoist with the tests that pinned the exemption
    e138baede  #263  search: release selection reads the quality profile, not the hardcoded ladder
    a3d03599f  #263  search: take the best candidate that is an upgrade, and fix two things in the commit before this
    a67678df1  #181  search: split the rejection gates out of the scorer, so the file is under the cap again
    33bb8825c  #181  search: two warnings, one of them a control this stack no longer has
    a60ecf27a  #182  naming(#182): render the settings naming preview through the real backend renderer
    e2d281f22  #182  naming(#182): wire the settings naming preview to the backend renderer
    3321784c2  #182  naming(#182): fix real divergences a fresh review found from GenerateFilePathAsync
    79b130b7c  #182  naming(#182): flip the lowercase-token preview test now that the case fix is in the tree
    94e400efb  #184  tests(notifications): pin the item-184 gaps blocked on PR #943 (WIP)
    35b7a7d7b  #184  tests(notifications): tighten item-184 gap pins after fresh review
    71f9dec2b  #187  feat(#187): free-space check before import (G7)
    21ddfdcdb  #187  feat(#187): free-space on root folders (G9), tests, and settings UI (G7/G9)
    761aa31ce  #187  fix(#187): the free-space guard silently never fired under the default naming pattern
    f44e6d5fa  #187  test(#187): prove required bytes are the real file size, not a stand-in like #821's 64
    5c3a88785  #284  Add failing test for metadata repair guard missing Entries include
    fa88437c9  #284  Fix metadata repair guard to load MoveJob Entries and CreatedDirectories
    9d3e7cbc7  #283  tests: add failing control for ProcessExecutionLogs table drop
    774c67a7e  #283  Drop the dead ProcessExecutionLogs store, repository, entity, and table
    3aa11fd36  #237  fix(search): reconcile the query ladder with fakes and tests from three unrelated items
    0a7e889c1  #237  test(startup): mock the metadata-refresh backfill this test's strict repository was missing
    40c3f2503  #237  style: run prettier over item 201's two touched files
    dae406099  #237  fix(search): reconcile the scorer 3-way with fakes and tests from unrelated items, plus two more real bugs found by the suite
    7cecd229c  #237  stack: reconcile the manual-run allowlist with the stacked tree
    239a0976c  #237  tests: read the observation's results, the way its sibling case already does
    a79b9361b  #237  tests: point the tiebreak harness at the signatures the stack actually has
    a612f0557  #237  tests: move the blank-cutoff pin to the answer fix/blank-cutoff-search-loop gave it
    2ce8ca535  #237  downloads: split the client-name projection out, so the controller is under the ceiling again
    a20618b37  #237  tests: guard that every migration Designer is its predecessor plus its own Up
    58587aa55  #237  stack: make every composed migration Designer the cumulative model at its position
    19f80defa  #191  qbittorrent: failing spec for postImportCategory form field
    a0a4fdba5  #191  qbittorrent: add Post-Import Category field to the client form
    40b6441b4  #288  symlink-chain: fail-first tests for a two-or-more-hop chain
    7491c11e6  #288  symlink-chain: walk the whole chain, not just the last segment
    058c0c0f4  #237  stack: make the three Designers after item 170's migration the cumulative model again
    f7c1f5a46  #293  fix: grid containers shrink to content instead of forcing viewport height
    08ac9a5d4  #293  fix(activity): virtual row stride must not be shorter than the rendered row
    f7ee6735a  #293  fix(activity,wanted): measure the virtual row stride from a rendered row
    6634b7735  #279  metadata-refresh: allow the scheduled walk onto the task surface's Run button
    8e8191527  #146  manual-import: reject items missing a matched audiobook id with 400
    17b4d02cd  #146  tests: follow the repository test-class conventions
    cb940ee77  #215  fix(import): refuse a manual import on its extension before publishing it
    11bdc5503  #215  fix(import): say that extensionless sources are refused too, and pin it
    59c784663  #139  fix(library): identify a series by its SeriesAsin, not its display name
    c0c38e354  #139  fix(library): series identity fixes from review: non-Latin names, author guard, stale links
    a065165fa  #139  fix(library): series author guard joins on a shared author, script-preserving
    21b7528e7  #291  fix(search): give the indexer backoff write a context of its own
    78b3268b1  #291  test(search): assert which context the backoff write used, not whether it collided
    79a9e5e9b  #215  import: move the tag enrichment out of ProcessItem, so the file is under the cap again
    259c0b862  #295  health: report indexers in failure backoff, and separately those failing for more than six hours
    c1c713fd1  #295  search: tell an indexer that could not be asked from one that had nothing
    5cda2e517  #295  ui: count indexer health checks on the sidebar's System entry
    61818896b  #295  search: no default for the outcome search, and tidier could-not-search reasons
    d585a0a8f  #295  ui: show the System health badge with authentication off, and re-read it every minute
    89778f297  #81   settings: add a stalled-download timeout, off by default
    d6f72dd09  #81   downloads: fail a torrent that has made no progress for the stalled-download timeout
    9407c058d  #81   ui: stalled download timeout field under failed download handling
    21f907214  #81   test: a pause shorter than the gap cap still clears the stall clock
    b4a4eea37  #223  search: one quality ladder instead of four copies
    b0b5347a6  #223  test(scoring): pin the ladder's rung order and every rung literal
    5140dfaf0  #63   search: make Prefer newer releases a real ranking bonus
    40b66e758  #63   ui: say what Prefer newer releases now does, and pin Maximum Age across a toggle
    10cef5335  #63   search: cap the newer-release bonus below a preferred word, and keep it off rejected releases
    210cbc67b  #63   ui: describe the smaller newer-release bonus, and check Maximum Age is visible
    85759e44a  #63   search: default PreferNewerReleases to false, matching the settings modal
    ed67b1adf  #218  fix(scan): report a scan whose files were all declined as CompletedNoFilesAccepted
    2f33372e6  #218  fix(ui): show a scan that accepted no files as its own outcome
    eca940ae7  #218  fix(scan): record a diagnostic when a candidate's metadata is read and declined
    18a7ea38a  #218  fix(metadata): name the file in the ffprobe-failure warning
    24d05953e  #218  fix(scan): count only files this book could claim, and skip other books' files
    313a36198  #218  fix(ui): label a no-files-accepted scan on the detail view's Files tab
    11bc5d89d  #218  fix(scan): probe other books' files again so the metadata conflict guard is unchanged
    bb32963c1  #218  fix(scan): decide no-files-accepted on durable ownership, not attribution
    250768e47  #218  test(scan): reproduce a folder-attributed file whose tags name another book (red)
    d761ae605  #218  fix(scan): decline folder-attributed files whose tags name another book
    544b56408  #218  test(scan): update probe-count and swap-timing expectations for the new pass
    0ce4d9480  #218  test(scan): give the tracked-file guard a real identity and a Times.Never
    0469b8546  #3    791 option D (NuGet ffprobe) composed onto the stack, for validation on the install [merged upstream as #786, not carried]
    ff224d345  #309  test: assert FailedDownloadAutoSearch defaults to true
    0a0b86694  #309  fix: default FailedDownloadAutoSearch to true, matching the *arr family
    e4ad1d65b  #298  test(redaction): reproduce ffprobe JSON corruption from RedactText fallback
    1be98d849  #298  fix(redaction): drop generic fallback that corrupted unrelated text
    09b1210a8  #299  fix(library): reject an unrecognized refresh-metadata field instead of silently upgrading to whole-library (#299)
    57d0e1b45  #299  fix(library): catch the deserialize JsonException so a wrong-typed field 400s, not 500s
    3535c7b73  #69   test(downloads): extend the seed-limit removal gate tests to the full matrix
    368f9792d  #69   fix(downloads): make CanRemove reach the seed-limit evaluator on the queue-poll path
    ba768f72d  #69   test(downloads): cover the exact ratio == limit boundary for both clients
    426cfb5ad  #69   test(downloads): pin the fail-closed seed policy on the queue-poll path
    60e92bdd0  #317  fix(imports): retry a failed file import instead of blocking at once
    1ade61f3f  #317  fix(imports): use the configured retry delay on the file-import retry
    3b4eb5fea  #317  fix(downloads): finalize a held completion whose client item is gone
    273b162ab  #324  fix(downloads): isolate OnDownloadFailed's blocklist/removal/search steps so one exception doesn't abort the rest
    c1de77f11  #326  test(qbittorrent): add numeric-tolerance coverage for the response mapper helpers (#1008)
    d6f689790  #326  fix(qbittorrent): tolerate every numeric JSON shape in the response mapper (#1008)
    66e611c03  #326  test(qbittorrent): malform state instead of downloaded in the unreadable-torrent fixtures (#1008)
    2ecfee4e3  #218  fix(scan): log per-file MetadataContradictsPath diagnostics on CompletedNoFilesAccepted
    3ac90bfb4  #325  fix(downloads): record History entry when orphan cleanup removes a download
    ba6f165cc  #340  fix(downloads): record History entry when DownloadRemovalWorkflow removes a download
    69c547916  #367  fix(downloads): a successful Removed History row no longer carries a non-null Error
    0a23235cc  #333  fix(nzbget): classify DeleteStatus-family history entries as Failed, not Ignored
    8c00e0f99  #333  fix(nzbget): correct Sonarr/Readarr citation line numbers in comments
    9d888a7f2  #335  feat(blocklist): add a list-everything endpoint for the frontend page
    c18a85869  #335  feat(blocklist): build the Blocklist page (list, remove, nav entry)
    3a6c1085a  #336  settings: add RejectClearlyMusicReleases to ApplicationSettings
    57b2fcc4c  #336  search: gate clearly-music releases in SearchResultScorer when enabled
    48754dfa0  #336  fe: add "Reject releases that are clearly music" search setting toggle
    a28368c1b  #336  test: extend the post-canary migration allow-list for the new migration
    da02d2c1e  #336  fix: Audiobooks id must override even alongside another Audio-range id
    a941eb7c5  #353  fix(blocklist): require confirmation before removing a blocklist entry
    6f280dcc5  #361  search: strengthen MusicReleaseClassifier with weighted signals (tracker #361)
    659b61eae  #361  search: fix 6 false positives found by independent review (tracker #361)
    bfc452677  #361  search: fix 6 more false positives, round 2 of independent review (tracker #361)
    0bdecdfa1  #361  search: fix 4 more false positives, round 3 of independent review (tracker #361)
    a63184585  #361  search: fix 4 more false positives, round 4 of independent review (tracker #361)
    a11feeb6d  #361  search: gate the bitrate/FLAC signal on a music-specific corroborator (tracker #361)
    f5589d4f3  #361  search: drop Artist/Album consistency from the bitrate gate (tracker #361)
    1efb96877  #361  search: require two-signal convergence to gate bitrate/FLAC (tracker #361)
    ac4aa7701  #361  search: gate bitrate/FLAC on an actual music category only (tracker #361)
    9b018688b  #361  search: remove the bitrate/FLAC/kbps signal as dead code (tracker #361)
    7a194b954  #371  fix(library-import): decouple manual-search ASIN detection
    b553148fd  #371  chore: replace non-corpus identifiers in comments and tests with placeholders
    52d5e8115  #371  test(metadata): use catalogue values for contributor role test data
    49d7c02d4  #371  test: take series and diacritics test data from the catalogue
    0a8680783  #366  fix(metadata): bare-anywhere-wins for author/contributor classification
    aa6ac02bf  #366  fix(metadata): close the author-credit vocabulary gap and the Introduction asymmetry
    548095c5d  #366  feat(metadata): add a standalone organization-as-author detector
    e0ece1482  #366  fix(metadata): apply the author-credit classifier at the 3 missing call sites
    d4c0e344c  #357  fix(nzbget): bound the unbounded history fetch, item 357
    598365250  #357  fix(settings): reject DownloadClientHistoryLimit below 1, item 357 follow-up
    1f6b3083d  #237  fix(downloads): skip the failure row for a qBittorrent duplicate
    6dc6805e3  #237  fix(qbittorrent): send an explicit false for Initial State Start
    dc9e8d009  #237  test: use neutral book titles in the fixtures this branch adds
    dcefe2c12  #237  test: use public-domain corpus titles in the fixtures this branch adds
    c6886475d  #237  fix(qbittorrent): a skipped torrent marks the snapshot incomplete
    e76f9a8b7  #237  fix(qbittorrent): an open circuit breaker still fails the poll
    eb857ed8b  #237  test(qbittorrent): drive both fixes through the monitor cycle
    9cd9019b5  #237  test(qbittorrent): use host-valid save paths in the gateway-level fixtures
    bebe8bcd0  #237  stack: #1021 take the head's comment wording for per-client-type HTTP handlers
    e71617b22  #237  refactor(quality): move QualityMatcher's helpers to a partial file
    ff5136542  #237  fix(search): keep refusing a codec the ladder has no rung for, as canary did
    23acc2a0a  #237  stack: #1023 take the head's wording for two scorer comments
    5ded74d50  #237  fix(notifications): redact discordapp.com and pushbullet:// webhook URLs
    dc965a756  #237  fix(logging): keep known env-secret masking in SanitizeWebhookUrl
    cb8701394  #237  test(logging): serialize env-mutating redaction tests
    4c1fddf7c  #237  stack: #1022 give the hardlink ASIN-tag test a metadata-writing lease on a real publication
    c6ad0b2c7  #237  stack: reconcile the stalled-download timeout tests with the settings controller
    9ae435a5c  #237  stack: reconcile the history-limit settings test with the settings controller
    de8a91622  #237  stack: reconcile the NZBGet history-entry test helper with required members
    2aa5b72f6  #237  stack: reconcile two scan test files with the scan command upstream reshaped
    a88adf938  #237  stack: reconcile the Designer-chain guard's sentinels without the recycle bin
    495075925  #237  stack: reconcile the pinned migration list without the recycle-bin migration
    42e78982d  #237  stack: reconcile upstream's released-schema upgrade tests with this stack's columns
    30f97deb4  #237  stack: reconcile #914's import-tag tests with leases that withhold metadata writes
    34004dded  #237  stack: reconcile the hardlink fallback tests with upstream's publication catch
    3040f4ed4  #237  stack: reconcile #902's refresh regression test comment with #993's snapshot
    0157d1754  #237  stack: reconcile the symlinked-source tests with content-only source proofs
    36596ac08  #237  stack: reconcile the Wanted selection specs with upstream's Search All confirmation flow
    2465eb9b0  #237  stack: reconcile the WantedView spec formatting after the row stride test
    54d890465  #237  stack: reconcile the detail view spec's signalRService import
    6e8d90bbd  #237  stack: reconcile the download settings spec with the stalled download timeout input
    26cc4416e  #237  stack: reconcile upstream's empty-send download test with the search query plan
    a0488c05a  #237  stack: reconcile the companion log-sanitization test with the provider it rebuilds
    177a881b0  #237  stack: reconcile DownloadMonitorService with the 500-line cap
    b2e338541  #237  stack: reconcile RootFoldersController with the 500-line cap
    f21747f72  #237  stack: reconcile upstream's SQLite-reload refresh test with the stack's UTC read converter
    514c485c2  #68   fix(filesystem): resolve a linked source ancestor only when no link in its chain sits inside a configured root
    3e266c4f2  #68   test(filesystem): pin symlinked source ancestors against the configured roots
    095646fab  #109  stack-image: fail the build if the version stamp is stale, not just missing
    858554ba2  #109  stack-image: anchor the version/patches grep to the manifest header
    809615814  #383  fix(delete): keep the recursive folder delete unless every tracked file is proven (#1071)
    3927659f1  #383  fix(delete): keep the recursive delete for durably owned folders (#1071)
    ba136e7b5  #237  stack: import AuthorCredits where the rescan role-suffix fix was ported
    23ced91c2  #237  stack: give the upgrade-allowed and embed-cover-art migrations IDs after the upstream frontier
    2f6b19624  #237  stack: point upstream's empty-query and fallback-query tests at this stack's search surface
    f0ee1d7ee  #237  stack: rebuild the migration Designer chain for v1.5.9's order
    da55e1e95  #237  stack: reconcile two files with the 500-line cap after v1.5.9
    f12f634d7  #237  stack: reconcile #1021's and upstream's new tests with this stack's search and queue shapes
    086e868f2  #237  stack: let the refresh-cycle spend test walk two regions under #1043's rule
    d9b09ffa0  #237  stack: order line 267's bare-anywhere-wins tests by bare credit, as #1013 now does
    25094ba6f  #237  stack: queue reconciliation tests mock the snapshot API the poller now calls
    664b9b137  #68   stack: adapt the nested-symlink source test to item 68's behaviour
    27bcde20e  #237  stack: pin System.Security.Cryptography.Pkcs to 10.0.13, the first version outside two published advisories
    30e299b97  #-    style(fe): run prettier over the 12 stack files that drifted from it
    1f5a95bb9  #-    fix(nzbget): hold DELETED/COPY and DELETED/DUPE as client duplicates, not failures
    0fccc2d27  #-    fix(downloads): reuse the blocklist filter's identity check for client-duplicate holds
    bb0b2c53c  #-    fix(downloads): keep client-duplicate holds out of failure alerts and retry-import
    3d611acd8  #-    Pin stored webhook type over URL sniffing in dispatch
    4d9d2e329  #-    fix(import): fail the job when the import refuses with a conflict
    9362211c0  #-    Don't wait the per-book search delay for books that are not searched
    c9729d21d  #-    Repeat the pre-search checks after the per-book wait
    100b6aa2d  #-    stack: reconcile two files with the 500-line cap after the ff6 merges
    8302d80ef  #-    stack: point the search-delay and import-conflict tests at this stack's surfaces
    f954281a9  #-    stack: drop the second copy of #843's read-write stream pieces
    ed1a3733c  #-    stack: carry the test adaptations the canary merges made into the linear stack
    8c3d4d4e9  #-    stack: write the failed-download blocklist entry through FailedDownloadBlocker
    998bdfbf2  #-    stack: clear the findings the backend format check reports
    acd731960  #-    stack: carry #1023's re-scaffolded migration id and move #914's after it
    119fab9b6  #-    stack: rebuild the migration Designer chain for #1023's new position
    e0b7ece52  #-    stack: drop 762ab6f9c along with d8f922865
    e3e7ee7f6  #-    stack: reconcile the 1.8.4 replay so it builds
    9f6930c20  #-    stack: reconcile upstream's automatic-search tests with the stack's scoring and query-plan parameters
    369bd42b2  #-    stack: give the tiebreak and client-duplicate grab fixtures a book the identity gate confirms
    67adfb86e  #-    feat(rescan): persist a rescan due time per file and add a due-page query
    f0e1558f9  #-    fix(rescan): keep the rescan schedule out of whole-entity file updates
    224b7756e  #-    feat(rescan): add the rescan outcome enum and the pure schedule policy
    6fb7dc06f  #-    feat(rescan): compare-and-set schedule writes for the rescan repository
    d1be45de4  #-    feat(rescan): report why a guarded metadata refresh did not write
    754916c1b  #-    feat(rescan): bounded selection loop for the metadata rescan cycle
    080c11bfd  #-    feat(rescan): processor runs the bounded selection and persists each outcome
    8ff33e1f8  #-    test(rescan): wire the non-audio boundary test for the bounded selection
    e73552b74  #-    feat(rescan): reset the rescan schedule when a physical generation is replaced
    6b859c460  #-    test(rescan): keep the shared large-library fixture to base schema columns
    b02f5dba8  #-    feat(rescan): manual file-metadata rescans record their extraction outcome
    53d87e58d  #-    Take the pinned directory open failure message from the caller
    d2e9650f1  #-    refactor(rescan): keep the rescan outcome and schedule policy in the application layer
    f587667d1  #-    test(rescan): keep the manual clock's start in its own field
    333bca04a  #-    Add request-boundary contract types and a pass-through request gate
    fe61c3944  #-    Carry search origin plumbing from the earlier budget work, without the budget
    e265ccb05  #-    stack: reconcile the search cluster's first picks with the stack's metadata refresh and query plan
    a1ac7cb29  #-    stack: reconcile two stack tests with the rescan column and the retry origin
    717944c2f  #-    stack: carry Indexers.Tags in the metadata-rescan migration's Designer
    76ec67edd  #-    stack: retire the stack's indexer search design before the request-gate rework
    10423398e  #-    Return typed outcomes from the indexer search providers
    639190baa  #-    Route Internet Archive requests through the request gate
    04d9ee163  #-    Send redirect hops and detail requests through the request gate
    5a8584681  #-    Send Torznab comments-page fetches through the request gate
    4435f2898  #-    stack: reconcile two stack Torznab tests with the request-gate provider
    1122c8e0f  #-    Add the automatic-search schedule columns, pair-state table and migration
    384b5369d  #-    Give pre-existing books their own automatic-search due time at startup
    69af66a24  #-    Read the due page and its pair states in SQL
    0ce8b6008  #-    Add the search wait ladders and the book-outcome decision
    0d846a19b  #-    Persist pair and book schedule outcomes set-based
    35851ba2a  #-    Plan-test the due page and pair lookup on the large library
    0e1d0214b  #-    stack: stub the pre-migration backup in the schedule startup-repair test
    2e8f33f88  #-    Search due books on their due indexers only, and persist every outcome
    154ea731d  #-    stack: carry the client selector and the indexer binding into the automatic-search acquisition
    3e6bb41da  #-    stack: keep the client-duplicate filter in the automatic-search acquisition
    1194887aa  #-    stack: give the automatic grab the cycle's token, so a client timeout is a failed grab
    ccbe83cc5  #-    stack: rank the automatic grab's log and broadcast in selection order
    afe703c93  #-    stack: take the best automatic candidate that is an upgrade, not only the first
    6c62c221c  #-    stack: do not wait the per-book delay before books the sweep does not search
    f96c09d73  #-    Run the title-only fallback per indexer, as a fallback query
    4bf2ee613  #-    Wait between books only before one that will search
    1f3fab14d  #-    Make every book due now when an indexer becomes eligible for automatic search
    88bafef2f  #-    Reset a book's automatic-search schedule when its search inputs change
    c617955af  #-    Search by the detailed report on a manual search and say why a deferred one found nothing
    1f4839156  #-    Count every search the request bound skipped as deferred
    dc0245ea0  #-    Record only Satisfied pairs from a manual search
    303230bb8  #-    Test that the startup repair leaves a real due time inside a chunk alone
    adfe22a57  #-    Test that a sweep grabs a book that belongs to two series
    822462136  #-    Load series memberships with the sweep's due page
    f60a315cc  #-    Test that the sweep does not grab a release blocked for the book
    a0fc28772  #-    stack: reconcile stack tests with the manual search's detailed report
    bafa1e55d  #-    stack: drop the duplicate sweep pacing tests
    aa16dad5b  #-    stack: make priority decide the unbound client-binding row
    474d575c4  #-    Add request-gate deferral reasons, grab and test kinds, reservation hook and deferred exception
    7eed0a13b  #-    Add indexer request policy constants and Retry-After parser
    abc2c273d  #-    Persist per-indexer request state for the request gate
    8ec1750ac  #-    Add indexer concurrency limiter and pacing schedule
    d147b7a57  #-    Add the indexer request gate and its per-indexer state cache
    b7f7b0d59  #-    Test the request gate's rate limits, restart, cancellation and unattributed requests
    ed5eea822  #-    Test the request gate's reservations, peeks, state load and concurrent writes
    d37cd4c91  #-    Count a rate limit once when requests already in flight draw it too
    18cacd7ad  #-    Keep unattributed indexer requests away from reservations and peeks
    d03e6cd82  #-    Report a failing indexer's cooldown with no candidates left as Failed
    922a61b97  #-    Send indexer requests on a no-redirect client, one gate call per hop
    b455838c9  #-    Send torrent fetches through the indexer request gate
    1b32753fa  #-    Drop usings the global imports already cover
    5fa9f41b7  #-    Update adapter test mocks for the indexer id parameter
    c0c8edc4d  #-    Send NZB fetches through the request gate on the Indexer client
    818758f33  #-    Route torrent preparation requests through the indexer request gate
    45e4e6cbb  #-    Test that torrent preparation requests go through the indexer request gate
    498b5a997  #-    Test that torrent preparation requests go through the indexer request gate (drop unused using)
    536f529cb  #-    Send connection tests and the debug search through the request gate
    6521ce599  #-    Test the gate on connection tests and the debug search
    d331d2478  #-    Send the MyAnonamouse search through the shared redirect dispatch
    14571a95c  #-    Test that indexer-path files send only through the request gate
    f0f8c92b7  #-    Test request accounting and concurrency through the real providers and gate
    a880c098d  #-    Drop unused usings from the accounting tests
    f40e9141f  #-    Run the indexer request state tests with connection pooling on, as production does
    87491fad2  #-    Run the pooled 200-caller state tests only when asked for
    cf80c8902  #-    Treat a deferred torrent or NZB fetch as a grab that did not complete
    924f69623  #-    Test the sweep over the real request gate: a 429 defers the pair and holds later books back
    b5c59c0f4  #-    Test that gate, cache and provider logs carry no URL query, key or indexer setting
    7cc2cbcaf  #-    Answer a deferred grab fetch as a deferral, word deferrals by reason, tighten tests
    820f73f5e  #-    Redact the URL in the missing-Location redirect error; rename a torrent preparation gate test
    65296368b  #-    Let a torrent fetch timeout feed the failure ladder
    93e64defd  #-    stack: classify the stack's HTTP-mentioning files for the indexer traffic test
    b3a1cc8c7  #-    stack: move the indexer connection-test helpers into a partial file for the 500-line cap
    4217b69f5  #-    stack: pin that a 429 leaves one IndexerRequestStates row and the retired backoff columns as stored
    1f33e82ec  #-    health: report indexers the request gate is holding back, with when and why
    8f83fc2da  #-    search: tell an indexer that was not searched from one that had nothing
    336ae562b  #-    ui: show request-gate cooldowns on the System page, the sidebar and the manual search
    34efaf7d5  #-    stack: check that the application container hands every gate consumer the one real gate
    e98d7b6fb  #-    test(ui): name the badge refresh spec for the IndexersUpdated message it drives
    bb33e9607  #-    test(health): pin the indexer health check to the interactive origin
    e5812b9c8  #-    stack: merge #930 (upstream PR head fca523ea0)
    077fa46ad  #-    stack: merge #975 (local/upbase-174-975 at 057969903)
    b52041beb  #-    stack: merge #978 (local/upbase-174-978 at 4569115e3)
    b79902076  #-    stack: drop the second AddSuccessResponseBody the #978 merge added to the qBittorrent mock
    eab08d228  #-    stack: merge #1015 (upstream PR head b461d4edf)
    cfbddc5bd  #-    stack: merge #1017 (upstream PR head 203aaa622)
    c6d261551  #-    stack: merge #1023 (upstream PR head dd2da9409)
    97dded410  #-    stack: point #1023's declared-attribute scoring tests at this stack's search surface
    8f0eeeb34  #-    stack: merge #1119 (upstream PR head d0f729866) [merged upstream as #1119, not carried]
    9392ff349  #-    stack: merge #1155 (upstream PR head ea2f349e7)
    41fd84eda  #-    stack: drop the second ApplicationConflictException catch the #1155 merge added
    9fc7291aa  #-    stack: merge #1164 (upstream PR head 639f0ca20)
    547e0af01  #-    stack: keep MyAnonamouseResponseParser under the 500-line cap after the #1023 merge
    a6b2ac773  #-    stack: merge #1016 (upstream PR head 6abb2bc86)
    f5cfa8302  #-    test(logging): fit #1016's new log tests to the stack's search and ffprobe shape
    f5fd639ec  #-    test(qbittorrent): pin that a presence-check error body cannot forge or flood the log
    1e3577190  #-    fix(qbittorrent): sanitize the presence-check error body before logging it
    d2bd4dcb0  #-    stack-image: count manifest patches along the first parent
    1418dc287  #-    test(qbittorrent): pin that a delete error body cannot forge or flood the log
    3ab019b6f  #-    fix(qbittorrent): sanitize the delete error body before logging it
    d653b2eb5  #-    test(manual-import): pin that companion files stay inside the destination boundary
    b0053c1bf  #-    stack: manifest for b635a856c plus 730 patches
    1aa3a30a6  #-    stack: merge v1.8.5-canary
    3b0cb5b58  #-    stack: manifest for 942fe990d plus 732 patches
    a4f963095  #-    stack: merge v1.8.6-canary
    4a201b702  #-    stack: manifest for c35b58fe3 plus 734 patches
    a6bd720d8  #-    stack: merge v1.9.0-canary
    2e03ac40a  #-    stack: manifest for 081c858be plus 736 patches
    7d96241b7  #-    stack: merge v1.9.1-canary
    6a25c15a7  #-    stack: regenerate the Playwright API types for the stack's API surface
    fed713305  #-    stack: manifest for bfbb0526e plus 739 patches
    ec3f02125  #-    stack: the new backend integration tests create their quality profile with upgrades off
    0bf42ffb1  #-    stack: manifest for bfbb0526e plus 741 patches
    dc1e01f4c  #-    stack: merge v1.9.2-canary
    f809ba909  #-    stack: manifest for 537137e99 plus 743 patches
    c8eee173f  #-    stack: restore upstream's StartupConfig comment
    e0e4fceb8  #-    stack: merge v1.10.3-canary
    9e82e0142  #-    stack: manifest for 8369487a7 plus 746 patches
    d59d00b86  #-    stack: merge v1.10.4-canary
    db92a9d85  #-    tests: name the timeout retry case for the stack's failure backoff
    27a3da625  #-    stack: manifest for e4fe947c9 plus 749 patches
    63e342c80  #-    stack: merge v1.10.5-canary (#1203) and reconcile the stack's download patches
    790689930  #-    tests: history written after guarded orphan delete; NZBGet unbounded presence read
    9b49c83a2  #-    stack: manifest for faf995e8c plus 752 patches
    27fc348dd  #-    stack: keep two files under the 500-line cap and give the Transmission category tests a response per request
    8eadec41c  #-    stack: manifest for faf995e8c plus 754 patches
    ebfdd71d0  #-    fix(downloads): a failed History write no longer aborts the rest of orphan cleanup
    853c49753  #-    fix(downloads): keep the completion hold when the release write is rejected
    bcc732b01  #-    fix(nzbget): a monitor poll reads the whole history, not just the configured limit
    cd7644d10  #-    stack: manifest for faf995e8c plus 758 patches
    3e2df3a26  #-    Merge upstream v1.10.6-canary (dc14be034) into the stack
    4ee39e318  #-    test(monitoring): stack tests pass a quality profile service after #1205
    ede49b5f2  #-    stack: rebase the AddMonitoringQualityProfile Designer on the stack model
    c31013dc3  #-    test(library): stack add and monitoring tests seed a quality profile
    849c13906  #-    fix(collection): the author page's series section monitors with the selected profile
    4ba9b3226  #-    test(collection): the series-identity page spec offers a default quality profile
    78f741d08  #-    test(quality-profiles): create the #1205 fixture profiles with upgrades off

## How this stack was built

A linear rebuild on upstream canary 1.8.4 (b635a856c) of the 1.7.4 stack (e696185ca plus 685 patches, manifest
1003d8eb7), followed by one merge per PR or issue-fix branch that is not already carried. Every patch of the 1.7.4 stack was
replayed in order with `cherry-pick -x` (rerere off), except the patches upstream now carries or supersedes: #916,
#1022 and #1153 (merged upstream), items #60, #22 and #94, commit 52c8d2f7b, the #268 series and the #269 core
(equal to upstream), #212, d8f922865, b3cdb8bd1 (moot on upstream's release selection), the fixture-title
commits 17e668616 and cb635d7c2, two old #1151 commits (a duplicate and a superseded design), and the
requested-book release filter (upstream's identity gate rejects the same cases). Each item number above is the one the 1.7.4 manifest gave the commit this one was picked from (the
last `cherry picked from` trailer); '#-' marks the stack-only commits, the merges, and the search rework.

The search rework (the request gate and the request-boundary line, 70 commits) is ported onto upstream's scoring
pipeline after the replay. It replaces the stack's own indexer outcome, failure ladder and fan-out design, which is
retired. Its gate is what now protects indexers: three failures open a cooldown; a 429 with a usable Retry-After
holds the indexer for the time it asks, capped at 24 h (60 s when the value is not in the future), and a 429 without
one holds it for 1 h, then 3 h, 6 h, 12 h and 24 h on consecutive rate limits, staying at 24 h after that; at most 2
requests per indexer and 8 overall with 2 s spacing; and a scheduled cycle searches at most 25 books and 300
requests. The stack's query plan is not carried; it comes back after the request-boundary PR
lands.

The merges ("stack: merge #N") bring in #930, #975, #978, #1015, #1017, #1023, #1119 (since merged upstream), #1155 and #1164 in that
order, then #1016, so each merged head is an ancestor of this stack. All of them except #975 and #978 are upstream
PRs, merged at their current heads. #975 and #978 are upstream issues, not PRs, so they have no upstream head: their
fixes are carried from the local/upbase-174-975 and local/upbase-174-978 branches, at the heads the 1.7.4 stack
merged. Where the stack already carried a PR in its own shape, the conflict was resolved as the 1.7.4 stack resolved
it; the "stack:" commits after a merge remove the same duplicates as then, or point the PR's tests at this stack's
search surface. PRs #849, #924, #925, #941, #954, #1012, #1018 and #1151 are carried as content and not
merged, so their heads are not ancestors (#1019 was carried the same way until upstream merged it); the same holds for the fix branch of upstream issue #958
(local/upbase-174-958). Not carried: PR #786 (replaced by the NuGet ffprobe rework, which upstream has since merged as #786), the fix branch of upstream issue
#981 (local/upbase-174-981, conflicts with the search rework), PR #987 (carried by #1016), PR #1152 (superseded by
the search rework).

After the merges come the stack-only fixes. qBittorrent removal sanitizes the client's response body before it logs
a failed presence check or a failed delete: CR/LF become spaces and the body is cut at 500 characters, so it cannot
start a forged log line or flood the log. The image workflow's manifest check counts patches along the first parent,
the way the rows above are listed; it used to count every commit past the base, which overshoots on a stack with
merges (779 against 726 before these fixes) and would have failed the image build. A test pins the companion-file
pass of manual import to its destination boundary: when the destination root lies outside the audiobook's authorized
filesystem boundary, the pass moves nothing and reports failure.

The 1.8.4 stack ends at its manifest commit, "stack: manifest for b635a856c plus 730 patches", which is the row
before the 1.8.5 merge. Upstream canary 1.8.5 (942fe990d) is then merged in ("stack: merge v1.8.5-canary"), so the
1.8.4 stack is an ancestor of this one and every row above it is the 1.8.4 stack's own commit. 1.8.5 brings upstream
#1176 and the version bump. #1176 makes the Search button retry without the author when the full query finds nothing;
upstream's retry calls SearchAsync, while this stack's Search button searches each indexer through the request gate.
The merge ports the retry onto that path the way the automatic sweep does it: only an indexer whose full query
completed with no results is asked again, as a fallback request for the same origin; a failed or deferred indexer is
not asked again, and a fallback the gate held back reports its deferral. As upstream, the retry runs only when the
full query found nothing at all, and its query is the title alone (in the same query form, edition annotations
removed). Upstream's tests for the retry are rewritten on this stack's search path, with cases for the per-indexer
handling added.

The 1.8.5 stack ends at its manifest commit, "stack: manifest for 942fe990d plus 732 patches", which is the row before
the 1.8.6 merge. Upstream canary 1.8.6 (c35b58fe3) is then merged in ("stack: merge v1.8.6-canary"), so the 1.8.5 stack
is an ancestor of this one and every row above it is the 1.8.5 stack's own commit. 1.8.6 brings upstream #1086 (the
frontend search consolidation: the search view, search store and advanced search modal are replaced by one
audiobookSearch service) and the version bump. The merge had no conflicts: no commit of this stack touches the files
#1086 removes or rewrites, and the stack's one change to a file #1086 also edits (the abridged-format check in
AddNewView and AddLibraryModal) merged cleanly.

The 1.8.6 stack ends at its manifest commit, "stack: manifest for c35b58fe3 plus 734 patches", which is the row before
the 1.9.0 merge. Upstream canary 1.9.0 (081c858be) is then merged in ("stack: merge v1.9.0-canary"), so the 1.8.6
stack is an ancestor of this one and every row above it is the 1.8.6 stack's own commit. 1.9.0 brings upstream #1082
(the "Broken books" group in the library: a server-computed list of the reasons a book cannot fetch metadata, and a
group menu entry that shows only those books) and the version bump. It adds no migration. The merge had one conflict,
in AudiobooksView.vue, where this stack's Select All is already scoped to the books shown (and labelled with their
count, and disabled when none is shown); that call covers the broken books group, so #1082's own Select All helper is
not carried, and its test finds the button by the "Select All" prefix because the label carries the count while the
group filters the list.

The 1.9.0 stack ends at its manifest commit, "stack: manifest for 081c858be plus 736 patches", which is the row before
the 1.9.1 merge. Upstream canary 1.9.1 (bfbb0526e) is then merged in ("stack: merge v1.9.1-canary"), so the 1.9.0 stack
is an ancestor of this one and every row above it is the 1.9.0 stack's own commit. 1.9.1 brings upstream #1159 (the
test overhaul: the Listenarr.BackendIntegrationTests and Listenarr.FakeProviders projects, the test mocks moved into
per-category folders and namespaces, the Audible and Audnexus hand-written mocks replaced by fixture files, the
Playwright suites replacing the Cypress ones, and new CI jobs) and the version bump. The merge had five conflicts, all
resolved by keeping both sides: the unit-tests job of run-tests.yml (the packaged-ffprobe check step stays, upstream's
new jobs follow it), the Select All button in AudiobooksView.vue (the stack's wiring, with upstream's test id), the
worker registrations (the stack's, through upstream's AddScanQueueService and AddUnmatchedScanWorker helpers), a
using in DownloadMonitorServiceTests, and the deleted AudibleApiMock, whose author-identity routes this stack had
extended: they are ported to fixture files and a contributors route in the new Audible fixture provider, and the one
test that needs the contributor endpoint to fail adds that failure in front of the stub. The stack's other tests are
pointed at the new mock namespaces, the Playwright API types are regenerated for the stack's API surface, and the new backend integration tests create their quality profile with upgrades off, because the stack refuses a profile whose cutoff names no allowed quality.
fe/eslint-report.json, which upstream deleted, is kept: the leak scanner blocks any commit that removes its lines.

The 1.9.1 stack ends at its manifest commit, "stack: manifest for bfbb0526e plus 741 patches", which is the row before
the 1.9.2 merge. Upstream canary 1.9.2 (537137e99) is then merged in ("stack: merge v1.9.2-canary"), so the 1.9.1 stack
is an ancestor of this one and every row above it is the 1.9.1 stack's own commit. 1.9.2 brings upstream's merged
ffprobe rework (#786: Openur.FFprobeStatic as a per-platform NuGet asset, no runtime download, a -version check on the
resolved binary, a containment check before chmod, licence texts and a publish-time check) and the version bump. The
stack had carried its own earlier copy of that rework as row 0469b8546 (the NuGet ffprobe rework composed onto the stack)
and a macOS ffprobe URL fix (d48745be7) for the download path that rework removed; both rows stay in the history above
and are marked merged upstream, not carried: the merge takes upstream's version of every file they touched, so the tree
holds upstream's #786 and none of the stack's older copy. The merge had fifteen conflicts, all in files of that rework
or beside it. The ffprobe sources, their tests, the documentation and the lock for the API project take upstream's
version; the stack's pieces beside them are kept: the Dockerfile's version build argument (upstream's prepare-ffprobe
step follows it), the MailKit reference in the infrastructure project, the stack's metadata refresh registrations in
MetadataRegistrationExtensions (upstream's TimeProvider registration added), and the API project's lock regenerated for
the stack's packages.

The 1.9.2 stack ends at "stack: restore upstream's StartupConfig comment", the row before the 1.10.3 merge, which
carries its manifest ("537137e99 plus 744 patches"). Upstream canary 1.10.3 (8369487a7) is then merged in ("stack: merge
v1.10.3-canary"), so the 1.9.2 stack is an ancestor of this one and every row above it is the 1.9.2 stack's own commit.
1.10.3 is four releases: 1.10.0 brings #841 (Date Added sorting and filtering, with the migration
20261119022628_AddAudiobookAddedDate and a startup backfill of the date from library history and file records), 1.10.1
brings #1177 (a timeout in a debug broadcast no longer aborts a book's automatic search), 1.10.2 brings our #1019
(indexer query building and result gating) and 1.10.3 brings our #1119 (a posted startup config is merged onto
config.json), each with its version bump. #1019 and #1119 are therefore merged upstream, not carried: their rows above
(the #1019 content of items #46 and #20, the #1119 patches f8c294773 and 379c5abda, and the merge of the #1119 PR head,
8f0eeeb34) stay in the history and are marked so, and the tree holds upstream's merged version of every file they
touched. Where the stack had built on that content (the rejection gates split out of the scorer, one quality ladder,
the music-release gate, the UrlBase check on a posted startup config), the stack's later changes are kept. #1177 is
ported onto this stack's automatic search, which runs in AutomaticSearchProcessor, AutomaticSearchBookSearch and
AutomaticSearchAcquisition: both debug broadcasts now contain every failure except a shutdown of the cycle, and
upstream's tests for it are rewritten on the stack's sweep harness. The new migration sorts after every migration of
this stack, and its Designer is rewritten as the stack's cumulative model at that position (the snapshot's model, plus
the Indexers.Tags column every database still has); the Date Added backfill runs under its own migration gate, like the stack's other startup repairs.

The 1.10.3 stack ends at its manifest commit, "stack: manifest for 8369487a7 plus 746 patches", which is the row before
the 1.10.4 merge. Upstream canary 1.10.4 (e4fe947c9) is then merged in ("stack: merge v1.10.4-canary"), so the 1.10.3
stack is an ancestor of this one and every row above it is the 1.10.3 stack's own commit. 1.10.4 brings the AsyncKeyedLock
update to 8.1.3 (the package version and every lock file), an ignore rule for the fake providers' local launch profile,
and the version bump. It adds no migration and had no conflicts: the stack's own package entries (MailKit and the
System.Security.Cryptography.Pkcs pin) are additive in each lock file and were left as they were.

The 1.10.4 stack ends at its manifest commit, "stack: manifest for e4fe947c9 plus 749 patches", which is the row before
the 1.10.5 merge. Upstream canary 1.10.5 (faf995e8c) is then merged in ("stack: merge v1.10.5-canary (#1203) and
reconcile the stack's download patches"), so the 1.10.4 stack is an ancestor of this one. 1.10.5 is upstream's #1203
(re-grabs after a client-side deletion: guarded client-poll writes, orphan removal that leaves a row the poll changed,
a category-independent lookup of tracked downloads, and a poll that reports only the downloads the client listed)
plus the version bump. It adds no migration. The merge had nine textual conflicts, all in the download pipeline, and
one conflict the merge could not show: the stack's release of a held completion whose client item has gone depended on
the poll returning every download, so it now runs over the polled downloads in its own step before orphan cleanup. The
stack's History "Removed" entry is now written after the guarded delete, and the NZBGet history limit does not apply to
the tracked-presence lookup. The commit after the merge adds the two tests that pin those last two points.

The 1.10.5 stack ends at its manifest commit, "stack: manifest for faf995e8c plus 758 patches", which is the row before
the 1.10.6 merge. Upstream canary 1.10.6 (dc14be034) is then merged in ("Merge upstream v1.10.6-canary (dc14be034) into
the stack"), so the 1.10.5 stack is an ancestor of this one. 1.10.6 is upstream's #1205 (a library add or a monitor needs
a valid quality profile: the requested one, or else the default, and the add or the monitor is refused when neither
exists; monitored authors and series store their profile and add new books with it, can be moved to another profile, and
a profile they use cannot be deleted) plus the version bump. It adds the migration 20261121052354_AddMonitoringQualityProfile
(a nullable QualityProfileId and its index on MonitoredAuthors and MonitoredSeries, no backfill). The merge had five
textual conflicts, each resolved by keeping both sides. The migration's Designer is rewritten as the stack's cumulative
model at that position, as for 20261119022628; its Up and Down are upstream's. The stack's own tests that add or monitor
now seed a default profile, #1205's fixtures that create a profile through the API send upgradeAllowed false (the
stack's cutoff rule refuses a profile that allows upgrades without a cutoff), and the stack's series section on the
author page sends the profile selected on that page instead of relying on the default.

## Known differences from the 1.7.4 stack

- A fresh install stores MaxConcurrentIndexerSearches=0. Rolled back to the 1.7.4 stack, such an install searches
  one indexer at a time. Upgraded installs keep their stored value and are unaffected.
- The six-hour failing-indexer health check is dropped: the gate keeps no run start time. Settings > Indexers has no
  cooldown row and there is no SignalR nudge; the System page card shows each cooldown with its end time and reason.
- Accepted for this bake: the stack's single-failure block, decrement on success, network hold, 401/403
  escalation and the operator fan-out setting are gone. The gate described above replaces them.
- #914 is the older head: migration 20261112054145. Its current head renames the migration to 20261116120000 for
  the same column, so a later upgrade to it adds a second migration and needs its own migration check.
- The ffprobe checksum URL redaction from #1016 has no call site, because the NuGet ffprobe package (now upstream's
  #786) replaced the download path.

Migrations: the newest fork migration is 20261114075422 (the request state table). #1023 stays at 20261112054132,
the fork-local #914 at 20261112054145, and #1151 at 20261114004926. Upstream added no migration between
20261029020400 and 1.9.2 (1.8.5, 1.8.6, 1.9.0, 1.9.1 and 1.9.2 add none); 1.10.0 adds 20261119022628_AddAudiobookAddedDate,
which sorts after every fork migration, so a database on the 1.9.2 stack applies it as the one pending migration. 1.10.6 adds
20261121052354_AddMonitoringQualityProfile, which also sorts after every fork migration, so a database on the 1.10.5 stack
applies it as the one pending migration. The history repoint for a database that ran an earlier stack is recomputed
with this build's upgrade packet.
