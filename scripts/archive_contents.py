"""Read distributable containers without extracting paths onto the host."""
from pathlib import Path, PurePosixPath
from zipfile import ZipFile
import io, stat, tarfile

def safe_name(name):
    normalized=name.replace('\\','/')
    parts=PurePosixPath(normalized).parts
    if normalized.startswith('/') or '..' in parts or any(':' in p for p in parts):
        raise ValueError('Unsafe archive entry')
    return normalized.removeprefix('./')

def _tar(data,prefix=''):
    with tarfile.open(fileobj=io.BytesIO(data),mode='r:*') as archive:
        for entry in archive:
            name=safe_name(entry.name)
            metadata=(name+' '+entry.uname+' '+entry.gname+' '+repr(entry.pax_headers)).encode()
            yield prefix+name+'#metadata',metadata,True
            if entry.isdir():continue
            if not entry.isfile():raise ValueError('Archive links and device entries are not permitted')
            stream=archive.extractfile(entry)
            if stream is None:raise ValueError('Unreadable archive entry')
            yield prefix+name,stream.read(),False

def archive_entries(path):
    path=Path(path)
    if path.suffix.lower()=='.zip':
        with ZipFile(path) as archive:
            yield 'archive-metadata',archive.comment,True
            for entry in archive.infolist():
                name=safe_name(entry.filename)
                if stat.S_ISLNK(entry.external_attr>>16):raise ValueError('Archive links are not permitted')
                yield name+'#metadata',entry.filename.encode()+entry.comment+entry.extra,True
                if not entry.is_dir():yield name,archive.read(entry),False
    elif path.name.endswith(('.tar.gz','.tgz','.tar.xz','.tar')):
        yield from _tar(path.read_bytes())
    elif path.suffix.lower()=='.deb':
        data=path.read_bytes()
        if not data.startswith(b'!<arch>\n'):raise ValueError('Invalid Debian archive')
        offset=8;seen=set()
        while offset<len(data):
            header=data[offset:offset+60]
            if len(header)!=60 or header[58:]!=b'`\n':raise ValueError('Invalid Debian member header')
            name=safe_name(header[:16].decode('ascii').strip().rstrip('/'))
            size=int(header[48:58]);offset+=60;body=data[offset:offset+size]
            if len(body)!=size or name in seen:raise ValueError('Truncated or duplicate Debian member')
            seen.add(name);offset+=size+(size%2)
            yield name+'#metadata',header,True
            if name=='debian-binary':
                if body!=b'2.0\n':raise ValueError('Unsupported Debian format')
                yield name,body,False
            elif name.startswith('data.tar'):yield from _tar(body)
            elif name.startswith('control.tar'):yield from _tar(body,'DEBIAN/')
            else:raise ValueError('Unsupported Debian archive member')
        if 'debian-binary' not in seen or not any(n.startswith('data.tar') for n in seen) or not any(n.startswith('control.tar') for n in seen):raise ValueError('Incomplete Debian archive')
    else:raise ValueError('Unsupported archive format')

def is_archive(path):
    return str(path).lower().endswith(('.zip','.tar.gz','.tgz','.tar.xz','.tar','.deb'))
