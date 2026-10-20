using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DO FROST DEMON (`--frostteste`).
///
/// ============================ O QUE SO AQUI DA PRA MEDIR ============================
/// Tres coisas desta escada nao cabem numa bancada de Core, e as tres sao justamente as que
/// quebrariam caladas:
///
///   1. **O REPOUSO.** O Frost Demon e o unico corpo do jogo que nao descansa em `base`. Isso so
///      existe de verdade quando o login roda e escreve `Forma.Atual` -- uma bancada de Core que
///      chamasse `Catalogo.PisoDaEscada` sozinha estaria medindo a funcao, e nao o jogo. O modo de
///      falha e mudo e caro: o Mutante logaria em 1x sobre um BP quadruplicado.
///   2. **O VAZAMENTO NO FUNIL.** `fd_release` e campo do `Fighter` e some dentro do
///      `powerlevel()`; o que se quer provar e que `expressedBP` REALMENTE cai a um decimo. Isso
///      exige a conta de poder inteira rodando num corpo, que so o servidor tem (no cliente o BP
///      chega `NaN` pelo sigilo).
///   3. **O MOTOR.** Fusivel, travamento do ki, recuperacao e bateria sao QUATRO relogios que so
///      andam com `dt` de tique. Reproduzi-los na bancada seria medir a copia.
///
/// (2026-10-19) **E O MUTANTE COMO O DM O TEM** (familias 5 a 12): a classe que sai antes da tela de formas e a lista de corpos
/// casada pelo degrau, a lista torta de quem ja nasceu, as teclas C e X degrau a degrau, a cena vermelha do descontrole, o relogio
/// que rearma e a trava que a zona ve, a maestria do Mutante, o verb de re-escolher os corpos e o nocaute que mantem a forma -- pelos
/// funis de producao, e cada conserto com o seu defeito injetado ao lado. O que a TELA mostra disso tudo e da `--diagmutante`.
///
///     Godot --headless -- --server --frostteste
/// </summary>
public partial class GameServer
{
	private bool _frostDeTeste;

	/// <summary>
	/// Um corpo de Frost Demon montado a mao pra bancada: raca, classe, lista de corpos saneada e a
	/// forma de repouso posta pelo MESMO caminho do login (`Catalogo.IdDoPiso`).
	/// </summary>
	private void VestirDeFrost(ServerPlayer pl, string classe)
	{
		// "Icer" E NAO "Frost Demon": o primeiro e a raca do `races.json`, o segundo e a CLASSE comum
		// dentro dela (a outra e "Mutant Frost Demon"). Trocar as duas nao derrubaria nada aqui -- o
		// `EhFrost` aceita as duas grafias --, mas a bancada estaria montando um personagem que a
		// criacao nunca produz, e todo o resto da ficha (stats raciais, envelhecimento, berco) leria
		// uma raca que nao existe. Ver `FormasDeFrost.Raca`.
		pl.Race = FormasDeFrost.Raca;
		pl.Ficha.Race = pl.Race;
		pl.Ficha.Class = classe;
		pl.Visual.FormasDeFrost = FormasDeFrost.Sanear(classe, null);

		pl.Forma = new EstadoDeForma { Atual = Catalogo.IdDoPiso(Perfil(pl)) };
		pl.Ficha.fd_release = 1;
		pl.Ficha.fd_ki_locked = false;
		pl.FrostInstavelSegundos = 0;
		pl.FrostAvisoEmSegundos = 0;
		pl.Ficha.KO = pl.Ficha.dead = false;
		pl.Ficha.Ki = pl.Ficha.MaxKi;

		// ============================ A BANCADA NAO ASSISTE CINEMATICA ============================
		// `Transformar` marca `CenaSegundos` como em jogo, e cena PARA os tres relogios de Ki (o
		// dreno, a carga e este motor -- ver `GameServer.Formas.EmCena`). Num teste que avanca o
		// tempo em `TickDoFrost(0.1)` sem passar pelo `TickDaForma` (que e quem faz a cena escorrer),
		// os 2,5 s de estreia de uma supressao NUNCA acabariam e o motor ficaria congelado pra sempre
		// -- e a bancada passaria a medir o congelamento, verde.
		//
		// Zerar aqui e o que faz o resto do arquivo medir o motor, e nao a tranca. Quem prova que a
		// tranca existe e a `--formasteste`, que e a bancada dela.
		pl.CenaSegundos = 0;
		AplicarForma(pl);
	}

	/// <summary>Solta o corpo da cinematica -- ver o bloco no <see cref="VestirDeFrost"/>.</summary>
	private static void SemCena(ServerPlayer pl) => pl.CenaSegundos = 0;

	private void RodarBancadaDoFrost(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA AO VIVO DO FROST DEMON =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// GUARDA O QUE ERA, e devolve no fim: esta bancada TROCA A RACA de um personagem vivo.
		string racaAntes = pl.Race, classeAntes = pl.Ficha.Class;
		List<string> corposAntes = [.. pl.Visual.FormasDeFrost];
		EstadoDeForma formaAntes = pl.Forma;
		double bpAntes = pl.Ficha.BP;

		try
		{
			OCatalogoDoFrost(Checa);
			ORepousoDoFrost(pl, Checa);
			AEscadaDoFrost(pl, Checa);
			OMotorDoMutante(pl, Checa);
			// (2026-10-19) o Mutante como o DM o tem -- ver o cabecalho de cada familia
			ACriacaoDoMutante(Checa);
			AListaTortaDeQuemJaNasceu(Checa);
			AsTeclasCeXDoFrost(pl, Checa);
			ACenaDoDescontrole(pl, Checa);
			ORelogioEATravaDoMutante(pl, Checa);
			AMaestriaDoMutante(pl, Checa);
			OVerboDasFormas(pl, Checa);
			ONocauteDoFrost(pl, Checa);
		}
		// (2026-10-19) UMA EXCECAO NO MEIO VIRA FALHA, e o placar sai do mesmo jeito -- sem isto o lancador esperaria o prazo inteiro
		// por uma linha de veredito que nunca vinha.
		catch (Exception e) { Checa("a bancada correu ate o fim sem excecao", false, e.ToString()); }
		finally
		{
			pl.Race = racaAntes;
			pl.Ficha.Race = racaAntes;
			pl.Ficha.Class = classeAntes;
			pl.Visual.FormasDeFrost = corposAntes;
			pl.Forma = formaAntes;
			pl.Ficha.BP = bpAntes;
			pl.Ficha.fd_release = 1;
			pl.Ficha.fd_ki_locked = false;
			pl.FrostInstavelSegundos = 0;
			// (2026-10-19) e o que as familias 7 a 11 mexem: o relogio do aviso, a maestria ja vista, a cena e a carga
			pl.FrostAvisoEmSegundos = 0;
			pl.FrostMaestriaVista = -1;
			pl.CenaSegundos = 0;
			PararCarga(pl);
			AplicarForma(pl);
		}

		GD.Print($"===== FROST: {ok} OK, {falhou} FALHA(S) =====\n");
	}

	// =====================================================================
	// 1. O CATALOGO -- os numeros, e que eles saem de UMA fonte so
	// =====================================================================
	private static void OCatalogoDoFrost(Action<string, bool, string> Checa)
	{
		FormaDef[] linha = [.. Catalogo.DaLinha(LinhaDeForma.FrostDemon)];
		Checa("a linha tem os SETE degraus", linha.Length == 7, $"{linha.Length}");

		// A ORDEM E O `fd_form`. E dela que o cliente tira o slot do corpo -- se ela virar 10/20/30
		// um dia, o Frost Demon passa a desenhar o corpo errado e nada mais quebra.
		Checa("a Ordem de cada entrada E o fd_form (1..7)",
			  linha.Select(d => d.Ordem).SequenceEqual([1, 2, 3, 4, 5, 6, 7]),
			  string.Join(",", linha.Select(d => d.Ordem)));

		foreach (FormaDef d in linha)
		{
			int n = Catalogo.DegrauDoFrost(d);
			Checa($"{d.Id}: multiplicador {FormasDeFrost.Multiplicador(n)}x sai do FormasDeFrost",
				  Math.Abs(d.Mult[0] - FormasDeFrost.Multiplicador(n)) < 1e-9,
				  $"{d.Mult[0]}");
			Checa($"{d.Id}: veste o corpo que o jogador escolheu",
				  d.Corpo == CorpoDeForma.FrostEscolhido, d.Corpo.ToString());
			// SEM DRENO -- `IcerTransform.dm:12`: "Formas NAO mexem mais no pool de Ki".
			Checa($"{d.Id}: nao drena Ki", d.Dreno.All(x => x == 0), string.Join(",", d.Dreno));
			Checa($"{d.Id}: a chama continua a do jogador", Catalogo.ChamaDoJogador(d), "");
			Checa($"{d.Id}: tem cinematica PROPRIA",
				  Cinematicas.Para(d) is { } c && c.Forma == d.Id, "");
		}

		// AS QUATRO SUPRESSOES SAO RAMO LATERAL, e sem isso `Anterior(frost5)` devolveria a 4a Forma
		// e o Frost Demon NORMAL nao conseguiria estar na propria forma base.
		Checa("as quatro supressoes sao ForaDoTronco",
			  linha.Where(d => d.Ordem <= 4).All(d => d.ForaDoTronco), "");
		Checa("e o anterior da forma base e a BASE do jogo (nao a 4a Forma)",
			  Catalogo.IdAnterior(Catalogo.Def(Catalogo.IdDaBaseDoFrost)!) == Catalogo.IdBase,
			  Catalogo.IdAnterior(Catalogo.Def(Catalogo.IdDaBaseDoFrost)!));
		Checa("o anterior da 1a Evolucao e a forma base",
			  Catalogo.IdAnterior(Catalogo.Def("frost6")!) == Catalogo.IdDaBaseDoFrost, "");

		// AS PORTAS SAO OS `#define` DO ORIGINAL, e nao ha limiar pessoal (nao existe `RolarIcer`).
		Checa("1a Evolucao: 250 milhoes de BP", Catalogo.Def("frost6")!.PortaBp == 250_000_000, "");
		Checa("2a Evolucao: 15 bilhoes de BP", Catalogo.Def("frost7")!.PortaBp == 15_000_000_000, "");
		Checa("nenhuma das duas sorteia limiar pessoal",
			  Catalogo.Def("frost6")!.ChaveDoLimiar.Length == 0
			  && Catalogo.Def("frost7")!.ChaveDoLimiar.Length == 0, "");

		// ============================ AS DUAS EVOLUCOES SE ENSINAM, E SO ELAS ============================
		// O `mst_teachable` do original lista `frost6` e `frost7` (`MasterStudent.dm:390`). Aqui a
		// lista e DERIVADA -- e por isso vale a pena mede-la: uma regra nova em `Discipulado.Ensinavel`
		// pode tirar as duas de la sem ninguem tocar em nada do Frost Demon.
		Checa("o mestre ensina as duas evolucoes",
			  Jandirus.Core.Skills.Discipulado.EhEnsinavel("frost6")
			  && Jandirus.Core.Skills.Discipulado.EhEnsinavel("frost7"), "");
		Checa("e nao ensina supressao nem a forma base",
			  linha.Where(d => d.Ordem <= 5)
				   .All(d => !Jandirus.Core.Skills.Discipulado.EhEnsinavel(d.Id)), "");

		// A CHAVE UNICA DE MAESTRIA -- o `fd_base_mastery`. Os sete degraus tem que cair na mesma.
		Checa("os sete degraus compartilham UMA barra de maestria",
			  linha.All(d => Catalogo.ChaveDaMaestria(d.Id) == Catalogo.IdDaBaseDoFrost), "");
		Checa("e a barra de outra linha continua sendo a dela",
			  Catalogo.ChaveDaMaestria("ssj1") == "ssj1", "");

		// SUSTENTAR SO TREINA DA FORMA BASE PRA CIMA (`icer.dm:45`).
		Checa("supressao nao treina nada",
			  linha.Where(d => d.Ordem < 5).All(d => !Catalogo.SustentarTreina(d)), "");
		Checa("da forma base pra cima, treina",
			  linha.Where(d => d.Ordem >= 5).All(Catalogo.SustentarTreina), "");
	}

