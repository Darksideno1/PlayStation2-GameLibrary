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
code = (root / 'src/PlayStation2GameLibrary/MainWindow.xaml.cs').read_text(encoding='utf-8')
handlers = set(re.findall(r'\b(?:Click|TextChanged|SelectionChanged)="([A-Za-z_][A-Za-z0-9_]*)"', xaml))
methods = set(re.findall(r'\b(?:private|public|protected)\s+(?:async\s+)?(?:void|Task(?:<[^>]+>)?|[A-Za-z_][A-Za-z0-9_<>?]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(', code))
assert not handlers - methods, f'Unresolved XAML event handlers: {handlers - methods}'
project = (root / 'src/PlayStation2GameLibrary/PlayStation2GameLibrary.csproj').read_text(encoding='utf-8')
assert 'CopyToOutputDirectory="PreserveNewest"' in project and 'schema.sql' in project
print(f'PASS: schema executes, {len(required)} required tables exist, {len(handlers)} XAML event handlers resolve, schema deployment is configured.')