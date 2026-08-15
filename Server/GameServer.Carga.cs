using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// REUNIR ENERGIA -- a tecla C segurada. O motor mora no <see cref="CargaDeKi"/> (Core, porque e
/// formula do jogo); aqui fica o encanamento: quem pode, quando para, o que o jogador ve e ouve.
///
/// ============================ A TECLA C FAZ TRES COISAS ============================
/// O dono corrigiu uma suposicao minha: "o C nao e so pra transformar". No original ele e:
///
///   segurar          reunir energia (`Draw_Energy` repetindo enquanto a tecla esta em baixo)
///   tocar duas vezes tentar transformar (`dblclk>=2` -> `Transformations_Activate`, Meditate.dm:188)
///   soltar           parar (`Stop_Draw_Energy`)
///
/// Eu tinha portado so o do meio. As outras duas sao a MAIOR parte do uso: o power-up e o gesto
/// mais repetido do anime, e sem ele o Ki so voltava sozinho, devagar, sem nada pra fazer.
/// ===================================================================================
///
/// POR QUE A CARGA MORRE SOZINHA EM VARIAS SITUACOES. Nao ha "cancelar carga" no DM -- o que ha
/// sao condicoes que deixam de valer. Aqui elas estao juntas em <see cref="PararCarga"/> pra que
/// nao exista um caminho que desligue o estado e esqueca a aura, ou vice-versa: som ligado sem
/// carga acontecendo e o tipo de defeito que ninguem reporta e todo mundo sente.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O jogador apertou ou soltou C.
	///
	/// A RECUSA E MUDA quando falta Ki Unlocked, e isso e do original: `Energy_Draw` comeca com
	/// `if(!MeditateGivesKiRegen) return`, sem mensagem. Mas aqui ela FALA uma vez -- o jogo do
	/// BYOND tinha um painel de skills onde dava pra ver que faltava a skill; este nao tem esse
	/// painel na mao do novato, e uma tecla que nao faz nada nem explica e um bug aos olhos de
	/// quem joga.
	/// </summary>
	private void Carregar(ServerPlayer pl, bool ligado)
	{
		if (!ligado) { PararCarga(pl); return; }
		if (pl.Carregando) return;

		if (!CargaDeKi.SabeReunir(pl.Ficha))
		{
			Avisar(pl, "você não sabe reunir a própria energia -- é o que Ki Unlocked ensina.");
			return;
		}

		if (pl.Ficha.KO || pl.Ficha.dead) return;

		pl.Carregando = true;
		pl.EstagioDaCarga = EstagioDaCarga.Nada;

		// ============================ DUAS COISAS DIFERENTES ============================
		// `carregando` e ESTADO: o Ki esta subindo. Abre so com Ki Unlocked.
		// `aura_carga` e VISUAL: o desenho e o zumbido. No original ele mora dentro de
		//   `else if(canPower && stamina > 1)` (Meditate.dm:181) -- ou seja quem so destravou o ki
		//   junta energia EM SILENCIO, e a aura e sinal de que ha controle de verdade.
		//
		// Amarrar os dois num efeito so obrigaria a escolher entre o jogador ver aura que nao
		// merece ou nao ter retorno nenhum de que o Ki esta subindo. Separados, cada um responde
		// pelo que e.
		//
		// (O DM tem um furo aqui que NAO se copia: o `poweruprunning = 1` da linha 193 fica FORA
		// do gate, entao la o zumbido escapa pra quem nao tem canPower. Reproduzir isso devolveria
		// metade da queixa do dono -- som pra quem o servidor recusou.)
		// ================================================================================
		pl.AuraDaCarga = CargaDeKi.TemControle(pl.Ficha) && pl.Ficha.stamina > 1;

		MandarEfeito(pl, "carregando", -1);
		if (pl.AuraDaCarga)
		{
			MandarEfeito(pl, "carga_inicio", 0);
			MandarEfeito(pl, "aura_carga", -1);
		}
		else
		{
			Avisar(pl, "você junta energia, mas ainda não sabe COMPRIMI-la além do que cabe.");
		}
	}

	/// <summary>
	/// PARA DE CARREGAR -- soltar a tecla, cair, morrer, mudar de zona, deslogar.
	///
	/// UM CAMINHO SO. Toda saida passa por aqui de proposito: o estado tem tres partes (a flag, a
	/// aura e o efeito no cliente) e desligar duas delas deixa a terceira presa. A aura de EXCESSO
	/// nao apaga aqui -- ela depende do Ki, nao da tecla, e quem esta a 130% continua brilhando
	/// depois de soltar o C, que e o certo.
	/// </summary>
	public void PararCarga(ServerPlayer pl)
	{
		if (!pl.Carregando) return;
		pl.Carregando = false;
		pl.EstagioDaCarga = EstagioDaCarga.Nada;
		MandarEfeito(pl, "carregando", 0);

		// A AURA DA CARGA morre com a tecla. A do EXCESSO nao -- ela depende do Ki e continua
		// acesa em quem passou dos 100%, o que e o certo e e por isso que sao duas.
		if (!pl.AuraDaCarga) return;
		pl.AuraDaCarga = false;
		MandarEfeito(pl, "aura_carga", 0);
	}

	/// <summary>
	/// O TIQUE. Roda pra todo mundo -- inclusive pra quem NAO esta carregando, porque o preco de
	/// ter passado dos 100% continua sendo cobrado depois que a tecla e solta. Foi isso que fez a
	/// funcao ter duas metades: uma so pra quem segura C, outra pra quem tem Ki demais.
	/// </summary>
	private void TickDaCarga(ServerPlayer pl, double dt)
	{
		Fighter f = pl.Ficha;

		// ============================ EM CENA O TANQUE NAO SE MEXE SOZINHO ============================
		// Ver `GameServer.Formas.EmCena`. Congelar SO o dreno da forma seria trocar uma mentira por
		// outra: o Ki SUBIRIA durante a cinematica, e o jogador sairia da propria estreia com mais
		// energia do que entrou. O honesto e o tanque nao andar -- pra nenhum lado.
		//
		// A CARGA (tecla C) PARA JUNTO porque ela e o mesmo tanque por outra porta: o corpo esta preso,
		// mas so o MOVIMENTO e bloqueado no cliente (`LocalPlayer`: "SO O MOVIMENTO E BLOQUEADO"),
		// entao segurar C durante a cena continuaria enchendo o Ki -- e o `extracharge` acumulado
		// voltaria como Ki depois, pelo troco do `RegenDeKi`. Suspensa, e nao cancelada: e o mesmo
		// tratamento que andar ja recebe, e retomar sozinho e o que evita brigar com o controle.
		//
		// O QUE **NAO** PARA E O PRECO DO EXCESSO, la embaixo. Ele e DANO, e dano tem que continuar
		// correndo pra que o nocaute no meio da cena possa acontecer (e desfazer a forma). O vazamento
		// de Ki que ele cobra nao fura o congelamento pelo que ele e: ele so morde acima de 100% e para
		// no proprio 100%, entao nao ha como ele derrubar a forma, que e o que este bloco protege.
		// ========================================================================================
		bool emCena = EmCena(pl);

		// A ENERGIA VOLTANDO SOZINHA. Roda ANTES da carga: o `KiRegen()` do original tambem vem
		// antes do `CheckPowerMod()` no `statify`, e a ordem importa -- a regeneracao para no
		// teto de MaxKi, e so depois a tecla C empurra dali pra cima.
		//
		// O DRENO DA FORMA entra como parametro porque quem conhece a escada e este arquivo, nao
		// o Core: forma nao masterizada derruba a regeneracao a 20% pra que o dreno fique liquido
		// negativo. Sem isso a regeneracao passa o dreno e o Super Saiyajin nunca cai.
		double drenoDaForma = pl.Forma.NaBase ? 0 : pl.Forma.DrenoPorSegundo();
		if (!emCena) RegenDeKi.Passo(f, dt, drenoDaForma);

		if (pl.Carregando)
		{
			// O NOCAUTE CONTINUA SENDO OUVIDO EM CENA -- ele fica FORA do `emCena` de proposito: cair
			// no meio da cinematica solta a tecla como sempre soltou.
			if (f.KO || f.dead) PararCarga(pl);
			else if (!emCena)
			{
				EstagioDaCarga e = CargaDeKi.Passo(f, dt, pl.Moving);
				AnunciarEstagio(pl, e);

				// ANDOU: a carga fica SUSPENSA (nao cancelada). A aura e o zumbido apagam na hora,
				// que e o que ensina a regra sem precisar de texto -- parou, volta sozinha. Cancelar
				// de vez obrigaria a soltar e reapertar o C a cada passo, e isso e briga com o
				// controle, nao regra de jogo.
				AjustarAura(pl, e != EstagioDaCarga.Andando && e != EstagioDaCarga.SemFolego);

				// SEM FOLEGO PARA A CARGA, e nao so a interrompe: e o freio natural do sistema.
				// Sem ele, segurar C indefinidamente seria energia de graca -- o custo e o
				// cansaco, e cansaco que nao para nada nao e custo.
				if (e == EstagioDaCarga.SemFolego)
				{
					PararCarga(pl);
					Avisar(pl, "o fôlego acaba e a energia se dispersa.");
				}
			}
		}

		// O PRECO DO EXCESSO, com ou sem tecla apertada.
		double dano = CargaDeKi.PrecoDoExcesso(f, dt);
		if (dano > 0) EspalharDanoDaCarga(pl, dano);

		// A AURA E DO KI, NAO DA TECLA. Ver CargaDeKi.AuraAcesa -- o limiar de acender (1,01) e
		// diferente do de apagar (1,0) justamente pra ela nao piscar em quem parou no limite.
		bool acesa = CargaDeKi.AuraAcesa(f, pl.AuraDeCarga);
		if (acesa == pl.AuraDeCarga) return;
		pl.AuraDeCarga = acesa;
		MandarEfeito(pl, "aura_ki", acesa ? -1 : 0);
	}

	/// <summary>
	/// Liga/desliga a AURA e o zumbido sem mexer no estado da carga.
	///
	/// Sao coisas separadas de proposito: `Carregando` diz "a tecla esta em baixo e o servidor
	/// aceitou"; isto diz "esta rendendo AGORA". Andar suspende o segundo e nao o primeiro.
	/// </summary>
	private void AjustarAura(ServerPlayer pl, bool rendendo)
	{
		bool quer = rendendo && CargaDeKi.TemControle(pl.Ficha) && pl.Ficha.stamina > 1;
		if (quer == pl.AuraDaCarga) return;
		pl.AuraDaCarga = quer;
		MandarEfeito(pl, "aura_carga", quer ? -1 : 0);
		if (quer) MandarEfeito(pl, "carga_inicio", 0);
	}

	/// <summary>
	/// Fala com o jogador SO quando o estagio muda de verdade.
	///
	/// Um aviso por tique seriam 30 linhas de chat por segundo -- e o chat e o unico lugar onde as
	/// coisas importantes aparecem. As tres frases sao as tres coisas distintas que estao
	/// acontecendo com o corpo, e elas dizem POR QUE a carga esta lenta ou rapida.
	/// </summary>
	private void AnunciarEstagio(ServerPlayer pl, EstagioDaCarga e)
	{
		if (e == pl.EstagioDaCarga) return;
		EstagioDaCarga antes = pl.EstagioDaCarga;
		pl.EstagioDaCarga = e;

		switch (e)
		{
			// so avisa uma vez por parada -- e a regra que o dono pediu, e ela precisa ser DITA
			case EstagioDaCarga.Andando when antes != EstagioDaCarga.Andando:
				Avisar(pl, "andando não dá pra reunir energia -- plante o pé.");
				break;

			case EstagioDaCarga.Retomando:
				Avisar(pl, "você reúne o que estava contendo e volta a expressar o próprio poder.");
				break;

			// O UNICO ESTAGIO QUE MERECE ALARDE: e o que da BP e o que machuca.
			case EstagioDaCarga.Ultrapassando when antes != EstagioDaCarga.Ultrapassando:
				Avisar(pl, "a energia passa do que o corpo comporta -- e o chão começa a tremer.");
				break;
		}
	}

	/// <summary>
	/// `SpreadDamage` da sobrecarga: o mesmo dano em cada membro, NAO-LETAL.
	///
	/// Nao-letal e escolha, e a mesma do Kaio-ken (ver `EspalharDanoG2`): comprimir ki demais
	/// derruba, nao mata. Matar por segurar uma tecla seria um jeito de morrer que o jogador nao
	/// tem como ver chegando -- o Ki nao esta na tela de ninguem sem scouter.
	/// </summary>
	private static void EspalharDanoDaCarga(ServerPlayer pl, double dano)
	{
		// CORPO INTOCAVEL NAO SE MACHUCA SEGURANDO A TECLA. Auto-infligido tambem entra no escudo da
		// cinematica pelo mesmo motivo do Kaio-ken (ver `EspalharDanoG2`): em cena o corpo esta preso,
		// o jogador nao consegue soltar nada, e um dano que ele nao pode evitar durante uma cena que
		// ele nao pode interromper e o "morri transformando e nao entendi por que" em pessoa.
		//
		// Antes do laco, e nao so no `Ferir`: o `DeveNocautear` la embaixo le o ESTADO do corpo.
		if (pl.Combate is not { Intocavel: false }) return;
		foreach (Jandirus.Core.Combat.BodyPart p in pl.Combate.Corpo.Partes.ToList())
			if (!p.Decepado && !p.Aninhado) pl.Combate.Ferir(p, dano, letal: false);
		pl.Combate.SincronizarVida();

		if (!pl.Ficha.KO && !pl.Ficha.dead && pl.Combate.Corpo.DeveNocautear())
			pl.Combate.Nocautear(MeleeResolver.SegundosDeNocaute);
	}
}
