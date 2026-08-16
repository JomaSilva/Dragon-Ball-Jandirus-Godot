using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O SOL QUEIMA -- a ramificacao que a Fase 3 deixou anotada e nao ligada.
///
/// ============================ O QUE ESTE ARQUIVO E, EM UMA FRASE ============================
/// Ele e o ENCANAMENTO. Quem decide quanto arde e <see cref="CalorDaEstrela"/>, no Core, e quem
/// decide onde a estrela esta e <see cref="Sistemas.EstrelaPerto"/>, tambem no Core. Aqui so se
/// pergunta, se fere o corpo e se avisa.
/// ========================================================================================
///
/// ============================ TRES REGRAS QUE ESTE ARQUIVO NAO PODE QUEBRAR ============================
/// 1. **A morte tem UMA porta.** `pl.Combate.Morrer()` -- e o retorno tem que ser OLHADO, porque a
///    Aura of Destruction pode negar (`CombatState.NegarMorte`). Anunciar a morte de quem nao morreu
///    e pior que nao ter o seguro. A bancada `disciplinas` varre os fontes atras de `Morrer()` com o
///    retorno jogado fora, entao um descuido aqui reprova la.
/// 2. **Nao ha algoz.** Uma estrela nao e um jogador: nao ha Zenkai, nao ha karma de PK, nao ha
///    `UltimoAgressor`. Passar a queimadura pelo `AoPerderALuta` daria poder de graca a quem se
///    jogasse no sol de proposito, e mancharia o karma de ninguem.
/// 3. **O corpo se fere pelo funil normal** (`Corpo.Ferir` -> `DeveNocautear`/`DeveMorrer`). E o
///    mesmo caminho do Kaio-ken que estoura (`EstourarG2`) e do dano de fome.
/// ===================================================================================================
///
/// ============================ O AVISO NAO E OPCIONAL ============================
/// Um sol que mata sem sinal e um bug do ponto de vista de quem joga. Sao QUATRO sinais, e tres
/// deles existem antes de o primeiro ponto de vida sair:
///
///   * a ESTRELA E DESENHADA no mundo (`Client.EstrelaDesenhada`) -- ate a menor delas mede 720 px
///     de diametro numa janela de 384x216, ou seja ela ocupa a tela inteira muito antes de encostar;
///   * a COROA (1,75x o raio) manda uma linha de chat quando se entra nela, com o nome da estrela e
///     o quanto o corpo aguenta la dentro;
///   * a ENTRADA no raio manda um berro com o dano por segundo, e ele se repete a cada 2 s;
///   * a TELA DO SISTEMA (tecla P -> Nav -> duplo clique) mostra o raio letal e a estimativa.
/// ==============================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// A CADA QUANTO O BERRO DE "VOCE ESTA QUEIMANDO" SE REPETE, em ms.
	///
	/// Dois segundos: curto o bastante pra insistir enquanto o corpo derrete, longo o bastante pra
	/// nao ser as 30 linhas por segundo que um aviso sem relogio proprio vira.
	/// </summary>
	private const long MsEntreBerrosDeCalor = 2000;

	/// <summary>
	/// O TIQUE DO SOL -- roda dentro do <see cref="TickDoEspaco"/>, que ja e por jogador e por tique
	/// e ja devolveu quem nao esta no espaco.
	///
	/// ============================ POR QUE NAO ENTROU NO `PlanetaSob` ============================
	/// A spec sugeria encaixar em `PlanetaSob`, "que ja detecta proximidade pra pousar". Nao da, e a
	/// Fase 2 ja tinha escrito o motivo: `PlanetaSob` varre `Espaco.PorPerto`, que devolve
	/// <see cref="PlanetaNoEspaco"/> -- e o que ele encontra, o jogador POUSA em cima. Uma estrela
	/// naquela lista seria o jogador pousando no sol, calado, numa zona de superficie que nao existe.
	///
	/// Entao o encaixe e IRMAO e nao dentro: mesma funcao, mesma cadencia, mesmo tique, duas
	/// perguntas. E ela vem ANTES do pouso de proposito -- quem esta dentro de uma estrela nao esta
	/// perto de planeta nenhum (a orbita mais interna nasce a 900 px da superficie,
	/// `Sistemas.FolgaDaCoroa`), mas a ordem deixa a regra escrita em vez de depender daquela folga.
	/// ========================================================================================
	/// </summary>
	private void TickDoSol(ServerPlayer pl, double dt)
	{
		// MORTO NAO QUEIMA. Sem esta linha o cadaver continuaria "recebendo dano" ate renascer, e o
		// `Morrer()` seria chamado a cada tique so pra devolver false.
		if (pl.Ficha.dead || pl.Combate == null) { EsfriarDeVez(pl); return; }

		if (!Sistemas.EstrelaPerto(SeedDoUniverso, pl.Pos, out Estrela e, out double dist))
		{ EsfriarDeVez(pl); return; }

		bool jaAvisado = pl.CalorNoTique != ProximidadeDaEstrela.Longe;
		ProximidadeDaEstrela onde = CalorDaEstrela.Onde(e, dist, jaAvisado);
		ProximidadeDaEstrela antes = pl.CalorNoTique;
		pl.CalorNoTique = onde;

		switch (onde)
		{
			case ProximidadeDaEstrela.Longe:
				if (antes != ProximidadeDaEstrela.Longe) EsfriarDeVez(pl);
				return;

			case ProximidadeDaEstrela.Coroa:
				// SAIU DE DENTRO E ESTA VIVO: o alivio, com o preco. E a unica linha do sistema que
				// diz "deu certo", e ela existe porque escapar tem que ser uma coisa que se sente.
				if (antes == ProximidadeDaEstrela.Dentro)
				{
					Avisar(pl, $"você escapa de {NomeDaEstrela(e)} com {pl.VidaQueimada:0} de vida a menos.");
					pl.VidaQueimada = 0;
				}
				else if (antes == ProximidadeDaEstrela.Longe) AvisarDaCoroa(pl, e);
				return;
		}

		Queimar(pl, e, dt);
	}

	/// <summary>Zera os marcadores de borda. Um lugar so, porque ha tres caminhos de saida.</summary>
	private static void EsfriarDeVez(ServerPlayer pl)
	{
		pl.CalorNoTique = ProximidadeDaEstrela.Longe;
		pl.VidaQueimada = 0;
	}

	/// <summary>
	/// O AVISO DA COROA -- a unica mensagem que chega ANTES de custar vida.
	///
	/// Ela diz o NUMERO (quanto tempo o corpo aguenta la dentro) e nao um adjetivo. "Perigoso" nao
	/// ensina nada: com o numero na frente, o jogador de 1e8 de BP sabe que pode atravessar e o de
	/// 1e4 sabe que nao pode, e os dois descobrem isso sem morrer uma vez pra aprender.
	/// </summary>
	private void AvisarDaCoroa(ServerPlayer pl, Estrela e)
	{
		double s = CalorDaEstrela.SegundosAteCeder(e, pl.Ficha.expressedBP,
												   pl.Combate!.Corpo.Vida(), CombatKnobs.RegenPorSegundo);
		Avisar(pl, $"o calor de {NomeDaEstrela(e)} chega até aqui. Dentro dela seu corpo cede em "
				   + $"{(double.IsInfinity(s) ? "muito tempo" : $"~{s:0} s")} — não entre sem motivo.");
	}

	/// <summary>
	/// O CORPO DENTRO DA ESTRELA, um tique de cada vez.
	///
	/// ============================ O DANO E LETAL E ESPALHADO ============================
	/// `letal: true` porque a estrela PODE matar -- o piso de `Ferir` no nao-letal para no limiar do
	/// nocaute, e um sol que so nocauteia nao e um sol.
	///
	/// Espalhado por TODO membro nao-aninhado (e nao sorteando um), pelo mesmo motivo que o
	/// `EspalharDanoG2` do Kaio-ken: nao ha mira num incendio. Os aninhados (cerebro, orgaos) se
	/// ferem pela propagacao que o proprio `Ferir` faz -- ferir os dois lados os cozinharia em dobro.
	/// ================================================================================
	/// </summary>
	private void Queimar(ServerPlayer pl, Estrela e, double dt)
	{
		CombatState c = pl.Combate!;

		// ============================ O CORPO INTOCAVEL NAO QUEIMA ============================
		// Este era o unico furo de dano de TERCEIRO no port -- e o que matava. O calor nunca passou
		// pelo funil do soco nem pelo do dano em area, entao a imunidade da cinematica de
		// transformacao (e a carencia de renascimento) simplesmente nao existiam aqui: o jogador
		// morreria dentro da estrela no meio da propria estreia do SSJ3, sem nada pra fazer.
		//
		// A SAIDA E ANTES DO LACO, e nao so no `Ferir`: o `DeveMorrer`/`DeveNocautear` la embaixo le o
		// ESTADO do corpo, nao o dano deste tique. Quem entrasse na cena ja no limiar seria morto por
		// um dano que nao aconteceu -- o escudo derrubando quem ele protege.
		// ==================================================================================
		if (c.Intocavel) return;

		double dano = CalorDaEstrela.DanoPorSegundo(e, pl.Ficha.expressedBP) * dt;
		if (dano <= 0) return;

		double antes = c.Corpo.Vida();
		foreach (BodyPart p in c.Corpo.Partes.ToList())
			if (!p.Decepado && !p.Aninhado) c.Ferir(p, dano, letal: true);
		c.SincronizarVida();
		pl.VidaQueimada += Math.Max(0, antes - c.Corpo.Vida());

		BerrarDoCalor(pl, e);

		// A MORTE PRIMEIRO, O NOCAUTE DEPOIS -- e nao o contrario. Um corpo que zerou um nucleo
		// satisfaz as DUAS perguntas (`DeveMorrer` implica `DeveNocautear`), e nocautear antes
		// deixaria o cadaver de pe por um tique com a bandeira errada.
		if (c.Corpo.DeveMorrer() && !c.Corpo.RegeneraDecepado)
		{
			// O RETORNO E OLHADO: a Aura of Destruction nega mortes, e ela nega esta tambem. Quem
			// foi salvo continua queimando no proximo tique -- e o certo: a aura barra a morte, nao
			// tira o corpo do fogo.
			if (c.Morrer())
			{
				Avisar(pl, $"você é consumido por {NomeDaEstrela(e)}.");
				GD.Print($"[server] {pl.Name} MORREU DENTRO de {NomeDaEstrela(e)} "
						 + $"(BP expresso {pl.Ficha.expressedBP:N0} contra poder {CalorDaEstrela.PoderDe(e.Classe):N0})");
				EsfriarDeVez(pl);
			}
			return;
		}

		if (!pl.Ficha.KO && c.Corpo.DeveNocautear())
		{
			c.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);
			Avisar(pl, "o calor te apaga. Você para de nadar.");
			GD.Print($"[server] {pl.Name} NOCAUTEADO dentro de {NomeDaEstrela(e)}");
		}
	}

	/// <summary>
	/// O BERRO, com relogio proprio. O primeiro sai no instante em que se entra (o campo nasce
	/// zerado e `NowMs()` nunca e zero), os seguintes a cada <see cref="MsEntreBerrosDeCalor"/>.
	/// </summary>
	private void BerrarDoCalor(ServerPlayer pl, Estrela e)
	{
		long agora = NowMs();
		if (agora < pl.AvisoDeCalorAte) return;
		pl.AvisoDeCalorAte = agora + MsEntreBerrosDeCalor;

		double dps = CalorDaEstrela.DanoPorSegundo(e, pl.Ficha.expressedBP);
		double s = CalorDaEstrela.SegundosAteCeder(e, pl.Ficha.expressedBP,
												   pl.Combate!.Corpo.Vida(), CombatKnobs.RegenPorSegundo);
		Avisar(pl, $"VOCÊ ESTÁ DENTRO DE {NomeDaEstrela(e).ToUpperInvariant()} — "
				   + $"{dps:0.#} de vida por segundo"
				   + (double.IsInfinity(s) ? "" : $", {s:0} s até o corpo ceder") + ". SAIA.");
	}

	/// <summary>
	/// COMO A ESTRELA SE CHAMA numa linha de chat.
	///
	/// O <see cref="SistemaSolar.NomeDaEstrela"/> nomeia o SISTEMA ("Sistema de Earth"), e dizer
	/// "você está dentro do Sistema de Earth" seria falso -- estar no sistema e o normal. Aqui o
	/// nome e o do CORPO, e ele sai da classe, que e o que o jogador ve na tela.
	/// </summary>
	private static string NomeDaEstrela(Estrela e) => e.Classe switch
	{
		ClasseDeEstrela.AnaVermelha => "uma anã vermelha",
		ClasseDeEstrela.Laranja => "uma estrela laranja",
		ClasseDeEstrela.Amarela => "um sol amarelo",
		ClasseDeEstrela.Branca => "uma estrela branca",
		ClasseDeEstrela.Azul => "uma estrela azul",
		ClasseDeEstrela.GiganteBranca => "uma gigante branca",
		ClasseDeEstrela.GiganteVermelha => "uma gigante vermelha",
		_ => "uma gigante azul",
	};
}
