"""roteiro.py -- o trailer de DRAGON BALL JANDIRUS, plano a plano, em BATIDAS da musica (161,5 BPM; compasso de 4).

A musica (medida em batidas.py, por compasso):
    0-7     abertura (cresce)            8-15   a banda entra
    16-31   verso 1                      32-47  pre-refrao
    48-63   refrao 1 (para no 63)        64-71  respiro
    72-87   verso 2                      88-111 refrao 2
    112-127 ponte (calma)                128-159 refrao final
    160-167 pico                         168-173 fim
"""

AJUSTES = {}   # nome do plano -> segundos somados ao `em` (afinacao depois de olhar a previa)


def roteiro(marca):
    P = []

    def m(tomada, trecho, mais=0.0, n=0):
        return marca(tomada, trecho, n) + mais

    def plano(tomada, em, batidas, texto=None, **kw):
        d = dict(tomada=tomada, em=em + AJUSTES.get(kw.get("id", ""), 0.0), batidas=batidas)
        if texto:
            d["texto"] = texto
        d.update(kw)
        P.append(d)

    def ki(verbo, batidas, texto=None, tomada="k_tarde", mais=None, **kw):
        """um plano do arsenal: comeca um pouco depois do gatilho (o Kamehameha carrega 1,8 s antes de sair)"""
        if mais is None:
            mais = {"Kamehameha": 1.9, "GalicGun": 1.9, "Makkankosappo": 1.9, "Death_Beam": 1.9, "Final_Flash": 0.3, "Masenko": 0.7,
                    "Death_Ball": 7.1, "SpiritBomb": 9.0}.get(verbo, 0.45)
        plano(tomada, m(tomada, f"ki {verbo}: GATILHO", mais), batidas, texto, **kw)

    NOITE = dict(claro=(0.04, 1.15, 1.15, 1.25))
    FERA = dict(claro=(0.03, 1.3, 1.25, 1.38))            # a noite de lua cheia e mais escura que a das outras cenas      # as tomadas de noite ganham um pouco de luz e contraste
    PERTO = dict(zoom=1.5)

    # ================================================================== A: abertura (compassos 0-7)
    plano("espaco", m("espaco", "EM ORBITA", 1.6), 8, entra=("black", 1.2), claro=(0.03, 1.15, 1.2, 1.35))
    plano("m_tarde", m("m_tarde", "voo em Earth: sai de (247,385)", 3.0), 8)
    plano("m_tarde", m("m_tarde", "voo em Namek: sai de (22,222)", 2.6), 4)
    plano("m_tarde", m("m_tarde", "voo em Vegeta: sai de (60,171)", 5.2), 4)
    plano("esf_shenron", m("esf_shenron", "INVOCOU", 1.2), 4, zoom=1.5, centro=(0.5, 0.42))
    plano("f_ssj", m("f_ssj", "forma ssj1: carregando", 1.2), 4, sai=("white", 0.18), **NOITE)

    # ================================================================== B: o titulo (8-15)
    P.append(dict(cartao="arte/titulo.png", batidas=8, entra=("white", 0.35)))
    plano("f_ssj_tarde", m("f_ssj_tarde", "forma ssj1: de pe", -3.3), 8, zoom=1.25)
    plano("eb_kame", m("eb_kame", "ENCONTRARAM", 1.4), 8, **NOITE)
    plano("c_freeza", m("c_freeza", "SOBE DE FORMA", -1.6, n=2), 8)

    # ================================================================== C: verso 1 -- o guerreiro e os mundos (16-31)
    plano("h_criacao", 2.8, 8, "Crie o seu guerreiro: 19 raças", id="criacao1", vel=0.55, zoom=1.7, centro=(0.5, 0.49))
    plano("h_criacao", 4.45, 8, "Corpo, cabelo e roupa do seu jeito", id="criacao2", vel=0.5, zoom=1.7, centro=(0.5, 0.49))
    plano("m_terra", m("m_terra", "voo em Earth: sai de (247,385)", 2.8), 8, "Explore a Terra")
    plano("m_planetas", m("m_planetas", "voo em Namek: sai de (22,222)", 3.0), 4, "Namek")
    plano("m_planetas", m("m_planetas", "voo em Namek: sai de (48,335)", 2.2), 4)
    plano("m_planetas", m("m_planetas", "voo em Vegeta: sai de (60,171)", 3.2), 4, "Planeta Vegeta")
    plano("m_planetas", m("m_planetas", "voo em Vegeta: sai de (37,45)", 3.4), 4)
    plano("m_planetas", m("m_planetas", "voo em Icer: sai de (265,87)", 5.6), 4, "Icer")
    plano("m_planetas", m("m_planetas", "voo em Icer: sai de (264,235)", 3.0), 4)
    plano("m_alem", m("m_alem", "voo em Lookout: sai de", 3.6), 4, "O Templo de Kami")
    plano("m_alem", m("m_alem", "voo em Hyperbolic_Time_Chamber: sai de", 3.2), 4, "A Sala do Tempo")
    plano("m_alem", m("m_alem", "voo em Heaven: sai de (213,224)", 4.2), 4, "O Céu")
    plano("m_alem", m("m_alem", "voo em Hell: sai de", 4.6), 4, "O Inferno")

    # ================================================================== D: pre-refrao -- o mundo vivo e os sistemas (32-47)
    plano("dia", m("dia", "DISPARA", 4.2), 8, "Dia, noite e lua no relógio do mundo", vel=3.0)
    plano("climas", m("climas", "clima: Chuva", 3.2), 4, "Clima em cada planeta")
    plano("climas", m("climas", "clima: Nevasca", 3.2), 4)
    plano("climas", m("climas", "clima: Areia", 3.2), 4)
    plano("climas", m("climas", "clima: ChuvaDeSangue", 3.2), 4)
    plano("menus", m("menus", "menu: aba Learning", 0.25), 8, "Árvores de habilidade e estilos de luta", zoom=2.4, centro=(0.5, 0.5))
    plano("h_mesa", 5.3, 8, "Invente a sua própria técnica", id="mesa", zoom=1.9, centro=(0.5, 0.46), claro=(0.03, 1.1, 1.0, 1.15))
    plano("espaco", m("espaco", "DECOLA", -1.0), 8, "Decole para o espaço")
    plano("espaco", m("espaco", "POUSOU", 0.9), 8, "Planetas gerados por semente")
    plano("h_nav", 3.2, 8, "Uma galáxia inteira na carta estelar", id="nav", zoom=2.0, centro=(0.5, 0.46), vel=0.8)

    # ================================================================== E: refrao 1 -- o combate (48-63)
    plano("d_tarde", m("d_tarde", "round 1 COMECA", 3.2), 8, "Combate em tempo real", entra=("white", 0.12))
    plano("d_vegeta", 16.4, 8, "O cenário se destrói", id="vegeta")
    ki("Kamehameha", 4, "Mais de 30 técnicas de ki")
    ki("Final_Flash", 4, mais=0.5)
    ki("Makkankosappo", 4)
    ki("Masenko", 4)
    ki("GalicGun", 2, mais=2.3)
    ki("Death_Beam", 2, mais=2.3)
    ki("Kienzan", 2, mais=0.9)
    ki("Scattering_Bullet", 2, mais=2.3)
    ki("BusterShell", 2, mais=1.0)
    ki("Hellzone_Grenade", 2, mais=1.7)
    ki("Death_Ball", 2)
    ki("SpiritBomb", 2)
    plano("eb_tarde", m("eb_tarde", "ENCONTRARAM", 0.6), 8, "Embate de raios")
    plano("eb_empate", m("eb_empate", "embate: ACABOU", -2.2), 8, sai=("black", 0.45), **NOITE)

    # ================================================================== F: respiro -- a morte e o Outro Mundo (64-71)
    plano("morte", m("morte", "o chefe ATACA", 2.0), 8, "A morte é de verdade", entra=("black", 0.5))
    plano("alem", m("alem", "CHEGOU ao Outro Mundo", 1.6), 8, "...e a jornada segue no Outro Mundo")
    plano("alem", m("alem", "diante do ENMA", 1.6), 8, "O julgamento do Rei Enma")
    plano("alem", m("alem", "CAMINHO DA SERPENTE", 1.8), 8)

    # ================================================================== G: verso 2 -- torneio, naves, fusao, lua cheia (72-87)
    plano("torneio", m("torneio", "CONVITE", 0.6), 4, "Torneio de Artes Marciais")
    plano("torneio", m("torneio", "a CHAVE esta na tela", 0.8), 4)
    plano("torneio", m("torneio", "LUTA 2", 9.0), 8, id="torneio")
    plano("h_embarque", 10.4, 8, "Construa e pilote naves", id="nave1", zoom=1.6, centro=(0.5, 0.42), claro=(0.05, 1.15, 1.1, 1.35))
    plano("h_embarque", 47.6, 8, id="nave2", zoom=1.7, centro=(0.5, 0.5))
    plano("fusao", m("fusao", "POTARA: a CINEMATICA", -0.5), 8, "Fusão: Potara e Metamoru", zoom=1.25)
    plano("fusao", m("fusao", "blue de pe", 2.6), 8, zoom=1.25)
    plano("f_oozaru", m("f_oozaru", "forma oozaru: COMECA", 2.8), 8, "Lua cheia: Oozaru", id="oozaru1", zoom=1.5, **FERA)
    plano("f_oozaru", m("f_oozaru", "forma oozaru_dourado: COMECA", 3.4), 8, id="oozaru2", zoom=1.5, **FERA)

    # ================================================================== H: refrao 2 -- as transformacoes (88-111)
    plano("f_ssj_tarde", m("f_ssj_tarde", "forma ssj1: COMECA", 13.4), 8, "46 transformações", entra=("white", 0.12), zoom=1.25)
    plano("f_ssj", m("f_ssj", "forma ssj1: carregando", 0.9), 8, "Super Saiyajin", zoom=1.25, **NOITE)
    plano("f_ssj", m("f_ssj", "forma ssj2: carregando", 0.9), 4, "SSJ 2", zoom=1.25, **NOITE)
    plano("f_ssj", m("f_ssj", "forma ssj3: carregando", 0.9), 4, "SSJ 3", zoom=1.25, **NOITE)
    plano("f_ssj", m("f_ssj", "forma ssj4: carregando", 0.9), 8, "Super Saiyajin 4", zoom=1.25, **NOITE)
    plano("f_deus", m("f_deus", "forma ssg: carregando", -6.5), 8, "Deus Super Saiyajin", zoom=1.25, **NOITE)
    plano("f_deus", m("f_deus", "forma blue: carregando", 0.9), 4, "Blue", zoom=1.25, **NOITE)
    plano("f_deus", m("f_deus", "forma blue_evolution: carregando", 0.9), 4, zoom=1.25, **NOITE)
    plano("f_deus", m("f_deus", "forma ui_sign: de pe", -5.0), 4, "Instinto Superior", zoom=1.25, **NOITE)
    plano("f_deus", m("f_deus", "forma ui_perfected: carregando", 0.9), 4, zoom=1.25, **NOITE)
    plano("f_ego", m("f_ego", "forma ultra_ego: de pe", -5.0), 4, "Ultra Ego", zoom=1.25, **NOITE)
    plano("f_ego", m("f_ego", "forma ultra_ego: carregando", 0.9), 4, zoom=1.25, **NOITE)
    plano("f_rose", m("f_rose", "forma rose: carregando", 0.9), 4, "Rosé", zoom=1.25, **NOITE)
    plano("f_deus", m("f_deus", "forma beast: carregando", 0.9), 4, "Beast", zoom=1.25, **NOITE)
    plano("f_lenda", m("f_lenda", "forma legendary: de pe", -4.0), 4, "Lendário Super Saiyajin", zoom=1.25, **NOITE)
    plano("f_lenda", m("f_lenda", "forma primal_legendary4: COMECA", 9.0), 4, zoom=1.25, **NOITE)
    plano("f_frost", m("f_frost", "forma frost6: carregando", 0.9), 4, "Frost Demon", zoom=1.25)
    plano("f_frost", m("f_frost", "forma frost7: carregando", 0.9), 4, zoom=1.25)
    plano("f_namek", m("f_namek", "forma snamek: carregando", 0.9), 4, "Super Namekuseijin", zoom=1.25)
    plano("f_heran", m("f_heran", "forma heran2: carregando", 0.9), 4, "Heran: Poder Máximo", zoom=1.25)

    # ================================================================== I: ponte -- as esferas e as sagas (112-127)
    plano("esf_shenron", m("esf_shenron", "REUNIDAS", 0.8), 8, "Reúna as sete Esferas do Dragão", zoom=2.0, centro=(0.5, 0.5), entra=("black", 0.35))
    plano("esf_shenron", m("esf_shenron", "INVOCOU", 0.2), 16, "Shenron atende ao seu desejo", zoom=1.5, centro=(0.5, 0.42))
    plano("esf_porunga", m("esf_porunga", "INVOCOU", 0.3), 8, "Porunga, o dragão de Namek", zoom=1.5, centro=(0.5, 0.36))
    plano("c_cell", m("c_cell", "SOBE DE FORMA", -3.0, n=1), 8, "Sagas com chefes: Freeza, Cell, Majin Boo")
    plano("h_agonia", 36.0, 8, "E vilões capazes de destruir um planeta", id="agonia1")
    plano("h_agonia", 128.0, 8, id="agonia1b")
    plano("h_agonia", 178.0, 8, id="agonia1c")

    # ================================================================== J: refrao final (128-159)
    plano("h_destrocos2", 23.35, 8, id="pavio", claro=(0.04, 1.2, 1.25, 1.4))
    plano("h_destrocos2", 26.30, 8, id="estouro", entra=("white", 0.25), vel=0.45, claro=(0.04, 1.2, 1.25, 1.4))
    plano("c_freeza", m("c_freeza", "SOBE DE FORMA", -1.2, n=0), 4, "Freeza")
    plano("c_freeza", m("c_freeza", "SOBE DE FORMA", -1.2, n=1), 4)
    plano("c_freeza", m("c_freeza", "SOBE DE FORMA", -1.2, n=3), 4)
    plano("c_freeza", m("c_freeza", "o rival SE TRANSFORMA", 0.8), 4)
    plano("c_cell", m("c_cell", "SOBE DE FORMA", -1.2, n=0), 4, "Cell")
    plano("c_cell", m("c_cell", "SOBE DE FORMA", 3.4, n=0), 4)
    plano("c_androide", 42.4, 4, "Androides 17 e 18", id="androide")
    plano("c_boo", 33.2, 4, "Majin Boo", id="boo2")
    plano("d_noite", m("d_noite", "round 1 COMECA", 5.0), 4, id="dn1", **NOITE)
    plano("d_noite", m("d_noite", "round 1 COMECA", 14.0), 4, id="dn2", **NOITE)
    plano("d_noite", m("d_noite", "round 2 COMECA", 6.0), 4, id="dn3", **NOITE)
    plano("eb_final", m("eb_final", "ENCONTRARAM", 1.0), 4, **NOITE)
    ki("Final_Flash", 4, tomada="k_noite", **NOITE)
    ki("Kamehameha", 4, tomada="k_noite", **NOITE)
    ki("Scattering_Bullet", 4, tomada="k_noite", mais=0.6, **NOITE)
    ki("BusterShell", 4, tomada="k_noite", mais=0.9, **NOITE)
    plano("fusao", m("fusao", "DANCA: a cinematica", 0.1), 4, zoom=1.25)
    plano("fusao", m("fusao", "ssj4 de pe", 3.4), 4, zoom=1.25)
    plano("torneio", m("torneio", "LUTA 3", 12.0), 4, id="torneio2")
    plano("d_namek", 84.2, 4, id="namek1")
    plano("f_lenda", m("f_lenda", "forma primal_legendary: COMECA", 9.0), 4, zoom=1.25, **NOITE)
    plano("f_deus", m("f_deus", "forma mistico: carregando", 0.9), 4, zoom=1.25, **NOITE)
    plano("f_rose", m("f_rose", "forma rose2: carregando", 0.9), 4, zoom=1.25, **NOITE)
    plano("f_oozaru", m("f_oozaru", "forma oozaru: de pe", 1.4), 4, id="oozaru3", zoom=1.5, **FERA)
    plano("d_tarde", m("d_tarde", "round 1 COMECA", 25.5), 4)
    plano("d_vegeta", 22.4, 4, id="vegeta2")
    plano("m_planetas", m("m_planetas", "voo em Namek: sai de (412,92)", 4.5), 4)
    plano("m_alem", m("m_alem", "voo em Heaven: sai de (80,310)", 3.0), 4)

    # ================================================================== K: o pico -- cortes de duas batidas (160-167)
    rapidos = [
        ("f_ssj_tarde", m("f_ssj_tarde", "forma ssj1: de pe", -1.6), dict(zoom=1.25)),
        ("eb_kame", m("eb_kame", "ENCONTRARAM", 6.0), NOITE),
        ("c_freeza", m("c_freeza", "SOBE DE FORMA", 0.4, n=3), {}),
        ("f_deus", m("f_deus", "forma ui_perfected: carregando", 1.4), dict(zoom=1.25, **NOITE)),
        ("d_tarde", m("d_tarde", "round 1 COMECA", 39.0), {}),
        ("k_tarde", m("k_tarde", "ki Final_Flash: GATILHO", 0.9), {}),
        ("fusao", m("fusao", "POTARA: a CINEMATICA", 0.5), dict(zoom=1.25)),
        ("f_ssj", m("f_ssj", "forma ssj4: carregando", 1.2), dict(zoom=1.25, **NOITE)),
        ("c_cell", m("c_cell", "SOBE DE FORMA", 3.0, n=1), {}),
        ("esf_shenron", m("esf_shenron", "INVOCOU", 3.0), dict(zoom=1.5, centro=(0.5, 0.42))),
        ("eb_empate", m("eb_empate", "embate: ACABOU", -0.9), NOITE),
        ("f_deus", m("f_deus", "forma blue: carregando", 1.3), dict(zoom=1.25, **NOITE)),
        ("d_noite", m("d_noite", "round 1 COMECA", 22.0), NOITE),
        ("f_ego", m("f_ego", "forma ultra_ego: carregando", 1.2), dict(zoom=1.25, **NOITE)),
        ("h_destrocos2", 26.45, dict(claro=(0.04, 1.2, 1.25, 1.4))),
        ("f_ssj", m("f_ssj", "forma ssj1: carregando", 1.6), dict(zoom=1.5, **NOITE)),
    ]
    for i, (t, em, kw) in enumerate(rapidos):
        d = dict(tomada=t, em=em, batidas=2)
        d.update(kw)
        if i == len(rapidos) - 1:
            d["sai"] = ("white", 0.2)
        P.append(d)

    # ================================================================== L: o nome (168-173)
    P.append(dict(cartao="arte/titulo.png", batidas=24, entra=("white", 0.4), sai=("black", 2.2), empurra=0.08))
    return P
