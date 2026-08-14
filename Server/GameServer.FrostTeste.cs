using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;

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
		}
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
}
