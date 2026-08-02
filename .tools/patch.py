import io

def edit(p, pairs):
    """Substitui em arquivo com qualquer fim de linha, preservando o do arquivo."""
    s = io.open(p, encoding='utf-8-sig', newline='').read()
    crlf = '\r\n' in s
    for old, new in pairs:
        o = old.replace('\r\n', '\n')
        n = new.replace('\r\n', '\n')
        if crlf:
            o = o.replace('\n', '\r\n')
            n = n.replace('\n', '\r\n')
        assert o in s, p + " NAO ACHOU:\n" + repr(o[:200])
        s = s.replace(o, n, 1)
    io.open(p, 'w', encoding='utf-8-sig', newline='').write(s)
    print("ok", p)
