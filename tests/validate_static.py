from pathlib import Path
import re, sqlite3
root = Path(__file__).resolve().parents[1]
schema = (root / 'database/schema.sql').read_text(encoding='utf-8')
con = sqlite3.connect(':memory:')
con.executescript(schema)
tables = {row[0] for row in con.execute("SELECT name FROM sqlite_master WHERE type='table'")}
required = {'Game','GameAlias','Release','Disc','ReleaseLanguage','Source','SourceRecord','EntitySourceLink','ReviewQueue','CollectionCopy','CopyMedia','OplTestHistory'}
assert required <= tables, f'Missing schema tables: {required - tables}'
xaml = (root / 'src/PlayStation2GameLibrary/MainWindow.xaml').read_text(encoding='utf-8')
code_dir = root / 'src/PlayStation2GameLibrary'
code = '\n'.join(path.read_text(encoding='utf-8') for path in sorted(code_dir.glob('MainWindow*.cs')))
handlers = set(re.findall(r'\b(?:Click|TextChanged|SelectionChanged|MouseDoubleClick)="([A-Za-z_][A-Za-z0-9_]*)"', xaml))
methods = set(re.findall(r'\b(?:private|public|protected)\s+(?:async\s+)?(?:void|Task(?:<[^>]+>)?|[A-Za-z_][A-Za-z0-9_<>?]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(', code))
assert not handlers - methods, f'Unresolved XAML event handlers: {handlers - methods}'
project = (root / 'src/PlayStation2GameLibrary/PlayStation2GameLibrary.csproj').read_text(encoding='utf-8')
assert 'CopyToOutputDirectory="PreserveNewest"' in project and 'schema.sql' in project
print(f'PASS: schema executes, {len(required)} required tables exist, {len(handlers)} XAML event handlers resolve across MainWindow partials, schema deployment is configured.')
