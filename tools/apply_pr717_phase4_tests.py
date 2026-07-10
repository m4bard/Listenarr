from pathlib import Path

# This script intentionally replaces the historical FK-assumption test with a
# deterministic SQLite delete failure so transaction rollback is exercised.
root = Path(__file__).resolve().parents[1]
path = root / "tests/Features/Infrastructure/Persistence/RootFolderReassignmentTransactionTests.cs"
content = path.read_text(encoding="utf-8")
old = """            db.RootFolderRelocations.Add(new RootFolderRelocation
            {
                RootFolderId = sourceRoot.Id,
                SourcePath = sourcePath,
                TargetPath = targetPath,
                DesiredName = \"Historical relocation\",
                Status = RootFolderRelocationStatus.Completed,
                CompletedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
"""
new = """            var triggerSql =
                \"\"\"
                CREATE TRIGGER prevent_root_reassignment_delete
                BEFORE DELETE ON RootFolders
                WHEN OLD.Id =
                \"\"\"
                + sourceRoot.Id
                + \"\"\"

                BEGIN
                    SELECT RAISE(ABORT, 'forced root delete failure');
                END;
                \"\"\";
            await db.Database.ExecuteSqlRawAsync(triggerSql);
"""
if content.count(old) != 1:
    raise RuntimeError("Rollback test failure anchor mismatch")
content = content.replace(old, new, 1)

assertion_old = """        await Assert.ThrowsAsync<DbUpdateException>(() =>
"""
assertion_new = """        await Assert.ThrowsAsync<Listenarr.Application.Common.PersistenceException>(() =>
"""
if content.count(assertion_old) != 1:
    raise RuntimeError("Rollback exception assertion anchor mismatch")
content = content.replace(assertion_old, assertion_new, 1)

path.write_text(content, encoding="utf-8", newline="\n")
