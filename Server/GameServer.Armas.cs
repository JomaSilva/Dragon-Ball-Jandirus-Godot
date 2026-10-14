using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Economia;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// (v0.2.1, lote T2 da tech) AS ARMAS, AS BOMBAS E AS DEFESAS -- o que e de atirar, de explodir e de vestir entre
/// os itens que ate a v0.2.0 caiam na mochila sem acao: as oito armas de fogo e a caixa de municao, o atordoador,
/// a nuke, o lixo toxico, a bomba de fumaca, o detonador, o campo de forca e as luvas e botas de dreno.
///
/// AS REGRAS moram no `Core/Tech` (`ArmasDeFogo`, `Atordoador`, `Bombas`, `CampoDeForca`, `DrenoDeEnergia`), cada uma
/// com o que o original faz e o que muda aqui. Este arquivo e a costura: o tiro sai pela porta de todo projetil
/// (`Disparar`), a explosao estraga o cenario pela porta de toda explosao (`EstragarOCenarioNaExplosao`), o dano em
/// area e o arremesso sao os das tecnicas (`EspalharDanoG3`, `Arremessar`), e o estado de cada unidade vai no id
/// dela (`TrocarItem`).
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// O DESPACHO
	// =====================================================================
	/// <summary>As acoes de mochila deste lote. Falso quando a acao nao e daqui.</summary>
	private bool AcaoDeArmaOuBomba(ServerPlayer pl, ItemDef def, string acao, string arg2)
	{
		switch (acao)
		{
			case ArmasDeFogo.AcaoAtirar: AtirarComOItem(pl, def); return true;
			case ArmasDeFogo.AcaoRecarregar: RecarregarAArma(pl, def, arg2); return true;
			case ArmasDeFogo.AcaoInfo: InfoDaArma(pl, def); return true;
			case ArmasDeFogo.AcaoForca:
			case ArmasDeFogo.AcaoCritico:
			case ArmasDeFogo.AcaoEmpurrao:
			case ArmasDeFogo.AcaoAtordoar:
			case ArmasDeFogo.AcaoReserva:
			case ArmasDeFogo.AcaoEncher:
				MelhorarAArma(pl, def, acao, arg2);
				return true;
			// `verb/Bolt(var/obj/O in oview(1))` (`buildable.dm:211-224`): o Bolter aparafusa o que e SEU, a um tile --
			// a mesma porta do "Aparafusar / soltar" do menu da tecla E, com a mesma conferencia de dono.
			case ArmasDeFogo.AcaoAparafusar: Aparafusar(pl); return true;

			case Bombas.AcaoArmar: ArmarABomba(pl, def, arg2); return true;
			case Bombas.AcaoCodigo: GravarOCodigo(pl, def, arg2); return true;
			case Bombas.AcaoReforcar: ReforcarABomba(pl, def, arg2); return true;
			case Bombas.AcaoInfo: InfoDaBomba(pl, def); return true;
			case Bombas.AcaoRomper: RomperOBarril(pl, def); return true;
			case Bombas.AcaoSoltarFumaca: SoltarAFumaca(pl, def); return true;
			case Bombas.AcaoDetonar: ArmarODetonador(pl, def, arg2); return true;

			case CampoDeForca.AcaoLigar: LigarOCampo(pl, def); return true;
			case CampoDeForca.AcaoInfo: InfoDoCampo(pl, def); return true;
			case DrenoDeEnergia.AcaoVestir: VestirODreno(pl, def); return true;
			// (lote T3) o reparador portatil e o destruidor -- `GameServer.MaquinasDeChao.cs`.
			default: return AcaoDeFerramentaDeParede(pl, acao, arg2) || AcaoDeMaquinaGrande(pl, def, acao, arg2);
		}
	}

	/// <summary>O verb de tecla deste lote (`arma_atirar`). Falso quando o comando nao e daqui.</summary>
	private bool ComandoDeArma(ServerPlayer pl, string cmd)
	{
		if (cmd != ArmasDeFogo.VerboAtirar) return false;

		// A PRIMEIRA ARMA DA MOCHILA QUE TEM COM O QUE ATIRAR -- a ordem e a da grade, que o jogador ve. Sem nenhuma
		// carregada, a primeira arma responde (e e ela que diz o que falta).
		ItemDef? primeira = null;
		foreach (Pilha p in pl.Mochila.Pilhas)
		{
			if (p.Quantidade <= 0 || CatalogoDeItens.Get(p.Id) is not { } def || Array.IndexOf(def.AcoesDoItem, ArmasDeFogo.AcaoAtirar) < 0) continue;
			primeira ??= def;
			if (!TemTiroNoCano(pl, def)) continue;
			AtirarComOItem(pl, def);
			return true;
		}
		if (primeira != null) AtirarComOItem(pl, primeira);
		else Avisar(pl, "você não tem uma arma de fogo.");
		return true;
	}

	/// <summary>Esta arma atira se o gatilho for apertado agora? (Carga no pente, na reserva ou no corpo.)</summary>
	private static bool TemTiroNoCano(ServerPlayer pl, ItemDef def)
	{
		string b = TechDeBolso.Base(def.Id);
		if (b == Atordoador.Id) return Atordoador.Carregado(def.Id);
		if (ArmasDeFogo.De(b) is not { } arma) return false;
		return arma.Fonte == ArmasDeFogo.Fonte.Ki
			? pl.Ficha.Ki >= ArmasDeFogo.KiDoGasterPorTiro * pl.Ficha.BaseDrain()
			: ArmasDeFogo.Ler(def.Id).Carga >= 1;
	}

	/// <summary>Este corpo carrega alguma arma de fogo? (O fato que faz o verb "Atirar" existir pra ele -- `Fatos.TemArmaDeFogo`.)</summary>
	private static bool TemArmaDeFogo(ServerPlayer pl)
	{
		foreach (Pilha p in pl.Mochila.Pilhas)
		{
			if (p.Quantidade <= 0) continue;
			string b = TechDeBolso.Base(p.Id);
			if (b == Atordoador.Id || ArmasDeFogo.De(b) != null) return true;
		}
		return false;
	}

	// =====================================================================
	// O TIRO -- `obj/items/Guns/proc/Shoot` (`Guns.dm:116-140`)
	// =====================================================================
	/// <summary>
	/// QUANDO A ARMA DESTE CORPO ATIRA DE NOVO: o `usr.blasting = 1` ... `spawn(refire) usr.blasting = 0` do original.
	/// La o `blasting` e um so pro ki e pra arma; aqui a arma tem o relogio dela, e quem segura as duas coisas e o
	/// `PodeAtirar` (raio na mao, tecnica de ki no ar).
	/// </summary>
	private readonly Dictionary<int, long> _armaProntaEm = [];

	private bool ArmaEsfriando(ServerPlayer pl) => _armaProntaEm.TryGetValue(pl.Id, out long pronta) && NowMs() < pronta;

	/// <summary>O `powerlevel` desta arma: o reforcado, do cofre (`Estado.Cofre`), ou o de fabrica do modelo.</summary>
	private double ForcaDaArma(string id) =>
		ArmasDeFogo.De(TechDeBolso.Base(id)) is { } arma ? NumeroDaGaveta(ArmasDeFogo.Ler(id).Cofre, 0, arma.Forca) : 0;

	/// <summary>O `strength` deste atordoador: o reforcado, do cofre, ou o de fabrica.</summary>
	private double ForcaDoAtordoador(string id) => NumeroDaGaveta(Atordoador.Cofre(id), 0, Atordoador.ForcaDeFabrica);

	/// <summary>O `strength` desta bomba: o reforcado, do cofre, ou o de fabrica.</summary>
	private double ForcaDaBomba(string id) => NumeroDaGaveta(Bombas.Cofre(id), 0, Bombas.ForcaDaNuke);

	/// <summary>A armadura de agora e a maxima deste campo de forca, do cofre. Zero nas duas = ainda nao foi ligado.</summary>
	private (double Armadura, double Maxima) ArmaduraDoCampo(string id)
	{
		int gaveta = CampoDeForca.Cofre(id);
		double maxima = NumeroDaGaveta(gaveta, 1, 0);
		return (Math.Min(NumeroDaGaveta(gaveta, 0, 0), maxima), maxima);
	}

	/// <summary>
	/// UM NUMERO NA ESCALA DE PODER NUMA FRASE -- a regra principal do BP (dono): *"sem scouter vc n tem como saber
	/// seu bp de nenhuma forma, oq vc sabe é se alguem é mais forte ou mais fraco q vc em porcentagem"*. A forca de
	/// uma arma reforcada, de uma bomba, a armadura de um campo saem do teto de tecnologia de quem pagou, que e
	/// funcao do poder dele: em numero so pra quem tem medidor; pros outros, medido contra o proprio poder.
	/// </summary>
	private string PoderEscrito(ServerPlayer leitor, double poder) =>
		Jandirus.Core.Stats.PoderNaFrase.EmNumero(TemScouter(leitor))
			? poder.ToString("N0")
			: Jandirus.Core.Stats.PoderNaFrase.ContraOSeu(poder, leitor.Ficha.expressedBP);

	/// <summary>A chave com que o tiro de uma arma pergunta pela arte e pelo som dele (`ArteDeProjetil`, `SonsDeKi`).</summary>
	private static string VerboDaArma(string @base) => ArmasDeFogo.PrefixoDeVerbo + @base;

	private void AtirarComOItem(ServerPlayer pl, ItemDef def)
	{
		string b = TechDeBolso.Base(def.Id);
		if (b == Atordoador.Id) { AtirarOAtordoador(pl, def); return; }
		if (ArmasDeFogo.De(b) is not { } arma) return;

		// `!usr.blasting`: no meio do intervalo o gatilho nao responde -- calado, como la (a tecla repete).
		if (ArmaEsfriando(pl)) return;

		ArmasDeFogo.Estado e = ArmasDeFogo.Ler(def.Id);
		double ki = arma.Fonte == ArmasDeFogo.Fonte.Ki ? ArmasDeFogo.KiDoGasterPorTiro * pl.Ficha.BaseDrain() : 0;
		if (!PodeAtirar(pl, ki, out string porque)) { Avisar(pl, porque); return; }
		if (arma.Fonte != ArmasDeFogo.Fonte.Ki && e.Carga < 1)
		{
			// `"Out of ammo, reload."` e `"[src]: Out of energy!"`.
			Avisar(pl, arma.Fonte == ArmasDeFogo.Fonte.Polvora
				? "sem munição: recarregue (uma caixa de munição enche o pente)."
				: "sem energia: encha a reserva.");
			return;
		}

		string nome = _obras?.Get(b)?.Nome ?? b;
		Vec2 rumo = RumoDoTiro(pl);
		var receita = new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = 1,                                  // o `basedamage=1` de fabrica do `obj/attack` (`objects.dm:44`)
			Velocidade = ArmasDeFogo.Velocidade,
			AlcanceTiles = ArmasDeFogo.AlcanceEmTiles,
			Fisico = arma.Fonte == ArmasDeFogo.Fonte.Polvora,   // `A.physdamage = 1` so nas de polvora
			Paralisia = e.Atordoa,                         // `if(stun) A.paralysis=1`
			Empurra = e.Empurra,                           // `if(knockback) A.shockwave=1`: sem a melhoria a bala nao joga ninguem
			Nome = nome,
		};

		int sairam = 0;
		Vec2 boca = BocaDeCano.De(pl.Pos, rumo);
		var lado = new Vec2(-rumo.Y, rumo.X);
		for (int k = 0; k < arma.Balas; k++)
		{
			// A ESCOPETA: a do meio sai da boca do cano; as outras duas, um tile pra cada lado dela (ver
			// `ArmasDeFogo.LequeDaEscopeta`). O passo de lado que bate em parede nao acontece -- `step()` falha e a bala
			// sai do mesmo lugar da do meio.
			Vec2? deOnde = null;
			double desvio = arma.Balas > 1 ? ArmasDeFogo.LequeDaEscopeta[k] : 0;
			if (desvio != 0)
			{
				Vec2 ponto = boca + lado * (float)(desvio * ZoneCollision.TileSize);
				if (Voo.AtravessaCenario(pl.Altitude) || MapaDaZonaOuCatalogo(pl.Zone) is not { } chao || !chao.BlockedAt(ponto))
					deOnde = ponto;
			}

			Projetil p = Disparar(pl, receita, rumo, deOnde, VerboDaArma(b));
			if (!p.Vivo) continue;
			sairam++;

			// O TIRO VALE O QUE A ARMA VALE: `A.mods = power`, `A.BP = powerlevel`, e o critico multiplica o poder por dez.
			p.ModsBase = arma.Power;
			if (!ArmasDeFogo.ForcaDeQuemAtiraDeTeste)
			{
				p.Bp = ForcaDaArma(def.Id) * (Sorteio(e.Critico) ? ArmasDeFogo.MultDoCritico : 1);
				p.PoderDeReferencia = p.Bp;
			}
			// `A.murderToggle=0` nas de energia e na de ki: o tiro delas nao mata. As de polvora seguem quem atira.
			if (arma.Fonte != ArmasDeFogo.Fonte.Polvora) p.Letal = false;
		}

		// O TETO DE PROJETEIS RECUSOU TODOS: nada saiu, nada se gasta (o `PodeAtirar` ja falou quando e o caso comum).
		if (sairam == 0) return;

		if (arma.Fonte == ArmasDeFogo.Fonte.Ki) pl.Ficha.Ki = Math.Max(pl.Ficha.Ki - ki, 0);
		else if (!ArmasDeFogo.PenteInfinitoDeTeste)
		{
			e = e with { Carga = e.Carga - 1 };
			TrocarItem(pl, def.Id, ArmasDeFogo.Escrever(b, e));
			if (e.Carga < 1)
				Avisar(pl, arma.Fonte == ArmasDeFogo.Fonte.Polvora ? "sem munição: recarregue." : $"{nome}: sem energia!");
		}
		_armaProntaEm[pl.Id] = NowMs() + (long)(arma.SegundosEntreTiros * 1000);
	}

	/// <summary>
	/// `verb/Reload()` (`Guns.dm:143-155`): uma caixa de municao enche o pente. La a caixa e gasta mesmo com o pente
	/// cheio; aqui o pente cheio recusa antes de gastar.
	/// </summary>
	private void RecarregarAArma(ServerPlayer pl, ItemDef def, string arg2)
	{
		string b = TechDeBolso.Base(def.Id);
		if (b == Atordoador.Id) { RecarregarOAtordoador(pl, def, arg2); return; }
		if (ArmasDeFogo.De(b) is not { } arma) return;
		if (arma.Fonte != ArmasDeFogo.Fonte.Polvora) { Avisar(pl, "esta arma não usa munição."); return; }

		ArmasDeFogo.Estado e = ArmasDeFogo.Ler(def.Id);
		if (e.Carga >= e.Pente) { Avisar(pl, "o pente já está cheio."); return; }
		if (pl.Mochila.Quantos(ArmasDeFogo.Municao) <= 0) { Avisar(pl, "você não tem mais munição pra recarregar."); return; }

		pl.Mochila.Tirar(ArmasDeFogo.Municao);
		TrocarItem(pl, def.Id, ArmasDeFogo.Escrever(b, e with { Carga = e.Pente }));
		// `to_chat(view(usr), "[usr] reloads [src]")`.
		Falar(pl, Protocol.Fala.Emote, $"recarrega {_obras?.Get(b)?.Nome ?? b}.");
	}

	/// <summary>`verb/Info()` (`Guns.dm:97-108`), sem a "Failure Chance" que nenhuma linha sorteia.</summary>
	private void InfoDaArma(ServerPlayer pl, ItemDef def)
	{
		string b = TechDeBolso.Base(def.Id);
		if (b == Atordoador.Id)
		{
			Avisar(pl, $"{def.Nome} -- força {PoderEscrito(pl, ForcaDoAtordoador(def.Id))}; "
					   + (Atordoador.Carregado(def.Id) ? "um tiro no cano." : "descarregado.")
					   + $" Derruba quem está com {Atordoador.VidaQueDerruba:0}% de vida ou menos, se a força alcançar a defesa do alvo.");
			return;
		}
		if (ArmasDeFogo.De(b) is not { } arma) return;
		ArmasDeFogo.Estado e = ArmasDeFogo.Ler(def.Id);
		string carga = arma.Fonte switch
		{
			ArmasDeFogo.Fonte.Polvora => $"munição {e.Carga}/{e.Pente}",
			ArmasDeFogo.Fonte.Energia => $"energia {e.Carga}/{e.Pente}",
			_ => $"cobra {ArmasDeFogo.KiDoGasterPorTiro * pl.Ficha.BaseDrain():0.#} de Ki por tiro",
		};
		Avisar(pl, $"{_obras?.Get(b)?.Nome ?? b} -- {carga}; poder do tiro {PoderEscrito(pl, ForcaDaArma(def.Id))}, vezes {arma.Power:0.#} no dano; "
				   + $"um tiro a cada {arma.SegundosEntreTiros:0.#}s; crítico {e.Critico}%"
				   + (e.Empurra ? "; arremessa" : "") + (e.Atordoa ? "; paralisa" : "") + ".");
	}

	/// <summary>
	/// `verb/Upgrade()` (`Guns.dm:173-235`): as seis melhorias, pagas em zeni ou (o dobro) em recursos. Confere TUDO
	/// antes de cobrar -- inclusive se o estado novo cabe no id.
	/// </summary>
	private void MelhorarAArma(ServerPlayer pl, ItemDef def, string acao, string arg2)
	{
		// `if(usr.KO) return`.
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não melhora nada."); return; }
		(_, Moeda moeda) = Recurso.LerPedidoPago(arg2);

		string b = TechDeBolso.Base(def.Id);
		if (b == Atordoador.Id) { ReforcarOAtordoador(pl, def, moeda); return; }
		if (ArmasDeFogo.De(b) is not { } arma) return;

		ArmasDeFogo.Estado e = ArmasDeFogo.Ler(def.Id);
		Jandirus.Core.Stats.Fighter f = pl.Ficha;
		double custo, novaForca = 0;
		string oQue, feito;
		switch (acao)
		{
			case ArmasDeFogo.AcaoForca:
			{
				// `powerlevel = usr.intBPcap`.
				double teto = TetoDeTecnologia.De(f.relBPmax, f.techskill, f.techmod);
				if (ForcaDaArma(def.Id) >= teto) { Avisar(pl, "o poder desta arma já está no teto da sua tecnologia."); return; }
				(custo, oQue, feito) = (ArmasDeFogo.CustoDaForca, "aumentar a força da arma",
										$"força aumentada: o poder do tiro vai ao teto da sua tecnologia ({PoderEscrito(pl, teto)}).");
				novaForca = teto;
				break;
			}
			case ArmasDeFogo.AcaoCritico:
				// `critical<20*usr.techmod`.
				if (e.Critico >= ArmasDeFogo.TetoDoCritico(f.techmod))
				{ Avisar(pl, $"o crítico desta arma já está no máximo que você sabe fazer ({ArmasDeFogo.TetoDoCritico(f.techmod):0}%)."); return; }
				(custo, oQue, feito) = (ArmasDeFogo.CustoDoCritico(e.Critico), "aumentar o crítico", $"chance de crítico: {e.Critico + 1}%.");
				e = e with { Critico = e.Critico + 1 };
				break;
			case ArmasDeFogo.AcaoEmpurrao:
				if (e.Empurra) { Avisar(pl, "esta arma já arremessa quem acerta."); return; }
				(custo, oQue, feito) = (ArmasDeFogo.CustoDoEmpurrao, "o efeito de arremesso", "efeito de arremesso instalado.");
				e = e with { Empurra = true };
				break;
			case ArmasDeFogo.AcaoAtordoar:
				if (e.Atordoa) { Avisar(pl, "esta arma já paralisa quem acerta."); return; }
				(custo, oQue, feito) = (ArmasDeFogo.CustoDoAtordoar, "o efeito de paralisia", "efeito de paralisia instalado.");
				e = e with { Atordoa = true };
				break;
			case ArmasDeFogo.AcaoReserva when arma.Fonte == ArmasDeFogo.Fonte.Energia:
				if (e.Pente * 2 > ArmasDeFogo.TetoDaReserva) { Avisar(pl, "a reserva desta arma não cresce mais."); return; }
				(custo, oQue, feito) = (ArmasDeFogo.CustoDaReserva(e.Pente), "dobrar a reserva de energia", $"reserva dobrada e cheia: {e.Pente * 2} tiros.");
				e = e with { Pente = e.Pente * 2, Carga = e.Pente * 2 };
				break;
			case ArmasDeFogo.AcaoEncher when arma.Fonte == ArmasDeFogo.Fonte.Energia:
				if (e.Carga >= e.Pente) { Avisar(pl, "a reserva já está cheia."); return; }
				(custo, oQue, feito) = (ArmasDeFogo.CustoDeEncher(e.Pente), "encher a reserva de energia", "energia restaurada.");
				e = e with { Carga = e.Pente };
				break;
			default:
				Avisar(pl, "esta arma não tem essa melhoria.");
				return;
		}

		// (a gaveta nova so e aberta DEPOIS da cobranca; o tamanho do id e conferido com a maior gaveta que cabe abrir)
		string previsto = ArmasDeFogo.Escrever(b, novaForca > 0 && e.Cofre == 0 ? e with { Cofre = int.MaxValue } : e);
		if (previsto.Length > TechDeBolso.MaxDoId) { Avisar(pl, "esta arma não comporta mais nada."); return; }
		if (!CobrarOuExplicar(pl, custo, moeda, oQue)) return;
		if (novaForca > 0) e = e with { Cofre = EscreverNaGaveta(e.Cofre, novaForca) };
		TrocarItem(pl, def.Id, ArmasDeFogo.Escrever(b, e));
		Avisar(pl, feito);
	}

	// =====================================================================
	// O ATORDOADOR -- `obj/items/Stungun` (`Tier2.dm:298-357`). A regra esta em `Atordoador`.
	// =====================================================================
	private void AtirarOAtordoador(ServerPlayer pl, ItemDef def)
	{
		if (ArmaEsfriando(pl)) return;
		if (!Atordoador.Carregado(def.Id)) { Avisar(pl, "o atordoador está descarregado: recarregue."); return; }
		if (!PodeAtirar(pl, 0, out string porque)) { Avisar(pl, porque); return; }

		double forca = ForcaDoAtordoador(def.Id);
		Projetil p = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = 1,
			Velocidade = ArmasDeFogo.Velocidade,
			AlcanceTiles = ArmasDeFogo.AlcanceEmTiles,
			Paralisia = true,          // `A.paralysis=2`: paralisa como o 1...
			Empurra = false,
			Nome = _obras?.Get(Atordoador.Id)?.Nome ?? "atordoador",
		}, verbo: VerboDaArma(Atordoador.Id));
		if (!p.Vivo) return;

		p.Nocauteia = true;            // ...e derruba (`Atordoador.Derruba`, no `Acertar`)
		p.ModsBase = 1;                // o `mods=1` de fabrica: o atordoador nao escreve `power`
		p.Bp = forca * (Sorteio(Atordoador.ChanceDeCritico) ? ArmasDeFogo.MultDoCritico : 1);   // `A.BP=strength`, `if(prob(1)) A.BP*=10`
		p.PoderDeReferencia = p.Bp;
		p.Letal = false;               // `A.murderToggle=0`

		TrocarItem(pl, def.Id, Atordoador.Escrever(false, Atordoador.Cofre(def.Id)));
		_armaProntaEm[pl.Id] = NowMs() + (long)(Atordoador.SegundosEntreTiros * 1000);
	}

	/// <summary>`verb/Reload()`: paga e devolve o tiro (la paga e NAO devolve -- ver o cabecalho do `Atordoador`).</summary>
	private void RecarregarOAtordoador(ServerPlayer pl, ItemDef def, string arg2)
	{
		if (Atordoador.Carregado(def.Id)) { Avisar(pl, "o atordoador já está carregado."); return; }
		(_, Moeda moeda) = Recurso.LerPedidoPago(arg2);
		Jandirus.Core.Stats.Fighter f = pl.Ficha;
		double custo = Atordoador.Custo(TetoDeTecnologia.De(f.relBPmax, f.techskill, f.techmod));
		if (!CobrarOuExplicar(pl, custo, moeda, "recarregar o atordoador")) return;
		TrocarItem(pl, def.Id, Atordoador.Escrever(true, Atordoador.Cofre(def.Id)));
		Avisar(pl, "atordoador recarregado.");
	}

	/// <summary>O `Click()` do item: `strength = usr.intBPcap`, por `100*log(intBPcap)`.</summary>
	private void ReforcarOAtordoador(ServerPlayer pl, ItemDef def, Moeda moeda)
	{
		Jandirus.Core.Stats.Fighter f = pl.Ficha;
		double teto = TetoDeTecnologia.De(f.relBPmax, f.techskill, f.techmod);
		if (ForcaDoAtordoador(def.Id) >= teto) { Avisar(pl, "a força do atordoador já está no teto da sua tecnologia."); return; }
		if (!CobrarOuExplicar(pl, Atordoador.Custo(teto), moeda, "reforçar o atordoador")) return;
		TrocarItem(pl, def.Id, Atordoador.Escrever(Atordoador.Carregado(def.Id), EscreverNaGaveta(Atordoador.Cofre(def.Id), teto)));
		Avisar(pl, $"atordoador reforçado: a força dele vai ao teto da sua tecnologia ({PoderEscrito(pl, teto)}).");
	}

	/// <summary>
	/// O `paralysis==2` NO IMPACTO (`objects.dm:351-354`), chamado pelo `Acertar` logo depois da paralisia: se o alvo
	/// esta de pe e sem campo de forca e a conta do `Atordoador` fecha, ele CAI -- e o tiro acaba ali. Devolve se derrubou.
	/// </summary>
	private bool DerrubarComOAtordoador(Projetil p, ServerPlayer alvo, ServerPlayer dono)
	{
		if (!p.Nocauteia || alvo.Ficha.KO || alvo.Combate is not { } cd || CampoLigadoDe(alvo) != null) return false;
		if (!Atordoador.Derruba(p.Bp, alvo.Ficha)) return false;

		cd.Nocautear(MeleeResolver.TetoDoNocaute, porVital: false);
		AoPerderALuta(alvo, dono, morreu: false);
		// `to_chat(view(M), "[M] has been stunned!")`.
		Falar(alvo, Protocol.Fala.Emote, "cai atordoado!");
		return true;
	}

	// =====================================================================
	// AS BOMBAS -- `Bombs.dm`, o detonador de `Tier2.dm:254-297` e a explosao de `explosions.dm`. A regra esta em `Bombas`.
	// =====================================================================
	/// <summary>Uma bomba contando no chao: a coisa largada que ela e, e quando estoura.</summary>
	private sealed class BombaArmada
	{
		public ZoneKey Zona;
		public int Largado;
		public long EstouraEm;
		public int Autor;
	}

	private readonly List<BombaArmada> _bombasArmadas = [];

	/// <summary>Um detonador contando: o codigo que ele dispara, e quando.</summary>
	private readonly List<(string Codigo, long Em, int Autor)> _detonadores = [];

	/// <summary>As nuvens de fumaca de pe: onde, ate quando, e quando sai o proximo tufo.</summary>
	private readonly List<(ZoneKey Zona, Vec2 Onde, long Ate, long ProximoTufo)> _fumacas = [];

	private static int Contagem(string arg2, out bool valida)
	{
		valida = int.TryParse(arg2, out int s) && s >= Bombas.MinDeContagem && s <= Bombas.MaxDeContagem;
		return s;
	}

	/// <summary>
	/// `verb/Use()` (`Bombs.dm:77-97`): a contagem comeca. A bomba cai aos pes de quem a arma (ver "ARMAR E LARGAR
	/// ARMADA" no cabecalho do `Bombas`); pega-la do chao antes do zero a desarma.
	/// </summary>
	private void ArmarABomba(ServerPlayer pl, ItemDef def, string arg2)
	{
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não arma nada."); return; }
		int segundos = Contagem(arg2, out bool valida);
		if (!valida) { Avisar(pl, $"a contagem vai de {Bombas.MinDeContagem} a {Bombas.MaxDeContagem} segundos."); return; }
		if (!PodeLargar(pl)) return;

		if (pl.Mochila.Tirar(def.Id) <= 0) return;
		MandarMochila(pl);
		Largado noChao = PorNoChao(pl.Zone, pl.Pos, TipoDeLargado.Item, def.Id, 1);
		// A BOMBA ARMADA NAO SOME COM O RELOGIO DO CHAO antes de estourar.
		noChao.SomeMs = Math.Max(noChao.SomeMs, NowMs() + (segundos + 5) * 1000L);
		_bombasArmadas.Add(new BombaArmada { Zona = pl.Zone, Largado = noChao.Id, EstouraEm = NowMs() + segundos * 1000L, Autor = pl.Id });

		Falar(pl, Protocol.Fala.Emote, $"arma {def.Nome} no chão: {segundos} segundos.");
		GD.Print($"[server] {pl.Name} ARMOU {def.Nome} em {pl.Zone.Name} ({segundos}s)");
	}

	/// <summary>`verb/Set()`: o codigo da bomba ou do detonador (`Bombs.dm:65-69`, `Tier2.dm:263-267`).</summary>
	private void GravarOCodigo(ServerPlayer pl, ItemDef def, string arg2)
	{
		string codigo = TechDeBolso.Limpo(arg2, Bombas.MaxDeCodigo);
		if (codigo.Length == 0) { Avisar(pl, "o código não pode ser vazio."); return; }
		string b = TechDeBolso.Base(def.Id);
		string novo = b == Bombas.Detonador ? Bombas.EscreverDetonador(codigo) : Bombas.EscreverNuke(Bombas.Cofre(def.Id), codigo);
		if (!TrocarItem(pl, def.Id, novo)) { Avisar(pl, "sua mochila está cheia."); return; }
		Avisar(pl, b == Bombas.Detonador
			? $"código gravado: este detonador dispara toda bomba de código \"{codigo}\"."
			: $"código gravado: qualquer detonador com \"{codigo}\" estoura esta bomba.");
	}

	/// <summary>`verb/Upgrade()` (`Bombs.dm:55-63`): a forca vai ao dobro do teto de tecnologia de quem paga.</summary>
	private void ReforcarABomba(ServerPlayer pl, ItemDef def, string arg2)
	{
		(_, Moeda moeda) = Recurso.LerPedidoPago(arg2);
		Jandirus.Core.Stats.Fighter f = pl.Ficha;
		double teto = TetoDeTecnologia.De(f.relBPmax, f.techskill, f.techmod);
		double nova = teto * Bombas.MultDeReforco;
		if (ForcaDaBomba(def.Id) >= nova) { Avisar(pl, "esta bomba já está no máximo que a sua tecnologia alcança."); return; }

		string previsto = Bombas.EscreverNuke(Bombas.Cofre(def.Id) > 0 ? Bombas.Cofre(def.Id) : int.MaxValue, Bombas.Codigo(def.Id));
		if (previsto.Length > TechDeBolso.MaxDoId) { Avisar(pl, "esta bomba não comporta mais nada."); return; }
		if (!CobrarOuExplicar(pl, Bombas.CustoDeReforcar(teto), moeda, "reforçar a bomba")) return;
		TrocarItem(pl, def.Id, Bombas.EscreverNuke(EscreverNaGaveta(Bombas.Cofre(def.Id), nova), Bombas.Codigo(def.Id)));
		Avisar(pl, $"bomba reforçada: a força dela vai ao dobro do teto da sua tecnologia ({PoderEscrito(pl, nova)}).");
	}

	private void InfoDaBomba(ServerPlayer pl, ItemDef def)
	{
		string codigo = Bombas.Codigo(def.Id);
		Avisar(pl, $"{def.Nome} -- força {PoderEscrito(pl, ForcaDaBomba(def.Id))}, raio de {Bombas.RaioDaNuke} tiles; "
				   + (codigo.Length > 0 ? $"código \"{codigo}\"." : "sem código (nenhum detonador a alcança)."));
	}

	/// <summary>`Del()` do barril (`Bombs.dm:307-310`): `"[src] goes off!"` e a nuvem toxica. Aqui o barril se rompe na mao de quem pede.</summary>
	private void RomperOBarril(ServerPlayer pl, ItemDef def)
	{
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não rompe nada."); return; }
		if (pl.Mochila.Tirar(def.Id) <= 0) return;
		MandarMochila(pl);
		Falar(pl, Protocol.Fala.Emote, "rompe um barril de lixo tóxico!");
		ExplodirBomba(pl.Zone, pl.Pos, Bombas.ForcaDoLixo, Bombas.RaioDoLixo, fere: false, estilhacos: false, veneno: true, pl);
	}

	/// <summary>`verb/Use()` da bomba de fumaca (`Bombs.dm:316-323`): `"[usr] uses [src]"`, e a nuvem.</summary>
	private void SoltarAFumaca(ServerPlayer pl, ItemDef def)
	{
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não usa nada."); return; }
		if (pl.Mochila.Tirar(def.Id) <= 0) return;
		MandarMochila(pl);
		Falar(pl, Protocol.Fala.Emote, "estoura uma bomba de fumaça!");
		long agora = NowMs();
		_fumacas.Add((pl.Zone, pl.Pos, agora + (long)(Bombas.SegundosDeFumaca * 1000), agora));
	}

	/// <summary>`verb/Use()` do detonador (`Tier2.dm:275-297`): no fim da contagem, toda bomba com o codigo dele estoura.</summary>
	private void ArmarODetonador(ServerPlayer pl, ItemDef def, string arg2)
	{
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não aperta nada."); return; }
		string codigo = Bombas.Codigo(def.Id);
		if (codigo.Length == 0) { Avisar(pl, "grave um código no detonador antes."); return; }
		int segundos = Contagem(arg2, out bool valida);
		if (!valida) { Avisar(pl, $"a contagem vai de {Bombas.MinDeContagem} a {Bombas.MaxDeContagem} segundos."); return; }

		_detonadores.Add((codigo, NowMs() + segundos * 1000L, pl.Id));
		Avisar(pl, $"detonador armado: {segundos} segundos pra toda bomba de código \"{codigo}\".");
		GD.Print($"[server] {pl.Name} ARMOU um detonador ({segundos}s)");
	}

	/// <summary>Uma vez por tique: as bombas armadas, os detonadores e as nuvens de fumaca.</summary>
	private void TickDasBombas()
	{
		long agora = NowMs();

		for (int i = _bombasArmadas.Count - 1; i >= 0; i--)
		{
			BombaArmada b = _bombasArmadas[i];
			List<Largado> chao = LargadosDe(b.Zona.Hash);
			Largado? l = chao.Find(x => x.Id == b.Largado);
			// PEGARAM DO CHAO (ou ela sumiu): desarmada -- o `Cancel()` de quem esta a um tile.
			if (l == null) { _bombasArmadas.RemoveAt(i); continue; }
			if (agora < b.EstouraEm) continue;

			_bombasArmadas.RemoveAt(i);
			chao.Remove(l);
			MandarOChao(b.Zona);
			EstourarONuke(b.Zona, new Vec2(l.X, l.Y), l.Item, _players.GetValueOrDefault(b.Autor));
		}

		for (int i = _detonadores.Count - 1; i >= 0; i--)
		{
			(string codigo, long em, int autor) = _detonadores[i];
			if (agora < em) continue;
			_detonadores.RemoveAt(i);
			DetonarOCodigo(codigo, _players.GetValueOrDefault(autor));
		}

		for (int i = _fumacas.Count - 1; i >= 0; i--)
		{
			(ZoneKey zona, Vec2 onde, long ate, long proximo) = _fumacas[i];
			if (agora >= ate) { _fumacas.RemoveAt(i); continue; }
			if (agora < proximo) continue;
			// UM TUFO NOVO, num ponto sorteado da nuvem: cada um dura pouco, e a nuvem e a soma deles.
			double a = _rng.NextDouble() * Math.PI * 2, r = Math.Sqrt(_rng.NextDouble()) * Bombas.RaioDaFumacaEmTiles * ZoneCollision.TileSize;
			MandarDecalque(zona, Protocol.Decal.Fumaca, onde + new Vec2((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r)), Facing.South);
			_fumacas[i] = (zona, onde, ate, agora + (long)(Bombas.SegundosPorTufo * 1000));
		}
	}

	/// <summary>`explode()` da nuke (`Bombs.dm:38-49`): `"The [src] goes off!"`, radioativa e com estilhacos.</summary>
	private void EstourarONuke(ZoneKey zona, Vec2 onde, string idDaBomba, ServerPlayer? autor)
	{
		GD.Print($"[server] NUKE estourou em {zona.Name} (forca {ForcaDaBomba(idDaBomba):N0}; autor {autor?.Name ?? "?"})");
		ExplodirBomba(zona, onde, ForcaDaBomba(idDaBomba), Bombas.RaioDaNuke, fere: true, estilhacos: true, veneno: true, autor);
	}

	/// <summary>
	/// `for(var/obj/items/Bomb/B in world) if(B.code == code) B.explode()`: toda bomba deste codigo estoura onde
	/// estiver -- largada no chao, na mochila de alguem (nos pes dele) ou guardada num deposito (em cima dele).
	/// </summary>
	private void DetonarOCodigo(string codigo, ServerPlayer? autor)
	{
		bool Casa(string id) => TechDeBolso.Base(id) == Bombas.Nuke
								&& (Bombas.DetonadorSemCodigoDeTeste || Bombas.Codigo(id) == codigo);

		var estouros = new List<(ZoneKey Zona, Vec2 Onde, string Id)>();

		// NO CHAO. A zona de uma coisa largada so se conhece pelo hash; a chave inteira vem de quem esta nela.
		foreach ((ulong hash, List<Largado> lista) in _largados)
		{
			if (lista.Count == 0 || MapaDaZonaDoHash(hash, out ZoneKey zona) == null && zona.Name is null) continue;
			bool mexeu = false;
			for (int i = lista.Count - 1; i >= 0; i--)
			{
				if (lista[i].Tipo != TipoDeLargado.Item || !Casa(lista[i].Item)) continue;
				for (int k = 0; k < Math.Max((int)lista[i].Quantidade, 1); k++) estouros.Add((zona, new Vec2(lista[i].X, lista[i].Y), lista[i].Item));
				_bombasArmadas.RemoveAll(b => b.Largado == lista[i].Id);
				lista.RemoveAt(i);
				mexeu = true;
			}
			if (mexeu) MandarOChaoPorHash(hash);
		}

		// NAS MOCHILAS.
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			bool mexeu = false;
			for (int i = pl.Mochila.Pilhas.Count - 1; i >= 0; i--)
			{
				Pilha p = pl.Mochila.Pilhas[i];
				if (p.Quantidade <= 0 || !Casa(p.Id)) continue;
				for (int k = 0; k < p.Quantidade; k++) estouros.Add((pl.Zone, pl.Pos, p.Id));
				pl.Mochila.Pilhas.RemoveAt(i);
				mexeu = true;
			}
			if (mexeu) MandarMochila(pl);
		}

		// NOS DEPOSITOS.
		bool gravar = false;
		foreach (Obra o in _noChao)
		{
			if (o.Tech is not { Itens.Count: > 0 } saco) continue;
			for (int i = saco.Itens.Count - 1; i >= 0; i--)
			{
				if (!Casa(saco.Itens[i].Id)) continue;
				for (int k = 0; k < saco.Itens[i].Quantidade; k++) estouros.Add((o.Zona, new Vec2(o.X, o.Y), saco.Itens[i].Id));
				saco.Itens.RemoveAt(i);
				gravar = true;
			}
		}
		if (gravar) GravarMundo();

		foreach ((ZoneKey zona, Vec2 onde, string id) in estouros) EstourarONuke(zona, onde, id, autor);
		if (autor != null)
			Avisar(autor, estouros.Count == 0 ? $"o detonador apita: nenhuma bomba de código \"{codigo}\" respondeu."
											  : $"o detonador apita: {estouros.Count} bomba(s) de código \"{codigo}\".");
	}

	/// <summary>
	/// A EXPLOSAO DE UMA BOMBA -- o `obj/explosions/proc/explosion()` como ele pretende ser (ver o cabecalho do
	/// `Bombas`): quem esta no raio e arremessado pra longe do centro, leva o dano espalhado (e de novo, membro a
	/// membro, se ha estilhacos), e fica envenenado se ela e radioativa; o cenario apanha pela regra das explosoes.
	/// </summary>
	/// <param name="fere">`harmful`: a nuvem toxica arremessa e envenena, e nao fere.</param>
	private void ExplodirBomba(ZoneKey zona, Vec2 onde, double forca, int raioEmTiles, bool fere, bool estilhacos,
							   bool veneno, ServerPlayer? autor)
	{
		bool temChao = MapaDaZonaOuCatalogo(zona) != null && !Espaco.EhEspaco(zona);
		if (temChao)
		{
			MandarDecalque(zona, Protocol.Decal.CrateraGrande, onde, Facing.South);
			MandarDecalque(zona, Protocol.Decal.Fumaca, onde, Facing.South);
		}
		BombasEstouradasDeTeste++;
		if (Bombas.ExplosaoSemDanoDeTeste) return;

		double raioPx = raioEmTiles * ZoneCollision.TileSize;
		if (fere) EstragarOCenarioNaExplosao(zona, onde, raioPx, forca, autor);

		// (uma COPIA da lista: a consequencia do dano -- um nocaute, uma morte -- pode mexer na da zona)
		foreach (ServerPlayer o in ZoneList(zona.Hash).ToList())
		{
			if (o.Combate is not { Intocavel: false } || o.Ficha.dead && !EhCadaver(o)) continue;
			// A EXPLOSAO E DO CHAO: quem voa acima do cenario esta fora dela (a regra do `Voo`, a mesma do soco).
			if (Voo.AtravessaCenario(o.Altitude)) continue;
			double distancia = Vec2.Distance(o.Pos, onde);
			if (distancia > raioPx) continue;

			MandarEfeito(o, "explosao_final", 600);
			// QUEM RESPONDE PELO DANO: o autor, ou a propria vitima quando ele ja nao esta no mundo (dano sem algoz).
			ServerPlayer algoz = autor ?? o;
			int degrau = Bombas.Degrau(distancia / ZoneCollision.TileSize, raioEmTiles);

			if (fere)
			{
				double dano = Bombas.Dano(forca, o.Ficha, degrau);
				EspalharDanoG3(o, algoz, dano, letal: estilhacos);
				// `if(gibbify) for(S in M.body) if(!S.lopped) S.health -= dmg`: os estilhacos batem de novo, em cada membro.
				if (estilhacos) EspalharDanoG3(o, algoz, dano, letal: true);
			}
			if (veneno && !EhCadaver(o) && !o.Ficha.dead) EnvenenarG14(algoz, o);

			// O ARREMESSO (`testback`, `explosions.dm:159-167`): so sai do lugar quem a conta manda mais de um tique.
			if (EhCadaver(o) || o.Ficha.dead) continue;
			double teto = 2 * CombatMath.BpModulus(forca, Math.Max(o.Ficha.expressedBP, 1));
			double testback = Math.Round((1 + _rng.NextDouble() * Math.Max(teto - 1, 0)) / Math.Max(o.Ficha.Ephysdef, 1e-6),
										 MidpointRounding.AwayFromZero);
			if (testback <= 1) continue;
			Vec2 rumo = o.Pos - onde;
			if (rumo.LengthSquared < 1e-4f) rumo = MeleeArea.Frente(o.Facing);
			Arremessar(o, rumo.Normalized(), forca, (int)Math.Min(testback, 15));
		}

		// OS BARRIS DE LIXO TOXICO LARGADOS NO RAIO SE ROMPEM (`fragile = 1`: o que os destroi os faz estourar).
		if (fere) RomperOsBarrisNoRaio(zona, onde, raioPx, autor);
	}

	/// <summary>Quantas explosoes de bomba aconteceram neste processo. So pra bancada (`--v021teste`).</summary>
	internal int BombasEstouradasDeTeste;

	private void RomperOsBarrisNoRaio(ZoneKey zona, Vec2 onde, double raioPx, ServerPlayer? autor)
	{
		List<Largado> chao = LargadosDe(zona.Hash);
		var rompidos = new List<Vec2>();
		for (int i = chao.Count - 1; i >= 0; i--)
		{
			Largado l = chao[i];
			if (l.Tipo != TipoDeLargado.Item || TechDeBolso.Base(l.Item) != Bombas.LixoToxico) continue;
			var ponto = new Vec2(l.X, l.Y);
			if (Vec2.Distance(ponto, onde) > raioPx) continue;
			for (int k = 0; k < Math.Max((int)l.Quantidade, 1); k++) rompidos.Add(ponto);
			chao.RemoveAt(i);
		}
		if (rompidos.Count == 0) return;
		MandarOChao(zona);
		foreach (Vec2 ponto in rompidos)
			ExplodirBomba(zona, ponto, Bombas.ForcaDoLixo, Bombas.RaioDoLixo, fere: false, estilhacos: false, veneno: true, autor);
	}

	/// <summary>
	/// `do_toxic_waste = 1` (`Bombs.dm:9`): fabricar uma nuke fabrica junto um barril de lixo toxico. Vai pra mochila;
	/// sem vaga, fica no chao aos pes de quem fabricou (que e onde o original poe tudo o que se fabrica).
	/// </summary>
	private void AoFabricarTech(ServerPlayer pl, string id)
	{
		// (lote T5) A CAPSULA DE SAIBAMAN GASTA A SEMENTE de que ele brota (`if(..()) usr.contents -= s`,
		// `companion_list.dm:37-41`). Quem conferiu que ela estava la foi o `PorQueNaoFabrica`.
		if (id == Companheiros.CapsulaDeSaibaman)
		{
			pl.Mochila.Tirar(CatalogoDeItens.SementeDeMaca);
			MandarMochila(pl);
			return;
		}
		if (id != Bombas.Nuke) return;
		if (pl.Mochila.Guardar(Bombas.LixoToxico) > 0) PorNoChao(pl.Zone, pl.Pos, TipoDeLargado.Item, Bombas.LixoToxico, 1);
		else MandarMochila(pl);
		Avisar(pl, "fabricar a bomba deixou um barril de lixo tóxico com você.");
	}

	// =====================================================================
	// O CAMPO DE FORCA -- `obj/items/Forcefield` (`Tier2.dm:209-253`). A regra esta em `CampoDeForca`.
	// =====================================================================
	/// <summary>O campo de forca LIGADO que este corpo carrega (o id inteiro da unidade), ou nulo.</summary>
	private static string? CampoLigadoDe(ServerPlayer pl)
	{
		foreach (Pilha p in pl.Mochila.Pilhas)
			if (p.Quantidade > 0 && TechDeBolso.Base(p.Id) == CampoDeForca.Id && CampoDeForca.Ligado(p.Id)) return p.Id;
		return null;
	}

	/// <summary>`verb/Equip()`: liga e desliga. O campo que nunca foi ligado ganha a armadura AGORA -- o teto de tecnologia de quem o liga.</summary>
	private void LigarOCampo(ServerPlayer pl, ItemDef def)
	{
		bool ligar = !CampoDeForca.Ligado(def.Id);
		if (ligar && CampoLigadoDe(pl) != null) { Avisar(pl, "você já está com um campo de força ligado."); return; }

		int gaveta = CampoDeForca.Cofre(def.Id);
		(double armadura, double maxima) = ArmaduraDoCampo(def.Id);
		if (maxima <= 0)
		{
			// `A.maxarmor = usr.intBPcap` (`TechSupport.dm:55`), que la e escrito ao fabricar.
			Jandirus.Core.Stats.Fighter f = pl.Ficha;
			armadura = maxima = Math.Max(TetoDeTecnologia.De(f.relBPmax, f.techskill, f.techmod), 1);
			gaveta = EscreverNaGaveta(gaveta, armadura, maxima);
		}
		if (!TrocarItem(pl, def.Id, CampoDeForca.Escrever(gaveta, ligar))) { Avisar(pl, "sua mochila está cheia."); return; }
		Avisar(pl, ligar ? $"você liga o campo de força (armadura em {armadura / maxima * 100:0}%)." : "você desliga o campo de força.");
	}

	private void InfoDoCampo(ServerPlayer pl, ItemDef def)
	{
		(double armadura, double maxima) = ArmaduraDoCampo(def.Id);
		Avisar(pl, maxima <= 0
			? "campo de força novo: a armadura dele será o teto da sua tecnologia quando você o ligar pela primeira vez."
			: $"campo de força -- armadura em {armadura / maxima * 100:0}% (inteira, ela vale {PoderEscrito(pl, maxima)}); "
			  + $"ignora tiros de poder até {PoderEscrito(pl, maxima * CampoDeForca.FracaoIgnorada)}. "
			  + (CampoDeForca.Ligado(def.Id) ? "Ligado." : "Desligado."));
	}

	/// <summary>
	/// O CAMPO LEVA O TIRO NO LUGAR DO CORPO (`objects.dm:421-423`) -- chamado pelo `Acertar` no ponto em que o corpo
	/// levaria o dano. Devolve se absorveu: o tiro acaba ali, sem dano, sem arremesso.
	/// </summary>
	private bool CampoDeForcaAbsorve(Projetil p, ServerPlayer alvo, ServerPlayer dono)
	{
		if (CampoDeForca.NaoAbsorveDeTeste || CampoLigadoDe(alvo) is not { } id) return false;

		(double armadura, double maxima) = ArmaduraDoCampo(id);
		double sobra = CampoDeForca.Depois(armadura, maxima, p.Bp);
		// (o `spawn M.updateOverlay(/obj/overlay/effects/flickeffects/forcefield)` de la e, aqui, o escudo que o cliente
		// desenha no relato de `Desfecho.Rebateu` -- quem anuncia e o `Acertar`.)

		if (sobra <= 0)
		{
			// `testDestroy()`: o campo desliga, estoura e some.
			alvo.Mochila.Tirar(id);
			MandarMochila(alvo);
			MandarEfeito(alvo, "explosao_final", 400);
			Avisar(alvo, "seu campo de força não aguenta e estoura!");
			Avisar(dono, $"o campo de força de {alvo.Name} estoura.");
		}
		// (a armadura mora no cofre: o id do item nao muda a cada tiro, e a mochila nem e reenviada)
		else if (sobra < armadura) EscreverNaGaveta(CampoDeForca.Cofre(id), sobra, maxima);

		Matar(p, FimDeProjetil.Defletido);
		return true;
	}

	// =====================================================================
	// AS LUVAS E AS BOTAS DE DRENO -- `Tier 1.5.dm:252-323`. A regra esta em `DrenoDeEnergia`.
	// =====================================================================
	/// <summary>`verb/Equip()`: veste e tira. Uma peca de cada tipo por corpo (`"You already have one equipped."`).</summary>
	private void VestirODreno(ServerPlayer pl, ItemDef def)
	{
		string b = TechDeBolso.Base(def.Id);
		bool vestir = !DrenoDeEnergia.Vestida(def.Id);
		if (vestir && pl.Mochila.Pilhas.Exists(p => p.Quantidade > 0 && p.Id != def.Id && TechDeBolso.Base(p.Id) == b && DrenoDeEnergia.Vestida(p.Id)))
		{ Avisar(pl, "você já veste uma dessas."); return; }
		if (!TrocarItem(pl, def.Id, DrenoDeEnergia.Escrever(b, vestir))) { Avisar(pl, "sua mochila está cheia."); return; }

		AcertarODreno(pl);
		ReapresentarOCorpo(pl);
		string nome = _obras?.Get(b)?.Nome ?? b;
		Avisar(pl, vestir ? $"você veste {nome}: elas drenam o seu Ki e fazem a sua energia máxima crescer mais depressa."
						  : $"você tira {nome}.");
	}

	/// <summary>
	/// O BONUS DE `KiMod` ACOMPANHA O QUE ESTA VESTIDO. `Fighter.drenoVestido` guarda quantas pecas ja estao somadas
	/// (e vai pro save com a ficha): a diferenca pro que a mochila mostra agora e o que se soma ou se tira.
	/// </summary>
	private static int AcertarODreno(ServerPlayer pl)
	{
		(bool luvas, bool botas) = DrenoDeEnergia.Vestidas(pl.Mochila);
		int pecas = DrenoDeEnergia.NaoFazNadaDeTeste ? 0 : (luvas ? 1 : 0) + (botas ? 1 : 0);
		Jandirus.Core.Stats.Fighter f = pl.Ficha;
		if (pecas != f.drenoVestido)
		{
			f.KiMod += DrenoDeEnergia.KiModPorPeca * (pecas - f.drenoVestido);
			f.drenoVestido = pecas;
		}
		return pecas;
	}

	/// <summary>Uma vez por segundo: acerta o bonus e cobra o dreno de quem veste.</summary>
	private void TickDoDreno()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.Cerebro != null && pl.Peer == null) continue;   // os corpos da IA nao vestem nada disto
			int pecas = AcertarODreno(pl);
			if (pecas <= 0 || pl.Ficha.dead) continue;
			pl.Ficha.Ki = Math.Max(pl.Ficha.Ki - DrenoDeEnergia.DrenoPorSegundo(pl.Ficha, pecas), 0);
		}
	}

	// =====================================================================
	// O RELOGIO DO LOTE
	// =====================================================================
	private double _relogioDoDreno;

	private void TickDasArmasEBombas(double dt)
	{
		TickDasBombas();
		_relogioDoDreno += dt;
		if (_relogioDoDreno < 1) return;
		_relogioDoDreno -= 1;
		TickDoDreno();
	}

	/// <summary>Quem saiu do mundo leva embora o relogio da arma dele.</summary>
	private void EsquecerArmas(int id) => _armaProntaEm.Remove(id);
}
