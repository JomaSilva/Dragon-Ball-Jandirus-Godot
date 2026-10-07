"""tomadas.py -- a lista de tomadas do trailer. Cada uma: a cena do diretor (ou uma bancada), o argumento dela e as flags do jogo."""

BASE = ["--bpteste", "3000000", "--kiteste", "--vooteste"]


def T(cena, arg="", secs=120, raca="Saiyan", nome="Kakaroto", flags=None, zoom=3, extra=None):
    """uma tomada dirigida: --trailer <cena> --trailerarg <arg>"""
    return dict(cena=cena, arg=arg, secs=secs, raca=raca, nome=nome, flags=BASE if flags is None else flags, zoom=zoom, extra=extra or [])


def R(cliente, secs, marker, server=None, espera=14, convidado=None, atraso=12, zoom=3):
    """uma tomada CRUA: uma bancada que ja existe no repo, gravada como ela e"""
    return dict(cru=True, cliente=cliente, secs=secs, marker=marker, server=server, espera=espera, convidado=convidado, atraso=atraso, zoom=zoom)


KI = ["--bpteste", "300000000", "--kiteste", "--vooteste"]
RIVAL = "Clothes_SaiyanSuit+Armor_Elite+Clothes, Saiyan Gloves+Clothes, Saiyan Shoes"
NEGRO = "Clothes_GiTop#2a2a33+Clothes_GiBottom#2a2a33+Clothes_Boots#3a1010+Clothes_Wristband#6a1010"
NOITE, TARDE, SOL = "hora=0.80", "hora=0.70", "hora=0.5"
GOKU_B = "cabeloa=Vegeta;roupaa=" + RIVAL + ";cabelob=Goku;roupab=Clothes_TurtleSuit"

