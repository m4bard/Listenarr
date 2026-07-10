from pathlib import Path

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
new = """            await db.Database.ExecuteSqlRawAsync(
                \"\"\"
                CREATE TRIGGER prevent_root_reassignment_delete
                BEFORE DELETE ON RootFolders
                WHEN OLD.Id = {0}
                BEGIN
                    SELECT RAISE(ABORT, 'forced root delete failure');
                END;
                \"\"\",
                sourceRoot.Id);
"""
if content.count(old) != 1:
    raise RuntimeError("Rollback test failure anchor mismatch")
path.write_text(content.replace(old, new, 1), encoding="utf-8", newline="\n")