	// =====================================================================
	// 2. O REPOUSO -- e a concordancia entre aparencia de criacao e combate
	// =====================================================================
	private void ORepousoDoFrost(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		pl.Ficha.BP = 1e6;
		pl.Ficha.Statify();

		// --- NORMAL ---------------------------------------------------------
		VestirDeFrost(pl, FormasDeFrost.ClasseNormal);
		Checa("o Frost Demon normal descansa na FORMA BASE (e nao em `base`)",
			  pl.Forma.Atual == Catalogo.IdDaBaseDoFrost, pl.Forma.Atual);
		Checa("e o multiplicador dele e 1x", Math.Abs(pl.Ficha.ssjBuff - 1) < 1e-9,
			  $"{pl.Ficha.ssjBuff}");
		Checa("ele escolhe TRES corpos", pl.Visual.FormasDeFrost.Count == 3,
			  $"{pl.Visual.FormasDeFrost.Count}");

		// ============================ APARENCIA E COMBATE CONCORDAM POR CONSTRUCAO ============================
		// `VisualCatalog.CorpoSprite` serve `FormasDeFrost[0]` como "o corpo desta pessoa". A camada de
		// forma pergunta `SlotDoDegrau(degrau do repouso, quantos corpos)`. Se as duas nao derem ZERO,
		// o Frost Demon parado tem DUAS aparencias -- e a divergencia so apareceria olhando a tela.
		Checa("o slot do repouso do normal e o slot 0 -- o mesmo que o corpo desenha",
			  FormasDeFrost.SlotDoDegrau(Catalogo.DegrauDoFrost(pl.Forma.Def),
										 pl.Visual.FormasDeFrost.Count) == 0, "");

		// A ESCADA SAIYAJIN NAO E DELE. Este e o buraco que a sessao fechou: `LinhasAbertas` entregava
		// a linha Saiyajin pra qualquer um, e um Frost Demon com BP acima do `ssjat` viraria Super
		// Saiyajin de cabelo dourado por cima do corpo do Freeza.
		pl.Ficha.BP = 1e13;
		pl.Ficha.Statify();
		Checa("a escada Saiyajin e RECUSADA por linha fechada, com 1e13 de BP",
			  pl.Forma.Avaliar("ssj1", pl.Ficha.BP, 1, false, Perfil(pl)) == RecusaForma.LinhaFechada,
			  pl.Forma.Avaliar("ssj1", pl.Ficha.BP, 1, false, Perfil(pl)).ToString());
		Checa("e a linha aberta dele e a do Frost Demon, sozinha",
			  Catalogo.LinhasAbertas(Perfil(pl)).SetEquals([LinhaDeForma.FrostDemon]),
			  string.Join(",", Catalogo.LinhasAbertas(Perfil(pl))));

		// --- MUTANTE --------------------------------------------------------
		pl.Ficha.BP = 1e6;
		pl.Ficha.Statify();
		VestirDeFrost(pl, FormasDeFrost.ClasseMutante);
		Checa("o MUTANTE nasce lacrado na 1a supressao",
			  pl.Forma.Atual == "frost1", pl.Forma.Atual);
		Checa("e o poder dele vale 0,25x -- a casca cobra o BP quadruplicado de volta",
			  Math.Abs(pl.Ficha.ssjBuff - 0.25) < 1e-9, $"{pl.Ficha.ssjBuff}");
		Checa("ele escolhe SETE corpos", pl.Visual.FormasDeFrost.Count == 7,
			  $"{pl.Visual.FormasDeFrost.Count}");
		Checa("o slot do repouso do Mutante tambem e o 0",
			  FormasDeFrost.SlotDoDegrau(Catalogo.DegrauDoFrost(pl.Forma.Def),
										 pl.Visual.FormasDeFrost.Count) == 0, "");
		Checa("e a 2a Evolucao dele desenha o ultimo slot",
			  FormasDeFrost.SlotDoDegrau(7, 7) == 6, $"{FormasDeFrost.SlotDoDegrau(7, 7)}");

		// E O NORMAL NAO TEM SLOT PRA SUPRESSAO NENHUMA -- a lista dele nem chega la.
		Checa("o normal nao tem slot pras supressoes", FormasDeFrost.SlotDoDegrau(1, 3) < 0, "");
	}

	// =====================================================================
	// 3. A ESCADA -- subir com a porta, e recuar UM DEGRAU
	// =====================================================================
	private void AEscadaDoFrost(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		VestirDeFrost(pl, FormasDeFrost.ClasseNormal);

		// ABAIXO DA PORTA: nao sobe.
		pl.Ficha.BP = 1e8;                       // 100 milhoes, abaixo dos 250
		pl.Ficha.Statify();
		Checa("com 100 milhoes de BP a 1a Evolucao e recusada por PODER",
			  pl.Forma.Avaliar("frost6", pl.Ficha.BP, 1, false, Perfil(pl)) == RecusaForma.SemPoder,
			  pl.Forma.Avaliar("frost6", pl.Ficha.BP, 1, false, Perfil(pl)).ToString());

		pl.Ficha.BP = 3e8;                       // 300 milhoes: passa a 6 e nao a 7
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		Transformar(pl, subir: true);
		Checa("com 300 milhoes ele sobe pra 1a Evolucao", pl.Forma.Atual == "frost6",
			  pl.Forma.Atual);
		Medir(pl);
		Checa("e o multiplicador vira 10x", Math.Abs(pl.Ficha.ssjBuff - 10) < 1e-9,
			  $"{pl.Ficha.ssjBuff}");
		Checa("a 2a Evolucao continua fechada por poder",
			  pl.Forma.Avaliar("frost7", pl.Ficha.BP, 1, false, Perfil(pl)) == RecusaForma.SemPoder,
			  "");

		// ============================ RECUAR E **UM DEGRAU**, E NAO um salto ate o chao ============================
		// `revertIcer()` faz `fd_form--`. Pro normal, recuar da 1a Evolucao e voltar pra forma base --
		// que por acaso e o piso dele --, e recuar de novo nao faz nada (`if(fd_form <= fl) return`).
		Transformar(pl, subir: false);
		Checa("recuar da 1a Evolucao devolve a forma base", pl.Forma.Atual == Catalogo.IdDaBaseDoFrost,
			  pl.Forma.Atual);
		Transformar(pl, subir: false);
		Checa("e recuar de novo nao tira ninguem do proprio corpo",
			  pl.Forma.Atual == Catalogo.IdDaBaseDoFrost, pl.Forma.Atual);

		// ============================ 15 BILHOES: A FORMA BLACK -- E ELA E **DOIS** APERTOS ============================
		// Com BP de sobra pras duas evolucoes, a primeira tecla nao pula pra 2a: `Avaliar(frost7)`
		// cobra estar NA 1a Evolucao (`EstaEmOuAcimaDe`), e da forma base ele so alcanca a 6. E
		// literalmente o `next = fd_form + 1` do original -- a escada do Frost Demon se sobe degrau a
		// degrau, e um salto aqui seria a regra tendo sumido.
		pl.Ficha.BP = 2e10;
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		Transformar(pl, subir: true);
		Checa("com 20 bilhoes o PRIMEIRO aperto ainda para na 1a Evolucao",
			  pl.Forma.Atual == "frost6", pl.Forma.Atual);
		Transformar(pl, subir: true);
		Checa("e o segundo chega na 2a Evolucao", pl.Forma.Atual == "frost7", pl.Forma.Atual);
		Medir(pl);
		Checa("e ela vale 20x", Math.Abs(pl.Ficha.ssjBuff - 20) < 1e-9, $"{pl.Ficha.ssjBuff}");

		// --- O MUTANTE RECUA PELA ESCADA DE SUPRESSAO -----------------------
		VestirDeFrost(pl, FormasDeFrost.ClasseMutante);
		pl.Ficha.BP = 1e6;
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Forma.Entrar(Catalogo.IdDaBaseDoFrost);
		AplicarForma(pl);

		Transformar(pl, subir: false);
		Checa("o Mutante recua da forma base pra 4a Forma", pl.Forma.Atual == "frost4",
			  pl.Forma.Atual);
		Transformar(pl, subir: false);
		Checa("e da 4a pra 3a", pl.Forma.Atual == "frost3", pl.Forma.Atual);
		Medir(pl);
		Checa("com o multiplicador da casca (0,75x)", Math.Abs(pl.Ficha.ssjBuff - 0.75) < 1e-9,
			  $"{pl.Ficha.ssjBuff}");
		Transformar(pl, subir: false);
		Transformar(pl, subir: false);
		Checa("ate o fundo do poco -- e para la", pl.Forma.Atual == "frost1", pl.Forma.Atual);
		Transformar(pl, subir: false);
		Checa("recuar da 1a Forma nao existe", pl.Forma.Atual == "frost1", pl.Forma.Atual);

		// E O RESTO DO JOGO NAO MUDOU: descer do SSJ3 continua caindo direto na base, porque o degrau
		// abaixo dele (o SSJ2) vale 4x -- nao e casca. Ver `ParaOndeSeRecua`.
		Checa("nada disso mexeu com a escada Saiyajin: o recuo do SSJ3 continua sendo a base",
			  ParaOndeSeRecua(new EstadoDeForma { Atual = "ssj3" }, PerfilDeFormas.Comum)
				  == Catalogo.IdBase, "");
	}