TOMADAS = {
    # ---------------- ensaios curtos (validar cada cena)
    "e_ki":      T("ki", "tecnicas=Kamehameha,Final_Flash,Kienzan,Death_Ball,SpiritBomb,Energy_Barrage;hora=0.80", 80, flags=KI),
    "e_duelo":   T("duelo", "a=guardiao_saiyajin;b=guardiao_saiyajin;seg=30;hora=0.5", 70),
    "e_embate":  T("embate", "verbo=Kamehameha;seg=20;hora=0.80", 60),
    "e_chefe":   T("chefe", "molde=freeza_namek;seg=45;cada=8", 90),
    "e_torneio": T("torneio", "lutas=2;seg=25", 150),
    "e_fusao":   T("fusao", "formapotara=;formadanca=", 100),
    "e_esferas": T("esferas", "hora=0.80", 60),
    "e_alem":    T("alem", "", 90),
    "e_espaco":  T("espaco", "viagem=60;passeio=6", 150),
    "e_climas":  T("climas", "cada=4", 70),
    "e_dia":     T("dia", "seg=16;dias=1", 50),
    "e_menus":   T("menus", "cada=2", 80, flags=BASE + ["--marcosteste", "40", "--techteste"]),

    # ================= TRANSFORMACOES =================
    "f_ssj":       T("formas", "formas=ssj1,ssj2,ssj3,ssj4;" + NOITE + ";zoom=4", 300),
    "f_ssj_tarde": T("formas", "formas=ssj1,ssj2;" + TARDE + ";zoom=4", 90),
    "f_deus":      T("formas", "formas=ssg,blue,blue_evolution,ui_sign,ui_perfected,beast,mistico;" + NOITE + ";zoom=4", 260),
    "f_rose":      T("formas", "formas=rose_ssg,rose,rose2;" + NOITE + ";zoom=4;roupa=" + NEGRO, 140),
    "f_ego":       T("formas", "formas=destroyer,ultra_ego;" + NOITE + ";zoom=4;cabelo=Vegeta;roupa=" + RIVAL, 100),
    "f_lenda":     T("formas", "formas=wrathful,legendary,primal_legendary,primal_legendary2,primal_legendary4;" + NOITE + ";zoom=4;cabelo=Broly;roupa=BrolyWaistrobe+Clothes_Wristband", 230),
    "f_frost":     T("formas", "formas=frost1,frost2,frost3,frost4,frost5,frost6,frost7;" + TARDE + ";zoom=4;cabelo=;roupa=;lugar=Icer,180,99", 200, raca="Icer", nome="Freezer"),
    "f_namek":     T("formas", "formas=snamek;" + TARDE + ";zoom=4;cabelo=;roupa=ItemPiccoloCape+Clothes_Turban;lugar=Namek,100,347", 50, raca="Namekian", nome="Piccolo"),
    "f_heran":     T("formas", "formas=heran1,heran2;" + TARDE + ";zoom=4;cabelo=Long;roupa=Clothes_WarriorPants+Clothes_Wristband", 80, raca="Heran", nome="Bojack"),
    "f_oozaru":    T("formas", "formas=oozaru,oozaru_dourado,ssj4;lua=1;zoom=3;pose=5", 110),

    # ================= KI =================
    "k_noite":     T("ki", NOITE + ";zoom=3", 300, flags=KI),
    "k_tarde":     T("ki", TARDE + ";zoom=3", 300, flags=KI),

    # ================= LUTAS =================
    "d_tarde":     T("duelo", "a=guardiao_saiyajin;b=guardiao_saiyajin;seg=50;rounds=2;" + TARDE, 150),
    "d_noite":     T("duelo", "a=guardiao_saiyajin;b=guardiao_saiyajin;seg=45;rounds=2;" + NOITE, 140),
    "d_vegeta":    T("duelo", "a=guardiao_saiyajin;b=guardiao_saiyajin;seg=50;" + SOL + ";lugar=Vegeta,88,171", 90),
    "d_namek":     T("duelo", "a=lutador_de_torneio;b=lutador_de_torneio;vestir=0;seg=45;rounds=2;" + SOL + ";lugar=Namek,45,235", 140),
    "eb_kame":     T("embate", "verbo=Kamehameha;seg=22;" + NOITE + ";" + GOKU_B, 60),
    "eb_empate":   T("embate", "verbo=Kamehameha;seg=24;empurra=0;" + NOITE + ";depois=6", 70),
    "eb_final":    T("embate", "verbo=Final_Flash;escala=4;tiles=14;seg=22;" + NOITE + ";" + GOKU_B, 60),
    "eb_tarde":    T("embate", "verbo=Kamehameha;seg=22;" + TARDE + ";" + GOKU_B, 60),

    # ================= CHEFES =================
    "c_freeza":    T("chefe", "molde=freeza_namek;seg=85;cada=14;" + SOL + ";lugar=Namek,100,347", 130),
    "c_cell":      T("chefe", "molde=cell;seg=70;cada=15;bprival=4e8;" + TARDE + ";lugar=Earth,430,250", 110),
    "c_boo":       T("chefe", "molde=majin_boo;seg=45;bprival=2e10;" + SOL + ";lugar=Earth,285,120", 80),
    "c_androide":  T("chefe", "molde=androide_18;rival=androide_17;seg=40;bprival=1e8;" + TARDE + ";lugar=Earth,150,170;cabelo=S17;roupa=Clothes_LongSleeveShirt", 80),

    # ================= SISTEMAS =================
    "torneio":     T("torneio", "lutas=4;seg=28;" + SOL, 280),
    "fusao":       T("fusao", "formapotara=blue;formadanca=ssj4;hora=0.66;zoom=4", 170),
    "esf_shenron": T("esferas", "hora=0.66;lugar=Earth,430,262;zoom=2;dragao=12", 60),
    "esf_porunga": T("esferas", "hora=0.60;lugar=Namek,100,347;zoom=2;dragao=12", 60, raca="Namekian", nome="Dende"),
    "alem":        T("alem", "hora=0.66", 100),
    "espaco":      T("espaco", "viagem=70;passeio=10;orbita=8", 170),
    "climas":      T("climas", "cada=8;climas=Chuva,Tempestade,Nevasca,Areia,ChuvaDeSangue,ChuvaDeNamek;" + SOL, 90),
    "dia":         T("dia", "seg=20;dias=1;hora=0.42", 60),
    "menus":       T("menus", "cada=2.6", 90, flags=BASE + ["--marcosteste", "40", "--techteste"]),
    "cidade":      T("cidade", SOL + ";rota=1,0,5|0,-1,5|-1,0,5|0,1,5", 60, raca="Human"),
    "cidade_vegeta": T("cidade", SOL + ";lugar=Vegeta,88,171;rota=1,0,5|0,-1,5|-1,0,5|0,1,5", 60),
    "cidade_namek":  T("cidade", SOL + ";lugar=Namek,125,305;rota=1,0,5|0,-1,5|-1,0,5|0,1,5", 60, raca="Namekian", nome="Nail"),

    # ================= O MUNDO (voando) =================
    "m_terra":     T("voo", SOL + ";altura=3;rota=Earth,247,385,0,1,10|Earth,105,337,1,0,7|Earth,400,250,1,0,8|Earth,262,95,1,0,8|Earth,125,150,1,0,9|Earth,180,130,1,0,8", 130),
    "m_planetas":  T("voo", SOL + ";altura=3;rota=Namek,22,222,1,0,9|Namek,105,301,1,0,9|Namek,48,335,0,-1,8|Namek,412,92,0,-1,9|Vegeta,60,171,1,0,10|Vegeta,37,45,0,-1,8|Icer,265,87,1,0,8|Icer,264,235,0,-1,8|Arconia,100,352,1,0,8", 190),
    "m_alem":      T("voo", SOL + ";altura=3;rota=Afterlife,150,381,1,0,8|Heaven,213,224,0,-1,8|Heaven,80,310,1,0,7|Hell,288,337,1,0,8|Lookout,116,306,1,0,9|Hyperbolic_Time_Chamber,146,348,0,-1,7|Hera,185,233,1,0,8|Desert,230,206,1,0,8|God_Realm,8,30,1,0,7|Big_Geti_Star,190,187,1,0,7|Small_Space_Station,80,62,1,0,6", 220),
    "m_tarde":     T("voo", "hora=0.71;altura=3;rota=Earth,247,385,0,1,10|Namek,22,222,1,0,9|Vegeta,60,171,1,0,10|Earth,400,250,1,0,8", 100),

    # ================= BANCADAS QUE JA EXISTEM (cruas) =================
    "h_criacao":   R(["--diagcarga", "--rede", "7801"], 120, r"\[carga\] ===== \d+ OK", server="--server --port 7801", espera=14),
    "morte":       T("morte", "hora=0.5;lugar=Namek,100,347;seg=60;depois=8", 110),
    "h_mesa":      R(["--host", "--rede", "7863", "--diagmesa", "--raca", "Human", "--conta", "bancada_mesa", "--nome", "Mesa"], 70, r"\[mesa\] ====="),
    "h_nav":       R(["--host", "--rede", "7865", "--diagnav", "--conta", "navtrailer", "--nome", "Piloto"], 220, r"\[nav\] ====="),
    "h_embarque":  R(["--host", "--rede", "7866", "--embarqueteste", "--diagembarque", "--horateste", "0.5", "--conta", "emb_trailer", "--nome", "Piloto"], 260, r"\[embarque\] fim"),
    "h_skills":    R(["--connect", "127.0.0.1", "--rede", "7867", "--diagskills", "--raca", "Saiyan", "--conta", "bancada_skills", "--nome", "Bancada"], 200, r"\[skills\]  PLACAR",
                     server="--server --port 7867 --marcosteste 40 --horateste 0.5", espera=20),
    # o palco e do servidor (flag); o diretor so tira a HUD e olha
    "h_agonia":    T("palco", "seg=235;vestir=1;lugar=Earth,247,436;zoom=3", 270, raca="Human", flags=["--agoniaviva", "--bpteste", "2000000", "--kiteste", "--vooteste"]),
    "h_destrocos": dict(T("palco", "seg=60;zoom=3", 90, flags=["--destrocosvivos", "--destrocos", "a"], nome="DestrocoA"),
                        convidado="--rede {rede} --connect 127.0.0.1 --raca Saiyan --conta trailer_destroco_b --nome DestrocoB --destrocos b", atraso=14),
}

