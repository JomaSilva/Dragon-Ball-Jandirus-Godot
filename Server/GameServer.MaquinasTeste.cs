using System.Text.Json;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DAS MAQUINAS (`--maquinasteste`) -- os dois pedidos do dono de 2026-10-09 que moram no servidor:
///
///   * *"as maquinas de regeneraçao nao estao funcionando (veja no DM como elas funcionavam)"*, com a regra
///     que veio em seguida: *"pra ligar vc tem q apertar E e abrir o menu de interaçoes e apertar pra
///     ligar, curando todos q estiverem no mesmo tile da maquina"*;
///   * *"verifique se a maquina de gravidade tb esta funcionando"*, e a regra nova: *"a area da gravidade
///     deve se adaptar ao local q ta a maquina ... igual um liquido"*.
///
/// No boot, como as outras de servidor: precisa das zonas com colisao e de ninguem logado. Os corpos sao
/// forjados (sem `Peer`), e TUDO passa pelo funil de producao -- a maquina e assentada pelo `posicionar`,
/// aparafusada, ligada e melhorada pelos verbos da tecla E (`ComandoDeInteracao`), a parede sobe pelo
/// `ComandoDeBloco`, e quem cura e quem pesa sao o pulso e o tique de producao.
///
/// O PALCO E A TERRA DE VERDADE, e tudo o que a bancada mexe volta no `finally`: as obras, os blocos, os
/// corpos e o relogio.
///
/// QUATRO DEFEITOS INJETADOS, um por regra que so existe por uma linha: o tanque que cura desligado
/// (`Regenerador.DesligadoCuraDeTeste`), o que cura o tile vizinho (`VizinhoContaDeTeste`), o campo que so
/// e relido quando mexem na maquina (`CampoDeGravidade.SoMudaNaMaquinaDeTeste`) e o que atravessa parede
/// (`AtravessaParedeDeTeste`).
///
///     Godot --headless -- --server --port 7961 --maquinasteste
/// </summary>
public sealed partial class GameServer
{
	private int _mqOk, _mqFalhou;