	// =====================================================================
	// 4. O MOTOR DO MUTANTE -- fusivel, travamento, vazamento, bateria
	// =====================================================================
	private void OMotorDoMutante(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		VestirDeFrost(pl, FormasDeFrost.ClasseMutante);
		pl.Ficha.BP = 1e6;
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;

		Checa("com 0% de maestria ele so segura a 1a Forma",
			  FormasDeFrost.DegrauEstavel(0) == 1, "");
		Checa("com 50% ja segura a 3a", FormasDeFrost.DegrauEstavel(50) == 3, "");
		Checa("e so com 100% a forma base e dele",
			  FormasDeFrost.DegrauEstavel(100) == FormasDeFrost.Base, "");

		// --- O FUSIVEL ------------------------------------------------------
		pl.Forma.Entrar(Catalogo.IdDaBaseDoFrost);
		AplicarForma(pl);
		SemCena(pl);
		Medir(pl);
		double bpInteiro = pl.Ficha.expressedBP;

		// 89 segundos: perto, mas antes.
		for (int i = 0; i < 890; i++) TickDoFrost(pl, 0.1);
		Checa("aos 89 s na forma base instavel o ki ainda esta na mao", !pl.Ficha.fd_ki_locked, "");

		// e mais dois: o fusivel de 90 s queima (`FD_LOSS_SECS_F5`, fator 1 na forma base, 0% de maestria)
		for (int i = 0; i < 20; i++) TickDoFrost(pl, 0.1);
		Checa("aos 91 s o ki SAI do controle", pl.Ficha.fd_ki_locked, "");

		// ============================ E A TECLA C MORRE ============================
		// A guarda mora no `CargaDeKi.Passo` (o `Power Control.dm:141` do original), e nao no tique do
		// servidor -- entao ela vale pra IA tambem. Sem isto o castigo seria so o vazamento, e
		// vazamento se compensa enchendo o tanque.
		Checa("e reunir energia deixa de funcionar",
			  Jandirus.Core.Combat.CargaDeKi.Passo(pl.Ficha, 0.1, mexendo: false)
				  == Jandirus.Core.Combat.EstagioDaCarga.Nada, "");

		// --- O VAZAMENTO, ATE O PISO ---------------------------------------
		for (int i = 0; i < 1000; i++) TickDoFrost(pl, 0.1);   // 100 s a 1,2%/s: passa do piso
		Checa("a liberacao despenca ate o piso de 10%",
			  Math.Abs(pl.Ficha.fd_release - FormasDeFrost.PisoDaLiberacao) < 1e-6,
			  $"{pl.Ficha.fd_release}");

		// ============================ E O FUNIL DE PODER JA CONSOME ISSO ============================
		// Esta e a checagem que nenhuma bancada de Core consegue fazer: `fd_release` desaparece dentro
		// do `powerlevel()`, na familia 3 ("aplicam no fim"). O que se mede aqui e o RESULTADO -- que
		// o numero que o dano, a guarda, o arremesso e o scouter leem caiu a um decimo.
		Medir(pl);
		Checa("e o poder EXPRESSO cai junto, a um decimo",
			  Math.Abs(pl.Ficha.expressedBP - bpInteiro * 0.1) <= bpInteiro * 0.02,
			  $"{pl.Ficha.expressedBP:N0} contra {bpInteiro * 0.1:N0}");

		// --- RECUAR RECUPERA -----------------------------------------------
		Transformar(pl, subir: false);   // base -> 4a Forma... que ele TAMBEM nao segura com 0%
		Transformar(pl, subir: false);
		Transformar(pl, subir: false);
		Transformar(pl, subir: false);   // ate a 1a, que ele segura sempre
		Checa("ele escapa recolhendo a casca ate o fundo", pl.Forma.Atual == "frost1",
			  pl.Forma.Atual);
		SemCena(pl);

		// 4%/s: do piso (10%) ao cheio sao 22,5 s. Um segundo nao basta -- e e isso que faz doer.
		for (int i = 0; i < 10; i++) TickDoFrost(pl, 0.1);
		Checa("um segundo de recuo NAO devolve o controle", pl.Ficha.fd_ki_locked,
			  $"{pl.Ficha.fd_release}");

		for (int i = 0; i < 240; i++) TickDoFrost(pl, 0.1);   // +24 s
		Checa("a liberacao volta ao cheio em ~22,5 s", pl.Ficha.fd_release >= 1,
			  $"{pl.Ficha.fd_release}");
		Checa("e ai sim o ki se acalma", !pl.Ficha.fd_ki_locked, "");
		Checa("o relogio de instabilidade zerou", pl.FrostInstavelSegundos == 0,
			  $"{pl.FrostInstavelSegundos}");

		// --- A MAESTRIA SO ANDA DA FORMA BASE PRA CIMA ----------------------
		pl.Forma.Maestria.Por(Catalogo.IdDaBaseDoFrost, 0);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		SemCena(pl);
		for (int i = 0; i < 300; i++) TickDaForma(pl, 0.1);    // 30 s na 1a Forma
		Checa("30 s recolhido na casca nao treinam nada",
			  pl.Forma.Maestria.De(Catalogo.IdDaBaseDoFrost) == 0,
			  $"{pl.Forma.Maestria.De(Catalogo.IdDaBaseDoFrost)}");

		pl.Forma.Entrar(Catalogo.IdDaBaseDoFrost);
		AplicarForma(pl);
		SemCena(pl);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		for (int i = 0; i < 300; i++) TickDaForma(pl, 0.1);
		Checa("30 s com o corpo inteiro treinam",
			  pl.Forma.Maestria.De(Catalogo.IdDaBaseDoFrost) > 0,
			  $"{pl.Forma.Maestria.De(Catalogo.IdDaBaseDoFrost)}");
		Checa("e a barra e a MESMA lida por qualquer degrau da linha",
			  Math.Abs(pl.Forma.Maestria.De("frost7")
					   - pl.Forma.Maestria.De(Catalogo.IdDaBaseDoFrost)) < 1e-9, "");

		// --- A BATERIA ------------------------------------------------------
		// 100% de maestria + supressao = regeneracao passiva por grau de casca. Na 1a Forma sao
		// quatro graus: 0,8% do MaxKi por segundo.
		pl.Forma.Maestria.Por(Catalogo.IdDaBaseDoFrost, 100);
		pl.Forma.Entrar("frost1");
		AplicarForma(pl);
		SemCena(pl);
		pl.Ficha.Ki = pl.Ficha.MaxKi * 0.5;
		double antes = pl.Ficha.Ki;
		for (int i = 0; i < 100; i++) TickDoFrost(pl, 0.1);    // 10 s -> +8% do MaxKi
		double ganho = (pl.Ficha.Ki - antes) / pl.Ficha.MaxKi;
		Checa("com a base dominada, a 1a Forma vira bateria (~8% do Ki em 10 s)",
			  Math.Abs(ganho - 0.08) < 0.005, $"{ganho:P2}");

		// E ELA NAO ULTRAPASSA O TANQUE: isto e regeneracao passiva, nao power-up.
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		for (int i = 0; i < 100; i++) TickDoFrost(pl, 0.1);
		Checa("e ela para no tanque cheio", pl.Ficha.Ki <= pl.Ficha.MaxKi + 1e-6,
			  $"{pl.Ficha.Ki} / {pl.Ficha.MaxKi}");

		// E COM 100% ELE SEGURA A FORMA BASE PRA SEMPRE -- o fusivel nem arma.
		pl.Forma.Entrar(Catalogo.IdDaBaseDoFrost);
		AplicarForma(pl);
		SemCena(pl);
		for (int i = 0; i < 2000; i++) TickDoFrost(pl, 0.1);   // 200 s, mais que o dobro do fusivel
		Checa("com a base dominada, 200 s nela nao tiram o controle de ninguem",
			  !pl.Ficha.fd_ki_locked && pl.Ficha.fd_release >= 1, "");

		// --- O NORMAL NAO TEM MOTOR NENHUM ---------------------------------
		VestirDeFrost(pl, FormasDeFrost.ClasseNormal);
		pl.Ficha.BP = 2e10;
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		Transformar(pl, subir: true);
		SemCena(pl);
		for (int i = 0; i < 3000; i++) TickDoFrost(pl, 0.1);   // 300 s na 2a Evolucao
		Checa("o Frost Demon NORMAL nao perde o controle de nada, nunca",
			  !pl.Ficha.fd_ki_locked && pl.Ficha.fd_release >= 1
			  && pl.FrostInstavelSegundos == 0, $"{pl.Forma.Atual}");
	}

