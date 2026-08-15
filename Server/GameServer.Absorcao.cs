using Godot;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A ABSORCAO DO BIO-ANDROIDE -- `Absorption.dm`, o galho `obj/Bio_Absorb`.
///
/// ============================ ELA E O MOTOR DA ESCADA, E NAO UM ENFEITE ============================
/// Sem este arquivo o bio-androide para no degrau IMPERFEITO pra sempre: semi-perfeito e perfeito
/// nao se treinam, nao se compram e nao vem do tempo -- eles vem de COMER gente
/// (`bio_note_absorb`, `DNALabs.dm:534`). Portar o resto do sistema sem isto teria entregue tres
/// quartos de uma escada, que e pior que nao entregar nada: o jogador ve o degrau acontecer uma vez
/// e acredita que os outros vem.
///
/// ============================ O BIO **CONSOME**; O MAJIN SELA ============================
/// `Absorption.dm:354-361` e o unico trecho do arquivo com um `if` de raca, e ele existe pra dizer
/// isso: pro bio-androide a vitima **morre na hora**, `buudead = 6` (fura ate o death-regen dela),
/// `killer_stuff` de verdade -- e por isso o `AbsorbBP` dele e PERMANENTE, porque nao ha o que
/// regurgitar. O Majin manda a vitima pra uma dimensao interna e pode cospi-la de volta.
///
/// **NAO HA `Expel` AQUI**, e a ausencia e a regra: o verb existe nas tres arvores de absorcao do
/// DM, e pro bio ele nunca tem o que expelir. Escreve-lo seria um botao que nao faz nada.
///
/// ============================ E O `AbsorbBP` ENTRA COMO **MEDIA**, NAO COMO SOMA ============================
/// `base.dm:110-111` -- com `AbsorbDeterminesBP` ligado, `tempBP = (tempBP + AbsorbBP) / 2`. E um
/// NERF deliberado, e o proprio DM explica na linha (`Absorption.dm:203-205`): *"You absorb to
/// compensate, not as a goddamn training method."* A conta ja estava portada em
/// `Fighter.PowerLevel` e **os dois campos nunca eram escritos por ninguem** -- este arquivo e o
/// consumidor que faltava.
/// ==========================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Um tile e meio, o mesmo alcance das outras tecnicas de contato.</summary>
	private const float RaioDaAbsorcao = 1.5f * ZoneCollision.TileSize;

	/// <summary>
	/// `sleep(20)` do `absorbing` -- dois segundos entre uma absorcao e a proxima. Segura o duplo
	/// clique, que num verb que MATA nao e detalhe.
	/// </summary>
	private const long RecargaDaAbsorcao = 2_000;

	private readonly Dictionary<int, long> _prontoAbsorver = [];

	/// <summary>
	/// ABSORVER. O verb `Absorb` do `obj/Bio_Absorb`, que a skill `bioabsorb` ("Tail Absorb") da.
	///
	/// A LARVA NAO ABSORVE, e a recusa e literal do original (`Absorption.dm:111-113`): *"os orgaos
	/// de absorcao so se formam ao amadurecer"*. E ela e a primeira coisa checada de proposito --
	/// dizer "nao ha ninguem caido" pra uma larva ensinaria que basta achar alguem.
	/// </summary>
	private void AbsorverBio(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;

		if (f.bio_lab_born && f.bio_stage <= Jandirus.Core.Races.BioAndroids.Larva)
		{
			Avisar(pl, "sua carapaca larval ainda nao desenvolveu os orgaos de absorcao. "
					   + "Espere-a se romper.");
			return;
		}
		if (f.KO || f.dead) { Avisar(pl, "nao da, caido."); return; }

		long agora = NowMs();
		if (_prontoAbsorver.TryGetValue(pl.Id, out long pronto) && agora < pronto)
		{ Avisar(pl, "seu corpo ainda esta absorvendo a ultima."); return; }

		// A VITIMA TEM QUE ESTAR NOCAUTEADA E VIVA -- `if(M.KO && !M.dead)`. Morto nao serve: nao ha
		// biomassa viva pra reescrever. E o mesmo crivo da agulha do laboratorio, e pela mesma razao
		// tematica: o preco do bio-androide e sempre DERRUBAR alguem primeiro.
		ServerPlayer? alvo = AlvoPertoG4(pl, RaioDaAbsorcao, o => o.Ficha.KO && !o.Ficha.dead);
		if (alvo == null)
		{ Avisar(pl, "o alvo precisa estar NOCAUTEADO, vivo, e ao seu lado."); return; }

		_prontoAbsorver[pl.Id] = agora + RecargaDaAbsorcao;

		// ============================ `AbsorbDeterminesBP = 1` -- E ELE E PRA SEMPRE ============================
		// O DM o liga dentro do proprio verb (`Absorption.dm:114`), toda vez. Ligado uma vez, ele
		// muda a conta de poder do corpo pro resto da vida: o BP absorvido deixa de SOMAR e passa a
		// tirar MEDIA com o proprio. Nao ha desligar, e nao deveria haver -- e o freio da mecanica.
		// ====================================================================================================
		f.AbsorbDeterminesBP = true;

		double bpAlvo = alvo.Ficha.BP;
		f.AbsorbBP += bpAlvo;

		// `usr.SpreadHeal(100,1,0)` + `Ki = MaxKi` + `Ki += M.Ki`: comer cura e enche o tanque. O
		// teto do Ki e do PORT e nao do DM, e e o mesmo do `SugarVidaG4`: aqui o `kiratio` do
		// `powerlevel()` nao tem teto por cima, entao Ki acima do maximo viraria poder de graca.
		// `Math.Max(MaxKi, Ki)` mantem quem ja estava comprimido pela tecla C onde estava.
		pl.Combate?.Corpo.Restaurar();
		pl.Combate?.SincronizarVida();
		f.Ki = Math.Min(f.MaxKi + alvo.Ficha.Ki, Math.Max(f.MaxKi, f.Ki));
		f.CurrentNutrition = Math.Min(f.CurrentNutrition + 50, Nutricao.Tanque(f.Metabolism));

		Falar(pl, Protocol.Fala.Emote, $"absorve {alvo.Name} POR COMPLETO -- nada resta da vitima!");
		Avisar(alvo, $"cada celula do seu corpo e consumida por {pl.Name}. Voce morreu.");

		// ---- A VITIMA MORRE DE VERDADE ----
		// `ignorarSeguro: true` e o `buudead = 6` do original ("consumido ate a ultima celula: nem
		// death-regen salva"): ser digerido nao e um golpe fatal que uma aura possa negar.
		//
		// PELA PORTA (`Combate.Morrer`) E COM O FUNIL DE DERROTA (`AoPerderALuta`), e nao na mao: e
		// dali que saem o relogio do cadaver, a viagem pro Outro Mundo, o Zenkai da vitima, o LUTO
		// de quem estava vendo (que abre Super Saiyajin) e a sucessao de trono. `killer_stuff` faz
		// exatamente essas coisas no DM, e o port ja tinha o funil pronto.
		bool morreu = alvo.Combate?.Morrer(ignorarSeguro: true) ?? false;
		if (morreu) AoPerderALuta(alvo, pl, morreu: true);

		f.Statify();
		RepercutirPoder(pl);

		Avisar(pl, $"voce absorve {alvo.Name} por inteiro (+{bpAlvo:N0} de poder absorvido -- "
				   + "e ele conta pela MEDIA com o seu, nao pela soma).");

		// ---- E SO ENTAO A CONTAGEM ----
		// Depois da morte e depois do poder, como no DM (`Absorption.dm:143`, ja dentro do galho de
		// absorcao maior). A evolucao e um EVENTO em cima de uma absorcao que ja aconteceu -- se ela
		// viesse antes, um degrau que falhasse deixaria a vitima viva e o contador andado.
		ContarAbsorcaoDoBio(pl, alvo);
		Persistir(pl);

		GD.Print($"[server] {pl.Name} ABSORVEU {alvo.Name}: +{bpAlvo:0} AbsorbBP "
				 + $"(jogadores {f.bio_abs_players:0.#}, androides {f.bio_abs_androids:0})");
	}

	// =====================================================================
	// O SUPER SAIYAJIN 2 DO BIO-ANDROIDE -- `bio_ssj2_death_check` (`DNALabs.dm:649-700`)
	// =====================================================================
	/// <summary>
	/// ELE NAO DESPERTA PELA RAIVA: ELE DESPERTA MORRENDO.
	///
	/// ============================ ESTA E A UNICA PORTA, E O DM FECHA AS OUTRAS DUAS ============================
	/// O bio de laboratorio nasce com o SSJ1 (`canSSJ` + `hasssj`) mas o SSJ2 dele tem guarda propria
	/// em TRES lugares no original, todos dizendo a mesma coisa por caminhos diferentes:
	///   * `SSj2()` (`supersaiyanbuff.dm:417-421`) **zera** um `hasssj2` concedido por engano pela via
	///     saiyajin e recusa: *"seu nucleo bio exige um gatilho mais extremo que a raiva"*;
	///   * o ensino Mestre-Aluno recusa (`MasterStudent.dm:255, 338`);
	///   * o login REVOGA retroativamente quem pegou pela raiva (`DNALabs.dm:714-717`).
	///
	/// No port nao ha `hasssj2` solto pra vazar: a forma se libera por `EstadoDeForma.Liberar`, e o
	/// unico lugar do jogo inteiro que libera `ssj2` pra um bio e esta funcao. As tres guardas do DM
	/// existem porque la a var podia ser escrita de fora; aqui a porta e uma so, e ela e esta.
	///
	/// **E POR ISSO O SSJ3 DELE FICA ATRAS DESTA MESMA PORTA**, sem uma linha a mais: o SSJ3 pede 50%
	/// de maestria no SSJ2 (`FormaDef.PedeMaestriaDe`), e nao se treina uma forma que nao se tem.
	/// =========================================================================================================
	///
	/// PENDURADO NO `NegarMorte`, que e a porta unica da morte -- entao vale contra soco, contra ki,
	/// contra explosao em area e contra a Final Explosion, sem uma linha em cada um. E o que ele
	/// devolve nao e "eu te salvei": e "esta morte virou outra coisa", que e literalmente o que o
	/// `Death()` do DM faz ao abortar no `if` do topo.
	/// </summary>
	private bool DespertarSsj2DoBio(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;

		// A ORDEM E A DO ORIGINAL, e ela e barata primeiro: os tres testes de bit antes dos dois que
		// leem tabela.
		if (!f.bio_lab_born || !f.bio_saiyan_dna || !f.canSSJ) return false;
		if (f.bio_ssj2_by_death) return false;                       // uma vez na vida
		if (f.bio_stage < Jandirus.Core.Races.BioAndroids.Perfeito) return false;  // a FORMA PERFEITA
		if (pl.Forma.Maestria.De("ssj1") < 100) return false;        // SSJ1 100% dominado

		// O PODER: a MESMA regua da via saiyajin (`BP >= ssj2at / ssjmult`). Sai do limiar PESSOAL
		// ja sorteado no nascimento, pela mesma funcao que a tecla C usa -- um numero escrito aqui
		// seria a segunda verdade sobre onde o SSJ2 comeca.
		Jandirus.Core.Forms.FormaDef? ssj2 = Jandirus.Core.Forms.Catalogo.Def("ssj2");
		if (ssj2 == null) return false;
		double porta = pl.Forma.Limiares?.Porta(ssj2) ?? ssj2.PortaBp;
		if (f.BP < porta) return false;

		// MORRER DENTRO DA PROPRIA CABECA NAO CONTA (`z != mind_z`). Sem esta linha o despertar mais
		// caro do bio sairia de graca: entre em transe, perca pro proprio reflexo, saia com o SSJ2.
		// E o mesmo corte que o `AoPerderALuta` ja faz pelo Zenkai e pelo luto, e pelo mesmo motivo.
		if (NaMente(pl)) return false;

		// ---- A MORTE VIROU O DESPERTAR ----
		f.bio_ssj2_by_death = true;

		// O CORPO SE RECOMPOE NO LUGAR (`bio_ssj2_awaken`): cura total, Ki e folego cheios, de pe.
		pl.Combate?.Corpo.Restaurar();
		pl.Combate?.SincronizarVida();
		f.KO = false;
		f.Ki = f.MaxKi;
		f.stamina = f.maxstamina;

		// O DESCONTO DE DESPERTAR -- `ssj2at /= 2`, o MESMO da via saiyajin. Ele existe porque a
		// porta e calibrada pra quem sobe treinando; quem chegou la morrendo ja pagou o preco.
		if (pl.Forma.Limiares is { } lim) lim.ssj2at /= 2;

		// ============================ ELE VOLTA A VIDA **JA NO SSJ2**, E ISSO E O PEDIDO ============================
		// O dono: *"ele vai CANCELAR A MORTE e voltar a vida de forma INSTANTANEA so q agr no SSJ2
		// (super perfeito)"*. Aqui morava `pl.Forma.Liberar("ssj2")` -- que DESTRAVA a forma e nao a
		// veste. O DM nao para nesse meio termo: `bio_ssj2_awaken` termina em `SSj2()`
		// (`DNALabs.dm:697`), que e a proc que aplica a forma de verdade, e o comentario da linha diz
		// por que ela passa (*"o guard interno passa: bio_ssj2_by_death=1"*).
		//
		// A auditoria anterior deu VERDE nisto porque carimbava `Despertou("ssj2")` -- a pergunta que o
		// `Liberar` responde. Quem morresse com os requisitos se recompunha na BASE, com um item novo no
		// menu de formas. `Entrar` ja LIBERA por dentro (`EstadoDeForma.Entrar`), entao a linha some em
		// vez de virar duas.
		//
		// **SEM CENA, E ISSO TAMBEM E DO DM.** A cinematica saiyajin do SSJ2 e pulada pro bio -- ele tem
		// a propria, de 8,0 s, que este mesmo metodo dispara logo abaixo. `semCena: true` e o unico
		// caminho do port pra "esta forma chegou sem a cena dela", e ele existe desde a queda do Oozaru
		// Dourado; a alternativa era o jogador assistir a estreia do Super Saiyajin 2 saiyajin por cima
		// da propria recomposicao.
		//
		// E PELO `AnunciarForma` E NAO PELO `SincronizarFormas`: aquele diz "ele ESTA em" pra quem
		// chega, este diz "ele MUDOU pra" pra zona inteira -- e e o funil por onde passam o clima da
		// transformacao, o `mst_note_form` (quem VIU a forma pode ser ensinado nela) e o prazo da cena.
		// Com o `Sincronizar` no lugar dele, ninguem em volta via a forma acontecer.
		// =========================================================================================================
		string antes = pl.Forma.Atual;
		bool estreia = pl.Forma.Entrar("ssj2");
		double marco = f.ReachMilestone("bio_ssj2");

		f.Statify();
		AplicarForma(pl);
		pl.SigAtributos = "";
		AnunciarForma(pl, antes, "ssj2", estreia, semCena: true);

		// A CENA CURTA DO BIO -- `bio_ssj2_awaken` (`DNALabs.dm:672-698`): 8,0 s de raios, poeira e
		// tremor, sem folha de transformacao nenhuma. Ela sai DEPOIS do anuncio de proposito: o corpo
		// ja tem que estar em SSJ2 quando a cena comeca, porque o que ela mostra e a recomposicao de
		// alguem que JA voltou -- e nao uma transformacao em curso.
		CenaDoBio(pl, Jandirus.Core.Forms.Cinematicas.CenaBio.Ssj2PelaMorte);

		Persistir(pl);

		GD.Print($"[server] {pl.Name}: MORREU e despertou o SSJ2 do bio-androide (BP {f.BP:N0})");

		Avisar(pl, "seu corpo destrocado se RECOMPOE -- o nucleo bio-androide se recusa a morrer. "
				   + "Relampagos DOURADOS rasgam a carapaca: a MORTE acendeu o que faltava no DNA "
				   + "Saiyajin. O SUPER SAIYAJIN 2 e seu.");
		if (marco > 0) Avisar(pl, $"voce rompeu um patamar: daqui pra frente treinar rende {marco:0.#}x.");

		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (o != pl) Avisar(o, $"o corpo destrocado de {pl.Name} esta se RECOMPONDO -- e ha "
								   + "relampagos dourados nas rachaduras da carapaca.");
		return true;
	}
}
