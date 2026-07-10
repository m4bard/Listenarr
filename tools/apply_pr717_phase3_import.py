from pathlib import Path

root = Path(__file__).resolve().parents[1]

persistence_path = root / "listenarr.infrastructure/Library/Moving/AudiobookContentMoveService.Persistence.cs"
persistence = persistence_path.read_text(encoding="utf-8")
persistence_old = "using Listenarr.Infrastructure.Persistence;\n"
persistence_new = "using Listenarr.Domain.Common;\nusing Listenarr.Infrastructure.Persistence;\n"
if persistence.count(persistence_old) != 1:
    raise RuntimeError("Persistence import anchor mismatch")
persistence_path.write_text(
    persistence.replace(persistence_old, persistence_new, 1),
    encoding="utf-8",
    newline="\n",
)

recovery_path = root / "listenarr.infrastructure/Library/Moving/AudiobookContentMoveService.Recovery.cs"
recovery = recovery_path.read_text(encoding="utf-8")
recovery_old = "using Listenarr.Domain.Common;\n"
recovery_new = "using Listenarr.Domain.Common;\nusing Microsoft.Extensions.Logging;\n"
if recovery.count(recovery_old) != 1:
    raise RuntimeError("Recovery import anchor mismatch")
recovery_path.write_text(
    recovery.replace(recovery_old, recovery_new, 1),
    encoding="utf-8",
    newline="\n",
)
