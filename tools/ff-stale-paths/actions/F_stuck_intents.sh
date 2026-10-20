# After a candidate delete-with-files refused (500, Planned intent kept): restart happens by
# starting this run; then retry the same delete, remove-from-library only, bulk delete, and a scan.
api GET /system/ready > "$RUN/ready.json"
api GET /library/9 > /dev/null
api DELETE "/library/9?deleteFiles=true" > /dev/null
api DELETE "/library/9?deleteFiles=false" > /dev/null
api POST /library/delete-bulk '{"ids":[7]}' > /dev/null
api POST /library/11/scan '{}' > /dev/null
sleep 15
api GET /library/11/files-debug > /dev/null
api DELETE "/library/11?deleteFiles=false" > /dev/null
