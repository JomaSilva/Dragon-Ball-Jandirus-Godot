using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Economia;
using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// (v0.2.1, lote T5 da tech) O SINTETIZADOR DE VIRUS -- a maquina, e o que os virus, os antivirus e as injecoes fazem
/// com quem os toma. A regra mora em `Core/Tech/Sintetizador.cs`, com o que o original faz e o que muda aqui.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O verbo de maquina deste lote (o menu da tecla E). Falso quando o verbo nao e daqui.</summary>
	private bool ComandoDeSintetizador(ServerPlayer pl, string cmd, string arg)
	{
		// (lote T6) o super computador -- `GameServer.SuperComputador.cs`.
		if (cmd != Sintetizador.VerboFazer) return ComandoDeSuperComputador(pl, cmd, arg);
		Sintetizar(pl, arg);
		return true;
	}

	/// <summary>As acoes de mochila deste lote. Falso quando a acao nao e daqui.</summary>
	private bool AcaoDeSintetizado(ServerPlayer pl, ItemDef def, string acao, string arg2)
	{
		switch (acao)
		{
			case Sintetizador.AcaoTomar: TomarOSintetizado(pl, def); return true;
			case Sintetizador.AcaoInjetar: InjetarEmOutro(pl, def); return true;
			// (lote T5) o kit de reparo e o kit mecanico -- `GameServer.KitsDeAndroide.cs`.
			default: return AcaoDeKit(pl, def, acao, arg2);
		}
	}

	// =====================================================================
	// A MAQUINA -- `verb/Synthesis()` (`Viruses.dm:29-258`)
	// =====================================================================
	private void Sintetizar(ServerPlayer pl, string arg)
	{
		Obra? o = ObraQueAceita(pl, Sintetizador.VerboFazer);
		if (o == null) { Avisar(pl, "não há sintetizador por perto."); return; }
		// `"You need to bolt [name] before you can use it!"`.
		if (!o.Aparafusada) { Avisar(pl, $"aparafuse {NomeDaObra(o)} antes de usar."); return; }
		(string qual, Moeda moeda) = Recurso.LerPedidoPago(arg);
		if (Sintetizador.De(qual) is not { } p) { Avisar(pl, "o sintetizador não faz isso."); return; }
		// `if(usr.techskill>=65) CreateList += "Virus Alfa"` -- la o item nem aparece na lista; aqui a recusa diz quanto falta.
		if (!Sintetizador.SemPedirTecnologiaDeTeste && pl.Ficha.techskill < p.Tecnologia)
		{ Avisar(pl, $"{p.Nome} pede {p.Tecnologia:0} de tecnologia -- você tem {pl.Ficha.techskill:0}."); return; }
		if (pl.Mochila.Cheio && pl.Mochila.Quantos(p.Id) <= 0) { Avisar(pl, $"sua mochila está cheia ({Inventario.Slots} espaços)."); return; }
		if (!CobrarOuExplicar(pl, p.Preco, moeda, p.Nome)) return;
		Guardar(pl, p.Id);
		Avisar(pl, $"o sintetizador prepara: {p.Nome}. Está na sua mochila.");
		AnunciarAVista(o, $"{NomeVisivel(pl)} sintetiza alguma coisa em {NomeDaObra(o)}.");
	}

	// =====================================================================
	// TOMAR -- os tres `verb/Consume()` (`Viruses.dm:265-286`, `:458-472`, `Injections.dm:126-135`)
	// =====================================================================
	private void TomarOSintetizado(ServerPlayer pl, ItemDef def)
	{
		if (Sintetizador.De(def.Id) is not { } p) return;
		Fighter f = pl.Ficha;
		if (f.KO || f.dead || EhCadaver(pl)) { Avisar(pl, "caído, você não consegue."); return; }
		// `"You're an android, you cannot use this."`.
		if (string.Equals(pl.Race, "Android", StringComparison.OrdinalIgnoreCase)) { Avisar(pl, "você é um androide: não pode usar isto."); return; }

		switch (p.Classe)
		{
			case Sintetizador.Classe.Virus: TomarOVirus(pl, p); break;
			case Sintetizador.Classe.AntiVirus: TomarOAntiVirus(pl, p); break;
			default: TomarAInjecao(pl, p); break;
		}
	}

	/// <summary>O item se gasta (`del(src)`).</summary>
	private void GastarOSintetizado(ServerPlayer pl, string id)
	{
		pl.Mochila.Tirar(id);
		MandarMochila(pl);
	}

	/// <summary>A ficha mudou: reconta, e avisa quem ve o numero.</summary>
	private void RecontarDepoisDoSintetizado(ServerPlayer pl)
	{
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
		pl.SigAtributos = "";
		RepercutirPoder(pl);
	}

	private void TomarOVirus(ServerPlayer pl, Sintetizador.Produto p)
	{
		Fighter f = pl.Ficha;
		// `"You've already consumed this virus."`.
		if (!Sintetizador.VirusRepeteDeTeste && f.VirusesConsumed.Contains(p.Id)) { Avisar(pl, "você já tomou este vírus."); return; }

		(int mutacoes, bool morre) = Sintetizador.Risco(f.Mutations, p.Benefico, _rng.NextDouble() * 100, _rng.NextDouble() * 100);
		if (SorteioDoVirusDeTeste is { } forcado) (mutacoes, morre) = Sintetizador.Risco(f.Mutations, p.Benefico, forcado.Mutar, forcado.Morrer);
		if (mutacoes > f.Mutations) Avisar(pl, "alguma coisa em você muda pra pior: o vírus deixou uma mutação.");
		f.Mutations = mutacoes;
		if (morre)
		{
			// `"[usr] begins to fucking die!!"` -- e o item NAO se gasta (o `return FALSE` vem antes do `del(src)`).
			Falar(pl, Protocol.Fala.Emote, "começa a morrer: o corpo não aguentou o vírus!");
			pl.Combate?.Morrer(ignorarSeguro: true);
			return;
		}
		Falar(pl, Protocol.Fala.Emote, "injeta uma agulha misteriosa!");   // `"[usr] injects a mysterious needle!"`
		if (!f.VirusesConsumed.Contains(p.Id)) f.VirusesConsumed.Add(p.Id);

		switch (p.Id)
		{
			case Sintetizador.Alfa:
				// `if(usr.techmod<4) add_to_stat("Tech Modifier",2) else if(usr.techmod>=4) add_to_stat("Tech Modifier",1)`.
				f.techmod += f.techmod < 4 ? 2 : 1;
				Avisar(pl, "sua inteligência aumenta.");
				break;
			case Sintetizador.Bravo:
				f.physdefBuff += 0.15; f.kidefBuff += 0.15;
				Avisar(pl, "suas defesas aumentam.");
				break;
			case Sintetizador.Charlie:
				// `maxstamina += 50`, `Ki = MaxKi`, `stamina = maxstamina`, `add_to_stat("Energy Level",1)`.
				f.maxstamina += 50;
				f.KiMod += 1;
				f.Statify();
				f.Ki = f.MaxKi; f.stamina = f.maxstamina;
				Avisar(pl, "seu Ki e seu vigor aumentam.");
				break;
			case Sintetizador.Delta:
				f.kiregenMod += 1; f.kidefBuff -= 1.15; f.kioffBuff -= 1.15;
				Avisar(pl, "sua regeneração de Ki aumenta.");
				break;
			case Sintetizador.Echo:
				f.HPregenbuff += 2; f.physdefBuff -= 1.25;
				Avisar(pl, "sua regeneração aumenta.");
				break;
			case Sintetizador.Golf:
				f.physoffBuff += 0.5; f.speedBuff += 0.5;
				Avisar(pl, "sua força e sua velocidade aumentam.");
				break;
			case Sintetizador.Hotel:
				f.techniqueBuff += 0.5; f.kioffBuff += 0.5; f.kidefBuff += 0.5;
				Avisar(pl, "sua técnica e seu Ki aumentam.");
				break;
		}
		GastarOSintetizado(pl, p.Id);
		RecontarDepoisDoSintetizado(pl);
		Persistir(pl);
	}

	/// <summary>
	/// SO PRA BANCADA: os sorteios deste lote (0..100), pra a cena nao depender do dado -- o primeiro e o de MUTAR (o do
	/// virus, o da injecao, o do contagio e a falha do Kilo), o segundo o de MORRER (o do virus, e o coma da agulha).
	/// Nulo em jogo.
	/// </summary>
	internal (double Mutar, double Morrer)? SorteioDoVirusDeTeste;

	private void TomarOAntiVirus(ServerPlayer pl, Sintetizador.Produto p)
	{
		Fighter f = pl.Ficha;
		Falar(pl, Protocol.Fala.Emote, "consome um objeto misterioso!");   // `"[usr] consumes a mysterious object!"`
		switch (p.Id)
		{
			case Sintetizador.AntiDelta:
				// `for(var/S in usr.VirusesConsumed) if(counterVirus==S)`: so desfaz quem tomou o virus.
				if (!f.VirusesConsumed.Remove(Sintetizador.Delta)) { Avisar(pl, "você não carrega o Virus Delta: o antivírus não tem o que desfazer."); return; }
				f.kiregenMod -= 1; f.kidefBuff += 1.15; f.kioffBuff += 1.15;
				Avisar(pl, "o Virus Delta se desfaz: a regeneração de Ki volta ao que era.");
				break;
			case Sintetizador.AntiEcho:
				if (!f.VirusesConsumed.Remove(Sintetizador.Echo)) { Avisar(pl, "você não carrega o Virus Echo: o antivírus não tem o que desfazer."); return; }
				f.HPregenbuff -= 2; f.physdefBuff += 1.25;
				Avisar(pl, "o Virus Echo se desfaz: a regeneração volta ao que era.");
				break;
			case Sintetizador.Kilo:
			{
				// `if(prob(25)) { "Cure failed."; return }` -- e o item fica.
				if ((SorteioDoVirusDeTeste?.Mutar ?? _rng.NextDouble() * 100) < 25) { Avisar(pl, "a cura falhou."); return; }
				int tirou = Math.Min(_rng.Next(1, 4), f.Mutations);
				f.Mutations -= tirou;
				Avisar(pl, $"{tirou} mutação(ões) destruída(s).");
				break;
			}
			case Sintetizador.Mike:
				// `"You must stand still and cannot take damage for 10 seconds!"`.
				Avisar(pl, "fique parado e sem apanhar por dez segundos!");
				EsperarParado(pl, Sintetizador.SegundosParado, "você se mexeu (ou apanhou): o antivírus não fez efeito.", quem =>
				{
					if (quem.Mochila.Quantos(Sintetizador.Mike) <= 0) return;
					// `if(!usr.Senzu)`: quem ainda digere uma semente (ou outro remedio) nao aproveita -- e o item se gasta do mesmo jeito.
					if (_senzuNoCorpoG12.GetValueOrDefault(quem.Id) > 0) Avisar(quem, "você ainda está sob o efeito de um remédio de cura.");
					else
					{
						_senzuNoCorpoG12[quem.Id] = 4;
						quem.Ficha.Ki = quem.Ficha.MaxKi;
						quem.Ficha.stamina = quem.Ficha.maxstamina;
						quem.Combate?.Corpo.Curar(100);
						quem.Combate?.SincronizarVida();
						Avisar(quem, "o antivírus refaz o seu corpo, o seu vigor e o seu Ki.");
					}
					GastarOSintetizado(quem, Sintetizador.Mike);
				});
				return;
		}
		GastarOSintetizado(pl, p.Id);
		RecontarDepoisDoSintetizado(pl);
		Persistir(pl);
	}

	private void TomarAInjecao(ServerPlayer pl, Sintetizador.Produto p)
	{
		Fighter f = pl.Ficha;
		// A INSTA-DEATH NAO PASSA PELO `Consume()` DE BASE (o dela chama `Inject(usr)` direto).
		if (p.Id == Sintetizador.MorteCerta)
		{
			GastarOSintetizado(pl, p.Id);
			InjetarAMorte(pl, pl);
			return;
		}

		// `if(prob(10)) usr.Mutations+=1`.
		if ((SorteioDoVirusDeTeste?.Mutar ?? _rng.NextDouble() * 100) < Sintetizador.ChanceDeMutarNaInjecao) f.Mutations++;
		Falar(pl, Protocol.Fala.Emote, "injeta uma agulha misteriosa!");

		switch (p.Id)
		{
			case Sintetizador.Juventude:
				// `if(usr.Age>=10) usr.Age = 10`.
				if (pl.Idade >= 10)
				{
					pl.Idade = 10;
					pl.MesesDaIdade = 0;   // (2026-10-17) o `usr.Age = 10` do DM nao guarda decimo
					f.Idade = 10;
					Avisar(pl, "o corpo volta no tempo: você tem dez anos de novo.");
				}
				break;
			case Sintetizador.Lsd:
				if (_rng.NextDouble() * 100 < Sintetizador.ChanceDeMorrerDeLsd && !LsdNuncaMataDeTeste)
				{
					Falar(pl, Protocol.Fala.Emote, "injeta uma dose absurda e desaba, espumando.");
					pl.Combate?.Nocautear(Sintetizador.SegundosDoComa + 5);
					f.MorteMarcadaEm = NowMs() + (long)(Sintetizador.SegundosDoComa * 1000);
				}
				else
				{
					// `usr.TempBuff(list("Tmagi"=1,"Tkiregen"=1),500)`. O prazo mora na ficha: quem sai do jogo no meio
					// perde o reforco na volta, em vez de carrega-lo pra sempre.
					if (f.LsdAte == 0) { f.Tmagi += 1; f.Tkiregen += 1; }
					f.LsdAte = NowMs() + (long)(Sintetizador.SegundosDoLsd * 1000);
					Avisar(pl, "o mundo fica estranho: por um tempo, seu Ki volta mais depressa e sua magia sobe.");
				}
				break;
			case Sintetizador.Raiva:
			case Sintetizador.Raiva2:
			{
				bool v2 = p.Id == Sintetizador.Raiva2;
				// A RAIVA E NUMERO DERIVADO neste port (so o `ProjetarRaiva` escreve o campo): a injecao acende a terceira
				// fonte dele, que goteja e nao abre forma -- ver `ServerPlayer.RaivaDeInjecaoDesde`. Quem a projeta na
				// ficha e o `RepercutirPoder`, no fim desta funcao.
				pl.RaivaDeInjecaoDesde = NowMs();
				pl.RaivaDeInjecaoFracao = Sintetizador.FracaoDaRaiva(v2);
				if (v2)
				{
					// `usr.SpreadHeal(25)` e `usr.BP += usr.capcheck(usr.relBPmax*(1/800))`.
					pl.Combate?.Corpo.Curar(25);
					pl.Combate?.SincronizarVida();
					f.BP += f.CapCheck(f.relBPmax * Sintetizador.FracaoDaRaiva2);
				}
				Falar(pl, Protocol.Fala.Emote, v2 ? "entra numa fúria de droga das grandes!" : "entra numa fúria de droga!");
				break;
			}
			case Sintetizador.Esteroide:
			case Sintetizador.Esteroide2:
				f.BP += f.CapCheck(f.relBPmax * (p.Id == Sintetizador.Esteroide2 ? Sintetizador.FracaoDoEsteroide2 : Sintetizador.FracaoDoEsteroide));
				Falar(pl, Protocol.Fala.Emote, "usa um esteroide!");   // `"[usr] uses a steroid!!"`
				break;
			case Sintetizador.Rabo:
				// `if(usr.has_Tail() && !usr.Tail) { usr.Tail_Grow(); spawn usr.KO(); spawn usr.SpreadDamage(45) }`. O rabo
				// perdido de vez nao volta nem por aqui (regra do dono, `RaboDoSaiyajin`): quem decide e o `Regenerar`.
				if (pl.Combate is { } cd && cd.Corpo.Achar("Rabo") is { Decepado: true } rabo && cd.Corpo.Regenerar(rabo))
				{
					AjustarGanhoDoRabo(pl);
					Avisar(pl, "o rabo cresce de novo -- e o esforço derruba você.");
					EspalharDanoG3(pl, pl, 45, false);
					cd.Nocautear(10);
				}
				else Avisar(pl, "você não sente nada de diferente.");
				break;
		}
		GastarOSintetizado(pl, p.Id);
		RecontarDepoisDoSintetizado(pl);
		Persistir(pl);
	}

	/// <summary>SO PRA BANCADA: o LSD nunca cai na chance em dez de matar. Falso em jogo, sempre.</summary>
	internal bool LsdNuncaMataDeTeste;

	// =====================================================================
	// A INSTA-DEATH -- `proc/Inject` e `verb/Inject_Mob` (`Injections.dm:222-247`)
	// =====================================================================
	/// <summary>
	/// A agulha entra: uma chance em dez de mutacao, e a morte -- nove em dez pelo coma de cinco segundos, senao dez
	/// segundos depois. A hora marcada mora na FICHA (`Fighter.MorteMarcadaEm`): sair do jogo no meio nao a desmarca.
	/// </summary>
	private void InjetarAMorte(ServerPlayer autor, ServerPlayer alvo)
	{
		if ((SorteioDoVirusDeTeste?.Mutar ?? _rng.NextDouble() * 100) < Sintetizador.ChanceDeMutarNaInjecao) alvo.Ficha.Mutations++;
		Falar(autor, Protocol.Fala.Emote, autor == alvo ? "injeta uma agulha misteriosa!" : $"injeta uma agulha misteriosa em {NomeVisivel(alvo)}!");
		if ((SorteioDoVirusDeTeste?.Morrer ?? _rng.NextDouble() * 100) < Sintetizador.ChanceDeComa)
		{
			// `"[TargetMob] becomes comatose!"`.
			Falar(alvo, Protocol.Fala.Emote, "entra em coma!");
			alvo.Combate?.Nocautear(Sintetizador.SegundosDoComa + 5);
			alvo.Ficha.MorteMarcadaEm = NowMs() + (long)(Sintetizador.SegundosDoComa * 1000);
		}
		else
		{
			Avisar(alvo, "parece que... não funcionou?");   // `"It... didn't work?"`
			alvo.Ficha.MorteMarcadaEm = NowMs() + (long)(Sintetizador.SegundosDaDemora * 1000);
		}
		if (alvo != autor) MarcarAgressao(alvo, autor);
	}

	/// <summary>`Inject_Mob(var/mob/M in view(1))`: avisa o alvo, espera dez segundos sem apanhar, e injeta se ele ainda estiver ao lado.</summary>
	private void InjetarEmOutro(ServerPlayer pl, ItemDef def)
	{
		if (def.Id != Sintetizador.MorteCerta) return;
		if (pl.Ficha.KO || CaidoPelaMorte(pl)) { Avisar(pl, "caído, você não consegue."); return; }
		ServerPlayer? alvo = QuemEstaNaFrente(pl, o => !o.Ficha.dead);
		if (alvo == null) { Avisar(pl, "não há ninguém à sua frente."); return; }
		int idDoAlvo = alvo.Id;
		// `to_chat(M, "[usr] is injecting you with something!")`.
		Avisar(alvo, $"{NomeVisivel(pl)} está injetando alguma coisa em você!");
		Avisar(pl, $"você prepara a agulha: dez segundos sem apanhar, com {NomeVisivel(alvo)} ao lado.");
		EsperarParado(pl, Sintetizador.SegundosParado, "você apanhou (ou saiu do lugar): a agulha não entrou.", quem =>
		{
			if (quem.Mochila.Quantos(Sintetizador.MorteCerta) <= 0) return;
			// `if(usr.HP>=prevHP&&!usr.KO&&M in view(1))`.
			if (!_players.TryGetValue(idDoAlvo, out ServerPlayer? m) || m.Ficha.dead || m.Zone.Hash != quem.Zone.Hash
				|| Vec2.Distance(m.Pos, quem.Pos) > 1.5f * ZoneCollision.TileSize)
			{ Avisar(quem, "o alvo saiu do seu lado: a agulha não entrou."); return; }
			GastarOSintetizado(quem, Sintetizador.MorteCerta);
			InjetarAMorte(quem, m);
		});
	}

	// =====================================================================
	// A ESPERA PARADA -- o `var/prevHP = usr.HP; var/prevloc = usr.loc; sleep(100); if(usr.HP>=prevHP&&usr.loc==prevloc&&!usr.KO)`
	// =====================================================================
	private sealed class EsperaParada
	{
		public int Quem;
		public long Em;
		public Vec2 Onde;
		public double Vida;
		public string Quebrou = "";
		public Action<ServerPlayer> AoFim = _ => { };
	}

	/// <summary>Quem esta esperando parado, e o que acontece se chegar ao fim. Uma espera por corpo: a nova troca a velha.</summary>
	private readonly Dictionary<int, EsperaParada> _esperasParadas = [];

	private void EsperarParado(ServerPlayer pl, double segundos, string quebrou, Action<ServerPlayer> aoFim) =>
		_esperasParadas[pl.Id] = new EsperaParada
		{
			Quem = pl.Id, Em = NowMs() + (long)(segundos * 1000), Onde = pl.Pos,
			Vida = pl.Combate?.Corpo.Vida() ?? 0, Quebrou = quebrou, AoFim = aoFim,
		};

	private void TickDasEsperasParadas()
	{
		if (_esperasParadas.Count == 0) return;
		long agora = NowMs();
		foreach (EsperaParada e in _esperasParadas.Values.ToList())
		{
			if (!_players.TryGetValue(e.Quem, out ServerPlayer? pl)) { _esperasParadas.Remove(e.Quem); continue; }
			bool quebrou = pl.Ficha.KO || pl.Ficha.dead || (pl.Pos - e.Onde).LengthSquared > 16f
						   || (pl.Combate?.Corpo.Vida() ?? 0) < e.Vida - 1e-6;
			if (quebrou)
			{
				_esperasParadas.Remove(e.Quem);
				Avisar(pl, e.Quebrou);
				continue;
			}
			if (agora < e.Em) continue;
			_esperasParadas.Remove(e.Quem);
			e.AoFim(pl);
		}
	}

	// =====================================================================
	// A MUTACAO, AS MORTES MARCADAS E O FIM DO LSD -- o relogio do lote
	// =====================================================================
	private double _relogioDasMutacoes;

	/// <summary>
	/// O CONTAGIO (`attack cmn.dm:117-120`): quem apanha de perto de um mutante pode ganhar uma mutacao -- so gente
	/// (`if(M.client)`), 10% por mutacao de quem bate. Chamado do funil de toda agressao (`MarcarAgressao`).
	/// </summary>
	private void ContagiarPeloGolpe(ServerPlayer vitima, ServerPlayer agressor)
	{
		int n = agressor.Ficha.Mutations;
		if (n <= 0 || Sintetizador.MutacaoInofensivaDeTeste || vitima.Peer == null && !ContagioSemPeerDeTeste) return;
		if (string.Equals(vitima.Race, "Android", StringComparison.OrdinalIgnoreCase)) return;
		if (vitima.Zone.Hash != agressor.Zone.Hash || Vec2.Distance(vitima.Pos, agressor.Pos) > 1.5f * ZoneCollision.TileSize) return;
		if ((SorteioDoVirusDeTeste?.Mutar ?? _rng.NextDouble() * 100) >= Sintetizador.ContagioPorMutacao * n) return;
		vitima.Ficha.Mutations++;
		Avisar(vitima, "o golpe deixa alguma coisa errada em você: uma mutação.");
	}

	/// <summary>SO PRA BANCADA: o contagio vale pra corpo sem conexao. Falso em jogo, sempre.</summary>
	internal bool ContagioSemPeerDeTeste;

	private void TickDoSintetizador(double dt)
	{
		TickDasEsperasParadas();

		_relogioDasMutacoes += dt;
		if (_relogioDasMutacoes < 1) return;
		_relogioDasMutacoes -= 1;
		long agora = NowMs();
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			Fighter f = pl.Ficha;
			// A MORTE MARCADA pela agulha (`sleep(50); TargetMob.Death()`). Quem morreu antes por outra mao fica desmarcado.
			if (f.MorteMarcadaEm != 0 && (f.dead || agora >= f.MorteMarcadaEm))
			{
				bool vivo = !f.dead;
				f.MorteMarcadaEm = 0;
				if (vivo) pl.Combate?.Morrer(ignorarSeguro: true);
			}
			// O FIM DO LSD (`TempBuff(..., 500)`).
			if (f.LsdAte != 0 && agora >= f.LsdAte)
			{
				f.LsdAte = 0;
				f.Tmagi -= 1; f.Tkiregen -= 1;
				f.Statify();
				Avisar(pl, "o efeito da injeção passa.");
			}
			if (f.Mutations <= 0) continue;
			// `if(Mutations) Mutations=0` na morte (`Death.dm:46`), e `if(Race=="Android") Mutations=0` (`Stats.dm:207`).
			if (f.dead || string.Equals(pl.Race, "Android", StringComparison.OrdinalIgnoreCase)) { f.Mutations = 0; continue; }
			// `SpreadDamage(0.005*Mutations)`: o mesmo fio de vida em cada membro. Direto no corpo, e nao pelo
			// `EspalharDanoG3`: aquele arma a tag de combate, e uma mutacao deixaria a pessoa "em combate" pra sempre.
			if (Sintetizador.MutacaoInofensivaDeTeste || f.KO || pl.Combate is not { } corpo) continue;
			foreach (BodyPart parte in corpo.Corpo.Partes.ToList())
				if (!parte.Decepado && !parte.Aninhado) corpo.Ferir(parte, Sintetizador.DanoPorMutacao * f.Mutations, false);
			corpo.SincronizarVida();
		}
	}

	/// <summary>Quem saiu do mundo leva a espera dele. (A morte marcada e o prazo do LSD moram na ficha, e voltam com ela.)</summary>
	private void EsquecerSintetizador(int id) => _esperasParadas.Remove(id);
}
