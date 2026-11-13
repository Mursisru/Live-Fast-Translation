from pathlib import Path
p = Path(__file__).with_name('test_e2e.ps1')
data = p.read_text(encoding='utf-8-sig')
p.write_text('\ufeff' + data.lstrip('\ufeff'), encoding='utf-8')
print('bom_ok')
