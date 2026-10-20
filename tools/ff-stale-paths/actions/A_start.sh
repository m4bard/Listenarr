# Measure 1 + 5: first start; reads now, hold through the 5/10-minute timers, reads again.
source "$(dirname "${BASH_SOURCE[0]}")/reads.sh"
reads_pass start
api GET /system/tasks > "$RUN/tasks.json"
sleep "${FF_A_HOLD:-660}"
reads_pass after-timers
