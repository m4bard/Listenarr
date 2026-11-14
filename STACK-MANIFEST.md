# Stack manifest

This build is NOT a stock release. It is upstream canary plus unmerged patches.

    base:        e696185ca50a01e07dc9018b3ef5ef6a6c2f5707
    base short:  e696185ca
    patches:     685
    version:     1.7.4+m4bard.685

## Patches, oldest first

    cfd961716  #105  fix(naming): don't lose a series position that isn't a plain number
    86d0df766  #105  fix(naming): derive both series-position fields from one source
    32f42a86d  #106  fix(metadata): populate SeriesAsin from the Audnexus series record
    f0ee0ed4a  #106  fix(metadata): carry the Audnexus series list, with identifiers, through the product lookup
    0f3dad23d  #106  test(metadata): pin what a blank Audnexus series ASIN stores
    e32db8039  #43   fix(ffmpeg): point macOS at evermeet's ffprobe archive, not its ffmpeg one
    7c9d3d515  #23   fix(qbittorrent): one unreadable torrent should not truncate the queue poll
    b04c1f819  #23   fix(qbittorrent): let a client that stops answering fail the poll
    0e8be04c4  #23   fix(qbittorrent): quieten the per-torrent skip and escape the hash
    64ceb97fa  #24   fix(import): actually embed the ASIN after an import
    ed41cdc6e  #24   tests: save and reopen a real m4b for the ASIN writer
    64d69cab5  #24   chore: exempt only the generated m4b fixture from the media ignore rules
    12dd8547c  #24   fix(import): do not write the ASIN tag through a hardlink to the source
    b3efd876a  #24   tests: pin that the hardlink case really shares the source's inode
    8e9898f77  #22   Group chapter files indexed as "N of M" into one unmatched-scan item
    4e5eca078  #22   test(scan): make the bare "N of M" case able to fail
    4c3be10ca  #22   fix(scan): leave a Book or Volume index alone when stripping "N of M"
    5456b5c27  #17   Library import: keep every series membership, not just the first
    60b055099  #17   fix(library-import): build series memberships from the enriched product and fix the unmatched-files mapping
    a6f4aa28d  #17   fix(library-import): require the B0 prefix before keeping a series ASIN
    5be0c6d06  #11   fix(scan): stop the Linux descriptor path leaking into metadata and size
    a9f0e7543  #11   tests: follow the scan's metadata read onto the two-part file source
    4b37bf9f1  #11   fix(scan): narrow to the metadata boundary, leave the size to #901
    c61526be8  #29   fix(download-clients): one circuit breaker per client, not one for all of them
    b0b8e9203  #29   test(download-clients): say what the isolation test does not cover
    4bf4c5652  #42   fix(manual-import): match the rename naming table on casing and missing tokens
    7b0c39470  #42   test(manual-import): assert path segments, not substrings
    b2f160709  #42   fix(manual-import): build every naming key the rename table builds
    2570e6d4e  #32   fix(prowlarr-import): build proxy URLs from the base Prowlarr answered on
    155211d05  #32   fix(prowlarr-import): only adopt a discovery base on the origin that was asked
    1e30d93ec  #34   Serve under a URL sub-path by honouring the UrlBase that already exists
    9fcbe6c41  #34   feat(notifications): give notifications their own ApplicationUrl
    8e622b6e9  #34   feat(notifications): read LISTENARR_PUBLIC_URL as the notification base
    a3a4622f2  #34   fix(api): set the path base from UrlBase instead of appending to it
    73f1205cd  #35   Record a failed grab in history when the download client rejects it
    53e888567  #35   test(downloads): assert the failure record on the blank-identifier path
    84e3bf3b0  #35   fix(downloads): sanitize the client message written to history
    e33ef5c80  #36   fix(metadata): order series positions with an invariant parse
    38c4a2b1e  #36   tests: bring the series-position test up to the convention #717 added
    2e6fe6b1e  #36   fix(metadata): reject a grouped series position instead of misreading it
    eb1741c9e  #36   fix(parsing): pin machine-format number parses to the invariant culture
    10bd6e8db  #36   fix(parsing): match size units and reject non machine-format numbers
    842c0e862  #35   fix(qbittorrent): tell a refused release apart from a failed submission
    1019aa507  #35   test(downloads): pin the controller half of the 409 change
    414f388e0  #35   fix(downloads): answer a refused release with 409 on the manual send path
    fce5e8628  #160  fix(config): overlay a posted startup config onto the file instead of replacing it
    a16f77323  #160  fix(fe): do not post a startup config the settings view never loaded
    b44ed0a1f  #64   feat(settings): add a URL Base field to General settings
    75cdec5d0  #64   feat(settings): tie the URL Base warning to the field it is about
    1bf3f3c93  #64   feat(settings): show an empty URL Base box when the stored value is the site root
    31edc38f5  #64   feat(settings): add an Application URL field beside URL Base
    ae147e272  #64   feat(api): reject a UrlBase that is a full URL
    b6af08f80  #33   fix(audible): tell a failed catalog lookup apart from a confirmed zero-match
    ddc4b8fa0  #33   test(audible): cover the other ways the catalog lookup fails, and the 503 itself
    c29c8b439  #33   test(audible): give the new controller class the repository's test conventions
    831d89171  #33   fix(audible): declare the search endpoint's responses, including the new 503
    53d157031  #25   feat(import): embed cover art into imported files, behind a setting
    64c4d2e38  #25   docs: attach the new doc comments to the members they describe
    efe9dfc55  #25   tests: pin that cover art replaces the existing artwork rather than appending
    3f5480bf7  #25   fix(import): embed cover art for a book that has artwork but no ASIN
    78d176a2b  #25   tests: keep this branch's migration out of the shared expected list
    47e6ba614  #25   refactor: keep the cover art code clear of PR 843's edits to the same file
    03de3b0dd  #25   fix(import): extend the hardlink guard to cover-art embedding, with its test
    34475b6df  #60   fix(filesystem): record why a file mutation failed, not only that it did
    9cdd626e6  #60   test(downloads): name the no-inner-cause case for what it now does
    2f63e8936  #60   refactor(downloads): format the failure cause where both writers can reach it
    0ef5bf408  #61   fix(downloads): make retry-import actually requeue the import
    1b71459d3  #61   fix(downloads): survive two retry requests racing on the same download
    3f94db632  #62   fix(downloads): give the three download-finalization settings a reader again
    9788702b1  #62   test(downloads): pin that the processor reads the configured retry delay
    e23fdf9bd  #62   fix(downloads): stop the retry backoff doubling past a day
    f0b76c6c6  #62   fix(downloads): hold only a first transition into Completed
    6cd06540c  #62   test(downloads): scope the completion-stability save to the one test that needs it
    9ca9ad6b5  #65   fix(quality-profiles): stop hiding Maximum Age, and load a profile's qualities
    53c32265d  #46   fix(search): read Indexer.MinimumAge and Indexer.MaximumSize, and measure age in UTC
    a62c334c3  #46   refactor(search): split the scorer, and test the UTC date parse directly
    9bb0da6d2  #46   test(search): pin that the indexer size ceiling applies to Usenet too
    ee57292de  #46   test(search): make the new parse tests follow the repository conventions
    25837ee9d  #46   fix(scoring): resolve indexers once per batch, not once per result in parallel
    bd5d22a8f  #46   test(scoring): assert the pre-resolved indexer actually reaches the scorer
    20ad78b5a  #46   fix(search): compute the profile size gates in long, not int
    4c76a4533  #47   fix(download-clients): make the Priority dropdown speak the planners' vocabulary
    c49e60bd5  #47   test(download-clients): drop two usings the global usings already cover
    a4939e481  #47   fix(download-clients): read a stored legacy priority back as Normal
    6bd4213a8  #47   fix(download-clients): say what Default does per client, and name Force's band
    1c98e451c  #47   fix(download-clients): lower the priority in the addurl path too
    15e387abb  #44   test(images): assert that a cached image is served, not that something happened
    4a3317a7c  #83   feat(library): render the list view when grouping by author or series
    10234cda0  #83   fix(library): fetch author covers and remember the mode in the grouped list
    94fa077ee  #45   fix(qbittorrent): send the four Advanced Settings to the client
    9fcc05f18  #45   fix(qbittorrent): do not fail a submission the client already accepted
    40a53fe1b  #68   fix(filesystem): say why a source file could not be pinned
    5bc71971b  #68   fix(filesystem): sanitize the pinning cause before it reaches the log
    15f063ed9  #68   test(filesystem): fail the refusal test when no cause is captured
    88205518e  #68   fix(filesystem): lead the refusal with the cause, through the shared formatter
    1f0654faf  #68   fix(filesystem): do not blame a configured root's link for an unrelated refusal
    05f320826  #68   fix(filesystem): name no link when the source overlaps an unavailable root
    0e965618a  #68   fix(filesystem): name the outermost link as a possible cause, not a certain one
    758602871  #68   test(filesystem): make the nested-link guard able to fail
    1dad28238  #72   fix(imports): retry a failed file import instead of blocking on the first attempt
    88f9319ea  #72   fix(imports): retry a failed import only when every file in it failed
    7308213bc  #80   fix(quality): let a real encoder bitrate reach the rung it was encoded for
    e4a60f089  #80   test(quality): pin the bitrate tolerance from both sides, not just the floor
    8948c0ec3  #102  fix(docker): give the image a healthcheck it can actually run
    2030c5b1b  #31   fix(notifications): dispatch webhooks for the triggers the settings screen offers
    3ca4c32c2  #49   Make the frontend follow the UrlBase by injecting a <base href> into the shell
    eeb7327ac  #49   fix(spa): anchor the shell at the site root even with no UrlBase
    fdea1b7ec  #109  stack: let a local build stamp its own version
    6a38579e1  #109  stack: publish the patched build to GHCR from the fork
    509d5c4c5  #109  stack: publish a moving current tag beside the immutable commit tag
    8dbfe2423  #108  fix(files): physical-generation snapshots reject database-loaded observation timestamps
    44b78f053  #108  fix(persistence): restore the UTC contract for PhysicalIdentityObservedAtUtc at EF materialization
    7bba06823  #73   feat(wanted): add a cutoff-unmet bucket to the Wanted page
    f21e89f61  #74   fix(collection): show an author's books under their membership series
    c4ecacd13  #75   feat(library): count the distinct series an author appears in
    6d8ad7ef1  #76   feat(downloads): implement the three reprocess endpoints instead of returning success
    eea23b873  #76   fix(downloads): refuse an ineligible reprocess instead of throwing at the caller
    83fae5141  #76   fix(downloads): drop the unused using and the seam-adjacent whitespace
    67fb2d213  #83   feat(activity): sort the queue, and show when an item was added
    c5725535a  #86   fix(collection): apply the language preference to library books, not just suggestions
    c2b797161  #94   fix(search): the minimum seeders gate never fired on a real torrent
    571fa673c  #110  fix(import): match blacklisted extensions without regard to case
    70e2c9719  #112  fix(ui): match System recent-log severity classes to the stylesheet
    bc2fef1c4  #20   fix(search): derive one indexer query title, shared by both search paths
    88d320a9a  #20   fix(search): leave the series out of the indexer query, as Readarr does
    11a09c495  #66   fix(downloads): give the queue a control that removes a terminal download
    5a19f38a1  #66   fix(downloads): tie a processing job's retention to the download it explains
    a9b2cb4e1  #99   fix(authors): stop attributing one author's ASIN to every co-author
    ef7c2a015  #100  fix(naming): spell an author's initials one way in folder names
    3986a51b9  #100  test(naming): pin byline order on two co-authors, not on a translator
    313bf3a82  #113  fix(system): stop inventing log entries when no log file exists
    2143b9359  #114  fix(library): stop announcing the list-view status badge as a button
    8680db002  #115  fix(settings): stop a failed clipboard write from reporting a failed regeneration
    a9ee1dfd8  #95   fix(api): the authentication gate ignored requests that varied the path casing
    24c2257e4  #68   fix(filesystem): resolve a symlinked source path instead of refusing it
    3b6ebb6ed  #68   docs(filesystem): stop two comments claiming what the symlink commit retracted
    6cfa02c45  #98   fix(logging): give silent catch blocks a real log call or a stated reason
    4d6e136a1  #108  fix(persistence): keep a persisted UTC timestamp UTC when it is read back
    4f82dc007  #78   test(scoring): pin the four hand-copied quality ladders to each other
    73b5a2cdb  #69   test(metadata): count the attempts the Audible retry policy actually makes
    9cb9168c4  #69   test(metadata): stop the retry policy tests racing the caller's clock
    a87bde606  #136  NZBGet: warn once per failed history entry, and only for monitored entries in polls
    734c5d1b9  #136  fix(nzbget): keep the warn-once keys case-insensitive after a scoped read
    a63940593  #92   refactor(collection): lift shared series and text helpers out of the collection view
    c4d159113  #92   feat(collection): show an author's series as a section on the author page
    8eca9791b  #141  fix(monitoring): keep every series membership and its ASIN when a monitored author or series adds a book
    c5acdd690  #141  fix(library): let a legacy-only metadata payload carry its series identifier
    4fd1e7b9f  #141  fix(monitoring): keep every series and its ASIN through the author catalog cache
    3ff536402  #141  test(monitoring): use catalog books with real identifiers in the series fixtures
    b9bc81f17  #141  fix(monitoring): refetch an author catalog cached before series entries existed
    5a85652ab  #145  feat(activity): queue selection that survives the poll instead of being reset by it
    ffc45ae33  #145  feat(activity): sequential bulk runner with per-id outcomes and one summary line
    0dc6c6c01  #145  feat(api): keep the queue-management API tests, drop the methods #917 and #927 already added
    630eb5d96  #145  feat(activity): a queue toolbar whose verbs say what they will do before doing it
    357533783  #145  fix(activity): the select cell reasserts its props after a click that changed nothing
    261450b06  #145  feat(downloads): a Retry button that appears only where the endpoint will accept it
    0936f510c  #145  feat(activity): select queue rows and act on them together
    b23a99747  #145  fix(activity): the filter is a viewport, so selection controls act on the rows in view
    f46f1add9  #145  feat(downloads): the Retry button does the retry, instead of promising one later
    069d5c45c  #145  fix(activity): the retry button dresses itself and select all goes quiet with nothing to select
    c2ea0a655  #145  fix(activity): the toolbar drops a confirmation the selection outlived and rests while busy
    81bc6205d  #145  fix(queue): overlay import status onto queue rows the client cannot represent
    cca37d4c2  #145  fix(activity): disclose the true count before an unbounded sweep
    d41f85adb  #144  metadata: name the two nothings a provider can answer with
    c80d30452  #144  metadata: answer the ASIN endpoints with a status, not a stack trace
    c6e025d90  #144  images: keep serving the placeholder when the provider does not answer
    a6bed9a84  #144  search: move the Audible series endpoints into a partial
    481f02cf2  #144  search: say the provider did not answer, rather than returning nothing
    0c0476de0  #144  metadata: walk every source, then report the fault rather than a miss
    add464716  #144  metadata: let the Audible client raise when the provider did not answer
    58a2b7853  #144  metadata: hold the Audnexus book lookup to the same division
    4c7a50361  #144  library: record when a book's provider metadata was last refreshed
    644fc038b  #144  settings: the refresh interval, staleness age and budget are operator-set
    884af93f3  #144  metadata: a request budget with an hourly refill and a spacing floor
    36bb9c29c  #144  metadata: lift the per-book rescan out of its HTTP wrapper
    1a7cdf988  #144  metadata: one gate, one run, one budget across every refresh entry point
    dcdf31719  #144  metadata: walk the stalest books on an interval, and let an operator ask
    955b4e6ca  #144  fe: a Metadata Refresh section in the settings screen
    a165cfb33  #144  metadata: provider 503s answer as a ProblemDetails the filter keeps
    1ae978219  #151  fix(logging): sanitize ASIN log arguments, rebased onto the metadata-refresh chain
    5c9f76fb3  #153  Attach download history rows to the audiobook they belong to
    ee64e5a1e  #153  fix(history): record the protocol a download actually used
    f4681d0f3  #153  fix(history): resolve the protocol for the remaining event types too
    1c982c309  #153  test(history): cover the audiobook key and the protocol on one call
    43988d9c4  #154  test(downloads): match the int? audiobook id in the grab history verifies
    570b101e0  #155  fix(imports): classify failures at the boundary instead of leaking them to History
    31d6253fb  #156  fix(naming): key the remaining naming tables case insensitively
    f9168c00e  #158  fix(qbittorrent): a 200 carrying "Fails." is a refusal, not a grab
    8cd99b425  #161  Record a download client timeout as a failed grab, not a shutdown
    456718eef  #163  fix(notifications): sanitize webhook URLs before logging them
    9caa57a77  #163  test(notifications): cover SanitizeWebhookUrl and pin the webhook log call sites
    03c466548  #166  feat(search): tell an indexer that answered with nothing apart from one that never answered
    9cc1af2f8  #166  fix(search): give the per-indexer timeout its own catch, correctly typed
    626645677  #166  test(search): match the fake provider to the observation-returning interface
    9725755b6  #166  search: isolate a per-indexer HTTP timeout from the multi-indexer fan-out
    0327c25dc  #166  search: bound indexer fan-out concurrency to 4 (tracker #119)
    ef6d004ab  #166  search: separate a rate limit and a refused credential from any other non-2xx
    26c65511f  #166  search: persist a per-indexer failure backoff ladder
    d83fecaa7  #166  search: skip an indexer that is in failure backoff, and record what each one answered
    0b49bcc35  #166  search: do not stamp LastSearchTime on a book whose indexer set was degraded
    0d2523eeb  #166  indexers: show on the card when an indexer is in failure backoff
    6d0bbb4e9  #166  feat(search): try a second form of a search before saying the book does not exist
    a91a180b6  #166  search: cover what the query ladder and indexer backoff do to each other
    e4610f2df  #166  search: drop the bare series-name query rung
    d8c144db4  #166  test(search): thread the cancellation token through item 107's fake provider
    6ce37afaf  #172  172: fix Abridged substring match stamping unabridged books abridged
    ce37a9e60  #172  172: fix Abridged substring match on the frontend too
    064fb8be6  #173  173c: expose the Listenarr.Backend meter through a listener and an endpoint
    a990dca52  #170  search: make the indexer fan-out ceiling an operator setting, shipped at 4
    2d57afe55  #170  settings: expose the indexer search concurrency ceiling in Search Settings
    51c09d81f  #123  notifications(#126): make EnableNotifications actually gate delivery
    7516995ea  #123  notifications(#127): dispatch webhooks by stored Type, not URL sniffing
    7a94b6ece  #126  notifications(#126): drop EnableNotifications, matching the family
    281bf24d6  #123  search(#123): wire up EnableAmazonSearch/EnableAudibleSearch, dead since introduction
    4a8cc06d1  #123  notifications(#128): remove the unreachable legacy /notifications/test endpoint
    183a298bb  #123  settings(#131): add a UI control for ExtractArchives, backend-only until now
    6e2944c31  #123  downloads(#132): fix RemoveCompletedDownloads for Transmission, SABnzbd, NZBGet
    b1b916b7b  #124  fix: wire AllowedFileExtensions into FileUtils.IsAudioFile
    d04a8e74a  #124  cleanup: remove two orphaned private AudioExtensions arrays
    3129a9e19  #129  Remove vestigial Indexer.Tags field
    7ab5c8558  #133  history: add retention setting UI and a daily cleanup worker
    b5dc78e14  #138  Preserve series ASIN when mapping monitored author/series books
    6bdd04e1e  #138  test(monitoring): resolve the series-monitoring root through identity, not a raw builder
    3b0f66de4  #199  Fix author name normalization to merge spaced-out initials
    ba3d0fcd0  #199  test(domain): bring StringUtilsNormalizeAuthorNameTests up to convention
    8feaac05d  #199  fix(authors): dedupe author-cache methods reintroduced against fix/72's own copy
    4d3d93fba  #151  fix(authors): skip the SQL narrowing pre-filter for non-ASCII author names
    4edf1116e  #151  fix: complete the NormalizeAuthorName redirect this branch left half-done
    88c8c31b2  #201  Finish the author-normalizer consolidation and re-derive the keys it left behind
    b9ea23ab9  #201  Keep the canonical author spelling the ASIN lookup already returned
    34f50ecd1  #201  Converge existing books onto one spelling per author at startup
    492433377  #201  fix(library): normalize author grouping and include every co-author
    d14d211fe  #200  Pin the author-identity behaviour these fixes are meant to change
    fa0b0ec5b  #200  Stop reading author identity out of a list that never recorded it
    81cfb8a46  #200  Refuse to rename a cached author when an ASIN is already somebody else's
    f18508e20  #200  Confirm an author ASIN with Audible before binding it as an identity
    95715e673  #200  Pin what stops a renamed cache row reaching stored author names
    bb574710c  #200  fix(authors): dedupe author-cache methods a third time, keeping the collision-safe version
    d838ccc5b  #122  search: add ReleaseShapeDetector, which tells a bundle from a single edition
    f23317038  #122  search: a per-profile preference between bundle and single-book releases
    2a57b043c  #122  fe: the release shape control, and a Bundle badge on search results
    99ef108f0  #172  fix(search): word-boundary word filters, and any-of required words
    2827aef2b  #172  fix(search): route PreferredWords through the same word-boundary matcher
    fff29a617  #172  fix(search): make indexer priority a genuine tie-break instead of dead weight or dominant term
    3efbe6e7c  #172  search: give the word-boundary term matcher its own file
    e1cb97729  #172  search: pin the precedence between the three scorer concerns
    cc90872bf  #173  fix(system): stop fabricating download-client connectivity
    2402e2230  #173  tests(system-service-logs): pass the download-client status cache the constructor now requires
    7ed2d332b  #169  fix(nav): dismiss the header search panel when the route changes
    6857af93e  #169  fix(library): Select All reached past the filter into the whole library
    cc1634b8b  #165  feat(detail): add Automatic Search action to audiobook detail toolbar
    dec3f1dd4  #165  feat(library): per-item automatic search icon on the audiobooks list
    a1af14b1d  #165  feat(library): bulk automatic search on the audiobooks index toolbar
    389e760bb  #165  feat(calendar): search for the missing books in the window on screen
    b2359ac85  #202  feat(ui): multi-row selection on Wanted and Downloads
    15a6b30eb  #289  tests: catch up DownloadsView.selection and WantedView.selection with items 73, 169 and 145
    342d4acb8  #174  fix(history): style the events that happen, not four that cannot
    ad61854d7  #174  fix(history): keep what a grab knew, and name the download client
    2ba4216d2  #174  feat(history): a global History page over the API that already existed
    b424b7ef7  #174  tests(download-service): match RecordDownloadFailedAsync's protocol parameter in the sanitize test
    08a0cad14  #209  fix(security): an IPv4 address in its mapped form never matched the private ranges
    b6f077e83  #190  fix(myanonamouse): spend the freeleech wedge on the download, not the search
    52c8d2f7b  #190  refactor(search): give SearchResultConverters its own file
    67dbf2667  #190  feat(indexers): parse the release flags trackers advertise
    626a5b4b4  #183  Add a task surface over the periodic workers, with a manual trigger
    115bb47cf  #183  scheduled tasks: an allowlist for manual runs, not a deny-list
    1574c2014  #183  scheduled tasks: answer the trigger with the row it started, and keep stopped workers
    d7e8519d7  #183  scheduled tasks: say which request started the cycle, and stop 404ing a listed task
    ad3d5f03f  #183  scheduled tasks: release the gate under the lock that ends the cycle
    1e9714792  #183  scheduled tasks: publish the interval in whole seconds, and refuse what will not fit
    ed4426904  #183  scheduled tasks: put the interval refusal on the row, and correct two claims made for it
    dab74383c  #183  scheduled tasks: make the interval refusal audible, and stop overclaiming what it buys
    955b22c3f  #183  tasks ui: put the task surface under System, where the family keeps it
    c2c6f03ae  #183  tasks ui: test the run control against the refusals the API actually answers
    113aea2b3  #183  tasks ui: fix two in-flight defects, make the refusal reachable, and cover the poll
    7ee21e424  #183  housekeeping: the frame for a daily retention sweep, with no table in it yet
    ba8dc31f4  #183  housekeeping: three tables, and the two that were left out on purpose
    ce87cd679  #183  housekeeping: move jobs and their three child tables, with the cascade measured
    472dced6b  #183  housekeeping: the supersede that never stamped its terminal timestamp, and four comments that overclaimed
    bb2dc1206  #285  settings: a Housekeeping section for the two switches item 183 shipped API-only
    65e87420c  #281  housekeeping: the two deferred populations, and the one slice of each that is provably unreachable
    47816c4a6  #281  housekeeping: say what the compatibility sweep's query actually costs
    be2dd534a  #185  notifications: a subscriber model and a Custom Script provider
    1dc925a3f  #185  configuration: keep stored custom scripts when a save omits them
    0c67649e6  #185  notifications: let the EnableNotifications switch gate the subscriber fan-out
    00ae39328  #189  feat(calendar): add the calendar endpoint and iCalendar feed
    0194d9b99  #189  feat(calendar): give the calendar page a feed subscribe URL
    22c01a7b0  #189  fix(calendar): correct the status precedence and test the two seams
    256e87497  #189  fix(calendar): accept Readarr's tag spelling, and test the rest of the review
    e6afdada4  #189  fix(calendar): close the rest of the parse/query seam, and stop citing what cannot be opened
    608960058  #189  test(calendar): the case-variant feed is refused by the enforcer now, not the attribute
    3a5160f2b  #189  feat(calendar): resolve per-event status in Readarr/Sonarr precedence order
    9e30587a0  #189  feat(calendar): add a first-day-of-week preference, defaulted from locale
    4f51afc18  #189  feat(calendar): add a legend component for the event status colours
    88429583b  #189  feat(calendar): colour events by status, add a legend, and a week-start option
    f164b5ea1  #189  test(calendar): reconcile search-missing spec with item 189's status classes
    887f4234e  #79   fix(status): stop reporting a non-preferred format as below cutoff
    81b2472c1  #79   test(status): make the agreement test read the real projection, and pin the widened cases
    016a59390  #252  WIP fix(quality): a blank cutoff means satisfied, not "search forever"
    8052e7015  #252  test: bring the new cutoff tests up to the repository's test conventions
    218953a3a  #252  test: flag the blank-cutoff consequence for the not-yet-landed upgrades flag
    d314aa734  #257  fix(quality): resolve the cutoff through QualityMatcher, changing two answers
    853ef0d6d  #254  torznab: a bitrate has to be its own token, not digits inside one
    044ac3e16  #254  torznab: tests for the x264 mislabel, each with its control
    051436cb0  #254  torznab: a number is a bitrate only where the text says it is
    263c90657  #254  torznab: recover the bracketed bitrate the tightening had lost
    15eb67685  #255  fix(search): let a quality profile's Allowed flag actually gate
    5784bbb8e  #255  fix(search): correct the claims around the quality gate, and pin what it does not decide
    ae304d125  #255  fix(search): refuse a quality label the gate cannot place
    97574fe20  #255  fix(search): answer the review of the quality gate fix
    d2dd68b90  #253  Break release selection ties deterministically instead of by indexer order
    e80c039bb  #253  Make the consuming-site tests actually discriminate their own site
    acbd6cdab  #253  Restore Readarr's protocol step, without which the chain is not transitive
    0bb3a8f73  #253  Close the second intransitivity, and three more review findings
    b3cdb8bd1  #253  Rank once per site instead of twice, to stay inside the focus budget
    7b0dacab8  #253  Put the same-group invariant in the code, and close the last apparatus gap
    938bbeb70  #256  quality profile: refuse a cutoff the profile does not allow
    24f1276d2  #256  tests: cover the cutoff rule at the API and at the attribute
    4a6cdea58  #256  quality profile: make the cutoff rule agree with the code that reads it
    a74363001  #256  quality profile: give it the UpgradeAllowed flag the family has
    071955ad4  #256  quality profile: carry upgrades-off across the upgrade, in a startup repair
    c7848fdfe  #256  tests: pin both directions of the upgrade flag, each against its control
    aa717ec00  #256  status evaluator: drop the upgrades-off guard, it was never the reason
    0e52bb0b9  #256  tests: pin the new migration id, the backfill gate depends on it
    3589cf210  #256  quality profile: decide "blank cutoff" the way the readers decide it
    f90f570b7  #256  comments: correct three claims a reviewer could check and find false
    db9c128d5  #261  search: skip the bare-title rung when the title cannot carry a query alone
    ed4486ef1  #261  search: stop the stem and series rungs unanchoring themselves when there is no author
    23bba3685  #262  Require indexer categories, the way the rest of the *arr family does
    b5e414097  #262  tests: pin that the draft test is gated too, since it binds the same entity
    29ff969ef  #262  Make the indexer form usable now that categories are required
    6bda22dda  #262  Move the category default off the entity, where it rewrote stored values
    e7244628b  #262  tests: pin that an explicit null category list is not a way past the rule
    1376cd230  #262  Validate the merged indexer on update, not just the request body
    96e2bf8c0  #190  fix(myanonamouse): send the torrent filter as tor[searchType]
    985bfc219  #190  fix(myanonamouse): send the page size as perpage, not tor[perpage]
    1e1a8a799  #190  fix(myanonamouse): send the language the caller asked for
    9745b4070  #190  fix(myanonamouse): stop asking the search for a freeleech wedge
    fbe0949d0  #268  import: give each companion file the source root it actually came from
    a642d4a34  #268  import: resolve the companion source roots once per batch, not per file
    1e73f5e23  #269  manual-import: place companions by the selected files' structure, not the caller's path
    8a76b1daa  #269  manual-import: a companion goes where the file it accompanies went, and nowhere else
    c13b7ad64  #269  import: match the companion's neighbour by path identity, and name the rule the manual path uses
    8b554fb59  #269  import: say why the neighbour comparison is safe for the reason that is actually true
    a1deba859  #21   tests: give the companion placement pass the root folder this branch now requires
    bb2036539  #269  fix(manual-import): log the companion files TopDirectoryOnly drops silently
    e2443bd24  #269  fix(manual-import): guard the nested-companion scan against a hostile tree
    eec308f0b  #273  cache: make the author and series cache upserts survive a lost race
    5d7a8cd41  #273  cache: do not retry a collision that re-reading cannot resolve
    6f7816435  #273  cache: the author upsert cannot reach that collision on this build, so pin what it does instead
    c8c9ac0ed  #273  cache: say only once that a rebind was refused, and describe the collision that can actually happen
    57b543b85  #273  cache: a refusal that first happens on a retry must still be reported
    a4bce5192  #273  cache: the comment on the test still described the guard that was removed
    ff16f25bb  #275  fix(metadata): spell a contributor's name without their job title
    d2489c922  #275  fix(metadata): store the author first, and keep an edition out of the byline
    8abe9e15f  #275  fix(metadata): strip contributor role suffixes on rescan too
    044153626  #275  test(metadata): use catalogue values for contributor role test data
    0f267086b  #275  fix(metadata): finish the contributor role cleanup on scan, dedupe and the singular author
    77564a077  #275  test(metadata): cite catalogue credits as the catalogue lists them
    e40376a0a  #275  fix(metadata): keep matching an author folder that still carries its role
    56e445c42  #276  fix(metadata): bind an author's ASIN only to a row that names them
    d72cedcb8  #276  metadata: lease the shared request budget to callers that are not a refresh run
    7b62003ff  #276  repair: a scheduled pass that corrects author identities already written
    98d803250  #276  repair: remove contributor roles from the credits already stored
    39bfb8698  #276  test: give the two new test classes the conventions the suite enforces
    4fc4cd0f8  #276  repair: a provider that did not answer is not evidence that an author has no ASIN
    df6070432  #276  repair: answer the fresh review, including a starvation defect nothing was checking
    23c651936  #270  filesystem: let a raced pinned hardlink fall back to a copy, and say so
    5d18bd876  #270  filesystem: narrow the hardlink race fallback, because the first attempt claimed too much
    103d4e7a3  #277  Fix the fail-open identifier check in DownloadRemovalWorkflow
    bba3efa4a  #277  Route the downloads delete endpoints through the client removal workflow
    b1b9bc424  #277  Add tests for the client-removing delete and the identifier check
    21f4b20f5  #277  Make a disabled client deletable again, and fail closed when no client id is known
    fd741a0fe  #277  Correct the outer-catch mechanism described in a test comment
    6899cc565  #277  Cover the disabled client, the unmapped record, and deleteFiles on the delete path
    16d05c2c2  #277  Let the delete control say whether the download client is involved
    9ba43f6da  #277  Make the record-only way out actually work, and stop it claiming too much
    814d00f84  #277  qbittorrent: check presence before delete, fail closed on idempotent-success
    05e199ef1  #277  qbittorrent: cover the unparseable-presence-check-body fail-closed path
    d7768c352  #277  qbittorrent: treat a torrent confirmed absent from the client as removed
    71c0ace4c  #277  tests: give the qBittorrent cleanup composition tests their Name and Category traits
    e535bf362  #277  fe(activity): the bulk remove path also defaulted to removing from the client
    9202e4470  #276  fe: an Author Identity Repair section in the settings screen
    a7891f1b0  #276  fe: the preview card promised a run button the frontend does not have
    886e3f3ab  #276  fe: the review's findings, one of which the UI could not have shown
    695455368  #278  notifications: give the Custom Script provider a settings surface
    6a4955cb4  #286  notifications: an Email provider, the case a webhook type cannot express
    1f0de1294  #286  notifications: give the Email provider a settings surface
    34145fc63  #286  notifications: assert both providers are actually wired into the container
    66f07b88f  #286  notifications: act on review of the Email provider
    204e0a8e1  #286  notifications: cover the sentinel guard on a database with no settings row
    1b7062247  #286  configuration: move the email members into their own partial
    f68818376  #286  notifications: exercise the SMTP transport against a socket, not a mock
    fef27cad7  #286  notifications: act on the second review of the Email provider
    6d8688d31  #286  configuration: split startup config out of ConfigurationService, so the file is under the cap again
    17f116be1  #177  feat(download-clients): one selector, with a priority the operator sets
    b41125215  #177  test(download-clients): pin the selection policy and the migration default
    1008be4fa  #177  settings(download-clients): a Client Priority control, so the field is reachable
    4dc26e15f  #177  test(download-clients): pin the rotation cursor's lifetime, with a control that moves
    acfb5d6ef  #177  fix(download-clients): four things a fresh review found
    6bd1fcefc  #177  settings(download-clients): give Client Priority its own section, keep queue priority usenet-only
    2fd2846b6  #177  test(indexers): pin per-indexer download client binding, failing first
    c7955b640  #177  indexers: give an indexer a nullable download client binding (shape only)
    b74e2e197  #177  indexers: route a bound indexer's grabs to its client, and fail loudly
    ffbba02d2  #177  test(download-clients): pin persisted client failure status, failing first
    88c5286e1  #177  download-clients: persisted client failure status, shape only
    3139f8a73  #177  download-clients: record client failures, back off, and route around a blocked client
    00893e026  #177  download-clients: record status in a gateway subclass, not a wrapper
    fe3c6caeb  #177  stack: make item 177's two migration Designers the cumulative model at their position
    f56ea9dcf  #179  backup: take a copy of the database before migrations run
    9ccf1614c  #179  backup: the settings column, and two defects the suite caught
    4465b177d  #179  backup: tests, each paired with a case that comes out differently
    3e73c0365  #179  backup: the operator surface, so this is reachable without a shell
    c69ed5335  #179  docs: describe the backups directory and the startup override
    0e71ca2e5  #179  backup: take the copy before the startup path's own repair writes
    d94d4dacf  #179  backup: cover what the review found had no test
    6dfbb720f  #179  wip: retarget the auth test
    0fb405d64  #179  backup: close the gaps the review found in the code and in its tests
    6188d8334  #179  backup: fix what the second review found, and rethink the manual limit
    b6d5bb850  #179  backup: pin the negative retention case
    f3b0e9ec8  #180  feat(indexers): per-indexer seed ratio and seed time
    65fb3dd96  #180  fix(indexers): close review findings on seed criteria
    e3bffde8c  #181  search: the release score has no ceiling, so a preference can still decide
    27a5976d9  #181  tests: SearchResultScorerCeilingTests follows the repository test conventions
    a652d52ed  #181  settings: the minimum score threshold is no longer capped at 100
    eb7475ecb  #181  search: correct what this branch claims, after review
    e191003db  #264  search: the profile's size and quality gates apply to Usenet results too
    2158903c5  #264  search: size bounds above 2047 MB, and three corrections to the commit before this
    78789c2d0  #264  search: reconcile the NZB gate hoist with the tests that pinned the exemption
    aa51e8084  #263  search: release selection reads the quality profile, not the hardcoded ladder
    92ac81ba9  #263  search: take the best candidate that is an upgrade, and fix two things in the commit before this
    b773df7d6  #181  search: split the rejection gates out of the scorer, so the file is under the cap again
    625702d30  #181  search: two warnings, one of them a control this stack no longer has
    bb836b4c2  #182  naming(#182): render the settings naming preview through the real backend renderer
    84d01bb92  #182  naming(#182): wire the settings naming preview to the backend renderer
    affb42dc5  #182  naming(#182): fix real divergences a fresh review found from GenerateFilePathAsync
    746e9fbc6  #182  naming(#182): flip the lowercase-token preview test now that the case fix is in the tree
    8cb7dae16  #184  tests(notifications): pin the item-184 gaps blocked on PR #943 (WIP)
    ba7cdf9b6  #184  tests(notifications): tighten item-184 gap pins after fresh review
    ade2e9e79  #187  feat(#187): free-space check before import (G7)
    5c84edaa5  #187  feat(#187): free-space on root folders (G9), tests, and settings UI (G7/G9)
    03d509a58  #187  fix(#187): the free-space guard silently never fired under the default naming pattern
    e71152d78  #187  test(#187): prove required bytes are the real file size, not a stand-in like #821's 64
    0794edfe8  #284  Add failing test for metadata repair guard missing Entries include
    191e1389d  #284  Fix metadata repair guard to load MoveJob Entries and CreatedDirectories
    142614e46  #283  tests: add failing control for ProcessExecutionLogs table drop
    032c0cdd1  #283  Drop the dead ProcessExecutionLogs store, repository, entity, and table
    50ef495c5  #237  fix(search): reconcile the query ladder with fakes and tests from three unrelated items
    28b7aa0ec  #237  test(startup): mock the metadata-refresh backfill this test's strict repository was missing
    661ef4db6  #237  style: run prettier over item 201's two touched files
    8c7c80c47  #237  fix(search): reconcile the scorer 3-way with fakes and tests from unrelated items, plus two more real bugs found by the suite
    5215627b5  #237  stack: reconcile the manual-run allowlist with the stacked tree
    334b8255e  #237  tests: read the observation's results, the way its sibling case already does
    49212b746  #237  tests: point the tiebreak harness at the signatures the stack actually has
    bcc53dc6a  #237  tests: move the blank-cutoff pin to the answer fix/blank-cutoff-search-loop gave it
    da4d89496  #237  downloads: split the client-name projection out, so the controller is under the ceiling again
    8c5b55e90  #237  tests: guard that every migration Designer is its predecessor plus its own Up
    4214259fd  #237  stack: make every composed migration Designer the cumulative model at its position
    6dccd141e  #212  tests: pin the no-audio-candidate import block from #890
    d095f4cc8  #212  fix(import): admit audio-only .mp4 via a probed ambiguous-container tier
    b764632e4  #212  fix(import): let manual import admit an audio-only .mp4
    d01c6368c  #212  tests: move the #890 cases out of a file PR 993 is rewriting
    a0d32fcf4  #212  refactor(files): move the content gate into its own partial, so it fits beside #901
    fa1c2840e  #191  qbittorrent: failing spec for postImportCategory form field
    2fee1f802  #191  qbittorrent: add Post-Import Category field to the client form
    9781a07ad  #288  symlink-chain: fail-first tests for a two-or-more-hop chain
    c97f66f88  #288  symlink-chain: walk the whole chain, not just the last segment
    55e251cbc  #237  stack: make the three Designers after item 170's migration the cumulative model again
    78e6bbcdb  #293  fix: grid containers shrink to content instead of forcing viewport height
    2f167d25c  #293  fix(activity): virtual row stride must not be shorter than the rendered row
    cec43409e  #293  fix(activity,wanted): measure the virtual row stride from a rendered row
    f54ccb262  #279  metadata-refresh: allow the scheduled walk onto the task surface's Run button
    f16fb0395  #146  manual-import: reject items missing a matched audiobook id with 400
    ce4ff8af2  #146  tests: follow the repository test-class conventions
    84416a3c7  #215  fix(import): refuse a manual import on its extension before publishing it
    11edda49e  #215  fix(import): say that extensionless sources are refused too, and pin it
    c1c737808  #292  search: WIP failing tests for the requested-book release check
    f8028c8e2  #292  search: drop releases that are plainly for a different book before an auto-grab
    81548479c  #292  test(search): give the blocklist wiring fixture a release for its own book
    8b777e4dd  #292  search: fix the requested-book check's author and short-title rules from review
    09966f02b  #292  search: a bare number after the title is a different volume, and only a year is packaging
    b56e33325  #310  test(search): series-entry cases for a release honestly titled for a different volume
    a7c1a1a96  #310  search: reject a release whose own series position disagrees with the catalog's
    43f25d6e1  #310  test(search): a release honestly for a fractional series entry, caught in review
    d46036848  #310  search: parse a release's fractional series position instead of dropping it
    30b8ebe9e  #310  test(search): pin the thousands-separator exclusion on the series-position parse
    2bef24840  #356  test(search): pin item 356's whole-number-only series-position tolerance
    639a96672  #356  search: fail open on series-position comparison when either side is fractional
    799583cfb  #139  fix(library): identify a series by its SeriesAsin, not its display name
    fafdfcea3  #139  fix(library): series identity fixes from review: non-Latin names, author guard, stale links
    12a2c3bb1  #139  fix(library): series author guard joins on a shared author, script-preserving
    fb24fe1b7  #291  fix(search): give the indexer backoff write a context of its own
    c57f06b2f  #291  test(search): assert which context the backoff write used, not whether it collided
    18fdbcec1  #215  import: move the tag enrichment out of ProcessItem, so the file is under the cap again
    d086a9163  #295  health: report indexers in failure backoff, and separately those failing for more than six hours
    ed1d804e2  #295  search: tell an indexer that could not be asked from one that had nothing
    94520f4f8  #295  ui: count indexer health checks on the sidebar's System entry
    e3ee771e9  #295  search: no default for the outcome search, and tidier could-not-search reasons
    a8cc992cb  #295  ui: show the System health badge with authentication off, and re-read it every minute
    3a855086a  #81   settings: add a stalled-download timeout, off by default
    4f0a1bb33  #81   downloads: fail a torrent that has made no progress for the stalled-download timeout
    467152362  #81   ui: stalled download timeout field under failed download handling
    e98a5b5de  #81   test: a pause shorter than the gap cap still clears the stall clock
    c03824262  #223  search: one quality ladder instead of four copies
    a375a8166  #223  test(scoring): pin the ladder's rung order and every rung literal
    291c7eed2  #63   search: make Prefer newer releases a real ranking bonus
    623ecf74f  #63   ui: say what Prefer newer releases now does, and pin Maximum Age across a toggle
    5c767d7ee  #63   search: cap the newer-release bonus below a preferred word, and keep it off rejected releases
    c99a437fd  #63   ui: describe the smaller newer-release bonus, and check Maximum Age is visible
    25d1232d5  #63   search: default PreferNewerReleases to false, matching the settings modal
    ae9f810e6  #218  fix(scan): report a scan whose files were all declined as CompletedNoFilesAccepted
    a9727e5a7  #218  fix(ui): show a scan that accepted no files as its own outcome
    4f5f2123e  #218  fix(scan): record a diagnostic when a candidate's metadata is read and declined
    bfe848946  #218  fix(metadata): name the file in the ffprobe-failure warning
    38e7a2b0f  #218  fix(scan): count only files this book could claim, and skip other books' files
    a98c49e2c  #218  fix(ui): label a no-files-accepted scan on the detail view's Files tab
    803266ca0  #218  fix(scan): probe other books' files again so the metadata conflict guard is unchanged
    6a3f13607  #218  fix(scan): decide no-files-accepted on durable ownership, not attribution
    69fcb7b74  #218  test(scan): reproduce a folder-attributed file whose tags name another book (red)
    e31d8ef71  #218  fix(scan): decline folder-attributed files whose tags name another book
    08df9ffe2  #218  test(scan): update probe-count and swap-timing expectations for the new pass
    4e454c755  #218  test(scan): give the tracked-file guard a real identity and a Times.Never
    04fc66ae0  #3    791 option D (NuGet ffprobe) composed onto the stack, for validation on the install
    353b1208d  #309  test: assert FailedDownloadAutoSearch defaults to true
    40702a193  #309  fix: default FailedDownloadAutoSearch to true, matching the *arr family
    bc16faa47  #298  test(redaction): reproduce ffprobe JSON corruption from RedactText fallback
    4aa3a304e  #298  fix(redaction): drop generic fallback that corrupted unrelated text
    6cef34b74  #299  fix(library): reject an unrecognized refresh-metadata field instead of silently upgrading to whole-library (#299)
    b1a1dfa02  #299  fix(library): catch the deserialize JsonException so a wrong-typed field 400s, not 500s
    831b575dd  #69   test(downloads): extend the seed-limit removal gate tests to the full matrix
    0441e394a  #69   fix(downloads): make CanRemove reach the seed-limit evaluator on the queue-poll path
    486e4934a  #69   test(downloads): cover the exact ratio == limit boundary for both clients
    ad149cc58  #69   test(downloads): pin the fail-closed seed policy on the queue-poll path
    af5b30d4f  #317  fix(imports): retry a failed file import instead of blocking at once
    48a49a188  #317  fix(imports): use the configured retry delay on the file-import retry
    32ccbbeac  #317  fix(downloads): finalize a held completion whose client item is gone
    deb9a211e  #324  fix(downloads): isolate OnDownloadFailed's blocklist/removal/search steps so one exception doesn't abort the rest
    8a9115cf2  #326  test(qbittorrent): add numeric-tolerance coverage for the response mapper helpers (#1008)
    a123742bf  #326  fix(qbittorrent): tolerate every numeric JSON shape in the response mapper (#1008)
    83e132346  #326  test(qbittorrent): malform state instead of downloaded in the unreadable-torrent fixtures (#1008)
    8c539225e  #218  fix(scan): log per-file MetadataContradictsPath diagnostics on CompletedNoFilesAccepted
    c1a347250  #325  fix(downloads): record History entry when orphan cleanup removes a download
    a29c100b3  #340  fix(downloads): record History entry when DownloadRemovalWorkflow removes a download
    e6fdc5a97  #367  fix(downloads): a successful Removed History row no longer carries a non-null Error
    1562e603d  #333  fix(nzbget): classify DeleteStatus-family history entries as Failed, not Ignored
    5a395d395  #333  fix(nzbget): correct Sonarr/Readarr citation line numbers in comments
    5940e57e5  #335  feat(blocklist): add a list-everything endpoint for the frontend page
    a0ee5d080  #335  feat(blocklist): build the Blocklist page (list, remove, nav entry)
    d0c63a6f3  #336  settings: add RejectClearlyMusicReleases to ApplicationSettings
    9f9c69da4  #336  search: gate clearly-music releases in SearchResultScorer when enabled
    6f10f39dd  #336  fe: add "Reject releases that are clearly music" search setting toggle
    21d29b508  #336  test: extend the post-canary migration allow-list for the new migration
    27ab77add  #336  fix: Audiobooks id must override even alongside another Audio-range id
    db2c5815b  #353  fix(blocklist): require confirmation before removing a blocklist entry
    0dc433f75  #361  search: strengthen MusicReleaseClassifier with weighted signals (tracker #361)
    c44abd0be  #361  search: fix 6 false positives found by independent review (tracker #361)
    af62f54ed  #361  search: fix 6 more false positives, round 2 of independent review (tracker #361)
    bd47ab6e1  #361  search: fix 4 more false positives, round 3 of independent review (tracker #361)
    e55616c0b  #361  search: fix 4 more false positives, round 4 of independent review (tracker #361)
    3b3e3e67a  #361  search: gate the bitrate/FLAC signal on a music-specific corroborator (tracker #361)
    5245efd0f  #361  search: drop Artist/Album consistency from the bitrate gate (tracker #361)
    0fc54bd4a  #361  search: require two-signal convergence to gate bitrate/FLAC (tracker #361)
    0eea25a82  #361  search: gate bitrate/FLAC on an actual music category only (tracker #361)
    5e872b604  #361  search: remove the bitrate/FLAC/kbps signal as dead code (tracker #361)
    bb26b7f79  #371  fix(library-import): decouple manual-search ASIN detection
    619c896f2  #371  chore: replace non-corpus identifiers in comments and tests with placeholders
    8645ede7d  #371  test(metadata): use catalogue values for contributor role test data
    2a5121365  #371  test: take series and diacritics test data from the catalogue
    e7605aca8  #366  fix(metadata): bare-anywhere-wins for author/contributor classification
    0982ffcda  #366  fix(metadata): close the author-credit vocabulary gap and the Introduction asymmetry
    143101f16  #366  feat(metadata): add a standalone organization-as-author detector
    d6087a8f5  #366  fix(metadata): apply the author-credit classifier at the 3 missing call sites
    c4ea7f2da  #357  fix(nzbget): bound the unbounded history fetch, item 357
    2b0f13d76  #357  fix(settings): reject DownloadClientHistoryLimit below 1, item 357 follow-up
    ede6e0ecb  #237  fix(downloads): skip the failure row for a qBittorrent duplicate
    130fca473  #237  fix(qbittorrent): send an explicit false for Initial State Start
    db3fa1ccf  #237  test: use neutral book titles in the fixtures this branch adds
    8d2cc0ec3  #237  test: use public-domain corpus titles in the fixtures this branch adds
    1e4830b0a  #237  fix(qbittorrent): a skipped torrent marks the snapshot incomplete
    5aaf93d5b  #237  fix(qbittorrent): an open circuit breaker still fails the poll
    c6e707b96  #237  test(qbittorrent): drive both fixes through the monitor cycle
    e527ce90a  #237  test(qbittorrent): use host-valid save paths in the gateway-level fixtures
    81cc26f33  #237  stack: #1021 take the head's comment wording for per-client-type HTTP handlers
    6ac781a21  #237  refactor(quality): move QualityMatcher's helpers to a partial file
    6d7454065  #237  fix(search): keep refusing a codec the ladder has no rung for, as canary did
    9b794867c  #237  stack: #1023 take the head's wording for two scorer comments
    d1d3b2e56  #237  fix(notifications): redact discordapp.com and pushbullet:// webhook URLs
    360d4bbe4  #237  fix(logging): keep known env-secret masking in SanitizeWebhookUrl
    ea6592d67  #237  test(logging): serialize env-mutating redaction tests
    d8f922865  #237  fix(manual-import): authorize companion files against the root folder
    17e668616  #237  test: use neutral book titles in the fixtures this branch adds
    cb635d7c2  #237  test: use public-domain corpus titles in the fixtures this branch adds
    c2e8f9235  #237  stack: #1022 give the hardlink ASIN-tag test a metadata-writing lease on a real publication
    103bb2f47  #237  stack: reconcile the stalled-download timeout tests with the settings controller
    93c8b9a48  #237  stack: reconcile the history-limit settings test with the settings controller
    0811d1a84  #237  stack: reconcile the NZBGet history-entry test helper with required members
    e516bfc0d  #237  stack: reconcile two scan test files with the scan command upstream reshaped
    3af46b034  #237  stack: reconcile the Designer-chain guard's sentinels without the recycle bin
    dede57526  #237  stack: reconcile the pinned migration list without the recycle-bin migration
    fb40bcc54  #237  stack: reconcile upstream's released-schema upgrade tests with this stack's columns
    6b9845779  #237  stack: reconcile #914's import-tag tests with leases that withhold metadata writes
    23c6b546e  #237  stack: reconcile the hardlink fallback tests with upstream's publication catch
    0b5806872  #237  stack: reconcile #902's refresh regression test comment with #993's snapshot
    2e74f77d0  #237  stack: reconcile the symlinked-source tests with content-only source proofs
    80e4028cb  #237  stack: reconcile the Wanted selection specs with upstream's Search All confirmation flow
    b413b89b9  #237  stack: reconcile the WantedView spec formatting after the row stride test
    a5f94b771  #237  stack: reconcile the detail view spec's signalRService import
    f5e552683  #237  stack: reconcile the download settings spec with the stalled download timeout input
    e14dfad81  #237  stack: reconcile upstream's empty-send download test with the search query plan
    21b9e8d58  #237  stack: reconcile the companion log-sanitization test with the provider it rebuilds
    e1e6c42a9  #237  stack: reconcile DownloadMonitorService with the 500-line cap
    4b3b47722  #237  stack: reconcile RootFoldersController with the 500-line cap
    7cfa89d0e  #237  stack: reconcile AudiobookFileService with the 500-line cap
    6e946ffb1  #237  stack: reconcile upstream's SQLite-reload refresh test with the stack's UTC read converter
    f4fb9ef9d  #68   fix(filesystem): resolve a linked source ancestor only when no link in its chain sits inside a configured root
    df3e2f829  #68   test(filesystem): pin symlinked source ancestors against the configured roots
    cbf457901  #109  stack-image: fail the build if the version stamp is stale, not just missing
    d71b35d54  #109  stack-image: anchor the version/patches grep to the manifest header
    973e1a087  #383  fix(delete): keep the recursive folder delete unless every tracked file is proven (#1071)
    58e398bd8  #383  fix(delete): keep the recursive delete for durably owned folders (#1071)
    4ec62df30  #237  stack: import AuthorCredits where the rescan role-suffix fix was ported
    c9850b9b4  #237  stack: give the upgrade-allowed and embed-cover-art migrations IDs after the upstream frontier
    6c62bcb9b  #237  stack: point upstream's empty-query and fallback-query tests at this stack's search surface
    c8e9edb43  #237  stack: rebuild the migration Designer chain for v1.5.9's order
    c27a10af9  #237  stack: reconcile two files with the 500-line cap after v1.5.9
    55d490163  #237  stack: reconcile #1021's and upstream's new tests with this stack's search and queue shapes
    edf75f2ce  #237  stack: let the refresh-cycle spend test walk two regions under #1043's rule
    32a50ac97  #237  stack: order line 267's bare-anywhere-wins tests by bare credit, as #1013 now does
    ae65d225f  #237  stack: queue reconciliation tests mock the snapshot API the poller now calls
    b96d1195b  #68   stack: adapt the nested-symlink source test to item 68's behaviour
    b9f2acd20  #237  stack: pin System.Security.Cryptography.Pkcs to 10.0.13, the first version outside two published advisories
    228ec0189  #-    style(fe): run prettier over the 12 stack files that drifted from it
    ed6ad5fae  #-    fix(nzbget): hold DELETED/COPY and DELETED/DUPE as client duplicates, not failures
    59d61f386  #-    fix(downloads): reuse the blocklist filter's identity check for client-duplicate holds
    0267a1c08  #-    fix(downloads): keep client-duplicate holds out of failure alerts and retry-import
    197fd53b4  #-    Pin stored webhook type over URL sniffing in dispatch
    854d39da8  #-    fix(import): fail the job when the import refuses with a conflict
    f240bc1c8  #-    Take the pinned directory open failure message from the caller
    91a2deb5d  #-    Defer metadata rescan candidates per file so failures cannot starve the batch
    4e4aa1c42  #-    Don't wait the per-book search delay for books that are not searched
    424fc4490  #-    Repeat the pre-search checks after the per-book wait
    2cb17dab1  #-    stack: reconcile two files with the 500-line cap after the ff6 merges
    ebb0a2729  #-    stack: point the search-delay and import-conflict tests at this stack's surfaces
    4e284eb5d  #-    stack: drop the second copy of #843's read-write stream pieces
    5a25f656a  #-    stack: carry the test adaptations the canary merges made into the linear stack
    074098e3a  #-    stack: write the failed-download blocklist entry through FailedDownloadBlocker
    dbc290935  #-    stack: clear the findings the backend format check reports
    c03975799  #-    stack: carry #1023's re-scaffolded migration id and move #914's after it
    c273b4c3e  #-    stack: rebuild the migration Designer chain for #1023's new position
    ef85e8316  #-    stack: merge #786 (local/upbase-174-786 at 1b993e459)
    af1fe0c65  #-    stack: merge #849 (local/upbase-174-849 at 08eee179b)
    e903626ef  #-    stack: merge #916 (local/upbase-174-916 at 34fa82924)
    8a6f82160  #-    stack: merge #924 (local/upbase-174-924 at c79161dfe)
    976d1e4b3  #-    stack: merge #925 (local/upbase-174-925 at fe6c6af0e)
    123fe3735  #-    stack: merge #930 (local/upbase-174-930 at 8e9d97720)
    a34dbc58b  #-    stack: merge #941 (local/upbase-174-941 at 470488b0e)
    71e0b3a7a  #-    stack: merge #954 (local/upbase-174-954 at 0f7d7b1d9)
    c2d4d66f9  #-    stack: merge #958 (local/upbase-174-958 at f0a70a09c)
    cfa839cb9  #-    stack: merge #975 (local/upbase-174-975 at 057969903)
    9d66924ca  #-    stack: merge #978 (local/upbase-174-978 at 4569115e3)
    b0666b4f2  #-    stack: merge #981 (local/upbase-174-981 at 315cfef58)
    b3411f3b6  #-    stack: merge #987 (local/upbase-174-987 at bb7dcc7c3)
    c2edebb7f  #-    stack: carry #981's cancel-after-accept submission test into the stack's test shape
    940643a6d  #-    stack: drop the second AddSuccessResponseBody the #978 merge added to the qBittorrent mock
    932940eed  #-    stack: merge #1012 (local/upbase-174-1012 at 1846e40fb)
    43282c52a  #-    stack: merge #1015 (local/upbase-174-1015 at 0136ea506)
    e9f5c4aa4  #-    stack: merge #1016 (local/upbase-174-1016 at eaa2d5d2f)
    434c4370d  #-    stack: merge #1017 (local/upbase-174-1017 at a5a77abd7)
    2695030ed  #-    stack: merge #1018 (local/upbase-174-1018 at 102faf8e4)
    e34a971a7  #-    stack: drop the two pieces the #1018 merge duplicated or left unreferenced
    982cabc7a  #-    stack: merge #1019 (local/upbase-174-1019 at 7ccf741c7)
    710c0b7c8  #-    stack: merge #1022 (local/upbase-174-1022 at a18ff0867)
    6fba13d61  #-    stack: drop the second copy of the content-probe helpers the #1022 merge added to FileUtils
    5251c44c5  #-    stack: merge #1119 (local/upbase-174-1119 at ad7cdd8de)
    36594f528  #-    stack: merge #1151 (local/upbase-174-1151 at 9b35b6b3b)
    154be8bda  #-    stack: merge #1153 (local/upbase-174-1153 at 5d625d780)
    1eafaa80c  #-    stack: merge #1155 (local/upbase-174-1155 at bb2f695b4)
    f617c1aa3  #-    stack: merge #1164 (local/upbase-174-1164 at 42a843849)
    44916f282  #-    stack: drop the second ApplicationConflictException catch the #1155 merge added
    faf4304eb  #-    stack: merge #914 (local/upbase-174-914 at e04fc6cad)
    6f01f1c14  #-    stack: keep the seed-criteria step off the caller's token after #981
    fd9bfa3d8  #-    stack: keep the Transmission seed step off the caller's token
    8d9a5abc6  #-    stack: keep an addTrackers timeout from failing an accepted qBittorrent add
    0bc2fe9d6  #-    stack: merge #1023 (local/upbase-174-1023 at 9d59fd63f)
    21d2bc675  #-    stack: point #1023's declared-attribute scoring tests at this stack's search surface

