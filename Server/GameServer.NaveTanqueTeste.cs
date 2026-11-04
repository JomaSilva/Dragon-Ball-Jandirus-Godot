using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// AS CAMADAS 12 E 13 DA `--naveteste` (2026-10-31) -- dois pedidos do dono no mesmo recado:
///
///  12. O SHIFT AO LEME (o pedido 207): *"Ta dando pra apertar shift na pod ou capital ship pra fazer ela voar mais rapido gastando ki (oq n
///      deveria acontecer)"*. Pacotes de input com o bit da corrida, pela porta de producao (`AplicarInput`): fora do leme o shift continua
///      (no chao corre e cobra Ki, no ar e o superflight); ao leme do pod no ar, no pod no espaco e ao leme da Capital Ship no espaco, nada --
///      nem a corrida, nem a marcha rapida, nem o Ki.
///  13. O TANQUE MAIOR (o pedido 209): *"adicione uma forma de melhoria na pod e capital ship de aumentar o tanque de combustivel"*. Os numeros
///      do Core (a capacidade por degrau, a escada do preco, o teto pela tecnologia) e o verbo `nave_tanque` pelas portas de producao: em zeni e
///      em recursos, o espaco novo vazio, o teto de quem paga, o limite, o dono, o abastecer ate o tanque novo, o "Ver estado", a mochila (so
///      cheia, e o tanque vai junto na gaveta) e a Capital Ship pelo console.
///
/// Elas emprestam o corpo do dono como as outras camadas, e devolvem tudo no fim.
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// 12. O SHIFT AO LEME
	// =====================================================================
	private void RodarBancadaDoShiftAoLeme(ServerPlayer pl, Action<string, bool, string> checa)
	{
		GD.Print("  -- 12) o shift ao leme nao corre (dono, 2026-10-31, o pedido 207) --");
		const int t = ZoneCollision.TileSize;
		ZoneKey zonaAntes = pl.Zone;
		Vec2 posAntes = pl.Pos;
		double kiAntes = pl.Ficha.Ki;
		if (NaveDoPiloto(pl) is { } jaPilotava) Desembarcar(pl, jaPilotava);
		ZoneKey terra = ZoneKey.Premade("Earth");
		PlanetaNoEspaco daTerra = Espaco.PreFeitos().First(p => string.Equals(p.Nome, "Earth", StringComparison.OrdinalIgnoreCase));
		Vec2 noEspaco = Espaco.PontoDeDecolagem(daTerra) + new Vec2(40 * t, 0);
		Nave? pod = null, grande = null;

		// O SHIFT SEGURADO: trinta pacotes andando pra leste com o bit da corrida, pela porta de producao, com o Ki cheio. Devolve se algum
		// pacote ganhou a corrida, se a marcha rapida do voo ligou e quanto Ki saiu.
		(bool Correu, bool Rapido, double Ki) Shift()
		{
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			bool correu = false, rapido = false;
			for (int i = 0; i < 30; i++)
			{
				float passo = MoveRules.SpeedPx(pl.SpeedStat, false) * Protocol.TickSeconds;
				pl.LastInputMs = NowMs() - (long)Math.Round(Protocol.TickSeconds * 1000);
				byte flags = (byte)((byte)Facing.East | Protocol.InputAndando | Protocol.InputCorrendo);
				AplicarInput(pl, pl.SeqInput + 1, (uint)RelogioDeQuadrosMs(), pl.Pos + new Vec2(passo, 0), flags);
				correu |= pl.Correndo;
				rapido |= pl.VooRapido;
			}
			return (correu, rapido, pl.Ficha.MaxKi - pl.Ficha.Ki);
		}

		try
		{
			// ---------------------------------------------------------------- a. fora do leme o shift continua
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
			pl.Voando = false;
			pl.Altitude = 0f;
			RecalcularVelocidade(pl);
			(bool correuAPe, _, double kiAPe) = Shift();
			pl.Voando = true;
			pl.Altitude = Voo.AlturaDePairar;
			(_, bool rapidoNoAr, _) = Shift();
			checa("(controle) **FORA DO LEME O SHIFT CONTINUA**: a pe ele corre e a corrida cobra Ki; voando sozinho, e o superflight -- a regra nova e so de "
				  + "quem pilota", correuAPe && kiAPe > 0 && rapidoNoAr, $"a pe correu {correuAPe}, Ki gasto {kiAPe:0.###}; no ar a marcha rapida {rapidoNoAr}");
			pl.Voando = false;
			pl.Altitude = 0f;

			// ---------------------------------------------------------------- b. o pod no ar, num planeta
			pod = new Nave { Id = _proximaNaveId++, Tipo = "Spacepod", X = pl.Pos.X + 12, Y = pl.Pos.Y, DonoConta = pl.Conta, DonoNome = pl.Name };
			pod.PorZona(terra);
			_naves.Add(pod);
			UsarNave(pl);
			pl.Altitude = Voo.AlturaDePairar;
			(bool correuNoAr, bool rapidoAoLeme, double kiNoAr) = Shift();
			checa("**AO LEME DO POD, NO AR, O SHIFT NAO ACELERA** (*\"Ta dando pra apertar shift na pod ... pra fazer ela voar mais rapido\"*): a nave anda "
				  + "na velocidade dela -- nem a corrida nem a marcha rapida do voo ligam -- e o Ki do piloto nao desce",
				  pod.PilotoId == pl.Id && pl.Voando && !correuNoAr && !rapidoAoLeme && kiNoAr < 1e-9,
				  $"pilota {pod.PilotoId == pl.Id}; voando {pl.Voando}; correu {correuNoAr}; marcha rapida {rapidoAoLeme}; Ki gasto {kiNoAr:0.###}");

			// ---------------------------------------------------------------- c. o pod no espaco
			MoveToZone(pl.Id, ZonaDoEspaco, noEspaco);
			TickDasNaves();
			(bool correuNoEspaco, _, double kiNoEspaco) = Shift();
			checa("**NO POD NO ESPACO O SHIFT NAO GASTA KI** (*\"... gastando ki (oq n deveria acontecer)\"*): la fora o piloto nao tem voo, e o shift era a "
				  + "corrida do chao, cobrada no Ki dele -- agora nao corre e o Ki fica",
				  pod.PilotoId == pl.Id && pod.Zona.Equals(ZonaDoEspaco) && !pl.Voando && !correuNoEspaco && kiNoEspaco < 1e-9,
				  $"pod no espaco {pod.Zona.Equals(ZonaDoEspaco)}; voando {pl.Voando}; correu {correuNoEspaco}; Ki gasto {kiNoEspaco:0.###}");
			Desembarcar(pl, pod);
			_naves.Remove(pod);
			pod = null;

			// ---------------------------------------------------------------- d. ao leme da Capital Ship, no espaco
			grande = NaveGrandeDeBancada(pl, noEspaco + new Vec2(0, 30 * t));
			ComandoDeInteracao(pl, "nave_pilotar", "");
			(bool correuNaGrande, bool rapidoNaGrande, double kiNaGrande) = Shift();
			checa("**AO LEME DA CAPITAL SHIP O SHIFT TAMBEM NAO FAZ NADA** (*\"... na pod ou capital ship\"*): nem corrida, nem marcha rapida, nem Ki",
				  grande.PilotoId == pl.Id && !correuNaGrande && !rapidoNaGrande && kiNaGrande < 1e-9,
				  $"pilota {grande.PilotoId == pl.Id}; correu {correuNaGrande}; marcha rapida {rapidoNaGrande}; Ki gasto {kiNaGrande:0.###}");
		}
		finally
		{
			if (grande != null && NaveDoPiloto(pl) == grande) PararDePilotar(pl, grande);
			if (NaveDoPiloto(pl) is { } aoLeme) Desembarcar(pl, aoLeme);
			if (pod != null) _naves.Remove(pod);
			if (grande != null) _naves.Remove(grande);
			GravarNaves();
			if (!pl.Zone.Equals(zonaAntes)) MoveToZone(pl.Id, zonaAntes, posAntes);
			else pl.Pos = posAntes;
			pl.Ficha.Ki = kiAntes;
			pl.Voando = false;
			pl.Altitude = 0f;
			pl.VooRapido = false;
			RecalcularVelocidade(pl);
			MandarFicha(pl);
		}
	}

	// =====================================================================
	// 13. O TANQUE MAIOR
	// =====================================================================
	private void RodarBancadaDoTanqueMaior(ServerPlayer pl, Action<string, bool, string> checa)
	{
		GD.Print("  -- 13) o tanque maior (dono, 2026-10-31, o pedido 209) --");
		const int t = ZoneCollision.TileSize;
		ZoneKey zonaAntes = pl.Zone;
		Vec2 posAntes = pl.Pos;
		double zeniAntes = pl.Ficha.Zeni, recursosAntes = pl.Ficha.Recursos, techAntes = pl.Ficha.techskill;
		if (NaveDoPiloto(pl) is { } jaPilotava) Desembarcar(pl, jaPilotava);
		ZoneKey terra = ZoneKey.Premade("Earth");
		PlanetaNoEspaco daTerra = Espaco.PreFeitos().First(p => string.Equals(p.Nome, "Earth", StringComparison.OrdinalIgnoreCase));
		Nave? pod = null, assentada = null, grande = null;
		ServerPlayer? outro = null;
		string Mil(double v) => v.ToString("N0");

		try
		{
			// ---------------------------------------------------------------- a. os numeros do Core
			bool capacidades = Enumerable.Range(0, Combustivel.DegrausDoTanque + 1).All(d =>
				Math.Abs(Combustivel.Capacidade("Spacepod", d) - 1_000 * (1 + 0.5 * d)) < 1e-9
				&& Math.Abs(Combustivel.Capacidade(NaveGrande.Tipo, d) - 5_000 * (1 + 0.5 * d)) < 1e-9);
			bool precos = Enumerable.Range(0, Combustivel.DegrausDoTanque).All(d =>
				Math.Abs(Combustivel.CustoDoDegrau("Spacepod", d) - 5_000 * (d + 1)) < 1e-9
				&& Math.Abs(Combustivel.CustoDoDegrau(NaveGrande.Tipo, d) - 25_000 * (d + 1)) < 1e-9);
			bool tetos = TetoPorTecnologia.DegrausDoTanque(0) == 0 && TetoPorTecnologia.DegrausDoTanque(40) == 4
						 && TetoPorTecnologia.DegrausDoTanque(100) == 10 && TetoPorTecnologia.DegrausDoTanque(500) == 10;
			checa("**OS NUMEROS DO TANQUE MAIOR** (escolha minha, confirmada pelo dono em 2026-11-01): cada degrau soma metade do tanque de fabrica, ate 10 (o pod de 1.000 a 6.000, a "
				  + "Capital Ship de 5.000 a 30.000); o degrau custa 10 zeni por unidade que soma vezes o numero dele (o pod 5.000, 10.000 ... 50.000; a "
				  + "Capital Ship 5x); e o teto e o da tecnologia de quem paga (40 -> 4, 100 -> 10) -- e o de fabrica nao muda (1.000 e 5.000)",
				  capacidades && precos && tetos && Combustivel.Capacidade("Spacepod") == 1_000 && Combustivel.Capacidade(NaveGrande.Tipo) == 5_000
				  && Combustivel.Capacidade("Rocket_Ship", 3) == 0,
				  $"capacidades {capacidades}; precos {precos}; tetos {tetos} (40 -> {TetoPorTecnologia.DegrausDoTanque(40)})");

			// ---------------------------------------------------------------- b. o primeiro degrau, em zeni: o espaco novo vem vazio
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
			pl.Ficha.techskill = 60;
			pl.Ficha.Zeni = 1_000_000;
			pl.Ficha.Recursos = 100_000;
			pod = new Nave
			{
				Id = _proximaNaveId++, Tipo = "Spacepod", X = pl.Pos.X + 12, Y = pl.Pos.Y, DonoConta = pl.Conta, DonoNome = pl.Name, ErguidaEm = NowMs(),
			};
			pod.PorZona(terra);
			_naves.Add(pod);
			List<string> primeiro = Ouvir(() => ComandoDeInteracao(pl, "nave_tanque", ""));
			checa("**O PRIMEIRO DEGRAU, EM ZENI** (*\"uma forma de melhoria na pod ... de aumentar o tanque de combustivel\"*): o tanque do pod vai a 1.500, o "
				  + "bolso paga 5.000 zeni -- e o espaco novo vem VAZIO: o pod cheio de fabrica continua com os 1.000 que tinha",
				  pod.DegrausDoTanque == 1 && Math.Abs(CapacidadeDe(pod) - 1_500) < 1e-9 && Math.Abs(NivelDoTanque(pod) - 1_000) < 1e-9
				  && Math.Abs(pl.Ficha.Zeni - 995_000) < 1e-6 && primeiro.Any(f => f.Contains("O espaço novo vem vazio", StringComparison.Ordinal)),
				  $"degrau {pod.DegrausDoTanque}; tanque {NivelDoTanque(pod):0.##} de {CapacidadeDe(pod):0}; zeni {pl.Ficha.Zeni:0}; {Ultimos(primeiro)}");

			// ---------------------------------------------------------------- c. o segundo, em recursos (o dobro)
			ComandoDeInteracao(pl, "nave_tanque", Recurso.PalavraDeRecursos);
			checa("**...E O SEGUNDO, EM RECURSOS**: pelo dobro do preco (10.000 zeni = 20.000 recursos) -- o tanque vai a 2.000 e o combustivel continua o mesmo",
				  pod.DegrausDoTanque == 2 && Math.Abs(CapacidadeDe(pod) - 2_000) < 1e-9 && Math.Abs(NivelDoTanque(pod) - 1_000) < 1e-9
				  && Math.Abs(pl.Ficha.Recursos - 80_000) < 1e-6 && Math.Abs(pl.Ficha.Zeni - 995_000) < 1e-6,
				  $"degrau {pod.DegrausDoTanque}; tanque {NivelDoTanque(pod):0.##} de {CapacidadeDe(pod):0}; recursos {pl.Ficha.Recursos:0}; zeni {pl.Ficha.Zeni:0}");

			// ---------------------------------------------------------------- d. o teto de quem paga
			pl.Ficha.techskill = 15;   // 10 x 15/100 = 1,5 -> dois degraus
			List<string> noTeto = Ouvir(() => ComandoDeInteracao(pl, "nave_tanque", ""));
			checa("**O TETO DA TECNOLOGIA DE QUEM PAGA** (a regra das melhorias, `TetoPorTecnologia`): com 15 de tecnologia o tanque para no degrau 2 -- a recusa "
				  + "vem antes da cobranca, e diz como o teto sobe",
				  pod.DegrausDoTanque == 2 && Math.Abs(pl.Ficha.Zeni - 995_000) < 1e-6
				  && noTeto.Any(f => f.Contains("já está no teto da sua tecnologia", StringComparison.Ordinal)),
				  $"degrau {pod.DegrausDoTanque}; zeni {pl.Ficha.Zeni:0}; {Ultimos(noTeto)}");
			pl.Ficha.techskill = 60;

			// ---------------------------------------------------------------- e. o limite do jogo, e o dono
			pod.DegrausDoTanque = Combustivel.DegrausDoTanque;
			List<string> noLimite = Ouvir(() => ComandoDeInteracao(pl, "nave_tanque", ""));
			pod.DegrausDoTanque = 2;
			outro = Forjar("bancada: quem nao e o dono", new Vec2(pod.X + t, pod.Y), terra, "bancada_nao_e_dono_tanque");
			outro.Ficha.techskill = 100;
			outro.Ficha.Zeni = 1_000_000;
			List<string> alheio = Ouvir(() => ComandoDeInteracao(outro, "nave_tanque", ""));
			checa("**...O LIMITE DO JOGO E O DONO**: no degrau 10 nao ha mais degrau, e quem nao e o dono nao mexe no tanque (nem pagando)",
				  noLimite.Any(f => f.Contains("já está no limite", StringComparison.Ordinal)) && pod.DegrausDoTanque == 2
				  && alheio.Any(f => f.Contains($"é de {pl.Name}", StringComparison.Ordinal)) && Math.Abs(outro.Ficha.Zeni - 1_000_000) < 1e-6,
				  $"{Ultimos(noLimite)} | {Ultimos(alheio)}; zeni dele {outro.Ficha.Zeni:0}");
			RemoverNpc(outro);
			outro = null;

			// ---------------------------------------------------------------- f. abastecer enche o tanque NOVO; o ver estado conta
			pl.Ficha.Recursos = 5_000;
			ComandoDeInteracao(pl, "nave_abastecer", "");
			List<string> estado = Ouvir(() => ComandoDeInteracao(pl, "nave_info", ""));
			string linhaDoTanque = $"  tanque: degrau 2 de {Combustivel.DegrausDoTanque}; o próximo soma {Mil(500)} e custa {Recurso.DoisPrecos(15_000)}";
			checa("**ABASTECER ENCHE O TANQUE NOVO, E O VER ESTADO CONTA**: os 1.000 que faltavam custam 1.000 recursos; o estado diz 2.000 de 2.000, o alcance "
				  + "de 40.000 tiles, o degrau e o preco do proximo",
				  Math.Abs(NivelDoTanque(pod) - 2_000) < 1e-9 && Math.Abs(pl.Ficha.Recursos - 4_000) < 1e-6
				  && estado.Any(f => f.StartsWith($"  combustível: {Mil(2_000)} de {Mil(2_000)} (um tanque leva {Mil(40_000)} tiles", StringComparison.Ordinal))
				  && estado.Any(f => f.Contains(linhaDoTanque.Trim(), StringComparison.Ordinal)),
				  $"tanque {NivelDoTanque(pod):0.##}; recursos {pl.Ficha.Recursos:0}; {Ultimos(estado)}");

			// ---------------------------------------------------------------- g. a mochila: so cheia, e o tanque vai junto
			if (pl.Mochila.Cheio) pl.Mochila.Tirar(pl.Mochila.Pilhas[0].Id, pl.Mochila.Pilhas[0].Quantidade);
			pod.Tanque = 1_999;
			List<string> furado = Ouvir(() => ComandoDeInteracao(pl, "nave_pegar", ""));
			bool ficou = _naves.Contains(pod);
			pod.Tanque = 2_000;
			List<string> recolhe = Ouvir(() => ComandoDeInteracao(pl, "nave_pegar", ""));
			string? item = pl.Mochila.Pilhas.Select(p => p.Id).FirstOrDefault(id => MaquinaGuardada.Tipo(id) == "Spacepod" && MaquinaGuardada.Gaveta(id) > 0);
			int antesDeAssentar = _naves.Count;
			if (item != null) ComandoDeTech(pl, "posicionar", $"{item}/{pl.Pos.X:0}/{pl.Pos.Y:0}");
			assentada = _naves.Count > antesDeAssentar ? _naves[^1] : null;
			checa("**O TANQUE MAIOR VAI PRA MOCHILA E VOLTA COM A NAVE**: so cheio (o tanque NOVO: a 1 de encher ela fica) -- e na gaveta, como a velocidade; "
				  + "assentada de novo ela volta com os 2 degraus e o tanque de 2.000 cheio",
				  ficou && furado.Any(f => f.Contains("só vai pra mochila de tanque cheio", StringComparison.Ordinal))
				  && item != null && item.Contains("tanque 2", StringComparison.Ordinal)
				  && recolhe.Any(f => f.Contains($"O tanque maior também: {Mil(2_000)} de combustível.", StringComparison.Ordinal))
				  && assentada is { DegrausDoTanque: 2 } && Math.Abs(NivelDoTanque(assentada) - 2_000) < 1e-9,
				  $"ficou com 1.999 {ficou}; item {item ?? "-"}; assentada: degrau {assentada?.DegrausDoTanque}, tanque "
				  + $"{(assentada != null ? NivelDoTanque(assentada) : -1):0.##}; {Ultimos(furado)} | {Ultimos(recolhe)}");
			_naves.Remove(pod);
			pod = null;

			// ---------------------------------------------------------------- h. a Capital Ship, pelo console
			grande = NaveGrandeDeBancada(pl, Espaco.PontoDeDecolagem(daTerra) + new Vec2(80 * t, 0));
			pl.Ficha.Zeni = 1_000_000;
			ComandoDeInteracao(pl, "nave_tanque", "");
			checa("**A CAPITAL SHIP TAMBEM, PELO CONSOLE** (*\"na pod e capital ship\"*): o tanque dela vai de 5.000 a 7.500 por 25.000 zeni, com o espaco "
				  + "novo vazio",
				  grande.DegrausDoTanque == 1 && Math.Abs(CapacidadeDe(grande) - 7_500) < 1e-9 && Math.Abs(NivelDoTanque(grande) - 5_000) < 1e-9
				  && Math.Abs(pl.Ficha.Zeni - 975_000) < 1e-6,
				  $"degrau {grande.DegrausDoTanque}; tanque {NivelDoTanque(grande):0.##} de {CapacidadeDe(grande):0}; zeni {pl.Ficha.Zeni:0}");
		}
		finally
		{
			if (outro != null) RemoverNpc(outro);
			if (NaveDoPiloto(pl) is { } aoLeme) Desembarcar(pl, aoLeme);
			if (pod != null) _naves.Remove(pod);
			if (assentada != null) _naves.Remove(assentada);
			if (grande != null) _naves.Remove(grande);
			GravarNaves();
			if (pl.Mochila.Quantos("Spacepod") > 0) pl.Mochila.Tirar("Spacepod", pl.Mochila.Quantos("Spacepod"));
			pl.Ficha.Zeni = zeniAntes;
			pl.Ficha.Recursos = recursosAntes;
			pl.Ficha.techskill = techAntes;
			if (!pl.Zone.Equals(zonaAntes)) MoveToZone(pl.Id, zonaAntes, posAntes);
			else pl.Pos = posAntes;
			pl.Voando = false;
			pl.Altitude = 0f;
			RecalcularVelocidade(pl);
			MandarFicha(pl);
			MandarMochila(pl);
		}
	}
}