	// =====================================================================
	// 5. (2026-10-19) A CRIACAO DO MUTANTE -- a lista casada pelo degrau, e a classe que sai antes da tela
	// =====================================================================
	/// <summary>
	/// O dono: *"frost demon mutante deveria ter como escolher todas as transformaçoes na criaçao de personagem"*. As duas metades, pelas
	/// funcoes de producao: o `Sanear` (a lista se casa pelo DEGRAU que o tamanho dela diz) e a classe da VAGA -- sorteada uma vez e
	/// gravada (`ClasseDoFrostDaVaga`), entregue pela lista de vagas (`SlotsVisiveisDe`, o mesmo codigo que o `MandarSlots` embrulha) e
	/// gasta pelo nascimento (`GastarAClasseDoFrost` + `Nascer`, a dupla do `CreateChar`, na mesma ordem). A conta e de mentira e mora
	/// num armazem de mentira: o sorteio da vaga GRAVA a conta. A criacao inteira, pela tela, e da `--diagmutante`.
	/// </summary>
	private void ACriacaoDoMutante(Action<string, bool, string> Checa)
	{
		// --- O SANEAR CASA PELO DEGRAU ---------------------------------------------
		string[] tres = [FormasDeFrost.Catalogo[10], FormasDeFrost.Catalogo[11], FormasDeFrost.Catalogo[12]];
		bool TresNoTopo(List<string> l) =>
			l.Count == FormasDeFrost.Total
			&& Enumerable.Range(1, 4).All(d => l[d - 1] == FormasDeFrost.PadraoDoDegrau(d))
			&& l[4] == tres[0] && l[5] == tres[1] && l[6] == tres[2];

		List<string> casada = FormasDeFrost.Sanear(FormasDeFrost.ClasseMutante, tres);
		Checa("os TRES corpos que chegam pra um Mutante vao pra forma base e as duas evolucoes -- e as quatro cascas ficam no padrao",
			  TresNoTopo(casada), string.Join(", ", casada));
		FormasDeFrost.CorposPorPosicaoDeTeste = true;
		try
		{
			List<string> torta = FormasDeFrost.Sanear(FormasDeFrost.ClasseMutante, tres);
			Checa("(defeito injetado: a lista casada pela POSICAO, o jogo de antes) a mesma regua REPROVA -- o corpo da base cai na 1a Forma",
				  !TresNoTopo(torta), string.Join(", ", torta));
		}
		finally { FormasDeFrost.CorposPorPosicaoDeTeste = false; }

		string[] sete = [.. FormasDeFrost.Catalogo.Take(FormasDeFrost.Total)];
		List<string> doNormal = FormasDeFrost.Sanear(FormasDeFrost.ClasseNormal, sete);
		Checa("...e os SETE que chegam pra um Frost Demon normal: ele fica com os tres de cima (a base e as evolucoes)",
			  doNormal.SequenceEqual(sete[4..]), string.Join(", ", doNormal));
		Checa("...e a lista do tamanho certo volta igual, nas duas classes",
			  FormasDeFrost.Sanear(FormasDeFrost.ClasseMutante, sete).SequenceEqual(sete)
			  && FormasDeFrost.Sanear(FormasDeFrost.ClasseNormal, tres).SequenceEqual(tres), "");

		// --- A CLASSE DA VAGA ----------------------------------------------------------
		if (_racas == null || _store == null) { Checa("o catalogo de racas e o armazem de contas existem", false, ""); return; }
		string caixa = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
			"jandirus-frostteste-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
		AccountStore real = _store;
		try
		{
			System.IO.Directory.CreateDirectory(caixa);
			_store = new AccountStore(caixa);
			var acc = new AccountSave { Conta = "bancada_frost_vaga" };

			string sorteada = ClasseDoFrostDaVaga(acc, 1);
			Checa("a vaga sorteia a classe do Frost Demon pelo `Class_Spread` da raca (Frost Demon ou Mutante)",
				  sorteada is FormasDeFrost.ClasseNormal or FormasDeFrost.ClasseMutante, sorteada);
			bool aMesma = Enumerable.Range(0, 20).All(_ => ClasseDoFrostDaVaga(acc, 1) == sorteada);
			Checa("...UMA vez: perguntar de novo (reabrir a criacao, relogar) devolve a mesma, e ela fica na conta",
				  aMesma && acc.ClassesDoFrost[1] == sorteada, $"{acc.ClassesDoFrost[1]}");
			Checa("...e vai pro DISCO com a conta (o servidor do 'Hospedar' reiniciado nao sorteia de novo)",
				  _store.Carregar(acc.Conta)?.ClassesDoFrost is { Length: > 1 } gravadas && gravadas[1] == sorteada, "");

			// O QUE A TELA RECEBE -- pelo metodo que o `MandarSlots` embrulha, e nao relendo o array.
			acc.Slots[0] = new CharacterSave
			{
				Nome = "Ocupado", Raca = FormasDeFrost.Raca,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = FormasDeFrost.Raca, Class = FormasDeFrost.ClasseMutante },
			};
			acc.ClassesDoFrost[2] = FormasDeFrost.ClasseMutante;   // o 1% que a bancada nao pode esperar sair
			Jandirus.Net.SlotInfo[] vagas = SlotsVisiveisDe(acc);
			Checa("a lista de vagas leva a classe de cada vaga VAZIA (a criacao abre com sete corpos pro Mutante e tres pro normal)",
				  vagas[1].ClasseDoFrost == sorteada && vagas[2].ClasseDoFrost == FormasDeFrost.ClasseMutante,
				  $"{vagas[1].ClasseDoFrost} / {vagas[2].ClasseDoFrost}");
			Checa("...e a vaga OCUPADA nao diz classe nenhuma (a do personagem que existe continua segredo)",
				  vagas[0].ClasseDoFrost.Length == 0 && vagas[0].Classe.Length == 0, $"'{vagas[0].ClasseDoFrost}' / '{vagas[0].Classe}'");
			acc.Slots[0] = null;

			acc.ProximaClasseRara = true;
			string comGarantia = SlotsVisiveisDe(acc)[1].ClasseDoFrost;
			acc.ProximaClasseRara = false;
			Checa("...e com a classe rara garantida pelo admin a vaga mostra o MUTANTE (no nascimento a garantia passa na frente)",
				  comGarantia == FormasDeFrost.ClasseMutante, comGarantia);

			// O NASCIMENTO GASTA A CLASSE QUE A TELA MOSTROU -- a dupla do `CreateChar`, na mesma ordem.
			var ficha = new CharacterDraft { Name = "Gelado", Race = FormasDeFrost.Raca, Age = 20 };
			Jandirus.Core.Stats.Fighter nasceu = Nascer(ficha, "Gelado", GastarAClasseDoFrost(acc, 2, ficha));
			Checa("o Frost Demon que nasce na vaga do Mutante NASCE Mutante -- e a vaga esvazia, pro proximo sortear de novo",
				  nasceu.Class == FormasDeFrost.ClasseMutante && acc.ClassesDoFrost[2] == null,
				  $"{nasceu.Class}, vaga {acc.ClassesDoFrost[2] ?? "vazia"}");
			acc.ClassesDoFrost[0] = FormasDeFrost.ClasseMutante;
			string outraRaca = GastarAClasseDoFrost(acc, 0, new CharacterDraft { Name = "Kuririn", Race = "Human", Age = 20 });
			Checa("...e quem nasce de OUTRA raca nao gasta a classe da vaga",
				  outraRaca.Length == 0 && acc.ClassesDoFrost[0] == FormasDeFrost.ClasseMutante, $"'{outraRaca}'");

			ClasseDoFrostSorteadaDeNovoDeTeste = true;
			try
			{
				var classes = new List<string>();
				for (int i = 0; i < 20; i++)
				{
					acc.ClassesDoFrost[2] = FormasDeFrost.ClasseMutante;
					var f = new CharacterDraft { Name = "Gelado", Race = FormasDeFrost.Raca, Age = 20 };
					classes.Add(Nascer(f, "Gelado", GastarAClasseDoFrost(acc, 2, f)).Class);
				}
				Checa("(defeito injetado: o nascimento sorteia a classe de novo, o jogo de antes) a mesma regua REPROVA -- a vaga do Mutante nasce Frost Demon comum",
					  !classes.All(c => c == FormasDeFrost.ClasseMutante), string.Join(", ", classes.Distinct()));
			}
			finally { ClasseDoFrostSorteadaDeNovoDeTeste = false; }
		}
		finally
		{
			_store = real;
			try { System.IO.Directory.Delete(caixa, recursive: true); } catch { /* temporario */ }
		}
	}

	// =====================================================================
	// 6. (2026-10-19) A LISTA TORTA DE QUEM JA NASCEU -- desentortada uma vez, no login
	// =====================================================================
	/// <summary>
	/// O Mutante criado antes do conserto tem a lista que o `Sanear` POR POSICAO deu: os tres corpos que ele escolheu (a base e as duas
	/// evolucoes) nas tres primeiras cascas, e os padroes dos degraus 4 a 7 por cima -- o caso do dono, achado pela sessao do corpo do
	/// Frost. O login a desentorta UMA vez (`CharacterSave.CorposVersao`), no funil unico save -> jogador (`AccountStore.ParaJogador`),
	/// que e por onde a bancada passa.
	/// </summary>
	private static void AListaTortaDeQuemJaNasceu(Action<string, bool, string> Checa)
	{
		string[] escolhidos = [FormasDeFrost.Catalogo[20], FormasDeFrost.Catalogo[21], FormasDeFrost.Catalogo[22]];
		List<string> torta = [.. escolhidos, .. Enumerable.Range(4, 4).Select(FormasDeFrost.PadraoDoDegrau)];
		List<string> certa = [.. Enumerable.Range(1, 4).Select(FormasDeFrost.PadraoDoDegrau), .. escolhidos];
		List<string> padrao = [.. Enumerable.Range(1, FormasDeFrost.Total).Select(FormasDeFrost.PadraoDoDegrau)];

		static List<string> Logar(IReadOnlyList<string> corpos, int versao, string classe)
		{
			var s = new CharacterSave
			{
				Nome = "Gelado", Raca = FormasDeFrost.Raca, CorposVersao = versao,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = FormasDeFrost.Raca, Class = classe },
				Visual = new Jandirus.Core.Appearance.Appearance { FormasDeFrost = [.. corpos] },
			};
			var quem = new ServerPlayer();
			AccountStore.ParaJogador(s, quem);
			return quem.Visual.FormasDeFrost;
		}

		List<string> logada = Logar(torta, CharacterSave.VersaoDasListasPorRaca, FormasDeFrost.ClasseMutante);
		Checa("o Mutante de antes do conserto loga com os tres corpos de volta na base e nas evolucoes, e as cascas no padrao",
			  logada.SequenceEqual(certa), string.Join(", ", logada));
		Checa("...UMA vez: o save que ja passou pela versao de agora fica como esta (a mesma lista pode ser a escolha de um Mutante novo)",
			  Logar(torta, CharacterSave.VersaoDosCorpos, FormasDeFrost.ClasseMutante).SequenceEqual(torta), "");
		Checa("...a lista inteira de padrao nao e torta (e a de quem nao escolheu nada)",
			  Logar(padrao, 0, FormasDeFrost.ClasseMutante).SequenceEqual(padrao), "");
		Checa("...e o Frost Demon NORMAL nao e mexido",
			  Logar(escolhidos, 0, FormasDeFrost.ClasseNormal).SequenceEqual(escolhidos), "");

		ListaTortaFicaDeTeste = true;
		try
		{
			List<string> ficou = Logar(torta, CharacterSave.VersaoDasListasPorRaca, FormasDeFrost.ClasseMutante);
			Checa("(defeito injetado: o login nao desentorta, o jogo de antes) a mesma regua REPROVA -- o corpo da base continua na 1a Forma",
				  !ficou.SequenceEqual(certa), string.Join(", ", ficou));
		}
		finally { ListaTortaFicaDeTeste = false; }

		// A VERSAO SUBIU PRA 2 POR CAUSA DO MUTANTE -- e a migracao do Androide (2026-10-18) continua sendo da versao 1: o convertido
		// de antes anda um no corpo UMA vez, e quem ja passou pela 1 nao anda de novo so porque o numero mudou.
		static int CorpoDoAndroide(int versao)
		{
			var s = new CharacterSave
			{
				Nome = "Dezessete", Raca = "Android", CorposVersao = versao,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = "Android", AndroideInfinito = true },
				Visual = new Jandirus.Core.Appearance.Appearance { Corpo = 3 },
			};
			var quem = new ServerPlayer();
			AccountStore.ParaJogador(s, quem);
			return quem.Visual.Corpo;
		}
		Checa("(controle) o Androide convertido anda um no corpo UMA vez: o save da versao 0 anda, o da 1 ja nao (a subida pra 2 e do Mutante)",
			  CorpoDoAndroide(0) == 4 && CorpoDoAndroide(CharacterSave.VersaoDasListasPorRaca) == 3,
			  $"{CorpoDoAndroide(0)} / {CorpoDoAndroide(CharacterSave.VersaoDasListasPorRaca)}");
	}

	// =====================================================================
	// 7. (2026-10-19) AS TECLAS C E X -- a escada inteira, um degrau por aperto
	// =====================================================================
	/// <summary>
	/// O dono: *"apertar C duas vezes como frost ele deve ir pro proxima transformaçao e na primeira vez com a cinematica, e apertar X
	/// segue a logica das outras transformaçoes, se o ki estiver acima de 100% ele volta pra 100% e se estiver em 100% ou abaixo ele
	/// volta pra forma anterior dele, no caso de mutante ele vai voltando nas formas reduzidas ate a forma mais reduzida"*.
	///
	/// PELOS DOIS FUNIS QUE AS TECLAS CHAMAM: o C duplo e o `Transformar(subir: true)` (o `C2S.Transformar` que o `LocalPlayer` manda) e
	/// o X e o `ReverterOuLiberar`. Nas tres preferencias de grade -- ligados (o jogador de sempre), nulo (corpo sem dono) e desligados
	/// (o verb `graus`) --, porque o seletor do C muda com ela. A cena de cada subida e da familia 8; aqui cada aperto a solta.
	/// </summary>
	private void AsTeclasCeXDoFrost(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		string Subir()
		{
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			Transformar(pl, subir: true);
			SemCena(pl);
			return pl.Forma.Atual;
		}
		string Descer()
		{
			ReverterOuLiberar(pl);
			SemCena(pl);
			return pl.Forma.Atual;
		}
		static string Apertos(int vezes, Func<string> tecla) => string.Join(" > ", Enumerable.Range(0, vezes).Select(_ => tecla()));
		void Mutante(bool? grades, double bp)
		{
			VestirDeFrost(pl, FormasDeFrost.ClasseMutante);
			pl.Forma.GradesLigados = grades;
			pl.Ficha.BP = bp;
			pl.Ficha.Statify();
			pl.Ficha.Ki = pl.Ficha.MaxKi;
		}

		// --- O MUTANTE ABRE A CASCA UMA POR VEZ ------------------------------------------
		foreach (bool? grades in new bool?[] { true, null, false })
		{
			Mutante(grades, 1e6);
			string subida = Apertos(5, Subir);
			Checa($"(grades {(grades is null ? "sem dono" : grades.Value ? "ligados" : "desligados")}) cada C duplo do Mutante abre UMA casca: "
				  + "2a, 3a, 4a, forma base -- e ali para (1 milhao de BP nao abre a 1a Evolucao)",
				  subida == "frost2 > frost3 > frost4 > frost5 > frost5", subida);
		}

		Mutante(true, 1e6);
		RecusaForma pedida = pl.Forma.Avaliar(Catalogo.IdDaBaseDoFrost, pl.Ficha.BP, 1, false, Perfil(pl));
		Checa("...e a forma base pedida direto da 1a Forma e recusada FORA DE ORDEM (a tecla de forma e a aba Formas passam pelo mesmo `Avaliar`)",
			  pedida == RecusaForma.ForaDeOrdem, pedida.ToString());
		EstadoDeForma.PulaACascaDeTeste = true;
		try
		{
			string pulo = Subir();
			Checa("(defeito injetado: a casca de baixo nao e cobrada, o jogo de antes) a mesma regua REPROVA -- o primeiro C pula da 1a Forma direto pra forma base",
				  pulo != "frost2", pulo);
		}
		finally { EstadoDeForma.PulaACascaDeTeste = false; }

		Mutante(false, 1e6);
		EstadoDeForma.CascaSaiDoCaminhoDeTeste = true;
		try
		{
			string preso = Subir();
			Checa("(defeito injetado: com os grades desligados as cascas saem do caminho do C) a mesma regua REPROVA -- o Mutante fica preso na 1a Forma",
				  preso != "frost2", preso);
		}
		finally { EstadoDeForma.CascaSaiDoCaminhoDeTeste = false; }

		// --- COM PODER DE SOBRA, A ESCADA INTEIRA ------------------------------------------
		Mutante(true, 2e10);
		string inteira = Apertos(7, Subir);
		Checa("com 20 bilhoes de BP o Mutante sobe a escada INTEIRA, um degrau por C duplo, e para na 2a Evolucao",
			  inteira == "frost2 > frost3 > frost4 > frost5 > frost6 > frost7 > frost7", inteira);

		// --- O X: PRIMEIRO O KI, DEPOIS O DEGRAU ----------------------------------------------
		pl.Ficha.Ki = pl.Ficha.MaxKi * 1.5;
		string liberou = Descer();
		Checa("o X com o Ki ACIMA de 100% so devolve o Ki aos 100% -- e o corpo fica na forma",
			  liberou == "frost7" && Math.Abs(pl.Ficha.Ki - pl.Ficha.MaxKi) <= 1e-9 * pl.Ficha.MaxKi,
			  $"{liberou}, Ki {pl.Ficha.Ki:0}/{pl.Ficha.MaxKi:0}");
		string descida = Apertos(7, Descer);
		Checa("...e com o Ki em 100% cada X volta UM degrau: as evolucoes, a forma base e as cascas, ate a mais reduzida -- e la para",
			  descida == "frost6 > frost5 > frost4 > frost3 > frost2 > frost1 > frost1", descida);

		Apertos(6, Subir);
		ReverterVaiAoPisoDeTeste = true;
		try
		{
			string salto = Descer();
			Checa("(controle, defeito injetado: o X salta direto ao piso) a mesma regua REPROVA -- da 2a Evolucao ele cai na 1a Forma",
				  salto != "frost6", salto);
		}
		finally { ReverterVaiAoPisoDeTeste = false; }

		// --- E O NORMAL: duas evolucoes, e a forma base dele e o chao ---------------------------
		VestirDeFrost(pl, FormasDeFrost.ClasseNormal);
		pl.Forma.GradesLigados = true;
		pl.Ficha.BP = 2e10;
		pl.Ficha.Statify();
		string normal = Apertos(3, Subir) + " | " + Apertos(3, Descer);
		Checa("o Frost Demon NORMAL: C, C -- a 1a e a 2a Evolucao; X, X -- de volta a forma base, e dali o X nao desce",
			  normal == "frost6 > frost7 > frost7 | frost6 > frost5 > frost5", normal);
	}

	// =====================================================================
	// 8. (2026-10-19) A CENA DO DESCONTROLE -- a grande vermelha, o surto e o aviso
	// =====================================================================
	/// <summary>
	/// O `Frost_Demon_Forms` do DM (`IcerTransform.dm:97-107`): o Mutante que sobe pra uma forma que NAO segura ganha a cena grande
	/// VERMELHA na primeira vez, o surto vermelho nas seguintes, e o aviso de que aquilo e demais pra segurar. O servidor decide no
	/// `AnunciarForma` e marca o prazo da cena (`CenaSegundos`) pela mesma escolha que o cliente faz com o bit -- e e esse prazo que a
	/// bancada le: os 28 s sao da cena grande, os ~2 s do surto valem pra qualquer casca. O que a tela mostra e da `--diagmutante`.
	/// </summary>
	private void ACenaDoDescontrole(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		var anuncios = new List<(int Quem, string De, string Para, DegrauDeCena Degrau)>();
		EscutaDeAnuncios = anuncios;
		try
		{
			(string Forma, double Cena, DegrauDeCena Degrau, List<string> Falas) Subir()
			{
				pl.Ficha.Ki = pl.Ficha.MaxKi;
				List<string> falas = Ouvir(() => Transformar(pl, subir: true));
				(string, double, DegrauDeCena, List<string>) r =
					(pl.Forma.Atual, pl.CenaSegundos, anuncios.Count > 0 ? anuncios[^1].Degrau : DegrauDeCena.Nenhuma, falas);
				SemCena(pl);
				return r;
			}
			void Mutante(double maestria)
			{
				VestirDeFrost(pl, FormasDeFrost.ClasseMutante);
				pl.Forma.GradesLigados = true;
				pl.Ficha.BP = 1e6;
				pl.Ficha.Statify();
				pl.Forma.Maestria.Por(Catalogo.IdDaBaseDoFrost, maestria);
			}
			double grande = Cinematicas.FrostDescontrole.SegundosPreso;
			// A CENA E O AVISO EM AFIRMACOES SEPARADAS: sao linhas diferentes do conserto (a escolha da cena no `MarcarCena`, o aviso no
			// `AoMudarFormaDoFrost`), e a contraprova por mutacao precisa saber qual das duas caiu.
			bool Grande((string Forma, double Cena, DegrauDeCena Degrau, List<string> Falas) s, string forma) =>
				s.Forma == forma && s.Degrau == DegrauDeCena.Estreia && Math.Abs(s.Cena - grande) < 1e-9;
			static bool Avisou((string Forma, double Cena, DegrauDeCena Degrau, List<string> Falas) s) => Disse(s.Falas, "demais para segurar");

			// 0% DE MAESTRIA: so a 1a Forma se segura.
			Mutante(0);
			var estreia = Subir();
			Checa($"o Mutante a 0% abrindo a 2a Forma pela PRIMEIRA vez: a cena e a do DESCONTROLE (a grande, vermelha, {grande:0} s)",
				  Grande(estreia, "frost2"), $"{estreia.Forma}, {estreia.Degrau}, cena {estreia.Cena:0.##} s");
			Checa("...e ele ouve que aquilo e demais pra segurar (a 2a Forma, com 0%)", Avisou(estreia), Ultimos(estreia.Falas));
			ReverterOuLiberar(pl);
			SemCena(pl);
			var surto = Subir();
			Checa("...e da SEGUNDA vez a cena encurta (o surto vermelho, e nao a estreia de novo)",
				  surto.Forma == "frost2" && surto.Degrau == DegrauDeCena.Curta && surto.Cena > 0 && surto.Cena < grande,
				  $"{surto.Degrau}, cena {surto.Cena:0.##} s");
			Checa("...e o aviso volta na segunda vez", Avisou(surto), Ultimos(surto.Falas));
			FormaDef f2 = Catalogo.Def("frost2")!;
			Checa("...e sao as duas cenas do descontrole que o bit escolhe: a grande na estreia, o surto na curta, nada na instantanea",
				  Cinematicas.NoDegrau(f2, DegrauDeCena.Estreia, instavel: true) == Cinematicas.FrostDescontrole
				  && Cinematicas.NoDegrau(f2, DegrauDeCena.Curta, instavel: true) == Cinematicas.FrostSurtoDoDescontrole
				  && Cinematicas.NoDegrau(f2, DegrauDeCena.Nenhuma, instavel: true) == null
				  && Cinematicas.NoDegrau(f2, DegrauDeCena.Estreia) == Cinematicas.Para(f2), "");

			// 30%: A 2a SE SEGURA -- a cena e a dela, sem aviso; a 3a, nao.
			Mutante(30);
			var estavel = Subir();
			Checa("com 30% de maestria a 2a Forma e ESTAVEL: a estreia e a cena de sempre dela, e ninguem avisa nada",
				  estavel.Forma == "frost2" && Math.Abs(estavel.Cena - Cinematicas.Frost2.SegundosPreso) < 1e-9
				  && !Disse(estavel.Falas, "demais para segurar"), $"cena {estavel.Cena:0.##} s | {Ultimos(estavel.Falas)}");
			var acima = Subir();
			Checa("...e a 3a, acima do que 30% seguram, volta a ser a grande vermelha", Grande(acima, "frost3"),
				  $"{acima.Forma}, {acima.Degrau}, cena {acima.Cena:0.##} s");
			Checa("...e com ela volta o aviso (a 3a nao se segura com 30%)", Avisou(acima), Ultimos(acima.Falas));

			// 60%: A PARTIR DE 50% A TRANSFORMACAO E INSTANTANEA -- e o aviso continua sendo verdade. A ESTREIA toca a cena inteira em
			// qualquer maestria (`Cinematicas.Degrau`), entao a 4a Forma sobe, desce e sobe de novo: quem vale e a SEGUNDA vez.
			Mutante(60);
			Subir();
			Subir();
			Subir();
			pl.Ficha.Ki = pl.Ficha.MaxKi;   // em 100% o X desce um degrau (acima, ele so devolveria o Ki aos 100%)
			ReverterOuLiberar(pl);
			SemCena(pl);
			var instantanea = Subir();
			Checa("com 60% a 4a Forma (acima do que ele segura) abre SEM cena, como toda transformacao a partir de 50% -- da segunda vez em diante",
				  instantanea.Forma == "frost4" && instantanea.Cena == 0, $"{instantanea.Forma}, cena {instantanea.Cena:0.##} s");
			Checa("...e o aviso vem do mesmo jeito, sem cena nenhuma", Avisou(instantanea), Ultimos(instantanea.Falas));

			Cinematicas.DescontroleSemCenaDeTeste = true;
			try
			{
				Mutante(0);
				var semCena = Subir();
				Checa("(defeito injetado: o bit do descontrole ignorado, o jogo de antes) a mesma regua REPROVA -- a 2a Forma abre com o surto de sempre, e calada",
					  !Grande(semCena, "frost2") && !Avisou(semCena), $"cena {semCena.Cena:0.##} s | {Ultimos(semCena.Falas)}");
			}
			finally { Cinematicas.DescontroleSemCenaDeTeste = false; }
		}
		finally { EscutaDeAnuncios = null; }
	}

	// =====================================================================
	// 9. (2026-10-19) O RELOGIO E A TRAVA -- e o que a zona, a tecla C e o painel veem dela
	// =====================================================================
	/// <summary>
	/// O motor da familia 4 ja estava aqui; o que faltava e o que o DM faz em volta dele: o relogio da forma instavel REARMA a cada
	/// troca de forma (`fd_unstable_since = 0`), a trava ACENDE e APAGA a aura vermelha na zona (`fd_menacing_red`), a tecla C com o ki
	/// travado nao acende a carga e diz por que (`Energy_Draw`), e o painel mostra a maestria e a liberacao (`HtmlUI.dm:197-200`).
	/// </summary>
	private void ORelogioEATravaDoMutante(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		double meditaAntes = pl.Ficha.MeditateGivesKiRegen;
		var viradas = new List<(int Quem, bool Aceso)>();
		EscutaDoDescontrole = viradas;
		try
		{
			void NaBase(double bp)
			{
				VestirDeFrost(pl, FormasDeFrost.ClasseMutante);
				pl.Forma.GradesLigados = true;
				pl.Ficha.BP = bp;
				pl.Ficha.Statify();
				pl.Forma.Entrar(Catalogo.IdDaBaseDoFrost);
				AplicarForma(pl);
				pl.Ficha.Ki = pl.Ficha.MaxKi;
			}
			void Correr(double segundos)
			{
				for (int i = 0; i < (int)Math.Round(segundos * 10); i++) TickDoFrost(pl, 0.1);
			}

			// --- O RELOGIO REARMA NA TROCA -----------------------------------------------
			bool Rearma()
			{
				NaBase(3e8);
				Correr(60);                       // 60 s na forma base: o fusivel dela e de 90
				pl.Ficha.Ki = pl.Ficha.MaxKi;
				Transformar(pl, subir: true);    // a 1a Evolucao: fusivel de 45 s
				SemCena(pl);
				Correr(30);
				return pl.Forma.Atual == "frost6" && !pl.Ficha.fd_ki_locked;
			}
			Checa("o relogio da forma instavel REARMA na troca: 60 s na forma base e mais 30 na 1a Evolucao (fusivel de 45 s) nao travam o ki",
				  Rearma(), $"{pl.Forma.Atual}, travado {pl.Ficha.fd_ki_locked}, relogio {pl.FrostInstavelSegundos:0.0} s");
			RelogioAtravessaATrocaDeTeste = true;
			try
			{
				Checa("(defeito injetado: o relogio atravessa a troca, o jogo de antes) a mesma regua REPROVA -- os 60 s da base mais os 30 passam dos 45",
					  !Rearma(), $"travado {pl.Ficha.fd_ki_locked}, relogio {pl.FrostInstavelSegundos:0.0} s");
			}
			finally { RelogioAtravessaATrocaDeTeste = false; }

			// --- A TRAVA ACENDE A AURA NA ZONA, E DERRUBA A CARGA ACESA -----------------------
			NaBase(1e6);
			pl.Ficha.MeditateGivesKiRegen = 1;   // sabe reunir energia (Ki Unlocked): a guarda que fica e a da trava
			Carregar(pl, ligado: true);
			bool carregava = pl.Carregando;
			viradas.Clear();
			Correr(91);                          // o fusivel de 90 s da forma base queima
			Checa("ao travar, a zona inteira e avisada pra ACENDER a aura vermelha no corpo dele",
				  pl.Ficha.fd_ki_locked && viradas.Contains((pl.Id, true)), string.Join(", ", viradas));
			Checa("...e o estado de forma dele (o que quem chega na zona recebe) leva a marca de descontrolado",
				  (MarcasDoFrost(pl, instavel: false) & Jandirus.Net.Protocol.MarcaDescontrolado) != 0, "");
			// O CORPO FORJADO DA `--diagforma` (`MedirAlcanceDoAnuncio`) anuncia forma SEM ficha: as marcas perguntam a raca antes de
			// ler o ki travado. Na primeira publicacao a ordem era a outra, e a `--diagforma` caiu num NullReference aqui.
			string semFicha;
			try { semFicha = MarcasDoFrost(new ServerPlayer { Id = pl.Id + 70_009, Peer = null, Name = "bancada: corpo sem ficha" }, instavel: false).ToString(); }
			catch (NullReferenceException) { semFicha = "NullReference"; }
			Checa("...e um corpo SEM ficha (o forjado da `--diagforma`) passa pelas marcas sem cair: a raca e perguntada antes da ficha",
				  semFicha == "0", semFicha);
			Checa("...e quem estava segurando C para de reunir no mesmo tique",
				  carregava && !pl.Carregando, $"carregava {carregava}, carregando {pl.Carregando}");

			// --- A TECLA C COM O KI TRAVADO -------------------------------------------------
			// A TECLA PARTE DO CORPO PARADO: quem apaga a carga acesa na trava e a afirmacao de cima, e sem esta linha uma carga que
			// sobrou dela faria o `Carregar` sair cedo, calado -- e a falha de uma linha do conserto cairia na conta da outra.
			PararCarga(pl);
			pl.FrostAvisoEmSegundos = 0;
			List<string> falas = Ouvir(() => Carregar(pl, ligado: true));
			Checa("com o ki travado a tecla C NAO acende a carga -- e ele ouve por que",
				  !pl.Carregando && Disse(falas, "DESCONTROLADO"), Ultimos(falas));
			CargaDoDescontroleAcendeDeTeste = true;
			try
			{
				Carregar(pl, ligado: true);
				Checa("(defeito injetado: a carga acende com o ki travado, o jogo de antes) a mesma regua REPROVA -- a aura e o zumbido acendem e o Ki nao sobe",
					  pl.Carregando, "");
			}
			finally
			{
				CargaDoDescontroleAcendeDeTeste = false;
				PararCarga(pl);
			}

			// --- O PAINEL ---------------------------------------------------------------------
			string painel = LinhasDoFrost(pl);
			Checa("o painel do Mutante mostra a maestria da base e, travado, o KI DESCONTROLADO com a liberacao",
				  painel.Contains("MAESTRIA DA BASE") && painel.Contains("KI DESCONTROLADO"), painel.Replace("\n", " | "));
			Checa("...embaixo das linhas raciais da ficha (a linha que o `TickFichas` manda)",
				  ComAsLinhasDoFrost(pl, "racial") == "racial\n" + painel && ComAsLinhasDoFrost(pl, "") == painel, "");
			PainelDoMutanteMudoDeTeste = true;
			try
			{
				Checa("(defeito injetado: o painel do Mutante some, o jogo de antes) a mesma regua REPROVA",
					  !LinhasDoFrost(pl).Contains("MAESTRIA DA BASE"), "");
			}
			finally { PainelDoMutanteMudoDeTeste = false; }

			// --- O CONTROLE VOLTA: a aura apaga ------------------------------------------------
			for (int i = 0; i < 4; i++) { ReverterOuLiberar(pl); SemCena(pl); }   // forma base -> 4a -> 3a -> 2a -> 1a
			viradas.Clear();
			Correr(25);
			string calmo = LinhasDoFrost(pl);
			Checa("quando o controle volta, a zona e avisada pra APAGAR a aura",
				  !pl.Ficha.fd_ki_locked && viradas.Contains((pl.Id, false)), $"{pl.Forma.Atual}, {string.Join(", ", viradas)}");
			Checa("...e o painel volta a mostrar so a maestria",
				  calmo.Contains("MAESTRIA DA BASE") && !calmo.Contains("DESCONTROLADO"), calmo.Replace("\n", " | "));

			// --- OS DOIS DEFEITOS DA TRAVA, NUM SEGUNDO TRAVAMENTO --------------------------------
			NaBase(1e6);
			Carregar(pl, ligado: true);
			viradas.Clear();
			AuraDoDescontroleMudaDeTeste = true;
			CargaDoDescontroleAcendeDeTeste = true;
			try
			{
				Correr(91);
				Checa("(defeito injetado: o descontrole nao vai pro fio, o jogo de antes) a mesma regua REPROVA -- travou, a zona nao ficou sabendo e o estado nao leva a marca",
					  pl.Ficha.fd_ki_locked && !viradas.Contains((pl.Id, true))
					  && (MarcasDoFrost(pl, instavel: false) & Jandirus.Net.Protocol.MarcaDescontrolado) == 0, string.Join(", ", viradas));
				Checa("(defeito injetado: a carga atravessa a trava, o jogo de antes) a mesma regua REPROVA -- ele continua 'reunindo' com o ki travado",
					  pl.Carregando, "");
			}
			finally
			{
				AuraDoDescontroleMudaDeTeste = false;
				CargaDoDescontroleAcendeDeTeste = false;
				PararCarga(pl);
			}
		}
		finally
		{
			EscutaDoDescontrole = null;
			pl.Ficha.MeditateGivesKiRegen = meditaAntes;
		}
	}

	// =====================================================================
	// 10. (2026-10-19) A MAESTRIA DO MUTANTE -- a taxa dele, os quatro marcos e o Ki zerado
	// =====================================================================
	/// <summary>
	/// Tres coisas do DM que o port nao tinha: o Mutante treina a forma base a `FD_MASTERY_PER_MIN` (2% por minuto, ~50 min) e nao nas
	/// ~3 h da escada; os quatro marcos (25/50/75/100) avisam que mais uma casca ficou estavel pra sempre (`fd_mastery_msg`); e a linha
	/// do Frost nao cai por Ki zerado (no DM as formas dele nao mexem no Ki, e so o X as recolhe).
	/// </summary>
	private void AMaestriaDoMutante(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		// --- A TAXA -----------------------------------------------------------------------
		(double Ganho, double Esperado) UmMinutoNaBase(string classe, double porMinuto)
		{
			VestirDeFrost(pl, classe);
			pl.Forma.Entrar(Catalogo.IdDaBaseDoFrost);
			AplicarForma(pl);
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			// O RITMO DA ZONA E DO CORPO ENTRA NA CONTA COMO NO FUNIL (`SubirMaestriaDaZona`): a bancada mede a TAXA, e nao a sala.
			double fator = Math.Max(pl.Ficha.zoneMasteryMult, 0) * FatorDeMaestriaDoCorpo(pl);
			for (int i = 0; i < 600; i++) TickDaForma(pl, 0.1);
			return (pl.Forma.Maestria.De(Catalogo.IdDaBaseDoFrost), porMinuto * fator);
		}
		static bool Bate((double Ganho, double Esperado) m) => m.Esperado > 0 && Math.Abs(m.Ganho - m.Esperado) <= 0.01 * m.Esperado;

		var mutante = UmMinutoNaBase(FormasDeFrost.ClasseMutante, FormasDeFrost.MaestriaDaBasePorMinuto);
		Checa($"um minuto na forma base treina o Mutante a {FormasDeFrost.MaestriaDaBasePorMinuto:0}% (o `FD_MASTERY_PER_MIN` do DM, ~50 min ate dominar) -- e nao nas ~3 h da escada",
			  Bate(mutante), $"{mutante.Ganho:0.000}% contra {mutante.Esperado:0.000}%");
		var normal = UmMinutoNaBase(FormasDeFrost.ClasseNormal, Catalogo.MaestriaPorSegundo * 60);
		Checa("...e o Frost Demon NORMAL continua na taxa da escada, a das outras racas",
			  Bate(normal), $"{normal.Ganho:0.000}% contra {normal.Esperado:0.000}%");
		MutanteNaTaxaDaEscadaDeTeste = true;
		try
		{
			var lento = UmMinutoNaBase(FormasDeFrost.ClasseMutante, FormasDeFrost.MaestriaDaBasePorMinuto);
			Checa("(defeito injetado: o Mutante na taxa da escada, o jogo de antes) a mesma regua REPROVA", !Bate(lento), $"{lento.Ganho:0.000}%");
		}
		finally { MutanteNaTaxaDaEscadaDeTeste = false; }

		// --- OS QUATRO MARCOS -----------------------------------------------------------------
		try
		{
			VestirDeFrost(pl, FormasDeFrost.ClasseMutante);
			pl.FrostMaestriaVista = -1;
			pl.Forma.Maestria.Por(Catalogo.IdDaBaseDoFrost, 60);
			List<string> login = Ouvir(() => TickDoFrost(pl, 0.1));
			Checa("quem loga com 60% de maestria NAO ouve de novo os marcos de 25 e 50 (a primeira leitura so guarda)",
				  !Disse(login, "maestria da base"), Ultimos(login));
			pl.Forma.Maestria.Por(Catalogo.IdDaBaseDoFrost, 74.9);
			TickDoFrost(pl, 0.1);
			pl.Forma.Maestria.Por(Catalogo.IdDaBaseDoFrost, 75.2);
			List<string> marco = Ouvir(() => TickDoFrost(pl, 0.1));
			Checa("cruzar os 75% avisa que a 4a Forma ficou estavel pra sempre",
				  Disse(marco, "maestria da base 75%") && Disse(marco, FormasDeFrost.Nome(4)), Ultimos(marco));
			pl.Forma.Maestria.Por(Catalogo.IdDaBaseDoFrost, 100);
			List<string> cem = Ouvir(() => TickDoFrost(pl, 0.1));
			Checa("...e os 100% dizem que a FORMA BASE e dele -- e que as cascas viram bateria",
				  Disse(cem, "maestria da base 100%") && Disse(cem, "a FORMA BASE") && Disse(cem, "regeneram Ki"), Ultimos(cem));
			MarcosDaBaseMudosDeTeste = true;
			try
			{
				pl.FrostMaestriaVista = 24.9;
				pl.Forma.Maestria.Por(Catalogo.IdDaBaseDoFrost, 25.3);
				List<string> mudo = Ouvir(() => TickDoFrost(pl, 0.1));
				Checa("(defeito injetado: os marcos mudos, o jogo de antes) a mesma regua REPROVA -- cruzar os 25% nao diz nada",
					  !Disse(mudo, "maestria da base 25%"), Ultimos(mudo));
			}
			finally { MarcosDaBaseMudosDeTeste = false; }
		}
		finally { pl.FrostMaestriaVista = -1; }

		// --- O KI ZERADO NAO DERRUBA A ESCADA DELE --------------------------------------------
		string CaiPorKi()
		{
			pl.Forma.Entrar(Catalogo.IdDaBaseDoFrost);
			AplicarForma(pl);
			// SEM CENA PENDURADA: a queda de uma medida anterior desce o corpo com a cena dela, e o `TickDaForma` nao roda em cena -- a
			// segunda medida mediria o congelamento, e nao o Ki.
			SemCena(pl);
			pl.Ficha.Ki = 0;
			TickDaForma(pl, 0.1);
			string onde = pl.Forma.Atual;
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			return onde;
		}
		VestirDeFrost(pl, FormasDeFrost.ClasseMutante);
		string comKiZero = CaiPorKi();
		Checa("com o Ki em ZERO o Mutante continua na forma base (no DM as formas do Frost nao mexem no Ki, e so o X as recolhe)",
			  comKiZero == Catalogo.IdDaBaseDoFrost, comKiZero);
		FrostCaiPorKiDeTeste = true;
		try
		{
			string caiu = CaiPorKi();
			Checa("(defeito injetado: a escada cai por Ki zerado, o jogo de antes) a mesma regua REPROVA -- ele volta pra 1a Forma (0,25x) no meio da briga",
				  caiu != Catalogo.IdDaBaseDoFrost, caiu);
		}
		finally { FrostCaiPorKiDeTeste = false; }
	}

	// =====================================================================
	// 11. (2026-10-19) O VERB DE RE-ESCOLHER OS CORPOS -- o `Icer_Form_Settings`
	// =====================================================================
	/// <summary>
	/// O `Icer_Form_Settings` do DM (`icer.dm:77-83`): reabre a tela de formas com os slots da classe e devolve o corpo a forma
	/// inicial. Pelo verb de producao (`Verbo` -> `formas_frost`), com os corpos em indices do catalogo. A tela e da `--diagmutante`.
	/// </summary>
	private void OVerboDasFormas(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		string classeDoCorpoAntes = pl.Class;
		try
		{
			VestirDeFrost(pl, FormasDeFrost.ClasseMutante);
			// O `Persistir` do verb grava a classe do CORPO (`ServerPlayer.Class`) por cima da ficha -- sem isto a bancada voltaria a ser
			// da classe de antes no meio da conta.
			pl.Class = pl.Ficha.Class;
			pl.Forma.Entrar("frost3");
			AplicarForma(pl);
			string[] sete = [.. FormasDeFrost.Catalogo.Take(FormasDeFrost.Total)];
			List<string> falas = Ouvir(() => Verbo(pl, "formas_frost", "0|1|2|3|4|5|6"));
			Checa("o verb `formas_frost` troca os SETE corpos do Mutante, pelos indices do catalogo (a mesma tabela nas duas pontas)",
				  pl.Visual.FormasDeFrost.SequenceEqual(sete), string.Join(", ", pl.Visual.FormasDeFrost));
			Checa("...devolve o corpo a forma inicial, como o `formchoose` do DM -- e diz que trocou",
				  pl.Forma.Atual == "frost1" && Disse(falas, "corpos das suas formas foram trocados"), $"{pl.Forma.Atual} | {Ultimos(falas)}");
			Checa("...e vai pro disco na hora",
				  pl.Slot >= 0 && _store?.Carregar(pl.Conta)?.Slots[pl.Slot]?.Visual.FormasDeFrost.SequenceEqual(sete) == true, "");

			bool CasouPeloDegrau(List<string> l) =>
				l.Count == FormasDeFrost.Total && Enumerable.Range(1, 4).All(d => l[d - 1] == FormasDeFrost.PadraoDoDegrau(d))
				&& l[4] == FormasDeFrost.Catalogo[10] && l[5] == FormasDeFrost.Catalogo[11] && l[6] == FormasDeFrost.Catalogo[12];
			Verbo(pl, "formas_frost", "10|11|12");
			Checa("tres indices pra um Mutante (um cliente velho, a tela do normal): casados pelo DEGRAU -- a base e as evolucoes",
				  CasouPeloDegrau(pl.Visual.FormasDeFrost), string.Join(", ", pl.Visual.FormasDeFrost));
			FormasDeFrost.CorposPorPosicaoDeTeste = true;
			try
			{
				Verbo(pl, "formas_frost", "10|11|12");
				Checa("(defeito injetado: a lista casada pela POSICAO) a mesma regua REPROVA no verb tambem",
					  !CasouPeloDegrau(pl.Visual.FormasDeFrost), string.Join(", ", pl.Visual.FormasDeFrost));
			}
			finally { FormasDeFrost.CorposPorPosicaoDeTeste = false; }

			// O LIXO PARTE DE UMA LISTA FORA DO PADRAO: com o verb que nao faz nada (ou sem o `Sanear` dele), a lista que ja estivesse no
			// padrao passaria por "lixo vira o padrao" -- a contraprova por mutacao achou esse buraco (os mutantes VB1 e FR22).
			pl.Visual.FormasDeFrost = [.. Enumerable.Range(1, FormasDeFrost.Total)
				.Select(d => FormasDeFrost.Catalogo.First(c => c != FormasDeFrost.PadraoDoDegrau(d)))];
			Verbo(pl, "formas_frost", "lixo|999|-4");
			Checa("...e lixo vira o padrao de cada degrau, sem estourar nada",
				  pl.Visual.FormasDeFrost.SequenceEqual(Enumerable.Range(1, FormasDeFrost.Total).Select(FormasDeFrost.PadraoDoDegrau)),
				  string.Join(", ", pl.Visual.FormasDeFrost));

			List<string> antes = [.. pl.Visual.FormasDeFrost];
			pl.CenaSegundos = 5;
			List<string> emCena = Ouvir(() => Verbo(pl, "formas_frost", "0|1|2|3|4|5|6"));
			pl.CenaSegundos = 0;
			Checa("no meio de uma cinematica o verb pede pra esperar -- a cena esta vestindo o corpo",
				  pl.Visual.FormasDeFrost.SequenceEqual(antes) && Disse(emCena, "espere"), Ultimos(emCena));

			// O BOTAO SO EXISTE PRO FROST DEMON: a linha do verb na tabela de requisitos pede a raca -- pela mesma posse que o menu le.
			bool frostVe = Jandirus.Core.Verbos.RequisitosDosVerbos.DoVerbo("formas_frost").Atende(PosseDe(pl));
			pl.Race = "Human";
			bool humanoVe = Jandirus.Core.Verbos.RequisitosDosVerbos.DoVerbo("formas_frost").Atende(PosseDe(pl));
			List<string> humano = Ouvir(() => Verbo(pl, "formas_frost", "0|1|2"));
			pl.Race = FormasDeFrost.Raca;
			Checa("o botao `Icer Form Settings` so aparece pro Frost Demon (a linha da tabela de requisitos pede a raca)",
				  frostVe && !humanoVe, $"Frost ve {frostVe}, Humano ve {humanoVe}");
			Checa("quem nao e Frost Demon ouve que o verb nao e dele -- e nada muda",
				  pl.Visual.FormasDeFrost.SequenceEqual(antes) && Disse(humano, "Frost Demon escolhe"), Ultimos(humano));
		}
		finally { pl.Class = classeDoCorpoAntes; }
	}

	// =====================================================================
	// 12. (2026-10-19) O NOCAUTE MANTEM A FORMA -- e o que a forma faz com o corpo caido
	// =====================================================================
	/// <summary>
	/// O dono: *"nocaute deveria manter a forma atual"*. No DM o nocaute nao toca no `fd_form`: o Frost Demon caido continua na forma em
	/// que estava. E o motor segue o do DM com o corpo no chao: a maestria da base nao sobe (`!S.KO`), e o relogio da forma que nao se
	/// segura continua correndo (e o do mundo). So a morte desfaz a escada. Pelos dois tiques de producao, o da forma e o do motor.
	/// </summary>
	private void ONocauteDoFrost(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		// O corpo caido por `segundos` numa forma, e levantado de novo. Devolve a forma em que ele ficou.
		string Caido(string forma, double segundos, bool morto = false)
		{
			pl.Forma.Entrar(forma);
			AplicarForma(pl);
			SemCena(pl);
			pl.Ficha.KO = true;
			pl.Ficha.dead = morto;
			try
			{
				for (int i = 0; i < (int)Math.Round(segundos * 10); i++)
				{
					TickDaForma(pl, 0.1);
					TickDoFrost(pl, 0.1);
				}
			}
			finally { pl.Ficha.KO = pl.Ficha.dead = false; }
			SemCena(pl);
			return pl.Forma.Atual;
		}

		// --- A FORMA FICA ---------------------------------------------------------------------
		VestirDeFrost(pl, FormasDeFrost.ClasseMutante);
		string mutante = Caido(Catalogo.IdDaBaseDoFrost, 1);
		Checa("o NOCAUTE mantem a forma do Frost Demon -- o Mutante nocauteado na forma base continua nela", mutante == Catalogo.IdDaBaseDoFrost, mutante);
		NocauteDesfazOFrostDeTeste = true;
		try
		{
			VestirDeFrost(pl, FormasDeFrost.ClasseMutante);
			string desfez = Caido(Catalogo.IdDaBaseDoFrost, 1);
			Checa("(defeito injetado: o nocaute desfaz a escada, o jogo de antes) a mesma regua REPROVA -- o Mutante acorda na 1a Forma",
				  desfez != Catalogo.IdDaBaseDoFrost, desfez);
		}
		finally { NocauteDesfazOFrostDeTeste = false; }

		VestirDeFrost(pl, FormasDeFrost.ClasseNormal);
		pl.Ficha.BP = 3e8;
		pl.Ficha.Statify();
		string normal = Caido("frost6", 1);
		Checa("...e o Frost Demon normal nocauteado continua na 1a Evolucao", normal == "frost6", normal);

		VestirDeFrost(pl, FormasDeFrost.ClasseMutante);
		string cadaver = Caido(Catalogo.IdDaBaseDoFrost, 0.2, morto: true);
		Checa("(controle) a MORTE continua desfazendo a escada: o cadaver do Mutante cai na 1a Forma", cadaver == "frost1", cadaver);

		// --- A FORMA QUE FICA NAO TREINA NO CHAO (o `!S.KO` da maestria do DM) ------------------------
		double UmMinutoCaido()
		{
			VestirDeFrost(pl, FormasDeFrost.ClasseMutante);
			pl.Forma.Maestria.Por(Catalogo.IdDaBaseDoFrost, 0);
			Caido(Catalogo.IdDaBaseDoFrost, 60);
			return pl.Forma.Maestria.De(Catalogo.IdDaBaseDoFrost);
		}
		double noChao = UmMinutoCaido();
		Checa("nocauteado na forma base, a maestria NAO sobe -- um minuto deitado nao treina nada", noChao == 0, $"{noChao:0.000}%");
		NocauteadoTreinaAFormaDeTeste = true;
		try
		{
			double treinou = UmMinutoCaido();
			Checa("(defeito injetado: a forma treina com o corpo caido) a mesma regua REPROVA", treinou > 0, $"{treinou:0.000}%");
		}
		finally { NocauteadoTreinaAFormaDeTeste = false; }

		// --- O RELOGIO DA FORMA QUE NAO SE SEGURA CONTINUA CORRENDO NO CHAO (o do DM e o do mundo) -------------
		bool TravouNoChao()
		{
			VestirDeFrost(pl, FormasDeFrost.ClasseMutante);
			Caido(Catalogo.IdDaBaseDoFrost, 91);
			return pl.Ficha.fd_ki_locked;
		}
		Checa("nocauteado na forma que nao segura, o fusivel continua correndo -- 91 s caido na forma base travam o ki, como no DM",
			  TravouNoChao(), $"relogio {pl.FrostInstavelSegundos:0.0} s");
		FusivelParaNoNocauteDeTeste = true;
		try
		{
			Checa("(defeito injetado: o fusivel para com o corpo caido, o jogo de antes) a mesma regua REPROVA",
				  !TravouNoChao(), $"relogio {pl.FrostInstavelSegundos:0.0} s");
		}
		finally { FusivelParaNoNocauteDeTeste = false; }
		pl.Ficha.fd_ki_locked = false;
		pl.Ficha.fd_release = 1;
	}
}
