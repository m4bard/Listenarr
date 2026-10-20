"""Plan and apply re-links of stale Listenarr AudiobookFiles paths.

PLAN is read-only: it works on a private copy of the database and only reads the library
tree. APPLY takes a reviewed plan, writes the database in one transaction while the
service is stopped, and never touches library files in either mode.
"""

TOOL_NAME = "listenarr-relink"
PLAN_FORMAT_VERSION = 3
