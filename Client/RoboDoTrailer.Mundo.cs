using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// AS CENAS DE MUNDO DO DIRETOR: o embate de raios, os chefes, o torneio, a fusao, as esferas, o Outro
/// Mundo, o espaco, o clima, o dia passando, os menus -- e o `palco`, que so olha um palco que o
/// servidor ja monta sozinho por flag (`--biovivo`, `--agoniaviva`, ...).
/// </summary>
public partial class RoboDoTrailer
{
	/// <summary>
	/// A luz da cena pelas chaves `hora=`, `clima=`, `forca=` (ver <see cref="Luz"/>). Hora negativa nao
	/// mexe no relogio do mundo -- so no tempo.
	/// </summary>
	private void LuzDaCena(double horaPadrao = Ceu.MeioDia)
	{
		TipoDeClima clima = Enum.TryParse(Opt("clima", "Limpo"), out TipoDeClima c) ? c : TipoDeClima.Limpo;
		Luz(OptN("hora", horaPadrao), clima, OptN("forca", clima == TipoDeClima.Limpo ? 0.02 : 1));
	}

	private static string[] Pecas(string lista) => lista.Split('+', StringSplitOptions.RemoveEmptyEntries);

	private const string RoupaDoRival = "Clothes_SaiyanSuit+Armor_Elite+Clothes, Saiyan Gloves+Clothes, Saiyan Shoes";

	// =====================================================================
	// O EMBATE DE RAIOS
	// =====================================================================
	/// <summary>
	/// DOIS RAIOS SE ENCONTRAM. Quem monta e a janela da bancada de foto (`EmbateDeFoto_Montar`): dois
	/// duelistas frente a frente, cada um canalizando o `verbo` pelo `Canalizar` de producao -- e a
	/// disputa, o medidor, a letra e o estouro do empate sao do jogo. `empurra=1` responde as letras do
	/// lado A (ele vence); `empurra=0` deixa os dois parados ate o estouro dos 15 s.
	/// `verbob=` da ao rival um raio diferente, e `cora=`/`corb=` ("R,G,B") a cor de ki de cada um -- e
	/// assim que se filma o Kamehameha azul contra o Galick Ho roxo.
	/// </summary>
	private IEnumerable<double> Embate()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,200,236");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena(0.80);
		Zoom((int)OptN("zoom", 3));
		EsconderOHeroi(true);
		yield return 1.0;

		bool empurra = Opt("empurra", "1") == "1";
		(int a, int b) = S?.EmbateDeFoto_Montar(_eu, (int)OptN("tiles", 12), OptN("bpa", 5000), OptN("bpb", 5000),
			tecladoEmA: true, tilesAbaixo: (int)OptN("abaixo", 3), verbo: Opt("verbo", "Kamehameha"), escala: OptN("escala", 1),
			verboDeB: Opt("verbob", ""), corDeA: Opt("cora", ""), corDeB: Opt("corb", "")) ?? (0, 0);
		if (a == 0 || b == 0) { Nota("o embate nao achou corredor de chao perto daqui"); yield break; }

		S?.VestirNoTrailer(a, Opt("cabeloa", "Goku"), Pecas(Opt("roupaa", "Clothes_TurtleSuit")));
		S?.VestirNoTrailer(b, Opt("cabelob", "Vegeta"), Pecas(Opt("roupab", RoupaDoRival)));
		SeguirDois(a, b);
		Marca($"embate {Opt("verbo", "Kamehameha")}: MONTADO");

		double t = 0, seg = OptN("seg", 20), apartir = OptN("apartir", 6);
		bool encontrou = false;
		while (t < seg)
		{
			yield return 0.3;
			t += 0.3;
			bool existe = S?.EmbateDeFoto_Estado().Existe ?? false;
			if (existe && !encontrou) { encontrou = true; Marca("embate: as cabecas se ENCONTRARAM"); }
			if (encontrou && !existe) { Marca("embate: ACABOU"); break; }
			if (empurra && t > apartir) S?.EmbateDeFoto_Apertar();
		}
		yield return OptN("depois", 4);

