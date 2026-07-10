from pathlib import Path

root = Path(__file__).resolve().parents[1]
path = root / "listenarr.infrastructure/Library/Moving/AudiobookContentMoveService.Persistence.cs"
content = path.read_text(encoding="utf-8")
old = "using Listenarr.Infrastructure.Persistence;\n"
new = "using Listenarr.Domain.Common;\nusing Listenarr.Infrastructure.Persistence;\n"
if content.count(old) != 1:
    raise RuntimeError("Persistence import anchor mismatch")
path.write_text(content.replace(old, new, 1), encoding="utf-8", newline="\n")