## How this stack was built

A linear rebuild on upstream canary 1.7.4 (e696185ca) of the 1.7.2 stack (df24a648f plus 653 patches, manifest
06fc36314), followed by one merge per rebased PR branch. Every patch of the 1.7.2 stack was replayed in order with
`cherry-pick -x` (rerere off), except four of the remote path mapping cache commits, which upstream now carries in
its reviewed form (0a46825c8, d4af12a16, 8811bd61c, 0483c8e57, plus its follow-ups). Each item number above is the
one the 1.7.2 manifest gave the commit this one was picked from (the last `cherry picked from` trailer); '#-' marks
the stack-only commits and the merges.

The merges ("stack: merge #N") bring in the PR branches local/upbase-174-N, rebased onto 1.7.4, so each PR head is
an ancestor of this stack. Where the stack already carried a PR in its own shape, the conflict was resolved to the
stack and only the PR's newer pieces were taken; three merges left duplicates, removed by the "stack: drop" commits.
The three commits before the #1023 merge are review fixes: the per-indexer setShareLimits call and Transmission's
seed criteria lookup and torrent-set still took the caller's token after the #981 merge moved the other post-add
requests off it, and an addTrackers timeout still escaped the add. The addTrackers fix is #981's second commit
(b442ff393), carried here as a stack commit rather than merged, so that head is not an ancestor of this stack.
#1023 is merged last, at its newer head: the stack already carried its migration, its earlier commits and most of
its later ones in the stack's own shape, so the merge takes only the declared Torznab format and codec precedence
and the stray-format-word guard in both Torznab parsers, the scorer comment that describes them, their tests, and
the migration test's real predecessor id. The commit after it points those tests at this stack's search surface.
Not merged: #1152 (indexer request budget; its migration and the snapshot need a decision first).

Migrations: no id changes against the 1.7.2 stack. #1023 stays at 20261112054132 and the fork-local #914 at
20261112054145. A database that ran a build before the 1.7.2 stack holds 20261029020317 and 20261029020330 in
__EFMigrationsHistory and needs those two rows renamed before this build first starts (R30-packet-b, r30-1r).
