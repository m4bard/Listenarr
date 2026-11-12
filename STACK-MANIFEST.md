# Stack manifest

This build is NOT a stock release. It is upstream canary plus unmerged patches.

    base:        df24a648fed4dcebe294985955754a48d039c7e2
    base short:  df24a648f
    patches:     651
    version:     1.7.2+m4bard.651

## Patches, oldest first

    b3fd93eab  #105  fix(naming): don't lose a series position that isn't a plain number
    21e9feb2b  #105  fix(naming): derive both series-position fields from one source
    e9a1b17a1  #106  fix(metadata): populate SeriesAsin from the Audnexus series record
    c3778cd12  #106  fix(metadata): carry the Audnexus series list, with identifiers, through the product lookup
    12e97a1d1  #106  test(metadata): pin what a blank Audnexus series ASIN stores
    d23fa5b84  #43   fix(ffmpeg): point macOS at evermeet's ffprobe archive, not its ffmpeg one
    ef452f173  #23   fix(qbittorrent): one unreadable torrent should not truncate the queue poll
    9c24ff642  #23   fix(qbittorrent): let a client that stops answering fail the poll
    463c04144  #23   fix(qbittorrent): quieten the per-torrent skip and escape the hash
    11b0f4d78  #24   fix(import): actually embed the ASIN after an import
    8f9564ad0  #24   tests: save and reopen a real m4b for the ASIN writer
    73bb3b928  #24   chore: exempt only the generated m4b fixture from the media ignore rules
    cd2cf23b8  #24   fix(import): do not write the ASIN tag through a hardlink to the source
    93125b324  #24   tests: pin that the hardlink case really shares the source's inode
    f5dfdbccf  #22   Group chapter files indexed as "N of M" into one unmatched-scan item
    52442e1ae  #22   test(scan): make the bare "N of M" case able to fail
    d0610ca32  #22   fix(scan): leave a Book or Volume index alone when stripping "N of M"
    e159aee86  #17   Library import: keep every series membership, not just the first
    baa147ae7  #17   fix(library-import): build series memberships from the enriched product and fix the unmatched-files mapping
    9e0512352  #17   fix(library-import): require the B0 prefix before keeping a series ASIN
    0833760ef  #11   fix(scan): stop the Linux descriptor path leaking into metadata and size
    0af3ab674  #11   tests: follow the scan's metadata read onto the two-part file source
    3690d75e5  #11   fix(scan): narrow to the metadata boundary, leave the size to #901
    55ad2579f  #29   fix(download-clients): one circuit breaker per client, not one for all of them
    ed499787c  #29   test(download-clients): say what the isolation test does not cover
    2877033e6  #42   fix(manual-import): match the rename naming table on casing and missing tokens
    8164c57bc  #42   test(manual-import): assert path segments, not substrings
    63d368a79  #42   fix(manual-import): build every naming key the rename table builds
    2975ff442  #32   fix(prowlarr-import): build proxy URLs from the base Prowlarr answered on
    f25633507  #32   fix(prowlarr-import): only adopt a discovery base on the origin that was asked
    f27bf0729  #34   Serve under a URL sub-path by honouring the UrlBase that already exists
    146e539ac  #34   feat(notifications): give notifications their own ApplicationUrl
    1a32901bc  #34   feat(notifications): read LISTENARR_PUBLIC_URL as the notification base
    de11bcc80  #34   fix(api): set the path base from UrlBase instead of appending to it
    2659f2407  #35   Record a failed grab in history when the download client rejects it
    a39f922d5  #35   test(downloads): assert the failure record on the blank-identifier path
    a8583515b  #35   fix(downloads): sanitize the client message written to history
    fb7e5dbbf  #36   fix(metadata): order series positions with an invariant parse
    c7c113c89  #36   tests: bring the series-position test up to the convention #717 added
    49a2745bd  #36   fix(metadata): reject a grouped series position instead of misreading it
    e2fac59d8  #36   fix(parsing): pin machine-format number parses to the invariant culture
    fbe4a2db6  #36   fix(parsing): match size units and reject non machine-format numbers
    c493acf73  #35   fix(qbittorrent): tell a refused release apart from a failed submission
    c3928f7b1  #35   test(downloads): pin the controller half of the 409 change
    d83a0ee6f  #35   fix(downloads): answer a refused release with 409 on the manual send path
    356b16bc8  #160  fix(config): overlay a posted startup config onto the file instead of replacing it
    b17f00853  #160  fix(fe): do not post a startup config the settings view never loaded
    26e3183e9  #64   feat(settings): add a URL Base field to General settings
    20403e470  #64   feat(settings): tie the URL Base warning to the field it is about
    9524a3994  #64   feat(settings): show an empty URL Base box when the stored value is the site root
    5117792eb  #64   feat(settings): add an Application URL field beside URL Base
    4fa0ffc48  #64   feat(api): reject a UrlBase that is a full URL
    d26b9c1ed  #33   fix(audible): tell a failed catalog lookup apart from a confirmed zero-match
    de90673a2  #33   test(audible): cover the other ways the catalog lookup fails, and the 503 itself
    ee2060772  #33   test(audible): give the new controller class the repository's test conventions
    1d3aedb2b  #33   fix(audible): declare the search endpoint's responses, including the new 503
    59207360a  #25   feat(import): embed cover art into imported files, behind a setting
    0c4c769e8  #25   docs: attach the new doc comments to the members they describe
    1ad84445b  #25   tests: pin that cover art replaces the existing artwork rather than appending
    6d6052333  #25   fix(import): embed cover art for a book that has artwork but no ASIN
    595fb0040  #25   tests: keep this branch's migration out of the shared expected list
    38986e996  #25   refactor: keep the cover art code clear of PR 843's edits to the same file
    058497731  #25   fix(import): extend the hardlink guard to cover-art embedding, with its test
    c811a1eee  #60   fix(filesystem): record why a file mutation failed, not only that it did
    772340c00  #60   test(downloads): name the no-inner-cause case for what it now does
    6d064c522  #60   refactor(downloads): format the failure cause where both writers can reach it
    050634c43  #61   fix(downloads): make retry-import actually requeue the import
    d3c0f7aa5  #61   fix(downloads): survive two retry requests racing on the same download
    a435316e4  #62   fix(downloads): give the three download-finalization settings a reader again
    c291c52fc  #62   test(downloads): pin that the processor reads the configured retry delay
    39c4c3e59  #62   fix(downloads): stop the retry backoff doubling past a day
    f88c2d15c  #62   fix(downloads): hold only a first transition into Completed
    79749ad66  #62   test(downloads): scope the completion-stability save to the one test that needs it
    cd7e91083  #65   fix(quality-profiles): stop hiding Maximum Age, and load a profile's qualities
    575bd6576  #46   fix(search): read Indexer.MinimumAge and Indexer.MaximumSize, and measure age in UTC
    54df6a493  #46   refactor(search): split the scorer, and test the UTC date parse directly
    350f91399  #46   test(search): pin that the indexer size ceiling applies to Usenet too
    e7393dd4b  #46   test(search): make the new parse tests follow the repository conventions
    1f982ef98  #46   fix(scoring): resolve indexers once per batch, not once per result in parallel
    d6c39995d  #46   test(scoring): assert the pre-resolved indexer actually reaches the scorer
    81a488372  #46   fix(search): compute the profile size gates in long, not int
    982c2383d  #47   fix(download-clients): make the Priority dropdown speak the planners' vocabulary
    19f0eeb7e  #47   test(download-clients): drop two usings the global usings already cover
    a12f2d55b  #47   fix(download-clients): read a stored legacy priority back as Normal
    23df91524  #47   fix(download-clients): say what Default does per client, and name Force's band
    b1a3c351d  #47   fix(download-clients): lower the priority in the addurl path too
    98f16df88  #44   test(images): assert that a cached image is served, not that something happened
    f9e8cd8fa  #83   feat(library): render the list view when grouping by author or series
    12efb1fd9  #83   fix(library): fetch author covers and remember the mode in the grouped list
    f1138e2a1  #45   fix(qbittorrent): send the four Advanced Settings to the client
    c8a94080b  #45   fix(qbittorrent): do not fail a submission the client already accepted
    69d92f47f  #68   fix(filesystem): say why a source file could not be pinned
    70f0cf555  #68   fix(filesystem): sanitize the pinning cause before it reaches the log
    1879940a9  #68   test(filesystem): fail the refusal test when no cause is captured
    499fb8eab  #68   fix(filesystem): lead the refusal with the cause, through the shared formatter
    8b1f24453  #68   fix(filesystem): do not blame a configured root's link for an unrelated refusal
    1994b15d2  #68   fix(filesystem): name no link when the source overlaps an unavailable root
    054fe362d  #68   fix(filesystem): name the outermost link as a possible cause, not a certain one
    bb8c1c7e8  #68   test(filesystem): make the nested-link guard able to fail
    3bfe7c544  #72   fix(imports): retry a failed file import instead of blocking on the first attempt
    bc7cb784f  #72   fix(imports): retry a failed import only when every file in it failed
    27acaf46c  #80   fix(quality): let a real encoder bitrate reach the rung it was encoded for
    bd18bf19c  #80   test(quality): pin the bitrate tolerance from both sides, not just the floor
    5d559f3ff  #102  fix(docker): give the image a healthcheck it can actually run
    ed545851f  #31   fix(notifications): dispatch webhooks for the triggers the settings screen offers
    8709a94a8  #49   Make the frontend follow the UrlBase by injecting a <base href> into the shell
    fbf276d2a  #49   fix(spa): anchor the shell at the site root even with no UrlBase
    ccc3c9c2c  #109  stack: let a local build stamp its own version
    e3e56f565  #109  stack: publish the patched build to GHCR from the fork
    7f1a65604  #109  stack: publish a moving current tag beside the immutable commit tag
    d925bb0ef  #108  fix(files): physical-generation snapshots reject database-loaded observation timestamps
    49ac56f12  #108  fix(persistence): restore the UTC contract for PhysicalIdentityObservedAtUtc at EF materialization
    d45475ee0  #73   feat(wanted): add a cutoff-unmet bucket to the Wanted page
    499430aab  #74   fix(collection): show an author's books under their membership series
    2e022d391  #75   feat(library): count the distinct series an author appears in
    f6237168c  #76   feat(downloads): implement the three reprocess endpoints instead of returning success
    dfd56e050  #76   fix(downloads): refuse an ineligible reprocess instead of throwing at the caller
    5d0514cac  #76   fix(downloads): drop the unused using and the seam-adjacent whitespace
    04550aa2d  #83   feat(activity): sort the queue, and show when an item was added
    ba77e35a2  #86   fix(collection): apply the language preference to library books, not just suggestions
    54a56bd69  #94   fix(search): the minimum seeders gate never fired on a real torrent
    076311c5d  #110  fix(import): match blacklisted extensions without regard to case
    d9a5adaa3  #112  fix(ui): match System recent-log severity classes to the stylesheet
    4f4c0253b  #20   fix(search): derive one indexer query title, shared by both search paths
    d3482a918  #20   fix(search): leave the series out of the indexer query, as Readarr does
    74fa13ff8  #66   fix(downloads): give the queue a control that removes a terminal download
    2b744259e  #66   fix(downloads): tie a processing job's retention to the download it explains
    a0d900c24  #99   fix(authors): stop attributing one author's ASIN to every co-author
    bf255b478  #100  fix(naming): spell an author's initials one way in folder names
    51f61872a  #100  test(naming): pin byline order on two co-authors, not on a translator
    2a11df1e3  #113  fix(system): stop inventing log entries when no log file exists
    b3e06fe99  #114  fix(library): stop announcing the list-view status badge as a button
    e1a899336  #115  fix(settings): stop a failed clipboard write from reporting a failed regeneration
    6510f4b74  #95   fix(api): the authentication gate ignored requests that varied the path casing
    316c6eafb  #68   fix(filesystem): resolve a symlinked source path instead of refusing it
    03a5009f0  #68   docs(filesystem): stop two comments claiming what the symlink commit retracted
    d9cb40f9b  #98   fix(logging): give silent catch blocks a real log call or a stated reason
    8c7947b8e  #108  fix(persistence): keep a persisted UTC timestamp UTC when it is read back
    30c6fe4d2  #78   test(scoring): pin the four hand-copied quality ladders to each other
    8e0cde3d1  #69   test(metadata): count the attempts the Audible retry policy actually makes
    a6d0bee81  #69   test(metadata): stop the retry policy tests racing the caller's clock
    608b1c429  #136  NZBGet: warn once per failed history entry, and only for monitored entries in polls
    36e12a74a  #136  fix(nzbget): keep the warn-once keys case-insensitive after a scoped read
    a539be9f4  #92   refactor(collection): lift shared series and text helpers out of the collection view
    b72dc8f65  #92   feat(collection): show an author's series as a section on the author page
    1a1620826  #141  fix(monitoring): keep every series membership and its ASIN when a monitored author or series adds a book
    e8c95289c  #141  fix(library): let a legacy-only metadata payload carry its series identifier
    c94905f4a  #141  fix(monitoring): keep every series and its ASIN through the author catalog cache
    4caae22c4  #141  test(monitoring): use catalog books with real identifiers in the series fixtures
    8a3073553  #141  fix(monitoring): refetch an author catalog cached before series entries existed
    c021d4f8c  #145  feat(activity): queue selection that survives the poll instead of being reset by it
    6e1790357  #145  feat(activity): sequential bulk runner with per-id outcomes and one summary line
    94023bdce  #145  feat(api): keep the queue-management API tests, drop the methods #917 and #927 already added
    5e43b17c0  #145  feat(activity): a queue toolbar whose verbs say what they will do before doing it
    7b837fe07  #145  fix(activity): the select cell reasserts its props after a click that changed nothing
    1cfd6d34b  #145  feat(downloads): a Retry button that appears only where the endpoint will accept it
    34f4708dd  #145  feat(activity): select queue rows and act on them together
    22a1e380a  #145  fix(activity): the filter is a viewport, so selection controls act on the rows in view
    b8ae2328d  #145  feat(downloads): the Retry button does the retry, instead of promising one later
    4c8fed6d3  #145  fix(activity): the retry button dresses itself and select all goes quiet with nothing to select
    ee6900138  #145  fix(activity): the toolbar drops a confirmation the selection outlived and rests while busy
    50619b1d7  #145  fix(queue): overlay import status onto queue rows the client cannot represent
    9237ce10b  #145  fix(activity): disclose the true count before an unbounded sweep
    49ad7b556  #144  metadata: name the two nothings a provider can answer with
    8ef62ddef  #144  metadata: answer the ASIN endpoints with a status, not a stack trace
    9e22c6fb4  #144  images: keep serving the placeholder when the provider does not answer
    9f2807b09  #144  search: move the Audible series endpoints into a partial
    9eaeb2cce  #144  search: say the provider did not answer, rather than returning nothing
    d5855a0df  #144  metadata: walk every source, then report the fault rather than a miss
    6795b3270  #144  metadata: let the Audible client raise when the provider did not answer
    bfab0e3df  #144  metadata: hold the Audnexus book lookup to the same division
    d3e741d31  #144  library: record when a book's provider metadata was last refreshed
    06dacdd24  #144  settings: the refresh interval, staleness age and budget are operator-set
    a790c5e35  #144  metadata: a request budget with an hourly refill and a spacing floor
    3b3e92e7f  #144  metadata: lift the per-book rescan out of its HTTP wrapper
    936946137  #144  metadata: one gate, one run, one budget across every refresh entry point
    4f94b04d2  #144  metadata: walk the stalest books on an interval, and let an operator ask
    732654ed2  #144  fe: a Metadata Refresh section in the settings screen
    2b240cf07  #144  metadata: provider 503s answer as a ProblemDetails the filter keeps
    6d676d73c  #151  fix(logging): sanitize ASIN log arguments, rebased onto the metadata-refresh chain
    460d4fb2a  #153  Attach download history rows to the audiobook they belong to
    d30afed2a  #153  fix(history): record the protocol a download actually used
    fb495361e  #153  fix(history): resolve the protocol for the remaining event types too
    89046db11  #153  test(history): cover the audiobook key and the protocol on one call
    7a9e1fa3c  #154  test(downloads): match the int? audiobook id in the grab history verifies
    8104033e1  #155  fix(imports): classify failures at the boundary instead of leaking them to History
    a065e6ce1  #156  fix(naming): key the remaining naming tables case insensitively
    581097387  #158  fix(qbittorrent): a 200 carrying "Fails." is a refusal, not a grab
    8a31a0068  #161  Record a download client timeout as a failed grab, not a shutdown
    34b0896b8  #163  fix(notifications): sanitize webhook URLs before logging them
    022c128f9  #163  test(notifications): cover SanitizeWebhookUrl and pin the webhook log call sites
    42ec94057  #166  feat(search): tell an indexer that answered with nothing apart from one that never answered
    e59c68b20  #166  fix(search): give the per-indexer timeout its own catch, correctly typed
    7926c37a3  #166  test(search): match the fake provider to the observation-returning interface
    a31578d8f  #166  search: isolate a per-indexer HTTP timeout from the multi-indexer fan-out
    11e987645  #166  search: bound indexer fan-out concurrency to 4 (tracker #119)
    e33e90cba  #166  search: separate a rate limit and a refused credential from any other non-2xx
    4a5cc61b2  #166  search: persist a per-indexer failure backoff ladder
    817d45739  #166  search: skip an indexer that is in failure backoff, and record what each one answered
    63cfbb5a5  #166  search: do not stamp LastSearchTime on a book whose indexer set was degraded
    7c8103ebb  #166  indexers: show on the card when an indexer is in failure backoff
    3418c62af  #166  feat(search): try a second form of a search before saying the book does not exist
    1a4f128b0  #166  search: cover what the query ladder and indexer backoff do to each other
    9495707d3  #166  search: drop the bare series-name query rung
    36c985019  #166  test(search): thread the cancellation token through item 107's fake provider
    34b54356f  #172  172: fix Abridged substring match stamping unabridged books abridged
    d630f5d37  #172  172: fix Abridged substring match on the frontend too
    f5e69598d  #173  173c: expose the Listenarr.Backend meter through a listener and an endpoint
    71889bac8  #170  search: make the indexer fan-out ceiling an operator setting, shipped at 4
    7a7a9c310  #170  settings: expose the indexer search concurrency ceiling in Search Settings
    2218553cb  #123  notifications(#126): make EnableNotifications actually gate delivery
    38f72c773  #123  notifications(#127): dispatch webhooks by stored Type, not URL sniffing
    31e5e299e  #126  notifications(#126): drop EnableNotifications, matching the family
    8ec73f318  #123  search(#123): wire up EnableAmazonSearch/EnableAudibleSearch, dead since introduction
    a942920ff  #123  notifications(#128): remove the unreachable legacy /notifications/test endpoint
    26f01b3c2  #123  settings(#131): add a UI control for ExtractArchives, backend-only until now
    219933c07  #123  downloads(#132): fix RemoveCompletedDownloads for Transmission, SABnzbd, NZBGet
    ec2b81428  #124  fix: wire AllowedFileExtensions into FileUtils.IsAudioFile
    f59b8a71a  #124  cleanup: remove two orphaned private AudioExtensions arrays
    7fe2dd7b9  #129  Remove vestigial Indexer.Tags field
    7f5ad8dd2  #133  history: add retention setting UI and a daily cleanup worker
    33b930dd2  #138  Preserve series ASIN when mapping monitored author/series books
    e39882574  #138  test(monitoring): resolve the series-monitoring root through identity, not a raw builder
    ac98efa76  #199  Fix author name normalization to merge spaced-out initials
    55b91c927  #199  test(domain): bring StringUtilsNormalizeAuthorNameTests up to convention
    e6213e847  #199  fix(authors): dedupe author-cache methods reintroduced against fix/72's own copy
    3cff69e6c  #151  fix(authors): skip the SQL narrowing pre-filter for non-ASCII author names
    465f6d79f  #151  fix: complete the NormalizeAuthorName redirect this branch left half-done
    4dac36062  #201  Finish the author-normalizer consolidation and re-derive the keys it left behind
    6624f082a  #201  Keep the canonical author spelling the ASIN lookup already returned
    acb2f01f5  #201  Converge existing books onto one spelling per author at startup
    79a0b5912  #201  fix(library): normalize author grouping and include every co-author
    45309c699  #200  Pin the author-identity behaviour these fixes are meant to change
    1d2fb47ff  #200  Stop reading author identity out of a list that never recorded it
    d2d6b151e  #200  Refuse to rename a cached author when an ASIN is already somebody else's
    162a49900  #200  Confirm an author ASIN with Audible before binding it as an identity
    7188ae645  #200  Pin what stops a renamed cache row reaching stored author names
    797c64ca3  #200  fix(authors): dedupe author-cache methods a third time, keeping the collision-safe version
    a3dfb892e  #122  search: add ReleaseShapeDetector, which tells a bundle from a single edition
    6b4211284  #122  search: a per-profile preference between bundle and single-book releases
    4bbd88cbd  #122  fe: the release shape control, and a Bundle badge on search results
    3b7b709ff  #172  fix(search): word-boundary word filters, and any-of required words
    74d037d71  #172  fix(search): route PreferredWords through the same word-boundary matcher
    79469dd33  #172  fix(search): make indexer priority a genuine tie-break instead of dead weight or dominant term
    f224cbea6  #172  search: give the word-boundary term matcher its own file
    be049de23  #172  search: pin the precedence between the three scorer concerns
    c6996a443  #173  fix(system): stop fabricating download-client connectivity
    00d5e8cd9  #173  tests(system-service-logs): pass the download-client status cache the constructor now requires
    3c6d5020c  #169  fix(nav): dismiss the header search panel when the route changes
    294f40e1e  #169  fix(library): Select All reached past the filter into the whole library
    c6720756e  #165  feat(detail): add Automatic Search action to audiobook detail toolbar
    9442bccb6  #165  feat(library): per-item automatic search icon on the audiobooks list
    9c63cc6e4  #165  feat(library): bulk automatic search on the audiobooks index toolbar
    64cba332d  #165  feat(calendar): search for the missing books in the window on screen
    1ac2c57a7  #202  feat(ui): multi-row selection on Wanted and Downloads
    3539d6c68  #289  tests: catch up DownloadsView.selection and WantedView.selection with items 73, 169 and 145
    cf44ae1b7  #174  fix(history): style the events that happen, not four that cannot
    43ee5a479  #174  fix(history): keep what a grab knew, and name the download client
    03a9f156f  #174  feat(history): a global History page over the API that already existed
    45c39a360  #174  tests(download-service): match RecordDownloadFailedAsync's protocol parameter in the sanitize test
    03cebdf78  #209  fix(security): an IPv4 address in its mapped form never matched the private ranges
    d1b5f6da8  #190  fix(myanonamouse): spend the freeleech wedge on the download, not the search
    cbc2b6ee2  #190  refactor(search): give SearchResultConverters its own file
    59f709d26  #190  feat(indexers): parse the release flags trackers advertise
    865c9ff3b  #183  Add a task surface over the periodic workers, with a manual trigger
    e67f8ee19  #183  scheduled tasks: an allowlist for manual runs, not a deny-list
    a5bcee853  #183  scheduled tasks: answer the trigger with the row it started, and keep stopped workers
    96cf1d206  #183  scheduled tasks: say which request started the cycle, and stop 404ing a listed task
    cebf6eaea  #183  scheduled tasks: release the gate under the lock that ends the cycle
    5061e06a3  #183  scheduled tasks: publish the interval in whole seconds, and refuse what will not fit
    8d449bcfe  #183  scheduled tasks: put the interval refusal on the row, and correct two claims made for it
    a5d745842  #183  scheduled tasks: make the interval refusal audible, and stop overclaiming what it buys
    9cd82c476  #183  tasks ui: put the task surface under System, where the family keeps it
    b10fe553b  #183  tasks ui: test the run control against the refusals the API actually answers
    c646aa8ea  #183  tasks ui: fix two in-flight defects, make the refusal reachable, and cover the poll
    4fa38d145  #183  housekeeping: the frame for a daily retention sweep, with no table in it yet
    a4d5c1a3c  #183  housekeeping: three tables, and the two that were left out on purpose
    d8844aa8d  #183  housekeeping: move jobs and their three child tables, with the cascade measured
    f10d779a1  #183  housekeeping: the supersede that never stamped its terminal timestamp, and four comments that overclaimed
    3d933dccf  #285  settings: a Housekeeping section for the two switches item 183 shipped API-only
    e8fe5c8a7  #281  housekeeping: the two deferred populations, and the one slice of each that is provably unreachable
    1dfba86d1  #281  housekeeping: say what the compatibility sweep's query actually costs
    3e8910128  #185  notifications: a subscriber model and a Custom Script provider
    6025c4a29  #185  configuration: keep stored custom scripts when a save omits them
    dfeaebe81  #185  notifications: let the EnableNotifications switch gate the subscriber fan-out
    bbbe498cc  #189  feat(calendar): add the calendar endpoint and iCalendar feed
    8eaed4ece  #189  feat(calendar): give the calendar page a feed subscribe URL
    78a5ed17a  #189  fix(calendar): correct the status precedence and test the two seams
    9bbd0b678  #189  fix(calendar): accept Readarr's tag spelling, and test the rest of the review
    ca73722e4  #189  fix(calendar): close the rest of the parse/query seam, and stop citing what cannot be opened
    9191d0e5f  #189  test(calendar): the case-variant feed is refused by the enforcer now, not the attribute
    44e57a5f5  #189  feat(calendar): resolve per-event status in Readarr/Sonarr precedence order
    adc2d3a8e  #189  feat(calendar): add a first-day-of-week preference, defaulted from locale
    e4f9d2570  #189  feat(calendar): add a legend component for the event status colours
    55ae2a8a8  #189  feat(calendar): colour events by status, add a legend, and a week-start option
    b90023576  #189  test(calendar): reconcile search-missing spec with item 189's status classes
    95155b91e  #79   fix(status): stop reporting a non-preferred format as below cutoff
    cd729a255  #79   test(status): make the agreement test read the real projection, and pin the widened cases
    1d8ca61b2  #252  WIP fix(quality): a blank cutoff means satisfied, not "search forever"
    c55db1d27  #252  test: bring the new cutoff tests up to the repository's test conventions
    f877ccf0f  #252  test: flag the blank-cutoff consequence for the not-yet-landed upgrades flag
    32ce9ce72  #257  fix(quality): resolve the cutoff through QualityMatcher, changing two answers
    0581a7b60  #254  torznab: a bitrate has to be its own token, not digits inside one
    11f2e9266  #254  torznab: tests for the x264 mislabel, each with its control
    a631a1ee2  #254  torznab: a number is a bitrate only where the text says it is
    92f565055  #254  torznab: recover the bracketed bitrate the tightening had lost
    163d664a7  #255  fix(search): let a quality profile's Allowed flag actually gate
    9c2ddb6db  #255  fix(search): correct the claims around the quality gate, and pin what it does not decide
    4eade914f  #255  fix(search): refuse a quality label the gate cannot place
    2d04445f1  #255  fix(search): answer the review of the quality gate fix
    3a0386e01  #253  Break release selection ties deterministically instead of by indexer order
    643182acf  #253  Make the consuming-site tests actually discriminate their own site
    f3a3ecf41  #253  Restore Readarr's protocol step, without which the chain is not transitive
    846ff260a  #253  Close the second intransitivity, and three more review findings
    b94e7888b  #253  Rank once per site instead of twice, to stay inside the focus budget
    bb1aebf34  #253  Put the same-group invariant in the code, and close the last apparatus gap
    5b3c1ee93  #256  quality profile: refuse a cutoff the profile does not allow
    863e64b16  #256  tests: cover the cutoff rule at the API and at the attribute
    2213b91ab  #256  quality profile: make the cutoff rule agree with the code that reads it
    37c8766a1  #256  quality profile: give it the UpgradeAllowed flag the family has
    1590ab3c6  #256  quality profile: carry upgrades-off across the upgrade, in a startup repair
    09d4e2e81  #256  tests: pin both directions of the upgrade flag, each against its control
    512040a7e  #256  status evaluator: drop the upgrades-off guard, it was never the reason
    f45911b8c  #256  tests: pin the new migration id, the backfill gate depends on it
    16aaa2926  #256  quality profile: decide "blank cutoff" the way the readers decide it
    14f581e47  #256  comments: correct three claims a reviewer could check and find false
    a39b9fe90  #261  search: skip the bare-title rung when the title cannot carry a query alone
    af7d2ca7b  #261  search: stop the stem and series rungs unanchoring themselves when there is no author
    8d41283ab  #262  Require indexer categories, the way the rest of the *arr family does
    f44f33853  #262  tests: pin that the draft test is gated too, since it binds the same entity
    3fb59341d  #262  Make the indexer form usable now that categories are required
    0cc732ad5  #262  Move the category default off the entity, where it rewrote stored values
    b4d80dc38  #262  tests: pin that an explicit null category list is not a way past the rule
    14b6504cd  #262  Validate the merged indexer on update, not just the request body
    03696d909  #190  fix(myanonamouse): send the torrent filter as tor[searchType]
    9ddf2418e  #190  fix(myanonamouse): send the page size as perpage, not tor[perpage]
    9912e82be  #190  fix(myanonamouse): send the language the caller asked for
    8a206f121  #190  fix(myanonamouse): stop asking the search for a freeleech wedge
    a7b048cc1  #268  import: give each companion file the source root it actually came from
    3c3e86d9f  #268  import: resolve the companion source roots once per batch, not per file
    0231682b8  #269  manual-import: place companions by the selected files' structure, not the caller's path
    030a83f6d  #269  manual-import: a companion goes where the file it accompanies went, and nowhere else
    520b6f30c  #269  import: match the companion's neighbour by path identity, and name the rule the manual path uses
    cad5e10a8  #269  import: say why the neighbour comparison is safe for the reason that is actually true
    b8327f8d4  #21   tests: give the companion placement pass the root folder this branch now requires
    e5b64d278  #269  fix(manual-import): log the companion files TopDirectoryOnly drops silently
    6b58ae9dd  #269  fix(manual-import): guard the nested-companion scan against a hostile tree
    44b948336  #273  cache: make the author and series cache upserts survive a lost race
    9935ccc2c  #273  cache: do not retry a collision that re-reading cannot resolve
    b2e858614  #273  cache: the author upsert cannot reach that collision on this build, so pin what it does instead
    d73d44dcc  #273  cache: say only once that a rebind was refused, and describe the collision that can actually happen
    5377ecefd  #273  cache: a refusal that first happens on a retry must still be reported
    19aae0dfa  #273  cache: the comment on the test still described the guard that was removed
    85478e3bd  #275  fix(metadata): spell a contributor's name without their job title
    4298c5c80  #275  fix(metadata): store the author first, and keep an edition out of the byline
    da155ede8  #275  fix(metadata): strip contributor role suffixes on rescan too
    f8d6cbdab  #275  test(metadata): use catalogue values for contributor role test data
    48bb56618  #275  fix(metadata): finish the contributor role cleanup on scan, dedupe and the singular author
    62263b531  #275  test(metadata): cite catalogue credits as the catalogue lists them
    0cc373132  #275  fix(metadata): keep matching an author folder that still carries its role
    ff6869e41  #276  fix(metadata): bind an author's ASIN only to a row that names them
    e0d5b3812  #276  metadata: lease the shared request budget to callers that are not a refresh run
    869a71c68  #276  repair: a scheduled pass that corrects author identities already written
    550276537  #276  repair: remove contributor roles from the credits already stored
    32b562e0c  #276  test: give the two new test classes the conventions the suite enforces
    3ce4a1ea1  #276  repair: a provider that did not answer is not evidence that an author has no ASIN
    2e6a55af7  #276  repair: answer the fresh review, including a starvation defect nothing was checking
    2eaeb27a1  #270  filesystem: let a raced pinned hardlink fall back to a copy, and say so
    c70eac489  #270  filesystem: narrow the hardlink race fallback, because the first attempt claimed too much
    123d4d199  #277  Fix the fail-open identifier check in DownloadRemovalWorkflow
    8f04ff49c  #277  Route the downloads delete endpoints through the client removal workflow
    e998fd503  #277  Add tests for the client-removing delete and the identifier check
    67d4b90c8  #277  Make a disabled client deletable again, and fail closed when no client id is known
    3d4276d4e  #277  Correct the outer-catch mechanism described in a test comment
    a6d2d350b  #277  Cover the disabled client, the unmapped record, and deleteFiles on the delete path
    b1bb79130  #277  Let the delete control say whether the download client is involved
    afdf83ee7  #277  Make the record-only way out actually work, and stop it claiming too much
    7fdba8bc5  #277  qbittorrent: check presence before delete, fail closed on idempotent-success
    1ec2f5b47  #277  qbittorrent: cover the unparseable-presence-check-body fail-closed path
    2f6ccf922  #277  qbittorrent: treat a torrent confirmed absent from the client as removed
    345ffcf16  #277  tests: give the qBittorrent cleanup composition tests their Name and Category traits
    055cf53b9  #277  fe(activity): the bulk remove path also defaulted to removing from the client
    96cdec5c3  #276  fe: an Author Identity Repair section in the settings screen
    2a78d0762  #276  fe: the preview card promised a run button the frontend does not have
    c47395670  #276  fe: the review's findings, one of which the UI could not have shown
    b8b6af22d  #278  notifications: give the Custom Script provider a settings surface
    15be9d1d4  #286  notifications: an Email provider, the case a webhook type cannot express
    49f81031a  #286  notifications: give the Email provider a settings surface
    ef7abb8b2  #286  notifications: assert both providers are actually wired into the container
    d2998fa85  #286  notifications: act on review of the Email provider
    5538fe8d9  #286  notifications: cover the sentinel guard on a database with no settings row
    e8ee0d2fa  #286  configuration: move the email members into their own partial
    c9f0a2cfa  #286  notifications: exercise the SMTP transport against a socket, not a mock
    d1a030578  #286  notifications: act on the second review of the Email provider
    edb345d1e  #286  configuration: split startup config out of ConfigurationService, so the file is under the cap again
    e3b608758  #177  feat(download-clients): one selector, with a priority the operator sets
    53c4067f7  #177  test(download-clients): pin the selection policy and the migration default
    1d3af852a  #177  settings(download-clients): a Client Priority control, so the field is reachable
    252cab9f0  #177  test(download-clients): pin the rotation cursor's lifetime, with a control that moves
    03928c119  #177  fix(download-clients): four things a fresh review found
    c910e4e7a  #177  settings(download-clients): give Client Priority its own section, keep queue priority usenet-only
    a5395d7dc  #177  test(indexers): pin per-indexer download client binding, failing first
    29caef02d  #177  indexers: give an indexer a nullable download client binding (shape only)
    90c8cb0e7  #177  indexers: route a bound indexer's grabs to its client, and fail loudly
    da2e2e39c  #177  test(download-clients): pin persisted client failure status, failing first
    ddcfac303  #177  download-clients: persisted client failure status, shape only
    c367c7dda  #177  download-clients: record client failures, back off, and route around a blocked client
    3520eedbe  #177  download-clients: record status in a gateway subclass, not a wrapper
    2a06c3ebe  #177  stack: make item 177's two migration Designers the cumulative model at their position
    1c8a11f44  #179  backup: take a copy of the database before migrations run
    546b4ac89  #179  backup: the settings column, and two defects the suite caught
    0fe6d466a  #179  backup: tests, each paired with a case that comes out differently
    92897b415  #179  backup: the operator surface, so this is reachable without a shell
    22b762383  #179  docs: describe the backups directory and the startup override
    fd7f2e46a  #179  backup: take the copy before the startup path's own repair writes
    f1e65d456  #179  backup: cover what the review found had no test
    2b79317cf  #179  wip: retarget the auth test
    2d12dfc38  #179  backup: close the gaps the review found in the code and in its tests
    08615a59c  #179  backup: fix what the second review found, and rethink the manual limit
    da533c645  #179  backup: pin the negative retention case
    fc3be1771  #180  feat(indexers): per-indexer seed ratio and seed time
    e42c0fb07  #180  fix(indexers): close review findings on seed criteria
    22451a6de  #181  search: the release score has no ceiling, so a preference can still decide
    9ef5c06b9  #181  tests: SearchResultScorerCeilingTests follows the repository test conventions
    1f30aec99  #181  settings: the minimum score threshold is no longer capped at 100
    006926374  #181  search: correct what this branch claims, after review
    c49753b3e  #264  search: the profile's size and quality gates apply to Usenet results too
    3c6fbdb0d  #264  search: size bounds above 2047 MB, and three corrections to the commit before this
    658a6ce56  #264  search: reconcile the NZB gate hoist with the tests that pinned the exemption
    7e606d002  #263  search: release selection reads the quality profile, not the hardcoded ladder
    282a01882  #263  search: take the best candidate that is an upgrade, and fix two things in the commit before this
    30e287334  #181  search: split the rejection gates out of the scorer, so the file is under the cap again
    eb53b09f4  #181  search: two warnings, one of them a control this stack no longer has
    6b3d1ee3f  #182  naming(#182): render the settings naming preview through the real backend renderer
    38730ba4f  #182  naming(#182): wire the settings naming preview to the backend renderer
    5362a790c  #182  naming(#182): fix real divergences a fresh review found from GenerateFilePathAsync
    59e949d64  #182  naming(#182): flip the lowercase-token preview test now that the case fix is in the tree
    15fbcf504  #184  tests(notifications): pin the item-184 gaps blocked on PR #943 (WIP)
    c26e9aa5a  #184  tests(notifications): tighten item-184 gap pins after fresh review
    645646d74  #187  feat(#187): free-space check before import (G7)
    c2b9c7cf2  #187  feat(#187): free-space on root folders (G9), tests, and settings UI (G7/G9)
    4e72180a5  #187  fix(#187): the free-space guard silently never fired under the default naming pattern
    ef21c6f02  #187  test(#187): prove required bytes are the real file size, not a stand-in like #821's 64
    22303e83d  #284  Add failing test for metadata repair guard missing Entries include
    4af96390b  #284  Fix metadata repair guard to load MoveJob Entries and CreatedDirectories
    7fefa42dc  #283  tests: add failing control for ProcessExecutionLogs table drop
    b3e984111  #283  Drop the dead ProcessExecutionLogs store, repository, entity, and table
    9a0d81829  #237  fix(search): reconcile the query ladder with fakes and tests from three unrelated items
    75871969f  #237  test(startup): mock the metadata-refresh backfill this test's strict repository was missing
    87246d5a4  #237  style: run prettier over item 201's two touched files
    1ea806969  #237  fix(search): reconcile the scorer 3-way with fakes and tests from unrelated items, plus two more real bugs found by the suite
    22a360d7b  #237  stack: reconcile the manual-run allowlist with the stacked tree
    69abe1adc  #237  tests: read the observation's results, the way its sibling case already does
    83369a27c  #237  tests: point the tiebreak harness at the signatures the stack actually has
    04ca6fc2e  #237  tests: move the blank-cutoff pin to the answer fix/blank-cutoff-search-loop gave it
    8ac0e652b  #237  downloads: split the client-name projection out, so the controller is under the ceiling again
    602b854a0  #237  tests: guard that every migration Designer is its predecessor plus its own Up
    610e4eb77  #237  stack: make every composed migration Designer the cumulative model at its position
    51add67f7  #212  tests: pin the no-audio-candidate import block from #890
    395ddc211  #212  fix(import): admit audio-only .mp4 via a probed ambiguous-container tier
    981f75a8b  #212  fix(import): let manual import admit an audio-only .mp4
    4ceef7eaf  #212  tests: move the #890 cases out of a file PR 993 is rewriting
    8fe4be0d4  #212  refactor(files): move the content gate into its own partial, so it fits beside #901
    13af5e88e  #191  qbittorrent: failing spec for postImportCategory form field
    b1fb49c48  #191  qbittorrent: add Post-Import Category field to the client form
    ffd660dd9  #288  symlink-chain: fail-first tests for a two-or-more-hop chain
    f539fc28e  #288  symlink-chain: walk the whole chain, not just the last segment
    a4e18cf4b  #237  stack: make the three Designers after item 170's migration the cumulative model again
    acfea313b  #293  fix: grid containers shrink to content instead of forcing viewport height
    59dcace57  #293  fix(activity): virtual row stride must not be shorter than the rendered row
    0b1920258  #293  fix(activity,wanted): measure the virtual row stride from a rendered row
    11800cfef  #279  metadata-refresh: allow the scheduled walk onto the task surface's Run button
    e7c0ada4d  #146  manual-import: reject items missing a matched audiobook id with 400
    c41afaa77  #146  tests: follow the repository test-class conventions
    ccca0b569  #215  fix(import): refuse a manual import on its extension before publishing it
    833a92c43  #215  fix(import): say that extensionless sources are refused too, and pin it
    2bd3f42e5  #292  search: WIP failing tests for the requested-book release check
    b15f19319  #292  search: drop releases that are plainly for a different book before an auto-grab
    e3913a35e  #292  test(search): give the blocklist wiring fixture a release for its own book
    a03a9d90c  #292  search: fix the requested-book check's author and short-title rules from review
    a9c2262f3  #292  search: a bare number after the title is a different volume, and only a year is packaging
    30af5246c  #310  test(search): series-entry cases for a release honestly titled for a different volume
    b22c68f88  #310  search: reject a release whose own series position disagrees with the catalog's
    aa092317d  #310  test(search): a release honestly for a fractional series entry, caught in review
    ff5cf760d  #310  search: parse a release's fractional series position instead of dropping it
    46a8436db  #310  test(search): pin the thousands-separator exclusion on the series-position parse
    bb2f29888  #356  test(search): pin item 356's whole-number-only series-position tolerance
    eb30f4e58  #356  search: fail open on series-position comparison when either side is fractional
    f4d11498a  #139  fix(library): identify a series by its SeriesAsin, not its display name
    464083951  #139  fix(library): series identity fixes from review: non-Latin names, author guard, stale links
    4d98c8c8d  #139  fix(library): series author guard joins on a shared author, script-preserving
    dd63ac90b  #291  fix(search): give the indexer backoff write a context of its own
    1eded1d69  #291  test(search): assert which context the backoff write used, not whether it collided
    b8bc155a2  #215  import: move the tag enrichment out of ProcessItem, so the file is under the cap again
    5b1285506  #295  health: report indexers in failure backoff, and separately those failing for more than six hours
    0161e8196  #295  search: tell an indexer that could not be asked from one that had nothing
    9f0c69bbf  #295  ui: count indexer health checks on the sidebar's System entry
    8b67874b6  #295  search: no default for the outcome search, and tidier could-not-search reasons
    84d19d639  #295  ui: show the System health badge with authentication off, and re-read it every minute
    2ee0166f7  #81   settings: add a stalled-download timeout, off by default
    61431b5bb  #81   downloads: fail a torrent that has made no progress for the stalled-download timeout
    7948dbdd5  #81   ui: stalled download timeout field under failed download handling
    498e95ce5  #81   test: a pause shorter than the gap cap still clears the stall clock
    651c62cee  #223  search: one quality ladder instead of four copies
    100428a85  #223  test(scoring): pin the ladder's rung order and every rung literal
    120fa9578  #63   search: make Prefer newer releases a real ranking bonus
    e42e76054  #63   ui: say what Prefer newer releases now does, and pin Maximum Age across a toggle
    3a1f9c08d  #63   search: cap the newer-release bonus below a preferred word, and keep it off rejected releases
    0470b8164  #63   ui: describe the smaller newer-release bonus, and check Maximum Age is visible
    b5a551a4d  #63   search: default PreferNewerReleases to false, matching the settings modal
    644369931  #218  fix(scan): report a scan whose files were all declined as CompletedNoFilesAccepted
    1f9dbbd10  #218  fix(ui): show a scan that accepted no files as its own outcome
    fa8196a82  #218  fix(scan): record a diagnostic when a candidate's metadata is read and declined
    4aa146327  #218  fix(metadata): name the file in the ffprobe-failure warning
    41e35b671  #218  fix(scan): count only files this book could claim, and skip other books' files
    043c2b1f9  #218  fix(ui): label a no-files-accepted scan on the detail view's Files tab
    4c1b81813  #218  fix(scan): probe other books' files again so the metadata conflict guard is unchanged
    35b5d9778  #218  fix(scan): decide no-files-accepted on durable ownership, not attribution
    81820e278  #218  test(scan): reproduce a folder-attributed file whose tags name another book (red)
    7a3d18b97  #218  fix(scan): decline folder-attributed files whose tags name another book
    1984ca6ac  #218  test(scan): update probe-count and swap-timing expectations for the new pass
    af67121cf  #218  test(scan): give the tracked-file guard a real identity and a Times.Never
    bfe303fa6  #3    791 option D (NuGet ffprobe) composed onto the stack, for validation on the install
    8e2b578c1  #309  test: assert FailedDownloadAutoSearch defaults to true
    1cb79caec  #309  fix: default FailedDownloadAutoSearch to true, matching the *arr family
    42edae4bf  #298  test(redaction): reproduce ffprobe JSON corruption from RedactText fallback
    0de977dbd  #298  fix(redaction): drop generic fallback that corrupted unrelated text
    04966def0  #299  fix(library): reject an unrecognized refresh-metadata field instead of silently upgrading to whole-library (#299)
    fc765e44e  #299  fix(library): catch the deserialize JsonException so a wrong-typed field 400s, not 500s
    d1746f3e7  #69   test(downloads): extend the seed-limit removal gate tests to the full matrix
    6b54dfdc7  #69   fix(downloads): make CanRemove reach the seed-limit evaluator on the queue-poll path
    e150f3356  #69   test(downloads): cover the exact ratio == limit boundary for both clients
    582d7bdc9  #69   test(downloads): pin the fail-closed seed policy on the queue-poll path
    92cd8fc2b  #317  fix(imports): retry a failed file import instead of blocking at once
    db90e4794  #317  fix(imports): use the configured retry delay on the file-import retry
    0a6bb1ac9  #317  fix(downloads): finalize a held completion whose client item is gone
    9d213c0ce  #324  fix(downloads): isolate OnDownloadFailed's blocklist/removal/search steps so one exception doesn't abort the rest
    a7edadc8b  #326  test(qbittorrent): add numeric-tolerance coverage for the response mapper helpers (#1008)
    cf8cf7778  #326  fix(qbittorrent): tolerate every numeric JSON shape in the response mapper (#1008)
    892a30d13  #326  test(qbittorrent): malform state instead of downloaded in the unreadable-torrent fixtures (#1008)
    c170d5539  #218  fix(scan): log per-file MetadataContradictsPath diagnostics on CompletedNoFilesAccepted
    0470a4168  #325  fix(downloads): record History entry when orphan cleanup removes a download
    1c9a82d1d  #340  fix(downloads): record History entry when DownloadRemovalWorkflow removes a download
    1e8fee67e  #367  fix(downloads): a successful Removed History row no longer carries a non-null Error
    d5205bba7  #333  fix(nzbget): classify DeleteStatus-family history entries as Failed, not Ignored
    27dcc527e  #333  fix(nzbget): correct Sonarr/Readarr citation line numbers in comments
    740b48b84  #335  feat(blocklist): add a list-everything endpoint for the frontend page
    302aea145  #335  feat(blocklist): build the Blocklist page (list, remove, nav entry)
    dff7099e0  #336  settings: add RejectClearlyMusicReleases to ApplicationSettings
    13bfb02de  #336  search: gate clearly-music releases in SearchResultScorer when enabled
    f70a99978  #336  fe: add "Reject releases that are clearly music" search setting toggle
    fcdcd6837  #336  test: extend the post-canary migration allow-list for the new migration
    e1061b244  #336  fix: Audiobooks id must override even alongside another Audio-range id
    c1b4240c8  #353  fix(blocklist): require confirmation before removing a blocklist entry
    0ec97c3f9  #361  search: strengthen MusicReleaseClassifier with weighted signals (tracker #361)
    0a276ee45  #361  search: fix 6 false positives found by independent review (tracker #361)
    f06b447b3  #361  search: fix 6 more false positives, round 2 of independent review (tracker #361)
    475b35b0f  #361  search: fix 4 more false positives, round 3 of independent review (tracker #361)
    927cb8b59  #361  search: fix 4 more false positives, round 4 of independent review (tracker #361)
    97f8ad436  #361  search: gate the bitrate/FLAC signal on a music-specific corroborator (tracker #361)
    2f2959e96  #361  search: drop Artist/Album consistency from the bitrate gate (tracker #361)
    8e8e068b3  #361  search: require two-signal convergence to gate bitrate/FLAC (tracker #361)
    70feb4698  #361  search: gate bitrate/FLAC on an actual music category only (tracker #361)
    fc065299c  #361  search: remove the bitrate/FLAC/kbps signal as dead code (tracker #361)
    95c32f5c2  #371  fix(library-import): decouple manual-search ASIN detection
    6106f6886  #371  chore: replace non-corpus identifiers in comments and tests with placeholders
    6ca1721cf  #371  test(metadata): use catalogue values for contributor role test data
    62e8c4aa2  #371  test: take series and diacritics test data from the catalogue
    7b2f0a0be  #366  fix(metadata): bare-anywhere-wins for author/contributor classification
    08efacdcc  #366  fix(metadata): close the author-credit vocabulary gap and the Introduction asymmetry
    1510e0cd8  #366  feat(metadata): add a standalone organization-as-author detector
    c36eb394c  #366  fix(metadata): apply the author-credit classifier at the 3 missing call sites
    877de6604  #357  fix(nzbget): bound the unbounded history fetch, item 357
    343b65f04  #357  fix(settings): reject DownloadClientHistoryLimit below 1, item 357 follow-up
    6a2e1a8d5  #237  test(downloads): count mapping reads per queue poll and pin the per-client cache contract
    e9011f901  #237  fix(downloads): serve a client's path mappings from the cache it already invalidates
    7938fb2f4  #237  test(downloads): cover a write racing a cold mapping read and a mapping moved between clients
    713665ad4  #237  fix(downloads): invalidate a client's cached mappings under the fill lock and clear both owners on a move
    cdd36d882  #237  fix(downloads): skip the failure row for a qBittorrent duplicate
    6df80fcd0  #237  fix(qbittorrent): send an explicit false for Initial State Start
    858ee9c33  #237  test: use neutral book titles in the fixtures this branch adds
    eae57319a  #237  test: use public-domain corpus titles in the fixtures this branch adds
    314867ee8  #237  fix(qbittorrent): a skipped torrent marks the snapshot incomplete
    819138e6a  #237  fix(qbittorrent): an open circuit breaker still fails the poll
    03f79d033  #237  test(qbittorrent): drive both fixes through the monitor cycle
    5c43e8bd2  #237  test(qbittorrent): use host-valid save paths in the gateway-level fixtures
    1d4814426  #237  stack: #1021 take the head's comment wording for per-client-type HTTP handlers
    104dacda6  #237  refactor(quality): move QualityMatcher's helpers to a partial file
    d95111fdc  #237  fix(search): keep refusing a codec the ladder has no rung for, as canary did
    adf995d9d  #237  stack: #1023 take the head's wording for two scorer comments
    0d6756e22  #237  fix(notifications): redact discordapp.com and pushbullet:// webhook URLs
    c07557317  #237  fix(logging): keep known env-secret masking in SanitizeWebhookUrl
    c30277c0b  #237  test(logging): serialize env-mutating redaction tests
    26c380c70  #237  fix(manual-import): authorize companion files against the root folder
    4ec3a73ef  #237  test: use neutral book titles in the fixtures this branch adds
    bf4ad773a  #237  test: use public-domain corpus titles in the fixtures this branch adds
    1682b094b  #237  stack: #1022 give the hardlink ASIN-tag test a metadata-writing lease on a real publication
    3f4b80ae4  #237  stack: reconcile the stalled-download timeout tests with the settings controller
    a561dbe6b  #237  stack: reconcile the history-limit settings test with the settings controller
    d853f7034  #237  stack: reconcile the NZBGet history-entry test helper with required members
    9496c5085  #237  stack: reconcile two scan test files with the scan command upstream reshaped
    ae9d93e9a  #237  stack: reconcile the Designer-chain guard's sentinels without the recycle bin
    d4f9624d8  #237  stack: reconcile the pinned migration list without the recycle-bin migration
    d0297a78d  #237  stack: reconcile upstream's released-schema upgrade tests with this stack's columns
    40ecc9e27  #237  stack: reconcile #914's import-tag tests with leases that withhold metadata writes
    cb951ac2f  #237  stack: reconcile the hardlink fallback tests with upstream's publication catch
    8fe068966  #237  stack: reconcile #902's refresh regression test comment with #993's snapshot
    3a1d34856  #237  stack: reconcile the symlinked-source tests with content-only source proofs
    178aa4d48  #237  stack: reconcile the Wanted selection specs with upstream's Search All confirmation flow
    eb5d7d0e1  #237  stack: reconcile the WantedView spec formatting after the row stride test
    d244d018e  #237  stack: reconcile the detail view spec's signalRService import
    e4c1d2193  #237  stack: reconcile the download settings spec with the stalled download timeout input
    261579068  #237  stack: reconcile upstream's empty-send download test with the search query plan
    dad20c3ce  #237  stack: reconcile the companion log-sanitization test with the provider it rebuilds
    6542d2701  #237  stack: reconcile DownloadMonitorService with the 500-line cap
    ab0b1f4c2  #237  stack: reconcile RootFoldersController with the 500-line cap
    0064da6b2  #237  stack: reconcile AudiobookFileService with the 500-line cap
    231559649  #237  stack: reconcile upstream's SQLite-reload refresh test with the stack's UTC read converter
    d563557e0  #68   fix(filesystem): resolve a linked source ancestor only when no link in its chain sits inside a configured root
    0f4b34344  #68   test(filesystem): pin symlinked source ancestors against the configured roots
    6472411a4  #109  stack-image: fail the build if the version stamp is stale, not just missing
    e5b5aa4d1  #109  stack-image: anchor the version/patches grep to the manifest header
    76c99d071  #383  fix(delete): keep the recursive folder delete unless every tracked file is proven (#1071)
    cea654948  #383  fix(delete): keep the recursive delete for durably owned folders (#1071)
    c235b06f2  #237  stack: import AuthorCredits where the rescan role-suffix fix was ported
    c27bb7c8b  #237  stack: give the upgrade-allowed and embed-cover-art migrations IDs after the upstream frontier
    813dab953  #237  stack: point upstream's empty-query and fallback-query tests at this stack's search surface
    1d02a6fd0  #237  stack: rebuild the migration Designer chain for v1.5.9's order
    da30488ff  #237  stack: reconcile two files with the 500-line cap after v1.5.9
    b4e89aae6  #237  stack: reconcile #1021's and upstream's new tests with this stack's search and queue shapes
    dd3c416ae  #237  stack: let the refresh-cycle spend test walk two regions under #1043's rule
    3b026f8f0  #237  stack: order line 267's bare-anywhere-wins tests by bare credit, as #1013 now does
    1237a2bd0  #237  stack: queue reconciliation tests mock the snapshot API the poller now calls
    7d2b3218a  #68   stack: adapt the nested-symlink source test to item 68's behaviour
    e292d9169  #237  stack: pin System.Security.Cryptography.Pkcs to 10.0.13, the first version outside two published advisories
    8158000cf  #-    style(fe): run prettier over the 12 stack files that drifted from it
    c96948e61  #-    fix(nzbget): hold DELETED/COPY and DELETED/DUPE as client duplicates, not failures
    8b723f5da  #-    fix(downloads): reuse the blocklist filter's identity check for client-duplicate holds
    d2224a9d3  #-    fix(downloads): keep client-duplicate holds out of failure alerts and retry-import
    c5b92854e  #-    Pin stored webhook type over URL sniffing in dispatch
    22f621941  #-    fix(import): fail the job when the import refuses with a conflict
    4ab86ff07  #-    Take the pinned directory open failure message from the caller
    7a5d34475  #-    Defer metadata rescan candidates per file so failures cannot starve the batch
    ba6f5fecd  #-    Don't wait the per-book search delay for books that are not searched
    ad410dbd3  #-    Repeat the pre-search checks after the per-book wait
    d79c1b412  #-    stack: reconcile two files with the 500-line cap after the ff6 merges
    e5f80c2d5  #-    stack: point the search-delay and import-conflict tests at this stack's surfaces
    d24ca768d  #-    stack: drop the second copy of #843's read-write stream pieces
    fe1cecfc3  #-    stack: carry the test adaptations the canary merges made into the linear stack
    c314c107f  #-    stack: write the failed-download blocklist entry through FailedDownloadBlocker
    9ccbcfb95  #-    stack: clear the findings the backend format check reports

## How this stack was built

A linear rebuild on upstream canary 1.7.2 (df24a648f), replacing the published stack d7ed6aee1, which was
192f5a9bd (1.6.3) plus our patches with four upstream canary merges and three fix-branch merges folded in. Every
non-merge commit of d7ed6aee1 not on upstream was replayed in order with `cherry-pick -x` (rerere off), except:
the 15 #918 blocklist commits upstream now carries patch-identically (item 51), item 167's two commits (upstream
#1150 ships the same whole-segment ".." rule with its own tests), and the seven earlier manifest commits. Commits
whose content upstream already supplies became empty and were skipped by git. Each item number above is the one the
round 27 manifest gave the commit this one was picked from (the last `cherry picked from` trailer); '#-' marks the
commits added since round 27 and the stack-only reconcile commits. The tree before this manifest commit equals the
tree of a merge of 1.7.2 into d7ed6aee1, except for the format-check commit, which is the last patch above.

Regenerate with tools/local_stack.sh in the tracker repo once tools/stack.items is redeclared for 1.7.2.