		S?.EmbateDeFoto_Limpar();
		Foco(null);
		EsconderOHeroi(false);
	}

	// =====================================================================
	// OS CHEFES
	// =====================================================================
	/// <summary>
	/// UM CHEFE DE SAGA CONTRA UM GUERREIRO. Os dois nascem pelo `NascerNpc` e lutam pelo cerebro de
	/// producao, com a presa imposta. O chefe sobe de degrau pelo `AvancarEstagio` do roteiro -- a cada
	/// `cada` segundos, pra o trailer ver as formas todas; no jogo o gatilho e a vida dele.
	/// `molde=freeza_namek|cell|majin_boo|androide_17|...`, `bprival=`, `seg=`.
	/// </summary>
	private IEnumerable<double> Chefe()
	{
		Tela(hud: false, chat: Opt("chat", "0") == "1");
		(string zona, float cx, float cy) = Lugar("Namek,100,347");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena();
		Zoom((int)OptN("zoom", 3));
		EsconderOHeroi(true);

		string molde = Opt("molde", "freeza_namek");
		double bpRival = OptN("bprival", 1.5e6);
		int chefe = S?.NpcDoTrailer(_eu, molde, new Vec2(5 * T, 3 * T), (int)OptN("estagio", 0)) ?? 0;
		int rival = S?.NpcDoTrailer(_eu, Opt("rival", "guardiao_saiyajin"), new Vec2(-5 * T, 3 * T), 0, [bpRival, bpRival * 3]) ?? 0;
		if (chefe == 0 || rival == 0) { Nota($"o chefe `{molde}` ou o rival nao nasceu"); yield break; }

		S?.VestirNoTrailer(rival, Opt("cabelo", "Goku"), Pecas(Opt("roupa", "Clothes_TurtleSuit")));
		SeguirDois(chefe, rival);
		yield return 2.0;

		S?.DueloNoTrailer(chefe, rival);
		Marca($"chefe {molde}: A LUTA COMECA");

		double t = 0, seg = OptN("seg", 80), cada = OptN("cada", 14), proximo = cada, cura = 0;
		bool rivalSubiu = false;
		while (t < seg)
		{
			yield return 0.5;
			t += 0.5;
			cura += 0.5;

			// DE PE ATE O FIM DA TOMADA: a cena e a luta, e nao quem ganha. Sem isto o primeiro golpe
			// certo de um chefe de 150 milhoes encerra o plano em tres segundos.
			if (cura >= 2.5) { cura = 0; S?.CurarDeTeste(chefe); S?.CurarDeTeste(rival); }

			if (t >= proximo)
			{
				proximo += cada;
				if (S?.DegrauNoTrailer(chefe) == true) Marca($"chefe {molde}: SOBE DE FORMA");
				else if (!rivalSubiu && S?.DegrauNoTrailer(rival) == true) { rivalSubiu = true; Marca("chefe: o rival SE TRANSFORMA"); }
			}

			if (!(S?.CorpoDoTrailer(chefe).Existe ?? false) || !(S?.CorpoDoTrailer(rival).Existe ?? false))
			{ Marca("chefe: um dos dois saiu de cena"); break; }
		}

		S?.DueloNoTrailer(chefe, 0);
		S?.DueloNoTrailer(rival, 0);
		yield return 1.5;
		Foco(null);
		S?.LimparOTrailer();
		EsconderOHeroi(false);
	}

	// =====================================================================
	// O TORNEIO
	// =====================================================================
	/// <summary>
	/// O TORNEIO DE ARTES MARCIAIS: o admin abre, o heroi se inscreve pelo painel do convite, as fases
	/// sao adiantadas (`trn_avancar`) ate a primeira luta, e a camera do espectador (`AssistirTorneio`)
	/// acompanha os NPCs que lutam. `lutas=` quantas assistir, `seg=` o teto de cada uma.
	/// </summary>
	private IEnumerable<double> Torneio()
	{
		Tela(hud: true, chat: false);
		foreach (double s in Viajar("Earth", 247.5f, 460.5f)) yield return s;
		LuzDaCena();
		Zoom((int)OptN("zoom", 3));
		VestirOHeroi();
		yield return 1.0;

		C?.SendVerbo("trn_iniciar", "terra");
		double t = 0;
		PainelDoTorneio? convite = null;
		while (t < 8 && convite?.VisivelDeTeste != true)
		{
			yield return 0.25;
			t += 0.25;
			convite = Hud.Instancia?.FindChild("Torneio", true, false) as PainelDoTorneio;
		}
		Marca("torneio: o CONVITE esta na tela");
		yield return 3.0;
		convite?.ClicarParticiparDeTeste();
		yield return 1.5;

		for (t = 0; t < 40 && S?.FaseDoTorneioDeTeste != "Luta"; t += 1.0) { C?.SendVerbo("trn_avancar"); yield return 1.0; }
		Marca($"torneio: fase {S?.FaseDoTorneioDeTeste} -- a CHAVE esta na tela");
		yield return 4.0;

		if (Hud.Instancia?.FindChild("Chave", true, false) is PainelDaChave chave && chave.VisivelDeTeste) Hud.Instancia?.AlternarChave();
		Tela(hud: false, chat: false);
		M?.AssistirTorneio(true);

		int lutas = (int)OptN("lutas", 4);
		double seg = OptN("seg", 35);
		for (int n = 0; n < lutas; n++)
		{
			(int a, int b) = S?.LutadoresDeTeste ?? (0, 0);
			if (a == _eu || b == _eu)
			{
				// A VEZ DO HEROI: ele esta aqui de plateia, entao perde por nocaute e o torneio segue com
				// as lutas dos outros -- que sao as que a camera do espectador sabe filmar.
				Marca("torneio: a vez do heroi -- ele cai e a plateia continua");
				S?.NocautearNoVelorioDeTeste(_eu, 20);
				for (t = 0; t < 25 && S?.LutadoresDeTeste == (a, b) && (S?.TorneioAtivo ?? false); t += 1.0)
				{
					if (t > 6) C?.SendVerbo("trn_avancar");
					yield return 1.0;
				}
				for (t = 0; t < 30 && S?.FaseDoTorneioDeTeste != "Luta" && (S?.TorneioAtivo ?? false); t += 1.0) { C?.SendVerbo("trn_avancar"); yield return 1.0; }
				M?.AssistirTorneio(true);
				n--;
				continue;
			}
			M?.AssistirTorneio(true);
			Marca($"torneio: LUTA {n + 1} ({S?.CorpoDoTrailer(a).Nome} x {S?.CorpoDoTrailer(b).Nome})");

			for (t = 0; t < seg && S?.FaseDoTorneioDeTeste == "Luta" && S.LutadoresDeTeste == (a, b); t += 0.5) yield return 0.5;
			Marca($"torneio: luta {n + 1} parou aos {t:0.0}s (fase {S?.FaseDoTorneioDeTeste})");
			yield return 1.5;

			// ate a proxima luta: o intervalo, o preparo e a contagem sao adiantados
			for (t = 0; t < 30 && (S?.FaseDoTorneioDeTeste != "Luta" || S.LutadoresDeTeste == (a, b)); t += 1.0)
			{
				if (!(S?.TorneioAtivo ?? false)) break;
				C?.SendVerbo("trn_avancar");
				yield return 1.0;
			}
			if (!(S?.TorneioAtivo ?? false)) { Marca("torneio: acabou"); break; }
		}

		M?.AssistirTorneio(false);
		C?.SendVerbo("trn_cancelar");
		yield return 1.0;
	}

	// =====================================================================
	// A FUSAO
	// =====================================================================
	/// <summary>
	/// AS DUAS FUSOES. A Potara entra pela porta (`Convidar` + o `fus_sim` do convidado): os dois sao
	/// PUXADOS um pro outro e a cinematica comeca quando se encostam. A danca Metamoro entra pela cena
	/// (`ComecarACenaNaFotoDeFusao`), que e onde a bancada de foto tambem entra. Depois de cada uma o
	/// fundido sobe de forma (`formapotara=`, `formadanca=`).
	/// </summary>
	private IEnumerable<double> Fusao()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,200,236");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena(0.68);
		Zoom((int)OptN("zoom", 4));
		VestirOHeroi();

		int outro = S?.ForjarParaAFotoDeFusao(_eu, new Vec2(8 * T, 0), "Vegeta", OptN("bp", 3e6), "Vegeta") ?? 0;
		if (outro == 0) { Nota("o parceiro de fusao nao nasceu"); yield break; }
		S?.VestirNoTrailer(outro, "Vegeta", Pecas(RoupaDoRival));
		S?.ApontarParaAVariedade(_eu, Facing.East);
		S?.ApontarNaFotoDeCorpos(outro, Facing.West);
		Foco(Aqui + new Vector2(4 * T, 0));
		yield return 2.5;

		// ---- POTARA: o puxao, o clarao, o corpo novo
		S?.ZerarRecargaNaFotoDeFusao(_eu, outro);
		(bool entrou, RecusaDeFusao porque) = S?.ConvidarNaFotoDeFusao(_eu, outro, TipoDeFusao.Potara) ?? (false, default);
		bool aceitou = S?.AceitarNaFotoDeFusao(outro) ?? false;
		Marca($"fusao POTARA: convite {(entrou ? "entrou" : $"RECUSADO ({porque})")}, {(aceitou ? "aceito -- o PUXAO comeca" : "nao aceito")}");

		double t = 0;
		bool emCena = false;
		for (; t < 30 && !(S?.EstadoDaCoreografiaNaFotoDeFusao(_eu, outro).Fundido ?? false); t += 0.25)
		{
			if (!emCena && S?.EstadoDaCoreografiaNaFotoDeFusao(_eu, outro).EmCena == true) { emCena = true; Marca("fusao POTARA: a CINEMATICA comeca"); Foco(null); }
			yield return 0.25;
		}
		Marca($"fusao POTARA: FUNDIDOS aos {t:0.0}s");
		Foco(null);
		yield return 4.0;

		foreach (double s in FormaDoFundido(Opt("formapotara", "blue"))) yield return s;

		S?.SepararNaFotoDeFusao(_eu, "o diretor vai filmar a danca agora");
		yield return 2.0;

		// ---- METAMORO: a danca
		Por(cx + 24, cy, Facing.East);
		yield return 0.6;
		S?.PorPertoNaFotoDeFusao(outro, _eu, new Vec2(T, 0));
		S?.ApontarNaFotoDeCorpos(outro, Facing.West);
		S?.PrepararAMetamoroNaFotoDeFusao(_eu, outro);
		S?.ZerarRecargaNaFotoDeFusao(_eu, outro);
		yield return 2.0;
		bool foi = S?.ComecarACenaNaFotoDeFusao(_eu, outro, TipoDeFusao.Danca) ?? false;
		Marca($"fusao DANCA: a cinematica {(foi ? "comeca" : "NAO comecou")}");
		for (t = 0; t < 20 && !(S?.EstadoDaCoreografiaNaFotoDeFusao(_eu, outro).Fundido ?? false); t += 0.25) yield return 0.25;
		Marca($"fusao DANCA: FUNDIDOS aos {t:0.0}s");
		yield return 4.0;

		foreach (double s in FormaDoFundido(Opt("formadanca", "ssj4"))) yield return s;

		S?.SepararNaFotoDeFusao(_eu, "fim da cena");
		yield return 1.5;
		S?.LimparAFotoDeFusao();
	}

	/// <summary>O corpo fundido sobe de forma, com a estreia, posa e carrega -- e volta a base.</summary>
	private IEnumerable<double> FormaDoFundido(string id)
	{
		if (id.Length == 0 || Catalogo.Def(id) is not { } def) yield break;
		double cena = Cinematicas.Para(def)?.Segundos ?? 0;
		Marca($"fusao: o fundido vira {id} (cena de {cena:0.0}s)");
		S?.FormaNaFotoDeFusao(_eu, id);
		yield return cena + 0.4;
		Marca($"fusao: {id} de pe");
		yield return 2.5;
		C?.SendCarregar(true);
		yield return 3.0;
		C?.SendCarregar(false);
		yield return 0.8;
		S?.FormaNaFotoDeFusao(_eu, Catalogo.IdBase);
		yield return 1.2;
	}

	// =====================================================================
	// AS ESFERAS DO DRAGAO
	// =====================================================================
	/// <summary>
	/// AS SETE REUNIDAS E O DRAGAO. A janela `EsferasNoTrailer` poe as sete do set desta zona em volta
	/// do heroi (em Namek e o Porunga eterno; noutro mundo ela ergue um Shenron), e dai em diante e o
	/// jogo: o `db_invocar` e o `db_desejar` do jogador.
	/// </summary>
	private IEnumerable<double> EsferasDoDragao()
	{
		Tela(hud: false, chat: Opt("chat", "0") == "1");
		(string zona, float cx, float cy) = Lugar("Earth,200,236");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena(0.68);
		Zoom((int)OptN("zoom", 2));
		VestirOHeroi();
		yield return 2.0;

		string dragao = S?.EsferasNoTrailer(_eu) ?? "";
		Marca($"esferas: as sete REUNIDAS ({(dragao.Length > 0 ? dragao : "NAO HA SET AQUI")})");
		yield return OptN("antes", 5);

		// O DRAGAO NASCE UM TILE AO NORTE e tem onze de altura (`Dragon.tres`, 256x353): a camera sobe
		// ate o meio dele, senao a cabeca fica fora do quadro.
		Vector2 pes = Aqui;
		Pan(pes, pes + new Vector2(0, -(float)OptN("sobe", 4.5) * T), 1.2);
		C?.SendVerbo("db_invocar");
		Marca("esferas: INVOCOU");
		yield return OptN("dragao", 10);

		string desejo = Opt("desejo", "");
		if (desejo.Length > 0)
		{
			C?.SendVerbo("db_desejar", desejo);
			Marca($"esferas: pediu `{desejo}`");
			yield return 8;
		}
	}

	// =====================================================================
	// A MORTE E O OUTRO MUNDO
	// =====================================================================
	/// <summary>
	/// O HEROI MORRE, o corpo fica, e ele acorda no Outro Mundo -- de aureola, diante do Rei Enma, e
	/// depois voando sobre o Caminho da Serpente. A morte e o `CombatState.Morrer` de producao; a
	/// viagem e a do relogio da morte, so que vencido na hora (`VencerORelogioDoVelorioDeTeste`).
	/// </summary>
	private IEnumerable<double> OutroMundo()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,200,236");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena(0.68);
		Zoom((int)OptN("zoom", 4));
		VestirOHeroi();
		yield return 2.5;

		bool morreu = S?.MatarNoVelorioDeTeste(_eu) ?? false;
		Marca($"alem: o heroi {(morreu ? "MORREU" : "nao morreu")}");
		yield return 5.0;

		S?.VencerORelogioDoVelorioDeTeste(_eu);
		double t = 0;
		for (; t < 12 && S?.ZonaDoTrailer(_eu) != Alem.ZonaDoOutroMundo; t += 0.25) yield return 0.25;
		if (S?.ZonaDoTrailer(_eu) != Alem.ZonaDoOutroMundo) S?.LevarProAlemNoVelorioDeTeste(_eu);
		yield return 4.0;
		Luz();
		Zoom((int)OptN("zoomalem", 3));
		Marca($"alem: CHEGOU ao Outro Mundo depois de {t:0.0}s");
		yield return 6.0;

		bool diante = S?.PorDianteDoEnmaDeTeste(_eu) ?? false;
		yield return 4.0;
		Luz();
		Marca($"alem: diante do ENMA ({diante})");
		yield return 6.0;

		// O CAMINHO DA SERPENTE, voando: morto voa, e a aureola vai junto.
		foreach (double s in Viajar(Alem.ZonaDoOutroMundo, 183, 330)) yield return s;
		Luz();
		S?.VoarNaBoca(_eu, 3 * T);
		yield return 1.0;
		Marca("alem: o CAMINHO DA SERPENTE");
		M?.AndarDeTeste(Vector2.Up);
		yield return 9.0;
		M?.PararDeTeste();
		yield return 0.5;
	}

	/// <summary>
	/// A MORTE EM COMBATE: um chefe no ultimo degrau recebe o heroi como presa (`PresaDoRoteiro`, o
	/// mesmo campo do torneio) e o heroi nao reage. O golpe, a queda e o cadaver sao do jogo -- e a
	/// primeira versao da cena do Outro Mundo matava o heroi com `Morrer` direto e ele ficava DE PE,
	/// sem golpe nenhum, ate ganhar a aureola: a morte nao aparecia.
	/// </summary>
	private IEnumerable<double> Morte()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Namek,100,347");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena();
		Zoom((int)OptN("zoom", 3));
		VestirOHeroi();
		yield return 2.0;

		int chefe = S?.NpcDoTrailer(_eu, Opt("molde", "freeza_namek"), new Vec2(7 * T, 0), (int)OptN("estagio", 4)) ?? 0;
		if (chefe == 0) { Nota("o algoz nao nasceu"); yield break; }
		foreach (double s in Encarar(Vector2.Right, P(Aqui))) yield return s;
		SeguirDois(chefe, _eu);
		yield return 2.5;

		S?.DueloNoTrailer(chefe, _eu);
		Marca("morte: o chefe ATACA");
		double t = 0;
		for (; t < OptN("seg", 60) && !(S?.CorpoDoTrailer(_eu).Morto ?? true); t += 0.25) yield return 0.25;
		Marca($"morte: o heroi CAIU aos {t:0.0}s");
		S?.DueloNoTrailer(chefe, 0);
		yield return OptN("depois", 7);
		Foco(null);
		S?.LimparOTrailer();
	}

	// =====================================================================
	// O ESPACO
	// =====================================================================
	/// <summary>
	/// DECOLA, VE O MUNDO DE FORA, ATRAVESSA O ESPACO E POUSA NOUTRO PLANETA. O traje espacial vai na
	/// mochila antes (sem ele o corpo sufoca em vinte segundos de vacuo, `GameServer.Vacuo.cs`); a
	/// decolagem e a habilidade `decolar`, a viagem e o piloto automatico, e o pouso e encostar no disco.
	/// `alvo=Nome` escolhe o planeta; sem isso vai o gerado por semente mais proximo.
	/// </summary>
	private IEnumerable<double> EspacoSideral()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,200,236");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena();
		Zoom((int)OptN("zoom", 3));
		VestirOHeroi();
		bool traje = S?.TrajeNaMochilaDeTeste(_eu) ?? false;
		yield return 2.0;

		S?.VoarNaBoca(_eu, 2 * T);
		yield return 1.5;
		Marca($"espaco: DECOLA (traje {traje})");
		C?.SendHabilidade("decolar");

		double t = 0;
		for (; t < 20 && !(C != null && Jandirus.Core.World.Espaco.EhEspaco(C.Zone)); t += 0.25) yield return 0.25;
		if (C == null || !Jandirus.Core.World.Espaco.EhEspaco(C.Zone)) { Nota("nao chegou ao espaco"); yield break; }
		yield return 3.0;
		Marca($"espaco: EM ORBITA depois de {t:0.0}s -- {C.Planetas.Count} planetas a vista");

		// O MUNDO DE ONDE SE SAIU, VISTO DE FORA: a camera desce do corpo ate o disco e volta.
		if (C.Planetas.OrderBy(p => (p.Pos - Aqui).Length()).Cast<GameClient.PlanetaInfo?>().FirstOrDefault() is { } casa)
		{
			double orbita = OptN("orbita", 8);
			Vector2 meio = Aqui.Lerp(casa.Pos, 0.6f);
			Pan(Aqui, meio, orbita * 0.45);
			yield return orbita * 0.7;
			Pan(meio, Aqui, orbita * 0.3);
			yield return orbita * 0.3;
			Foco(null);
		}

		// O PLANETA DE DESTINO sai da MESMA funcao pura que o servidor e a carta estelar usam
		// (`Sistemas.Do`, que le os sistemas da seed): varre as celulas em volta daqui e fica com o
		// mundo mais perto -- preferindo os de jardim, que e onde ha o que ver ao pousar --, ou com o de
		// nome pedido em `alvo=`. A vizinhanca que o servidor manda (`C.Planetas`) so alcanca as chunks
		// em volta, e em orbita da Terra ela so tem a Terra.
		//
		// ============================ LONGE DA ESTRELA, E PELO LADO DE FORA ============================
		// A primeira tomada desta cena pos o corpo a 1500 px de um mundo vulcanico de orbita 0 -- em
		// cima da coroa da estrela dele --, e o heroi morreu queimado (`GameServer.Sol.cs`) antes de
		// pousar. Entao o mundo escolhido tem que orbitar longe, e o corpo chega pelo lado OPOSTO ao
		// sol: o planeta fica entre os dois.
		// ==============================================================================================
		string querido = Opt("alvo", "");
		Vector2 eu = Aqui;
		PlanetaNoEspaco? alvo = null;
		Vec2 sol = default;
		double melhor = double.MaxValue;
		SistemaId aqui = SistemaId.De(P(eu));
		for (int dy = -3; dy <= 3; dy++)
			for (int dx = -3; dx <= 3; dx++)
			{
				if (Sistemas.Do(C.SeedDoUniverso, aqui.Sx + dx, aqui.Sy + dy) is not { } sistema) continue;
				for (int k = 0; k < sistema.Orbitas; k++)
				{
					PlanetaNoEspaco p = sistema.Planeta(k);
					if (querido.Length > 0 ? p.Nome != querido : p.Premade) continue;
					if ((p.Pos - sistema.Estrela.Pos).Length < sistema.Estrela.Raio + Sistemas.RaioLetalMaximo + 1600) continue;

					double nota = (V(p.Pos) - eu).Length()
								* (p.Nome.StartsWith("Jardim") ? 0.2 : p.Nome.StartsWith("Vulcanico") || p.Nome.StartsWith("Morto") ? 4 : 1);
					if (nota < melhor) { melhor = nota; alvo = p; sol = sistema.Estrela.Pos; }
				}
			}
		if (alvo is not { } destinoNoMapa) { Nota($"nao achei planeta `{querido}` em volta daqui"); yield break; }
		var destino = new GameClient.PlanetaInfo(destinoNoMapa.Nome, V(destinoNoMapa.Pos), destinoNoMapa.Raio, destinoNoMapa.Seed, destinoNoMapa.Premade);

		// A VIAGEM DE VERDADE LEVA DIAS (a Terra e Namek estao a sete, `Espaco.DistanciaTerraNamek`), e
		// uma tomada nao tem dias: o corpo e posto no espaco a alguns segundos de voo do destino, e dali
		// em diante voa, se aproxima e pousa por conta propria.
		float folga = destino.Raio + (float)OptN("chegada", 1300);
		{
			Vector2 perto = destino.Pos + (destino.Pos - V(sol)).Normalized() * folga;
			S?.MoveToZone(_eu, S.ZonaDoEspaco, P(perto));
			for (t = 0; t < 12 && Aqui.DistanceTo(perto) > 200f; t += 0.25) yield return 0.25;
			yield return 3.5;
			eu = Aqui;
			Marca($"espaco: posto a {(destino.Pos - eu).Length():N0} px de {destino.Nome}");
			yield return OptN("antes", 2);
		}

		Marca($"espaco: rumo a {destino.Nome}, a {(destino.Pos - eu).Length():N0} px (raio {destino.Raio:0})");
		Input.ActionPress("run");
		M?.Pilotar(destino.Pos);

		ZoneKey espaco = C.Zone;
		double teto = OptN("viagem", 90);
		for (t = 0; t < teto && C.Zone.Hash == espaco.Hash; t += 0.25)
		{
			// O PILOTO SOLTA SOZINHO quando chega perto (`LocalPlayer`: "voce chegou"), e o disco e maior
			// que essa folga: se ele parou no espaco, religa ate encostar.
			if (((int)(t * 4)) % 8 == 0) M?.Pilotar(destino.Pos);
			yield return 0.25;
		}
		Input.ActionRelease("run");
		if (C.Zone.Hash == espaco.Hash) { Marca("espaco: NAO POUSOU no prazo"); yield break; }

		yield return 3.5;
		Luz();
		Marca($"espaco: POUSOU em {C.Zone.Name} depois de {t:0.0}s de viagem");
		S?.VoarNaBoca(_eu, 3 * T);
		yield return 1.0;
		M?.AndarDeTeste(new Vector2(1, 0.3f));
		yield return OptN("passeio", 12);
		M?.PararDeTeste();
		yield return 0.5;
	}

	// =====================================================================
	// O CLIMA E O DIA
	// =====================================================================
	/// <summary>UM CLIMA DEPOIS DO OUTRO no mesmo lugar (`climas=Chuva,Tempestade,...`, `cada=` segundos).</summary>
	private IEnumerable<double> Climas()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,430,250");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		Zoom((int)OptN("zoom", 3));
		VestirOHeroi();
		double hora = OptN("hora", Ceu.MeioDia), cada = OptN("cada", 6);
		Luz(hora);
		yield return 2.0;

		foreach (string nome in Opt("climas", "Chuva,Tempestade,Neve,Nevasca,Areia,Neblina,ChuvaDeSangue,ChuvaDeNamek").Split(','))
		{
			if (!Enum.TryParse(nome, out TipoDeClima clima)) { Nota($"nao existe o clima `{nome}`"); continue; }
			Luz(hora, clima, 1);

			// AQUI O RELOGIO ANDA SOLTO, e tem que andar: o clima ENTRA aos poucos, e a rampa dele e
			// contada no tempo do mundo (`ClimaForcado`). Com a hora cravada (`HoraDoTrailer` puxa o
			// relogio de volta duas vezes por segundo) a rampa nunca saia do zero -- a primeira tomada
			// desta cena tem oito climas e nenhuma gota. Um minuto de sol andando nao muda a luz.
			_horaCravada = -1;
			Marca($"clima: {clima}");
			yield return cada;
		}
		Luz(hora);
		yield return 1.0;
	}

	/// <summary>
	/// O DIA PASSANDO DEPRESSA: o ceu e funcao pura do tempo (`Ceu.De`), entao adiantar o relogio do
	/// mundo E a cena -- o sol desce, a noite cai, a lua sobe, amanhece. `dias=` quantos, `seg=` em quanto.
	/// </summary>
	private IEnumerable<double> DiaENoite()
	{
		Tela(hud: Opt("hud", "0") == "1", chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,247,405");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		Zoom((int)OptN("zoom", 2));
		VestirOHeroi();
		Luz(OptN("hora", 0.45));

		// PAIRANDO: de dentro de um patio murado o veu de sombra tapa o resto do mapa, e o que esta cena
		// mostra e a LUZ do mundo. Acima do cenario o veu abre (`Voo.AlturaQueAtravessa`).
		S?.VoarNaBoca(_eu, (float)OptN("altura", 3) * T);
		yield return 2.5;

		double seg = OptN("seg", 20), dias = OptN("dias", 1);
		Marca($"dia: o relogio DISPARA ({dias:0.#} dia(s) em {seg:0}s)");
		_horaCravada = -1;
		_ceuPorSegundo = dias * Ceu.SegundosPorDia / seg;
		yield return seg;
		_ceuPorSegundo = 0;
		Marca("dia: o relogio volta ao normal");
		yield return 1.5;
	}

	private double _ceuPorSegundo;

	// =====================================================================
	// O PALCO ALHEIO, OS MENUS E A CIDADE
	// =====================================================================
	/// <summary>
	/// SO OLHA. Pra os palcos que o SERVIDOR ja monta sozinho por flag (`--biovivo`, `--biofilme`,
	/// `--agoniaviva`, `--macacovivo`): a cena acontece la, e o diretor so tira a HUD, escolhe a lente e
	/// aponta a camera (`foco=dx,dy` em tiles a partir do corpo; `nome=` segue um corpo pelo nome).
	/// </summary>
	private IEnumerable<double> Palco()
	{
		Tela(hud: Opt("hud", "0") == "1", chat: Opt("chat", "0") == "1");
		if (Opt("lugar", "").Length > 0)
		{
			(string zona, float cx, float cy) = Lugar("");
			foreach (double s in Viajar(zona, cx, cy)) yield return s;
		}
		Zoom((int)OptN("zoom", 3));
		if (Opt("vestir", "0") == "1") VestirOHeroi();
		if (Opt("luz", "0") == "1") LuzDaCena();
		if (OptN("altura", 0) > 0) S?.VoarNaBoca(_eu, (float)OptN("altura", 0) * T);
		if (Opt("esconder", "0") == "1") EsconderOHeroi(true);

		string[] foco = Opt("foco", "").Split(',');
		if (foco.Length == 2 && float.TryParse(foco[0], System.Globalization.CultureInfo.InvariantCulture, out float dx)
							 && float.TryParse(foco[1], System.Globalization.CultureInfo.InvariantCulture, out float dy))
			_desvioDoFoco = new Vector2(dx * T, dy * T);

		string nome = Opt("nome", "");
		double seg = OptN("seg", 60);
		Marca($"palco: olhando por {seg:0}s");
		for (double t = 0; t < seg; t += 0.5)
		{
			if (nome.Length > 0 && _seguindo == 0 && M?.IdPeloNome(nome) is int id and not 0) { Seguir(id); Marca($"palco: achei `{nome}` (id {id})"); }
			yield return 0.5;
		}
	}

	/// <summary>
	/// O MENU P, ABA POR ABA, devagar o bastante pra ler (`abas=Stats,Forms,...`, `cada=` segundos).
	/// As abas sao trocadas pelo `IrPara`, o mesmo caminho do botao.
	/// </summary>
	private IEnumerable<double> Menus()
	{
		Tela(hud: true, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,430,250");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena();
		Zoom((int)OptN("zoom", 3));
		VestirOHeroi();
		S?.ArmarParaAVariedade(_eu);
		S?.ScouterNaMochilaDeTeste(_eu);
		S?.NavSystemNaMochilaDeTeste(_eu, true);
		yield return 2.0;

		if (MenuJogo.Instancia is not { } menu) { Nota("o menu P nao existe"); yield break; }
		menu.Abrir();
		yield return 1.0;
		Nota("abas vivas: " + string.Join(", ", menu.AbasDeTeste));

		double cada = OptN("cada", 3);
		string pedidas = Opt("abas", "");
		foreach (string aba in pedidas.Length > 0 ? pedidas.Split(',') : menu.AbasDeTeste)
		{
			menu.IrPara(aba);
			Marca($"menu: aba {aba}");
			yield return cada;
		}
		menu.Fechar();
		yield return 0.5;
	}

	/// <summary>
	/// A VIDA NA CIDADE, com a HUD ligada: o heroi anda entre os habitantes, do jeito que o jogador ve.
	/// `rota=dx,dy,seg|dx,dy,seg` sao os trechos a pe.
	/// </summary>
	private IEnumerable<double> Cidade()
	{
		Tela(hud: Opt("hud", "1") == "1", chat: Opt("chat", "1") == "1");
		(string zona, float cx, float cy) = Lugar("Earth,247,432");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena();
		Zoom((int)OptN("zoom", 3));
		VestirOHeroi();
		yield return 1.0;

		// PRA ONDE O POVO ESTA: o habitante com mais vizinhos desta zona (`OndeHaGenteNoTrailer`).
		if (S?.OndeHaGenteNoTrailer(_eu) is { Achou: true } gente)
		{
			S.PorNoPontoNaFotoDoBorrao(_eu, gente.Onde + new Vec2(0, 2 * T), Facing.North);
			Marca($"cidade: {gente.Quantos} habitantes em volta de ({gente.Onde.X / T:0},{gente.Onde.Y / T:0})");
		}
		yield return 2.5;

		foreach (string trecho in Opt("rota", "1,0,6|0,1,6|-1,0,6").Split('|'))
		{
			string[] p = trecho.Split(',');
			if (p.Length < 3) continue;
			var rumo = new Vector2(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
								   float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
			double seg = double.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);
			Marca($"cidade: anda rumo ({rumo.X:0.#},{rumo.Y:0.#}) por {seg:0.#}s");
			if (rumo.LengthSquared() > 0.001f) M?.AndarDeTeste(rumo);
			yield return seg;
			M?.PararDeTeste();
			yield return 0.6;
		}
	}
}