	private void AfirmarMq(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _mqOk++; GD.Print($"[maquinas]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _mqFalhou++; GD.PrintErr($"[maquinas]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDasMaquinas()
	{
		_mqOk = _mqFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[maquinas] ================ O REGENERADOR E A MAQUINA DE GRAVIDADE (dono, 2026-10-09) ================");

		ZoneKey terra = ZonaDaBancadaDeProjetil;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(terra);
		List<BlocoDePe> deAntes = [.. _bases.Values.SelectMany(z => z.Celulas.Values)];
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		int obrasAntes = _noChao.Count;

		try
		{
			AfirmarMq("(montagem) a Terra tem mapa, e os catalogos de obras e de blocos carregaram",
					  mapa != null && _obras != null && _blocos is { Total: > 0 });
			if (mapa == null || _obras == null || _blocos is not { Total: > 0 }) return;
			ZerarBases();

			(int px, int py) = PracaDaBancada(mapa);
			MqDoMenu();
			MqDoTanque(px, py);
			LimparTudoDaBancada();
			MqDaGeometria();
			MqDoCampo(terra, px, py);
		}
		finally
		{
			Regenerador.DesligadoCuraDeTeste = Regenerador.VizinhoContaDeTeste = false;
			CampoDeGravidade.SoMudaNaMaquinaDeTeste = CampoDeGravidade.AtravessaParedeDeTeste = false;

			// O RELOGIO VOLTA, e os tres prazos que foram medidos contra o relogio adiantado voltam com ele --
			// senao o pulso e a leitura de producao ficariam parados ate a parede alcancar o adianto.
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			_proximoDreno = _proximaLeituraDosCampos = _proximoPulsoDosTanques = 0;

			// O MUNDO VOLTA: as obras e os blocos de antes da bancada, e nada dela no disco.
			if (_noChao.Count > obrasAntes) _noChao.RemoveRange(obrasAntes, _noChao.Count - obrasAntes);
			GravarMundo();
			AplicarColisaoDasObras(terra);
			ZerarBases();
			foreach (BlocoDePe b in deAntes) GuardarBloco(b);
			foreach (BaseDaZona z in _bases.Values) AssentarBase(z);
			_basesSujas = false;
			LimparTudoDaBancada();   // os corpos forjados saem do mundo
			LerOsCampos();           // ...e nenhum campo da bancada fica de pe
		}

		GD.Print($"[maquinas] ================ {_mqOk} OK, {_mqFalhou} FALHA(S) ================");
	}

	/// <summary>
	/// ASSENTA UMA MAQUINA PELO FUNIL DE PRODUCAO: o item entra na mochila e o `posicionar` o poe no chao --
	/// com as guardas de lugar, a obra com dono e a gravacao. Devolve a obra, ou nulo se o servidor recusou.
	/// </summary>
	private Obra? AssentarDeTeste(ServerPlayer dono, string tipo, int cx, int cy)
	{
		const int T = ZoneCollision.TileSize;
		int antes = _noChao.Count;
		Guardar(dono, tipo);
		// INTEIROS de proposito: o `posicionar` le com `float.TryParse`, e um ponto decimal dependeria da cultura.
		ComandoDeTech(dono, "posicionar", $"{tipo}/{cx * T + T / 2}/{cy * T + T / 2 - (int)MoveRules.FeetOffsetY}");
		return _noChao.Count > antes ? _noChao[^1] : null;
	}

	// =====================================================================
	// 1) O MENU DA TECLA E
	// =====================================================================
	private void MqDoMenu()
	{
		GD.Print("[maquinas] -- 1) o menu da tecla E: o que cada maquina oferece --");

		string[] doTanque = [.. Interacoes.De(Regenerador.Tipo).Select(a => a.Verbo)];
		AfirmarMq("o regenerador oferece LIGAR/DESLIGAR, melhorar, ver estado e aparafusar",
				  doTanque.Contains("regen_ligar") && doTanque.Contains("regen_info") && doTanque.Contains("aparafusar")
				  && Interacoes.De(Regenerador.Tipo).Any(a => a.Forma == Interacoes.Forma.Submenu), string.Join(",", doTanque));
		AfirmarMq("...e o servidor aceita as melhorias dele (o verbo so existe dentro do submenu)",
				  Interacoes.Aceita(Regenerador.Tipo, "regen_up")
				  && Interacoes.De($"{Regenerador.Tipo}/upgrades").Select(a => a.Arg)
							   .SequenceEqual(["recarga", "bateria", "velocidade", "ferimentos", "nanites"]));
		AfirmarMq("o campo bio NAO ganhou interruptor (ele trabalha sozinho): so aparafusar e ver estado",
				  !Interacoes.Aceita("Bio_Field", "regen_ligar") && Interacoes.Aceita("Bio_Field", "aparafusar")
				  && Interacoes.Aceita("Bio_Field", "maq_info"));
		AfirmarMq("a maquina de gravidade ganhou o PARAFUSO no proprio menu (ela nao liga solta)",
				  Interacoes.Aceita("Gravity", "aparafusar") && Interacoes.Aceita("Gravity", "grav_definir")
				  && Interacoes.Aceita("Gravity", "grav_up"));
	}

	// =====================================================================
	// 2) O REGENERADOR
	// =====================================================================
	private void MqDoTanque(int px, int py)
	{
		GD.Print("[maquinas] -- 2) o regenerador: interruptor, pulso de 2 s, bateria e melhorias (`Tier 1.5.dm:55-218`) --");
		const int T = ZoneCollision.TileSize;
		Vec2 No(int cx, int cy) => new(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);
		(int tx, int ty) = (px + 8, py + 1);

		ServerPlayer dono = Forjar("mq: o dono do tanque", No(tx, ty + 1), 1_000);
		dono.Conta = "bancada_tanque";
		dono.Ficha.Zeni = 10_000_000;

		Obra? tanque = AssentarDeTeste(dono, Regenerador.Tipo, tx, ty);
		AfirmarMq("(montagem) o tanque assentou pelo funil de producao (`posicionar`), e e do dono",
				  tanque is { Tipo: Regenerador.Tipo, DonoConta: "bancada_tanque" });
		if (tanque == null) return;
		Construcao? doCatalogo = _obras!.Get(Regenerador.Tipo);

		RegeneradorDaObra r = EstadoDoTanque(tanque);
		AfirmarMq("o tanque que alguem assenta nasce como o de fabrica: desligado, velocidade 1, uma carga, sem nanites e sem tratar ferimentos",
				  r is { Ligado: false, Eficiencia: 1, Energia: 1, EnergiaMax: 1, Nanites: 0, CuraFerimentos: false });

		ServerPlayer p = Forjar("mq: o paciente", No(tx, ty), 1_000);
		BodyPart Parte(ServerPlayer de, string nome) => de.Combate.Corpo.Achar(nome)!;
		void Inteiro(ServerPlayer de)
		{
			de.Combate.Corpo.Restaurar();
			de.Combate.SincronizarVida();
			de.Ficha.Ki = de.Ficha.MaxKi;
		}
		void Ferir(ServerPlayer de, string nome, double vida)
		{
			Parte(de, nome).Vida = vida;
			de.Combate.SincronizarVida();
		}
		bool Perto(double a, double b) => Math.Abs(a - b) < 1e-9;

		// ------------------------------------------------------------ SOLTO NAO LIGA
		List<string> falas = Ouvir(() => ComandoDeInteracao(dono, "regen_ligar", ""));
		AfirmarMq("SOLTO, o tanque nao liga -- e o dono ouve que falta aparafusar",
				  !r.Ligado && Disse(falas, "aparafuse"), string.Join(" | ", falas));
		ComandoDeInteracao(dono, "aparafusar", "");
		AfirmarMq("o parafuso entra pelo mesmo menu", tanque.Aparafusada);

		// ------------------------------------------------------------ O INTERRUPTOR
		Ferir(p, "Braco esquerdo", 50);
		PulsoDosTanques();
		AfirmarMq("DESLIGADO, o tanque nao cura quem esta em cima dele, e nao gasta bateria (a regra nova: liga-se pelo menu)",
				  Perto(Parte(p, "Braco esquerdo").Vida, 50) && Perto(r.Energia, 1),
				  $"braco {Parte(p, "Braco esquerdo").Vida:0.##}, bateria {r.Energia:0.####}");
		AfirmarMq("...e o desenho dele e so a base", EstadoDaArte(tanque, doCatalogo) == Regenerador.ArteDesligado,
				  EstadoDaArte(tanque, doCatalogo));

		try
		{
			Regenerador.DesligadoCuraDeTeste = true;
			PulsoDosTanques();
			AfirmarMq("(defeito injetado: o tanque cura desligado) a mesma regua REPROVA -- o braco sobe sem ninguem ter ligado",
					  Parte(p, "Braco esquerdo").Vida > 50, $"braco {Parte(p, "Braco esquerdo").Vida:0.##}");
		}
		finally { Regenerador.DesligadoCuraDeTeste = false; }
		r.Energia = 1;

		falas = Ouvir(() => ComandoDeInteracao(dono, "regen_ligar", ""));
		AfirmarMq("\"Ligar / desligar\" LIGA, e quem esta vendo a maquina fica sabendo",
				  r.Ligado && Disse(falas, "liga o regenerador"), string.Join(" | ", falas));
		AfirmarMq("...e o desenho passa a levar a campanula", EstadoDaArte(tanque, doCatalogo) == Regenerador.ArteLigado,
				  EstadoDaArte(tanque, doCatalogo));

		// ------------------------------------------------------------ UM PULSO, EM NUMEROS
		Inteiro(p);
		Ferir(p, "Braco esquerdo", 50);
		Ferir(p, "Perna direita", 90);
		Ferir(p, "Cerebro", 90);   // vital, acima dos 70%, e nao se mira: fica de fora do segundo ramo
		p.Ficha.Ki = p.Ficha.MaxKi - 10;
		p.Facing = Facing.North;
		r.Energia = 1;
		long antesDoPulso = NowMs();
		PulsoDosTanques();
		AfirmarMq("um pulso (velocidade 1) da +3 de vida a CADA membro ferido que se pode mirar",
				  Perto(Parte(p, "Braco esquerdo").Vida, 53) && Perto(Parte(p, "Perna direita").Vida, 93),
				  $"braco {Parte(p, "Braco esquerdo").Vida:0.##}, perna {Parte(p, "Perna direita").Vida:0.##}");
		AfirmarMq("...e NAO ao cerebro acima de 70% (o `targetable = 0` do `SpreadHeal`)",
				  Perto(Parte(p, "Cerebro").Vida, 90), $"cerebro {Parte(p, "Cerebro").Vida:0.##}");
		AfirmarMq("...+2 de Ki (as duas somas de `1*efficiency`)", Perto(p.Ficha.Ki, p.Ficha.MaxKi - 8),
				  $"faltavam 10, faltam {p.Ficha.MaxKi - p.Ficha.Ki:0.##}");
		AfirmarMq("...gasta 0,002 de bateria por paciente", Perto(r.Energia, 0.998), $"{r.Energia:0.####}");
		AfirmarMq("...vira o paciente de frente (`M.dir = SOUTH`)", p.Facing == Facing.South, $"{p.Facing}");
		AfirmarMq("...e o segura por um segundo sem golpear (o `inregen`)",
				  p.NoTanqueAte >= antesDoPulso + 1000 && p.NoTanqueAte <= NowMs() + 1000, $"{p.NoTanqueAte - antesDoPulso} ms");

		falas = Ouvir(() => Atacar(p, Protocol.Golpe.Leve));
		AfirmarMq("o golpe do paciente e RECUSADO enquanto o tanque trabalha nele, e ele ouve por que",
				  Disse(falas, "regenerador"), string.Join(" | ", falas));
		AdiantoDoRelogioDeTeste += 1100;
		p.Facing = Facing.North;   // o dono esta ao sul: este soco e pro ar, o que se mede e o portao
		falas = Ouvir(() => Atacar(p, Protocol.Golpe.Leve));
		AfirmarMq("...e passado o segundo o portao abre de novo", !Disse(falas, "regenerador"), string.Join(" | ", falas));

		// ------------------------------------------------------------ PRIMEIRO O QUE MATA
		Inteiro(p);
		Ferir(p, "Cabeca", 50);
		Ferir(p, "Braco esquerdo", 50);
		PulsoDosTanques();
		AfirmarMq("com um VITAL abaixo de 70% a cura vai so pra ele: a cabeca sobe, o braco espera (`FocusVitals`)",
				  Perto(Parte(p, "Cabeca").Vida, 53) && Perto(Parte(p, "Braco esquerdo").Vida, 50),
				  $"cabeca {Parte(p, "Cabeca").Vida:0.##}, braco {Parte(p, "Braco esquerdo").Vida:0.##}");

		// ------------------------------------------------------------ O KI NAO PASSA DO TETO
		Inteiro(p);
		Ferir(p, "Braco esquerdo", 50);
		p.Ficha.Ki = p.Ficha.MaxKi - 1;
		PulsoDosTanques();
		bool noTeto = Perto(p.Ficha.Ki, p.Ficha.MaxKi);
		p.Ficha.Ki = p.Ficha.MaxKi + 50;
		PulsoDosTanques();
		AfirmarMq("o Ki para no teto, e o Ki comprimido ACIMA dele nao e mexido",
				  noTeto && Perto(p.Ficha.Ki, p.Ficha.MaxKi + 50), $"no teto {noTeto}; comprimido {p.Ficha.Ki - p.Ficha.MaxKi:0.##} acima");

		// ------------------------------------------------------------ O MESMO TILE, E SO ELE
		Inteiro(p);
		Ferir(p, "Braco esquerdo", 50);
		p.Pos = No(tx + 1, ty);
		r.Energia = 1;
		PulsoDosTanques();
		AfirmarMq("no tile VIZINHO ninguem e curado (`view(0,src)`: o mesmo tile da maquina)",
				  Perto(Parte(p, "Braco esquerdo").Vida, 50) && Perto(r.Energia, 1),
				  $"braco {Parte(p, "Braco esquerdo").Vida:0.##}");
		try
		{
			Regenerador.VizinhoContaDeTeste = true;
			PulsoDosTanques();
			AfirmarMq("(defeito injetado: o vizinho conta) a mesma regua REPROVA -- o tile ao lado tambem cura",
					  Parte(p, "Braco esquerdo").Vida > 50, $"braco {Parte(p, "Braco esquerdo").Vida:0.##}");
		}
		finally { Regenerador.VizinhoContaDeTeste = false; }

		// ------------------------------------------------------------ TODOS OS QUE ESTAO NELE
		ServerPlayer p2 = Forjar("mq: o segundo paciente", No(tx, ty), 1_000);
		p.Pos = No(tx, ty);
		Inteiro(p);
		Ferir(p, "Braco esquerdo", 50);
		Ferir(p2, "Perna esquerda", 40);
		r.Energia = 1;
		PulsoDosTanques();
		AfirmarMq("DOIS no mesmo tile: os dois sao curados, e a bateria paga os dois",
				  Perto(Parte(p, "Braco esquerdo").Vida, 53) && Perto(Parte(p2, "Perna esquerda").Vida, 43) && Perto(r.Energia, 0.996),
				  $"{Parte(p, "Braco esquerdo").Vida:0.##} / {Parte(p2, "Perna esquerda").Vida:0.##} / bateria {r.Energia:0.####}");
		p2.Pos = No(tx - 1, ty + 3);

		// ------------------------------------------------------------ QUEM JA ESTA INTEIRO
		Inteiro(p);
		r.Energia = 1;
		long seguroAntes = p.NoTanqueAte;
		PulsoDosTanques();
		AfirmarMq("quem ja esta inteiro fica no tanque de graca: sem bateria gasta e sem os golpes presos",
				  Perto(r.Energia, 1) && p.NoTanqueAte == seguroAntes);

		// ------------------------------------------------------------ AS MELHORIAS E OS PRECOS
		double zeni = dono.Ficha.Zeni;
		ComandoDeInteracao(dono, "regen_up", "velocidade");
		bool v2 = r.Eficiencia == 2 && Perto(zeni - dono.Ficha.Zeni, 1000) && Perto(Regenerador.CuraPorPulso(2), 3);
		zeni = dono.Ficha.Zeni;
		ComandoDeInteracao(dono, "regen_up", "velocidade");
		AfirmarMq("VELOCIDADE custa 1000 x a atual; a cura e `3*round(vel/2)`: 3, 3, 6 -- e 15 na velocidade 10",
				  v2 && r.Eficiencia == 3 && Perto(zeni - dono.Ficha.Zeni, 2000) && Perto(Regenerador.CuraPorPulso(3), 6)
				  && Perto(Regenerador.CuraPorPulso(1), 3) && Perto(Regenerador.CuraPorPulso(10), 15),
				  $"velocidade {r.Eficiencia}, cura {Regenerador.CuraPorPulso(r.Eficiencia):0}");

		Ferir(p, "Braco esquerdo", 50);
		p.Ficha.Ki = p.Ficha.MaxKi - 20;
		r.Energia = 1;
		PulsoDosTanques();
		AfirmarMq("na velocidade 3 o pulso da +6 de vida, +6 de Ki e gasta 0,006 de bateria",
				  Perto(Parte(p, "Braco esquerdo").Vida, 56) && Perto(p.Ficha.Ki, p.Ficha.MaxKi - 14) && Perto(r.Energia, 0.994),
				  $"braco {Parte(p, "Braco esquerdo").Vida:0.##}, Ki faltando {p.Ficha.MaxKi - p.Ficha.Ki:0.##}, bateria {r.Energia:0.####}");

		zeni = dono.Ficha.Zeni;
		ComandoDeInteracao(dono, "regen_up", "bateria");
		AfirmarMq("BATERIA MAIOR custa 500 x o teto, soma uma carga e enche",
				  Perto(zeni - dono.Ficha.Zeni, 500) && Perto(r.EnergiaMax, 2) && Perto(r.Energia, 2));

		zeni = dono.Ficha.Zeni;
		falas = Ouvir(() => ComandoDeInteracao(dono, "regen_up", "recarga"));
		bool cheiaRecusa = Perto(zeni, dono.Ficha.Zeni) && Disse(falas, "cheia");
		r.Energia = 0.5;
		ComandoDeInteracao(dono, "regen_up", "recarga");
		AfirmarMq("RECARREGAR custa 250 x o teto e enche -- e nao cobra por uma bateria que ja esta cheia",
				  cheiaRecusa && Perto(zeni - dono.Ficha.Zeni, 500) && Perto(r.Energia, 2),
				  $"cheia recusa {cheiaRecusa}, pagou {zeni - dono.Ficha.Zeni:0}");

		zeni = dono.Ficha.Zeni;
		ComandoDeInteracao(dono, "regen_up", "ferimentos");
		bool comprou = r.CuraFerimentos && Perto(zeni - dono.Ficha.Zeni, 5000);
		zeni = dono.Ficha.Zeni;
		ComandoDeInteracao(dono, "regen_up", "ferimentos");
		AfirmarMq("TRATAR FERIMENTOS custa 5000, uma vez so", comprou && Perto(zeni, dono.Ficha.Zeni));

		zeni = dono.Ficha.Zeni;
		dono.Ficha.techskill = 0;
		falas = Ouvir(() => ComandoDeInteracao(dono, "regen_up", "nanites"));
		bool semTech = r.Nanites == 0 && Perto(zeni, dono.Ficha.Zeni) && Disse(falas, "tecnologia");
		dono.Ficha.techskill = Regenerador.TechDosNanites;
		ComandoDeInteracao(dono, "regen_up", "nanites");
		AfirmarMq("NANITES pedem 6 de tecnologia e custam 2000 x (nivel + 1)",
				  semTech && r.Nanites == 1 && Perto(zeni - dono.Ficha.Zeni, 2000) && Perto(Regenerador.CustoDeNanites(1), 4000));

		dono.Ficha.Zeni = 10;
		falas = Ouvir(() => ComandoDeInteracao(dono, "regen_up", "velocidade"));
		AfirmarMq("sem zeni a melhoria e recusada, e nada muda", r.Eficiencia == 3 && Perto(dono.Ficha.Zeni, 10) && Disse(falas, "custa"));
		dono.Ficha.Zeni = 10_000_000;

		// ------------------------------------------------------------ O MEMBRO PERDIDO
		Inteiro(p);
		r.CuraFerimentos = false;
		r.Eficiencia = 100;   // a chance do sorteio vira 100%: o que se mede e a regra, e nao o dado
		r.Nanites = 0;
		r.Energia = r.EnergiaMax = 50;
		BodyPart braco = Parte(p, "Braco esquerdo"), mao = Parte(p, "Mao esquerda");
		p.Combate.Corpo.Decepar(braco);
		p.Combate.SincronizarVida();
		for (int i = 0; i < 50; i++) PulsoDosTanques();
		AfirmarMq("SEM a melhoria de ferimentos o tanque nao tem o que fazer por um membro perdido: 50 pulsos, braco no chao, bateria intacta",
				  braco.Decepado && mao.Decepado && Perto(r.Energia, 50), $"bateria {r.Energia:0.###}");

		r.CuraFerimentos = true;
		falas = Ouvir(PulsoDosTanques);
		AfirmarMq("COM ela, o sorteio do pulso refaz o braco -- e a mao volta junto (a cascata do `RegrowLimb`)",
				  !braco.Decepado && !mao.Decepado && Disse(falas, "refaz"), string.Join(" | ", falas));
		AfirmarMq("...com 70% da vida, como todo membro devolvido",
				  Perto(braco.Vida, braco.VidaMax * Regras.VidaAoRegenerar), $"{braco.Vida:0.##}");

		// ------------------------------------------------------------ A BATERIA ACABA
		Inteiro(p);
		Ferir(p, "Braco esquerdo", 50);
		r.Eficiencia = 1;
		r.CuraFerimentos = false;
		r.EnergiaMax = 1;
		r.Energia = 0.003;
		falas = Ouvir(PulsoDosTanques);
		AfirmarMq("o ultimo pulso que a bateria paga ainda cura; depois dele o tanque AVISA e DESLIGA",
				  Perto(Parte(p, "Braco esquerdo").Vida, 53) && !r.Ligado && Disse(falas, "bateria do regenerador acabou")
				  && EstadoDaArte(tanque, doCatalogo) == Regenerador.ArteDesligado,
				  $"braco {Parte(p, "Braco esquerdo").Vida:0.##}, ligado {r.Ligado}, bateria {r.Energia:0.####} | {string.Join(" | ", falas)}");
		falas = Ouvir(() => ComandoDeInteracao(dono, "regen_ligar", ""));
		AfirmarMq("...e sem bateria ele nao liga: o dono ouve quanto custa recarregar",
				  !r.Ligado && Disse(falas, "sem bateria") && Disse(falas, "250"), string.Join(" | ", falas));

		// ------------------------------------------------------------ OS NANITES
		r.Nanites = 1000;   // 0,1 ponto por nivel: 1000 = 100%
		r.Energia = 0.5;
		PulsoDosTanques();
		bool esperou = Perto(r.Energia, 0.5);
		r.Energia = 0.05;
		falas = Ouvir(PulsoDosTanques);
		AfirmarMq("os NANITES so trabalham com a bateria abaixo de 10% -- e recarregam ate com o tanque desligado",
				  esperou && Perto(r.Energia, r.EnergiaMax) && !r.Ligado && Disse(falas, "nanites"),
				  $"esperou {esperou}, bateria {r.Energia:0.###} | {string.Join(" | ", falas)}");
		r.Nanites = 0;

		// ------------------------------------------------------------ SOLTAR O PARAFUSO COM ELE LIGADO
		ComandoDeInteracao(dono, "regen_ligar", "");
		bool ligou = r.Ligado && EstadoDaArte(tanque, doCatalogo) == Regenerador.ArteLigado;
		ComandoDeInteracao(dono, "aparafusar", "");
		bool desenhoCaiu = !tanque.Aparafusada && EstadoDaArte(tanque, doCatalogo) == Regenerador.ArteDesligado;
		Inteiro(p);
		Ferir(p, "Braco esquerdo", 50);
		PulsoDosTanques();
		AfirmarMq("SOLTAR o parafuso com ele ligado: a campanula cai na hora, ele nao cura e o interruptor volta pro desligado",
				  ligou && desenhoCaiu && Perto(Parte(p, "Braco esquerdo").Vida, 50) && !r.Ligado,
				  $"ligou {ligou}, desenho caiu {desenhoCaiu}, braco {Parte(p, "Braco esquerdo").Vida:0.##}, ligado {r.Ligado}");

		// ------------------------------------------------------------ O TANQUE DA CIDADE, E O DISCO
		RegeneradorDaObra daCidade = EstadoDoTanque(new Obra { Tipo = Regenerador.Tipo, DoMapa = true, Aparafusada = true });
		AfirmarMq("o tanque que vem do MAPA nasce como o do laboratorio de Vegeta: velocidade 10 (+15 por pulso), bateria 1000, nanites 10, trata ferimentos -- e desligado",
				  daCidade is { Ligado: false, Eficiencia: 10, Energia: 10, EnergiaMax: 10, Nanites: 10, CuraFerimentos: true });

		ComandoDeInteracao(dono, "aparafusar", "");
		ComandoDeInteracao(dono, "regen_ligar", "");
		string noDisco = JsonSerializer.Serialize(tanque, new JsonSerializerOptions { IncludeFields = true });
		AfirmarMq("o interruptor do tanque de alguem VAI pro `mundo.json` (ele volta do reinicio como estava)",
				  r.Ligado && noDisco.Contains("\"Ligado\":true"), noDisco.Length > 200 ? noDisco[..200] : noDisco);
	}

	// =====================================================================
	// 3) A GEOMETRIA DO CAMPO -- o Core puro
	// =====================================================================
	private void MqDaGeometria()
	{
		GD.Print("[maquinas] -- 3) o campo como geometria: a caixa do DM, e o liquido do dono --");

		AfirmarMq("a caixa do original: `32*Range` px de lado no tile da maquina, e pega quem ENCOSTA nela (meio corpo de folga)",
				  CampoDeGravidade.Dentro(15, 0, 0) && !CampoDeGravidade.Dentro(17, 0, 0)
				  && CampoDeGravidade.Dentro(31, -31, 1) && !CampoDeGravidade.Dentro(33, 0, 1)
				  && CampoDeGravidade.Dentro(47, 47, 2) && !CampoDeGravidade.Dentro(0, 49, 2)
				  && CampoDeGravidade.Dentro(175, 0, 10) && !CampoDeGravidade.Dentro(177, 0, 10));

		bool Nada(int x, int y) => false;
		int[] aberto = [.. new[] { 0, 1, 2, 3, 4, 10 }.Select(a => CampoDeGravidade.Preencher((50, 50), a, Nada).Count)];
		AfirmarMq("em campo aberto o liquido enche o quadrado inteiro da caixa: 1, 9, 9, 25, 25 tiles e 121 no alcance 10",
				  aberto.SequenceEqual([1, 9, 9, 25, 25, 121]), string.Join(",", aberto));

		// A BASE 4x4 DO EXEMPLO DO DONO: um anel de parede 6x6 (0..5), a maquina dentro, e um campo de 5x5.
		bool Anel(int x, int y) => x >= 0 && x <= 5 && y >= 0 && y <= 5 && (x == 0 || x == 5 || y == 0 || y == 5);
		HashSet<(int X, int Y)> sala = CampoDeGravidade.Preencher((2, 2), 4, Anel);
		AfirmarMq("\"base 4x4 e area 5x5 -> ela diminui pra 4x4\": o campo enche os 16 tiles de dentro e nenhum de fora",
				  sala.Count == 16 && sala.All(c => c.X is >= 1 and <= 4 && c.Y is >= 1 and <= 4), $"{sala.Count} tiles");
		HashSet<(int X, int Y)> salaForte = CampoDeGravidade.Preencher((2, 2), 10, Anel);
		AfirmarMq("...e continua 4x4 com o alcance no teto (10): parede nao vaza, por mais forte que seja a maquina",
				  salaForte.Count == 16, $"{salaForte.Count} tiles");

		// A SALA RETANGULAR: 3 de largura por 7 de altura por dentro (anel 5x9), maquina no meio.
		bool Retangulo(int x, int y) => x >= 0 && x <= 4 && y >= 0 && y <= 8 && (x == 0 || x == 4 || y == 0 || y == 8);
		HashSet<(int X, int Y)> ret = CampoDeGravidade.Preencher((2, 4), 10, Retangulo);
		AfirmarMq("\"se o local for um retangulo\": 3x7 por dentro, o campo enche os 21 tiles -- a forma do lugar",
				  ret.Count == 21 && ret.All(c => c.X is >= 1 and <= 3 && c.Y is >= 1 and <= 7), $"{ret.Count} tiles");

		// O CORREDOR COMPRIDO: 1 de largura, 30 de comprimento. O liquido escorre ate o maximo de distancia e para.
		bool Corredor(int x, int y) => x != 1 || y < 1 || y > 30;
		HashSet<(int X, int Y)> cor = CampoDeGravidade.Preencher((1, 15), 10, Corredor);
		AfirmarMq("\"vai preencher ate o maximo de distancia\": num corredor comprido ele anda 5 tiles pra cada lado da maquina e para",
				  cor.Count == 11 && cor.Min(c => c.Y) == 10 && cor.Max(c => c.Y) == 20, $"{cor.Count} tiles, de y={cor.Min(c => c.Y)} a {cor.Max(c => c.Y)}");

		// O L: o liquido dobra a esquina (e nao e uma linha de visada).
		bool EmL(int x, int y) => !((y == 5 && x >= 5 && x <= 8) || (x == 8 && y >= 2 && y <= 5));
		HashSet<(int X, int Y)> l = CampoDeGravidade.Preencher((5, 5), 10, EmL);
		AfirmarMq("o liquido DOBRA a esquina: num corredor em L ele chega ao braco que a maquina nao ve",
				  l.Count == 7 && l.Contains((8, 2)), $"{l.Count} tiles");

		// A QUINA: falta o bloco do canto (5,5) do anel. Os tiles (4,4) e (5,5) so se tocam pela diagonal.
		bool SemACanto(int x, int y) => Anel(x, y) && !(x == 5 && y == 5);
		HashSet<(int X, int Y)> quina = CampoDeGravidade.Preencher((2, 2), 10, SemACanto);
		AfirmarMq("o campo NAO vaza pela quina: dois tiles que so se encostam na diagonal nao sao passagem",
				  quina.Count == 16 && !quina.Contains((5, 5)), $"{quina.Count} tiles");

		try
		{
			CampoDeGravidade.AtravessaParedeDeTeste = true;
			HashSet<(int X, int Y)> crua = CampoDeGravidade.Preencher((2, 2), 4, Anel);
			AfirmarMq("(defeito injetado: o campo atravessa parede) a mesma regua REPROVA -- a caixa crua do DM, 25 tiles, parede incluida",
					  crua.Count == 25, $"{crua.Count} tiles");
		}
		finally { CampoDeGravidade.AtravessaParedeDeTeste = false; }
	}

	// =====================================================================
	// 4) A MAQUINA DE GRAVIDADE, DE PE NO MUNDO
	// =====================================================================
	private void MqDoCampo(ZoneKey terra, int px, int py)
	{
		GD.Print("[maquinas] -- 4) a maquina de gravidade: cinco segundos, quem entra e quem sai, e a sala (`Gravity.dm`) --");
		const int T = ZoneCollision.TileSize;
		Vec2 No(int cx, int cy) => new(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);
		void Passa(double segundos)
		{
			AdiantoDoRelogioDeTeste += (long)(segundos * 1000);
			TickDaGravidade();
		}
		(int mx, int my) = (px + 2, py + 2);

		ServerPlayer dono = Forjar("mq: o dono da maquina", No(mx + 1, my + 1), 1_000);
		dono.Conta = "bancada_gravidade";
		dono.Ficha.Zeni = 100_000_000;

		Obra? maq = AssentarDeTeste(dono, "Gravity", mx, my);
		AfirmarMq("(montagem) a maquina assentou pelo funil de producao, e e do dono", maq is { Tipo: "Gravity", DonoConta: "bancada_gravidade" });
		if (maq == null) return;
		ComandoDeInteracao(dono, "grav_info", "");   // o estado nasce no primeiro uso
		GravidadeDaObra g = maq.Gravidade!;
		AfirmarMq("a maquina de fabrica: desligada, teto 10x, alcance 0, uma carga",
				  g is { Grav: 0, Max: 10, Range: 0, Energia: 1, EnergiaMax: 1 });

		// ------------------------------------------------------------ SOLTA NAO LIGA
		List<string> falas = Ouvir(() => ComandoDeInteracao(dono, "grav_definir", "5"));
		AfirmarMq("SOLTA, ela nao liga -- e o dono ouve que falta aparafusar", g.Grav == 0 && g.MudaEm == 0 && Disse(falas, "aparafuse"),
				  string.Join(" | ", falas));
		ComandoDeInteracao(dono, "aparafusar", "");
		AfirmarMq("o parafuso entra pelo menu dela", maq.Aparafusada);

		// ------------------------------------------------------------ OS CINCO SEGUNDOS
		falas = Ouvir(() => ComandoDeInteracao(dono, "grav_definir", "5"));
		AfirmarMq("o pedido NAO muda na hora: a maquina avisa \"cinco segundos\" e fica em espera",
				  g.Grav == 0 && g.MudaEm != 0 && Disse(falas, "cinco segundos"), string.Join(" | ", falas));
		AfirmarMq("...e avisa quem liga uma maquina de alcance 0 que ela nao pega ninguem",
				  Disse(falas, "alcance desta máquina é 0"), string.Join(" | ", falas));
		falas = Ouvir(() => ComandoDeInteracao(dono, "grav_definir", "9"));
		AfirmarMq("enquanto ela espera, um segundo pedido e recusado (o `choosinggrav`)",
				  Disse(falas, "já está mudando") && g.Pedida == 5, string.Join(" | ", falas));
		Passa(4);
		bool aindaNao = g.Grav == 0;
		falas = Ouvir(() => Passa(1.3));
		AfirmarMq("aos 4 s ainda nao mudou; passados os 5, muda -- e quem ve a maquina fica sabendo quem ajustou e pra quanto",
				  aindaNao && g.Grav == 5 && g.MudaEm == 0 && Disse(falas, "ajusta a gravidade para 5x"), string.Join(" | ", falas));
		AfirmarMq("com alcance 0 o campo NAO sai do tile da maquina: quem esta colado nela nao sente nada",
				  dono.Ficha.gravmult == 0, $"gravmult {dono.Ficha.gravmult}");

		// ------------------------------------------------------------ O ALCANCE
		double zeni = dono.Ficha.Zeni;
		for (int i = 0; i < 4; i++) ComandoDeInteracao(dono, "grav_up", "alcance");
		AfirmarMq("ALCANCE custa 500 x (atual + 1): 500 + 1000 + 1500 + 2000 pelos quatro primeiros",
				  g.Range == 4 && Math.Abs(zeni - dono.Ficha.Zeni - 5000) < 1e-6, $"alcance {g.Range}, pagou {zeni - dono.Ficha.Zeni:0}");
		AfirmarMq("...e a compra ja poe no campo quem estava ao lado (5x)", dono.Ficha.gravmult == 5, $"gravmult {dono.Ficha.gravmult}");

		// ------------------------------------------------------------ O TETO, E O CAMPO NEUTRALIZADO
		g.Energia = g.EnergiaMax = 1000;   // a bateria fica fora desta parte: ela tem a secao dela la embaixo
		falas = Ouvir(() => ComandoDeInteracao(dono, "grav_definir", "50"));
		AfirmarMq("pedir alem do teto e aparado no teto DESTA maquina (10x), e o dono ouve",
				  g.Pedida == 10 && Disse(falas, "não passa de 10x"), string.Join(" | ", falas));
		AfirmarMq("...e o campo cai NA HORA do pedido (\"Gravity temporarily neutralized\"): quem estava nele sai",
				  g.Grav == 0 && dono.Ficha.gravmult == 0 && Disse(falas, "neutralizado"), $"gravmult {dono.Ficha.gravmult}");
		Passa(5.3);
		AfirmarMq("cinco segundos depois ele volta com o valor novo", g.Grav == 10 && dono.Ficha.gravmult == 10,
				  $"grav {g.Grav}, gravmult {dono.Ficha.gravmult}");

		// ------------------------------------------------------------ ENTRAR E SAIR ANDANDO
		ServerPlayer v = Forjar("mq: o visitante", No(mx + 7, my + 3), 1_000);
		Passa(0.25);
		bool deFora = v.Ficha.gravmult == 0;
		v.Pos = No(mx + 2, my);
		falas = Ouvir(() => Passa(0.25));
		AfirmarMq("ENTRAR ANDANDO num campo ligado PESA (era o defeito: so quem estava la na hora do ajuste sentia)",
				  deFora && v.Ficha.gravmult == 10 && Disse(falas, "campo de gravidade te pressiona"),
				  $"de fora {deFora}, dentro {v.Ficha.gravmult} | {string.Join(" | ", falas)}");
		v.Pos = No(mx + 3, my);
		falas = Ouvir(() => Passa(0.25));
		AfirmarMq("...e SAIR dele alivia (alcance 4: dois tiles e meio pra cada lado do centro da maquina)",
				  v.Ficha.gravmult == 0 && Disse(falas, "sai do campo"), $"fora {v.Ficha.gravmult}");

		try
		{
			CampoDeGravidade.SoMudaNaMaquinaDeTeste = true;
			v.Pos = No(mx + 2, my);
			Passa(0.25);
			Passa(0.25);
			AfirmarMq("(defeito injetado: o campo so e relido quando mexem na maquina) a mesma regua REPROVA -- quem entra andando nao sente nada",
					  v.Ficha.gravmult == 0, $"gravmult {v.Ficha.gravmult}");
		}
		finally { CampoDeGravidade.SoMudaNaMaquinaDeTeste = false; }
		Passa(0.25);

		// ------------------------------------------------------------ SAIR DO PLANETA
		bool pesava = v.Ficha.gravmult == 10;
		ZoneKey outra = ZoneKey.Premade("Vegeta");
		ZoneList(v.Zone.Hash).Remove(v);
		v.Zone = outra;
		ZoneList(outra.Hash).Add(v);
		Passa(0.25);
		bool levou = v.Ficha.gravmult != 0;
		ZoneList(outra.Hash).Remove(v);
		v.Zone = terra;
		ZoneList(terra.Hash).Add(v);
		AfirmarMq("TROCAR DE ZONA larga o campo: a gravidade da maquina nao viaja com o corpo",
				  pesava && !levou, $"pesava {pesava}, levou {levou}");

		// ------------------------------------------------------------ OS CAMPOS SOMAM
		dono.Pos = No(mx + 2, my + 2);
		Obra? maq2 = AssentarDeTeste(dono, "Gravity", mx + 2, my + 1);
		if (maq2 != null)
		{
			ComandoDeInteracao(dono, "aparafusar", "");
			ComandoDeInteracao(dono, "grav_up", "alcance");
			ComandoDeInteracao(dono, "grav_up", "alcance");
			ComandoDeInteracao(dono, "grav_definir", "7");
			Passa(5.3);
			v.Pos = No(mx + 1, my + 1);
			Passa(0.25);
			bool somou = maq2.Gravidade is { Grav: 7, Range: 2 } && v.Ficha.gravmult == 17;
			double naSoma = v.Ficha.gravmult;

			ComandoDeInteracao(dono, "pegar", "");
			Passa(0.25);
			AfirmarMq("DUAS maquinas sobrepostas SOMAM (10x + 7x = 17x) -- e recolher uma leva o campo dela junto",
					  somou && !_noChao.Contains(maq2) && v.Ficha.gravmult == 10, $"na soma {naSoma}, depois de recolher {v.Ficha.gravmult}");
		}
		else AfirmarMq("(montagem) a segunda maquina assentou", false);
		dono.Pos = No(mx + 1, my + 1);

		// ------------------------------------------------------------ A SALA: O CAMPO COMO LIQUIDO
		GD.Print("[maquinas]    ...a sala: um anel de parede 6x6 em volta da maquina (4x4 por dentro), com uma porta a leste");
		BlocoDef parede = Bloco(ClasseDeBloco.Parede)!, porta = Bloco(ClasseDeBloco.Porta)!;
		(int x0, int y0, int x1, int y1) = (px, py, px + 5, py + 5);
		(int dx, int dy) = (x1, my);   // a porta, no meio da parede leste
		v.Pos = No(x0 + 1, y0 + 1);
		int erguidos = 0;
		for (int y = y0; y <= y1; y++)
			for (int x = x0; x <= x1; x++)
			{
				if (x != x0 && x != x1 && y != y0 && y != y1) continue;
				ComandoDeBloco(dono, Protocol.BlocoErguer, x, y, (x, y) == (dx, dy) ? porta.Numero : parede.Numero, "");
				if (BlocoEm(terra, x, y) != null) erguidos++;
			}
		AfirmarMq("(montagem) a sala subiu pelo funil de producao: 19 paredes e 1 porta",
				  erguidos == 20 && BlocoEm(terra, dx, dy)?.Def.Classe == ClasseDeBloco.Porta, $"{erguidos} blocos");

		for (int i = 0; i < 6; i++) ComandoDeInteracao(dono, "grav_up", "alcance");
		Passa(0.25);
		AfirmarMq("com o alcance no teto (10, um quadrado de 11x11) o campo enche so a SALA: os 16 tiles de dentro",
				  g.Range == 10 && g.Cheias is { Count: 16 }, $"alcance {g.Range}, {g.Cheias?.Count} tiles");

		bool cantos = true;
		foreach ((int cx, int cy) in new[] { (x0 + 1, y0 + 1), (x1 - 1, y0 + 1), (x0 + 1, y1 - 1), (x1 - 1, y1 - 1) })
		{
			v.Pos = No(cx, cy);
			Passa(0.25);
			cantos &= v.Ficha.gravmult == 10;
		}
		AfirmarMq("os quatro cantos de dentro da sala pesam", cantos);

		ServerPlayer fora = Forjar("mq: o vizinho", No(x1 + 1, my), 1_000);
		Passa(0.25);
		AfirmarMq("do lado de FORA da parede -- a um tile dela, bem dentro da caixa do alcance -- ninguem pesa: o campo nao vazou",
				  fora.Ficha.gravmult == 0, $"gravmult {fora.Ficha.gravmult}");

		try
		{
			CampoDeGravidade.AtravessaParedeDeTeste = true;
			g.Cheias = null;
			Passa(0.25);
			AfirmarMq("(defeito injetado: o campo atravessa parede) a mesma regua REPROVA -- o vizinho do lado de fora e esmagado",
					  fora.Ficha.gravmult == 10, $"gravmult {fora.Ficha.gravmult}");
		}
		finally { CampoDeGravidade.AtravessaParedeDeTeste = false; }
		g.Cheias = null;
		Passa(0.25);

		// A PORTA ABERTA NAO DERRAMA: o corpo no vao e o vizinho logo adiante continuam leves.
		BlocoDePe aPorta = BlocoEm(terra, dx, dy)!;
		AbrirPortaErguida(BaseDe(terra), aPorta, NowMs());
		v.Pos = No(dx, dy);
		g.Cheias = null;
		Passa(0.25);
		AfirmarMq("a PORTA ABERTA nao derrama o campo: quem esta no vao e quem esta logo depois dele nao pesam",
				  aPorta.AbertaAte != 0 && v.Ficha.gravmult == 0 && fora.Ficha.gravmult == 0,
				  $"no vao {v.Ficha.gravmult}, depois dele {fora.Ficha.gravmult}");
		v.Pos = No(x0 + 1, y0 + 1);

		// A PAREDE QUE CAI: o campo acompanha em ate um segundo, sem ninguem mexer na maquina.
		ComandoDeBloco(dono, Protocol.BlocoDesmanchar, x1, my + 1, 0, "");
		Passa(1.2);
		AfirmarMq("abrir um BURACO na parede derrama o campo por ele em ate um segundo: agora o vizinho pesa",
				  BlocoEm(terra, x1, my + 1) == null && fora.Ficha.gravmult == 10 && g.Cheias is { Count: > 16 },
				  $"gravmult {fora.Ficha.gravmult}, {g.Cheias?.Count} tiles");

		// ------------------------------------------------------------ A BATERIA
		g.EnergiaMax = 1;
		g.Energia = 0.15;
		_proximoDreno = NowMs() + MsDoDrenoDeGravidade;
		Passa(10.1);
		bool drenou = Math.Abs(g.Energia - 0.05) < 1e-9 && g.Grav == 10;
		falas = Ouvir(() => Passa(10.1));
		AfirmarMq("a bateria gasta `Grav*0,01` a cada dez segundos; no fim a maquina desliga, avisa e solta todo mundo",
				  drenou && g.Grav == 0 && g.Energia == 0 && v.Ficha.gravmult == 0 && fora.Ficha.gravmult == 0
				  && Disse(falas, "sem bateria e desliga"), $"drenou {drenou}, grav {g.Grav}, bateria {g.Energia:0.###} | {string.Join(" | ", falas)}");
		falas = Ouvir(() => ComandoDeInteracao(dono, "grav_definir", "5"));
		AfirmarMq("...e sem bateria ela nao aceita pedido", g.MudaEm == 0 && Disse(falas, "sem bateria"), string.Join(" | ", falas));

		g.Nanites = 100;   // um ponto percentual por nivel: 100 = certeza
		falas = Ouvir(() => Passa(10.1));
		AfirmarMq("as NANITES recarregam a maquina DESLIGADA", Math.Abs(g.Energia - g.EnergiaMax) < 1e-9 && Disse(falas, "nanites"),
				  $"bateria {g.Energia:0.###} | {string.Join(" | ", falas)}");

		// ------------------------------------------------------------ O DISCO
		ComandoDeInteracao(dono, "grav_definir", "5");
		Passa(5.3);
		string noDisco = JsonSerializer.Serialize(maq, new JsonSerializerOptions { IncludeFields = true });
		AfirmarMq("a gravidade LIGADA nao vai pro `mundo.json` (toda maquina acorda desligada, `New(): Grav = 0`); o alcance e o teto vao",
				  g.Grav == 5 && !noDisco.Contains("\"Grav\"") && !noDisco.Contains("\"Cheias\"") && !noDisco.Contains("\"MudaEm\"")
				  && noDisco.Contains("\"Range\":10") && noDisco.Contains("\"Max\":10"));
	}
}
