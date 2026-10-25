"""Shared package-content gate. Local development instructions are never product files."""
from pathlib import Path, PurePosixPath
from zipfile import ZipFile

def forbidden(name):
    parts=PurePosixPath(str(name).replace('\\','/').lower()).parts
    leaf=parts[-1] if parts else ''
    private_extension=leaf.endswith(('.pfx','.p12','.pem','.key','.crt','.cer','.der','.p7b','.keystore','.jks','.dpapi','.db','.db-wal','.db-shm','.sqlite','.sqlite3','.log','.har'))
    return private_extension or any((p=='local.properties' or p=='.env' or p.startswith('.env.') and not p.endswith('.example')) for p in parts) or any(p in {'agents.md','project-guidance','.tools','.git','private-evidence','private-research','companion-private','profiles','test-results','node_modules','.gradle','build'} for p in parts)

def assert_names(names):
    rejected=[str(n) for n in names if forbidden(n)]
    if rejected:raise ValueError('Development/private content in package: '+', '.join(rejected[:8]))

def assert_tree(folder):
    root=Path(folder)
    assert_names(p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file())

def assert_archive(path):
    with ZipFile(path) as archive:assert_names(archive.namelist())