LOTE_1 = ["f_ssj_tarde", "eb_kame", "k_noite", "d_tarde", "c_freeza", "fusao", "esf_shenron", "alem", "espaco", "m_terra", "torneio"]
LOTE_2 = ["f_ssj", "f_deus", "k_tarde", "d_noite", "c_cell", "eb_empate", "eb_final", "m_planetas", "climas", "dia", "menus", "cidade"]
LOTE_3 = ["f_rose", "f_ego", "f_lenda", "f_frost", "f_namek", "f_heran", "f_oozaru", "d_vegeta", "d_namek", "c_boo", "c_androide", "eb_tarde",
          "esf_porunga", "m_alem", "m_tarde", "cidade_vegeta", "cidade_namek"]
LOTE_4 = ["h_criacao", "h_mesa", "h_nav", "h_embarque", "h_skills", "h_agonia", "h_destrocos"]
LOTE_5 = ["f_ssj", "f_deus", "k_tarde", "k_noite", "eb_kame", "eb_empate", "eb_final", "d_noite", "espaco", "c_cell", "m_planetas", "climas", "dia", "menus", "cidade"]
TOMADAS["h_destrocos2"] = dict(T("palco", "seg=60;zoom=2;foco=0,7", 90, flags=["--destrocosvivos", "--destrocos", "a"], nome="DestrocoA"),
                              convidado="--rede {rede} --connect 127.0.0.1 --raca Saiyan --conta trailer_destroco_b --nome DestrocoB --destrocos b", atraso=14)
LOTE_6 = ["h_criacao", "morte"]
LOTE_7 = ["h_destrocos2"]
LOTES = {"7": LOTE_7, "6": LOTE_6, "1": LOTE_1, "2": LOTE_2, "3": LOTE_3, "4": LOTE_4, "5": LOTE_5}
