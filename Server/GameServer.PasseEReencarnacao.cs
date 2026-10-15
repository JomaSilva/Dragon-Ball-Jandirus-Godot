using Godot;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS DUAS SAIDAS NOVAS DA MESA DO ENMA (v0.3.0) -- o pedido do dono de 2026-10-15, itens 7 e 11:
///
///   * A REENCARNACAO (o `enma_reincarnate()` do DM, `SkyNPCs.dm:200-204` + `Reincarnation.dm`): o personagem acaba PARA
///     SEMPRE, o jogador volta pra criacao, e o proximo personagem da conta nasce com 10% do BP dele;
///   * O PASSE (nao existe no DM): 100.000 zeni pra visitar os vivos por sete dias -- morto, de aureola, e de volta pro
///     Outro Mundo quando o prazo vence.
///
/// A fala e a volta paga continuam na secao 5 do `GameServer.Alem.cs`. O JULGAMENTO VEM ANTES DE QUALQUER OFERTA, nas
/// quatro: coracao negro nao compra nada, vai pro Inferno (`enma_interact()`, `SkyNPCs.dm:159-161`).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>DEFEITO INJETADO (`--v030teste`): o eco da reencarnacao e ignorado -- o novo personagem nasce do zero, o jogo de antes.</summary>
	internal static bool EcoDaReencarnacaoIgnoradoDeTeste;

	/// <summary>DEFEITO INJETADO (`--v030teste`): o passe nao poe o morto de pe -- entre os vivos ele e o cadaver de sempre.</summary>
	internal static bool PasseDeitaDeTeste;

	/// <summary>DEFEITO INJETADO (`--v030teste`): o passe nunca vence -- o morto fica entre os vivos pra sempre.</summary>
	internal static bool PasseSemVoltaDeTeste;

	// =====================================================================
	// 1. A REENCARNACAO (item 7)
	// =====================================================================
	/// <summary>
	/// REENCARNAR: o personagem acaba PARA SEMPRE e o jogador volta pra CRIACAO; o proximo personagem da conta nasce com 10%
	/// do BP deste. O dono (2026-10-15): *"falta a opcao de reencarnar no enma, ao qual vc perde seu personagem voltando pra
	/// criacao de personagem porem o personagem reencarnado comeca com 10% do bp do personagem anterior"*.
	///
	/// A CONFIRMACAO E A DO `DeleteChar` DESTE PORT (digitar o nome), e nao o `alert()` de dois botoes do DM: e o mesmo gesto
	/// -- apagar um personagem pra sempre --, e o port ja escolheu como se confirma isso.
	///
	/// O ECO MORA NA CONTA, e nao na sessao: no DM ele e do `client` (`client.ReincarnationBonus`), e o jogador e avisado pra
	/// nao deslogar antes de criar o novo (*"You must create a new character to claim these bonuses within this login
	/// session"*). Aqui ele vai pro arquivo da conta no MESMO gravar que apaga o slot -- uma queda no meio nao rouba a
	/// reencarnacao. Quem nascer primeiro na conta, em qualquer vaga, o leva (`AplicarOEcoDaReencarnacao`).
	///
	/// EM FUSAO, NAO -- divergencia declarada: o DM separa a fusao primeiro (`Reincarnate`, `:19-31`); aqui o Enma recusa e
	/// pede pra desfazer, porque o eco sairia do poder FUNDIDO, que nao e de ninguem.
	///
	/// A VOLTA PRA CRIACAO e do cliente: o servidor apaga e despede com o motivo (`FioDaDespedida`, no
	/// `ApagarOPersonagemParaSempre`), e o cliente disca de novo e abre a criacao na vaga que vagou (`Boot.AoSerDespedido`)
	/// -- o `client.mob = lobby` do DM (`Reincarnation.dm:43-49`).
	/// </summary>
	private void EnmaReencarnar(ServerPlayer pl, string digitado)
	{
		if (ObraQueAceita(pl, "enma_reencarnar") == null) { Avisar(pl, "o Enma não está ao alcance -- fale com ele na mesa dele."); return; }
		if (!pl.Ficha.dead) { Avisar(pl, "Enma Daioh troveja: \"Os vivos não têm negócio na minha mesa!\""); return; }
		if (pl.Karma < 0) { JulgarProInferno(pl); return; }   // o julgamento vem antes de qualquer oferta
		if (EstaFundido(pl.Id))
		{
			Avisar(pl, "Enma Daioh: \"Duas almas num corpo só? Desfaça a fusão antes de pedir uma vida nova.\"");
			return;
		}
		if (!string.Equals(digitado.Trim(), pl.Name, StringComparison.OrdinalIgnoreCase))
		{
			Avisar(pl, $"Enma Daioh: \"Reencarnar apaga {pl.Name} PARA SEMPRE, e a próxima vida começa com um eco de 10% do "
					 + $"poder. Se é isso mesmo, escreva o nome -- \"{pl.Name}\" -- e eu carimbo.\"");
			return;
		}
		if (PodeApagarOPersonagem(pl, out string porque) == null)
		{
			Avisar(pl, $"Enma Daioh franze a testa: a sua ficha não se deixa apagar ({porque})");
			return;
		}

		var eco = new Reencarnacao { De = pl.Name, BPAntigo = pl.Ficha.BP, Fracao = Alem.FracaoDaReencarnacao };
		GD.Print($"[server] {pl.Name} pediu ao Enma pra reencarnar: o proximo personagem de {pl.Conta} nasce com {eco.BPDoNovo():0} de BP");
		ApagarOPersonagemParaSempre(pl, "reencarnou pelo Enma", eco);
	}

	/// <summary>
	/// A frase da reencarnacao -- a do DM (`SkyNPCs.dm:203`) e o que vem depois. Vai no chat e na despedida (a criacao a
	/// mostra em cima). Sem numero de poder: e a regra principal do dono, e "10%" e uma razao, nao uma leitura.
	/// </summary>
	private static string FraseDaReencarnacao(string nome) =>
		"Enma Daioh: \"Uma alma que escolhe recomeçar... que assim seja. Vá -- renasça, e leve só um eco de quem você foi.\" "
		+ $"{nome} se foi. A próxima vida desta conta nasce com 10% do poder de {nome}.";

	/// <summary>
	/// O ECO NASCE COM O PROXIMO PERSONAGEM -- o `CheckIncarnate()` do DM (`Reincarnation.dm:4-16`), chamado no mesmo ponto
	/// da criacao (`CharacterCreation.dm:208`): `BP = max(BP, round(oldBP * startBPMult))`, uma vez so. O eco e consumido
	/// aqui, em TODAS as copias vivas da conta, antes de o slot novo ir pro disco (quem grava e o `CreateChar`, logo depois);
	/// sem isto, a segunda copia (a tela de selecao de outra conexao) o daria de novo.
	///
	/// A `naturalization` do God Ki que o DM carrega junto (`:14`) nao vem: o port nao tem naturalizacao.
	/// </summary>
	private Reencarnacao? AplicarOEcoDaReencarnacao(AccountSave acc, Fighter lutador)
	{
		if (acc.Reencarnacao is not { } eco) return null;
		acc.Reencarnacao = null;
		EmTodaCopiaViva(acc.Conta, v => v.Reencarnacao = null);
		if (EcoDaReencarnacaoIgnoradoDeTeste) return null;

		lutador.BP = Math.Max(lutador.BP, eco.BPDoNovo());
		GD.Print($"[server] {acc.Conta}: o eco de {eco.De} nasce junto -- BP base {lutador.BP:0}");
		return eco;
	}

	// =====================================================================
	// 2. O PASSE DO ENMA (item 11)
	// =====================================================================
	/// <summary>
	/// O PASSE PRO MUNDO DOS VIVOS -- nao existe no DM. O dono (2026-10-15): *"o enma deveria ter uma opcao q custa 100k
	/// zeni, q permite vc ir mesmo morto pra qualquer planeta descoberto do jogo, ou planeta pre feito, vc fica no mundo dos
	/// vivos por 7 dias e dps volta pro ceu, porem vc continua com a aureola e os efeitos de morto no mundo dos vivos (ate pq
	/// vc ainda ta morto) inspirado no goku"*.
	///
	/// ============================ O MORTO CONTINUA MORTO ============================
	/// `dead` nao muda e a aureola fica (`MorteJaViajou`). O que poe o morto DE PE entre os vivos e o prazo na ficha
	/// (`Fighter.passe_do_enma_ate`, lido pelo `ServerPlayer.MortoDePe` -- ver `Alem.EmVisita`). Tudo o que pergunta "este
	/// morto anda?" (o passo, a pose, o voo, os golpes, o tiro, o alvo das tecnicas, as passagens) le aquela propriedade, entao
	/// o visitante anda, luta e e alvo como o fantasma do Outro Mundo -- e o cadaver dos 2 s continua o unico morto deitado.
	/// O cliente sabe pelo bit `SheetState.EmVisita` (`LocalPlayer.Caido`).
	/// ================================================================================
	///
	/// OS SETE DIAS SAO DO JOGO (o dia do ceu, `Espaco.SegundosPorDiaInGame` = 24 min): 2 h 48 min de relogio. E a medida de
	/// "dia" que o resto do port ja usa (as missoes de cargo, a visita do Bills), e ela corre com o servidor desligado -- o
	/// prazo e hora de parede, como a pena do Inferno.
	///
	/// OS DESTINOS (`DestinosDoPasse`): os pre-feitos com mapa e os mundos gerados que algum JOGADOR ja pisou (o livro dos
	/// nomes, `NomeDePlaneta.Descobridor`) -- menos os que morreram. Um mundo gerado descarregado recebe o morto em ORBITA,
	/// sobre o disco, e o `TickDoEspaco` pousa (o funil do berco, `MandarProBerco`).
	///
	/// A VOLTA: o prazo (`CumprirOPasseDoEnma`, de volta a mesa do Enma -- o "volta pro ceu" do dono e o lugar onde os mortos
	/// chegam), o "morrer de novo" (o `else if(dead)` do `Death()` devolve a mesa do Enma, e o passe acaba ali) e a volta a
	/// vida (as esferas, a tecnica de um vivo: o passe sobra e some). Saltar de planeta continua fechado pro morto
	/// (`PodeSaltarDePlaneta`): o passe e pra um mundo, e quem escolhe e o Enma.
	/// </summary>
	private void EnmaPasse(ServerPlayer pl, string destino)
	{
		if (ObraQueAceita(pl, "enma_passe") == null) { Avisar(pl, "o Enma não está ao alcance -- fale com ele na mesa dele."); return; }
		if (!pl.Ficha.dead) { Avisar(pl, "Enma Daioh troveja: \"Os vivos não têm negócio na minha mesa!\""); return; }
		if (pl.Karma < 0) { JulgarProInferno(pl); return; }   // o julgamento vem antes de qualquer oferta
		if (pl.Ficha.passe_do_enma_ate != 0) { Avisar(pl, "Enma Daioh: \"Você já tem um passe carimbado.\""); return; }

		List<(string Nome, ZoneKey Zona)> destinos = DestinosDoPasse();
		string lista = string.Join(", ", destinos.Select(d => d.Nome));
		string pedido = destino.Trim();
		if (pedido.Length == 0)
		{
			Avisar(pl, $"Enma Daioh abre o livro dos mundos: \"Por {Alem.PrecoDoPasseDoEnma:N0} zeni, {Alem.DiasDoPasseDoEnma} dias "
					 + $"entre os vivos -- morto, de auréola. Escolha o mundo: {lista}.\"");
			return;
		}

		int i = destinos.FindIndex(d => string.Equals(d.Nome, pedido, StringComparison.OrdinalIgnoreCase)
									   || string.Equals(d.Zona.Name, pedido, StringComparison.OrdinalIgnoreCase));
		if (i < 0)
		{
			Avisar(pl, $"Enma Daioh: \"Não conheço mundo nenhum chamado '{pedido}'. Escolha: {lista}.\"");
			return;
		}
		(string nome, ZoneKey zona) = destinos[i];
		if (ChegadaDoPasse(zona) is not { } chegada)
		{
			Avisar(pl, $"Enma Daioh: \"{nome} não tem chão pra receber ninguém agora.\"");
			return;
		}

		// SO EM ZENI, como a volta paga: o dono, 2026-10-10 -- *"o enma mantenha so zeni"*.
		double custo = EnmaNaoCobraDeTeste ? 0 : Alem.PrecoDoPasseDoEnma;
		if (pl.Ficha.Zeni < custo)
		{
			Avisar(pl, $"Enma Daioh: \"O passe custa {custo:N0} zeni. Você tem {pl.Ficha.Zeni:N0}.\"");
			return;
		}

		pl.Ficha.Zeni -= custo;
		pl.Ficha.passe_do_enma_ate = NowMs() + Alem.MsDoPasseDoEnma;
		pl.AvisadoDoFimDoPasse = false;
		Avisar(pl, $"Enma Daioh carimba um passe na sua ficha: \"{Alem.DiasDoPasseDoEnma} dias entre os vivos -- nem um a mais. "
				 + "Você continua MORTO: a auréola fica, e quando o prazo acabar a sua alma volta pra cá.\"");
		MandarProBerco(pl, chegada);
		if (!Espaco.EhEspaco(pl.Zone))
			Avisar(pl, $"você desce em {nome}, de auréola na cabeça. O passe vence em {Alem.DiasDoPasseDoEnma} dias do jogo.");
		MandarFicha(pl);
		Persistir(pl);
		GD.Print($"[server] {pl.Name} comprou o passe do Enma ({custo:N0} zeni): {Alem.DiasDoPasseDoEnma} dias em {nome} ({zona})");
	}

	/// <summary>
	/// OS MUNDOS QUE O PASSE ALCANCA, com o nome que o jogador le: os pre-feitos com mapa carregado (o batizado, ou "Terra")
	/// e os gerados que um JOGADOR ja pisou -- o "descoberto" do dono e o descobridor do livro dos nomes. Os mortos ficam de
	/// fora: num cadaver nao se pousa (`TickDoEspaco`).
	/// </summary>
	private List<(string Nome, ZoneKey Zona)> DestinosDoPasse()
	{
		var lista = new List<(string Nome, ZoneKey Zona)>();
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
		{
			ZoneKey z = ZoneKey.Premade(p.Nome);
			if (_catalogo?.Get(z) == null || ZonaMorta(z)) continue;
			lista.Add((NomeDoPlaneta(p.Nome), z));
		}
		foreach (NomeDePlaneta n in _batismos.Todos)
		{
			if (n.AssinaturaDoDescobridor.Length == 0) continue;
			if (!ChaveDePlaneta.Ler(n.Chave, n.Original, out ChaveDePlaneta chave) || chave.PreFeito) continue;
			ZoneKey z = ChaveDePlaneta.ZonaDe(n.Chave, n.Original);
			if (ZonaMorta(z)) continue;
			lista.Add((NomeDoMundo(z), z));
		}
		return lista;
	}

	/// <summary>
	/// ONDE O MORTO DESCE: o marco de nascimento do pre-feito (`PontoDeNascimento`), a chegada do mundo gerado que ja esta
	/// vivo, ou a ORBITA do que esta frio -- o mesmo trio do `PousarNo` do berco. Nulo quando o mundo nao e um corpo deste
	/// universo.
	/// </summary>
	private (ZoneKey Zona, Vec2 Pos)? ChegadaDoPasse(ZoneKey zona)
	{
		if (zona.Kind == ZoneKey.KindPremade) return (zona, PontoDeNascimento(zona));
		if (ChegadaDaZonaGerada(zona) is { } chegada) return (zona, chegada);
		if (CorpoGerado.DaZona(SeedDoUniverso, zona) is { } achado) return (ZonaDoEspaco, achado.Corpo.Pos);
		return null;
	}

	/// <summary>
	/// O RELOGIO DO PASSE, no laco de fichas (ao lado da pena do Inferno): avisa quando falta um dia e, vencido, devolve a
	/// alma a mesa do Enma. Quem voltou a vida no meio da visita perde o passe calado -- ele nao serve pra nada vivo.
	/// </summary>
	private void CumprirOPasseDoEnma(ServerPlayer pl)
	{
		long ate = pl.Ficha.passe_do_enma_ate;
		if (ate == 0) return;
		if (!pl.Ficha.dead)
		{
			pl.Ficha.passe_do_enma_ate = 0;
			pl.AvisadoDoFimDoPasse = false;
			return;
		}

		long agora = NowMs();
		if (agora < ate || PasseSemVoltaDeTeste)
		{
			if (!pl.AvisadoDoFimDoPasse && ate - agora <= Alem.MsDoPasseDoEnma / Alem.DiasDoPasseDoEnma)
			{
				pl.AvisadoDoFimDoPasse = true;
				Avisar(pl, "falta um dia pro seu passe do Enma vencer -- depois disso, a sua alma volta pro Outro Mundo.");
			}
			return;
		}
		AcabarOPasse(pl, "o seu passe do Enma venceu: os dias entre os vivos acabaram, e a sua alma é puxada de volta pro Outro Mundo.");
	}

	/// <summary>O passe acaba e a alma volta a mesa do Enma -- o prazo vencido. (O "morrer de novo" acaba o passe no `MorrerDeNovoNoAlem`.)</summary>
	private void AcabarOPasse(ServerPlayer pl, string frase)
	{
		pl.Ficha.passe_do_enma_ate = 0;
		pl.AvisadoDoFimDoPasse = false;
		Avisar(pl, frase);
		if (!Alem.EhOAlem(pl.Zone))
		{
			// QUEM ESTA FORA DO CORPO VOLTA PRO CORPO ANTES DE VIAJAR (a mente, o leme) -- a mesma linha do `Drop`.
			if (pl.BonecoLargado != null) VoltarDeOndeEstiver(pl, "");
			ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
			MoveToZone(pl.Id, alem, MesaDoEnma(alem));
		}
		MandarFicha(pl);
		Persistir(pl);
		GD.Print($"[server] {pl.Name}: o passe do Enma acabou -- de volta ao Outro Mundo");
	}
}
