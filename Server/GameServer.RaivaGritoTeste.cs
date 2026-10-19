using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Social;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A FAMILIA 12 DA `--raivateste` (2026-10-18): O AMIGO QUE DERRUBA O AMIGO NAO ENFURECE, E A RAIVA GRITA. O dono: *"faça tb q caso um jogador
/// sinta raiva ele fale no chat algo do tipo como : "miseravel", "seu desgraçado", "vou acabar com vc" etc, alem disso se um amigo seu derruba
/// seu outro amigo, n deveria triggar raiva, somente pessoas q vc n conhece/neutras ou rivais ruins pra baixo q contam pra vc ficar com raiva"*.
/// Pelo funil de producao (`AoPerderALuta`), com o host assistindo o amigo dele (o da montagem) cair:
///   a. derrubado pelo OUTRO AMIGO do host, ou pelo rival bom dele: nada acende;
///   b. derrubado pelo desconhecido, pelo neutro ou pelo rival ruim: a raiva comum acende; contra-exemplo `RaivaSoPelaVitimaDeTeste`;
///   c. a erupcao faz o host dizer uma das falas da raiva comum (`Fala.Diz`); a renovacao nao grita; a morte grita as da furia; contra-exemplo
///      `RaivaMudaDeTeste`;
///   d. (2026-10-18, o mesmo dono no mesmo dia: *"adicionar na raiva uma chance do personagem gritar o nome do amigo derrubado tipo "kuririn!"
///      e coisas do tipo, e na furia quando o amigo morre ele fala algo "KURIRIN NAO!" e fala as frases normais em sequencia"*) O NOME DO AMIGO:
///      a raiva comum o grita numa chance (a moeda solta, em 200 nocautes), a morte grita o nome com o "NAO" na hora e a frase da furia depois do
///      intervalo (em 20 mortes, sempre as duas, na ordem) -- o nome e o que o host CONHECE do amigo (a foto da lista dele), com o amigo sem
///      jogador na tela (o corpo forjado, como o Kuririn do robo) e com ele JOGADOR (o `Peer` do host emprestado); contra-exemplo
///      `RaivaEmDegraus.GritoSemONomeDeTeste`;
///   e. o amigo que cai e morre no MESMO golpe nao perde o nome com o "NAO": o grito e do servidor e nao respeita o teto de 400 ms do cliente;
///      contra-exemplo `GritoNoTetoDaFalaDeTeste`.
/// </summary>
public partial class GameServer
{
	private void OAmigoQueDerrubaEOGrito(ServerPlayer pl, ServerPlayer amigo, Action<string, bool, string> checa, List<ServerPlayer> forjados)
	{
		GD.Print("[raiva] -- 12) o amigo que derruba o amigo nao enfurece; e a raiva GRITA (dono, 2026-10-18) --");
		void EmPaz()
		{
			pl.FuriaExtremaAte = 0;
			pl.RaivaComumAte = 0;
			pl.FuriaCenaAte = 0;
			ProjetarRaiva(pl);
		}
		ServerPlayer Pessoa(int i, string nome, float tiles)
		{
			ServerPlayer p = ForjarPessoaDaRaiva(pl, i, nome, tiles);
			forjados.Add(p);
			return p;
		}
		ServerPlayer outroAmigo = Pessoa(21, "bancada: o outro amigo", 3);
		ServerPlayer rivalBom = Pessoa(22, "bancada: o rival bom", -3);
		ServerPlayer rivalRuim = Pessoa(23, "bancada: o rival ruim", -4);
		ServerPlayer neutro = Pessoa(24, "bancada: o neutro", 4);
		ServerPlayer desconhecido = Pessoa(25, "bancada: o desconhecido", 5);
		DeclararDeTeste(pl, outroAmigo, Relacao.Bom);
		DeclararDeTeste(pl, rivalBom, Relacao.RivalBom);
		DeclararDeTeste(pl, rivalRuim, Relacao.RivalRuim);
		DeclararDeTeste(pl, neutro, Relacao.Neutro);
		checa("(montagem) pra VITIMA os cinco sao inimigos (ela nao declarou nenhum): a condicao do DM passa em todos -- o que se mede aqui e a de quem assiste",
			  amigo.Social.AlgozEhInimigo(outroAmigo.Assinatura) && amigo.Social.AlgozEhInimigo(rivalBom.Assinatura)
			  && amigo.Social.AlgozEhInimigo(desconhecido.Assinatura), "");

		bool Enfurece(ServerPlayer algoz)
		{
			EmPaz();
			AoPerderALuta(amigo, algoz, morreu: false);
			return NivelDaRaivaDe(pl) != NivelDeRaiva.Nenhuma;
		}
		bool porAmigo = Enfurece(outroAmigo), porRivalBom = Enfurece(rivalBom);
		bool porRivalRuim = Enfurece(rivalRuim), porNeutro = Enfurece(neutro), porDesconhecido = Enfurece(desconhecido);
		checa("**O AMIGO QUE DERRUBA O AMIGO NAO ENFURECE** (*\"se um amigo seu derruba seu outro amigo, n deveria triggar raiva\"*): nem o outro "
			  + "amigo do host, nem o rival bom dele", !porAmigo && !porRivalBom, $"amigo {porAmigo}, rival bom {porRivalBom}");
		checa("**...SO O DESCONHECIDO, O NEUTRO E O RIVAL RUIM PRA BAIXO** (*\"somente pessoas q vc n conhece/neutras ou rivais ruins pra baixo q "
			  + "contam pra vc ficar com raiva\"*)", porRivalRuim && porNeutro && porDesconhecido,
			  $"rival ruim {porRivalRuim}, neutro {porNeutro}, desconhecido {porDesconhecido}");
		RaivaSoPelaVitimaDeTeste = true;
		bool deAntes;
		try { deAntes = Enfurece(outroAmigo); }
		finally { RaivaSoPelaVitimaDeTeste = false; }
		checa("   DEFEITO INJETADO (a raiva so pela lista da vitima, o jogo de antes): o amigo que derruba o amigo enfurece -- a regua de cima reprovaria",
			  deAntes, "");

		// ---------------------------------------------------------------- o grito
		var falas = new List<(int Quem, Protocol.Fala Canal, string Texto)>();
		List<(int Quem, Protocol.Fala Canal, string Texto)>? falasDeFora = EscutaDeFalas;
		EscutaDeFalas = falas;
		List<string> DoHost() => [.. falas.Where(x => x.Quem == pl.Id && x.Canal == Protocol.Fala.Diz).Select(x => x.Texto)];
		void Calar() { falas.Clear(); _ultimaFala.Remove(pl.Id); _gritosQueFaltam.Remove(pl.Id); }
		// (2026-10-18) O NOME QUE O HOST CONHECE DO AMIGO -- a foto da lista dele, a mesma que a aba People desenha. E por ela que as reguas
		// perguntam se uma fala e o nome do amigo, e nao pelo campo do corpo: o nome do grito tem de ser o que quem grita conhece.
		string conhecido = pl.Social.Ficha(amigo.Assinatura)?.Nome ?? "";
		bool ONome(NivelDeRaiva grau, string fala) => RaivaEmDegraus.FormasDoNome(grau, conhecido).Contains(fala);
		// O PULSO NAO BATE NO MEIO DESTA BANCADA (ela roda inteira dentro de um quadro): o relogio anda na mao, e a agenda dos gritos e chamada
		// daqui -- a mesma que o pulso chama em jogo.
		void Esperar(double segundos) { AdiantoDoRelogioDeTeste += (long)(segundos * 1000); SoltarOsGritosQueFaltam(); }
		double intervalo = RaivaEmDegraus.SegundosAteAFraseDaFuria;
		try
		{
			checa("(montagem) o host conhece o amigo PELO NOME (a foto da lista dele) -- e e por ela que as reguas do nome perguntam",
				  RaivaEmDegraus.FormasDoNome(NivelDeRaiva.Extrema, conhecido).Length > 0, $"'{conhecido}'");
			EmPaz();
			Calar();
			AoPerderALuta(amigo, desconhecido, morreu: false);
			List<string> comum = DoHost();
			checa("**A RAIVA GRITA** (*\"caso um jogador sinta raiva ele fale no chat algo do tipo como : 'miseravel', 'seu desgraçado', 'vou acabar "
				  + "com vc' etc\"*): o nocaute do amigo faz o host DIZER uma fala so -- uma das da raiva comum, ou o nome do amigo (a moeda)",
				  comum.Count == 1 && (RaivaEmDegraus.GritosDaRaivaComum.Contains(comum[0]) || ONome(NivelDeRaiva.Comum, comum[0])),
				  string.Join(" | ", comum));
			Calar();
			AoPerderALuta(amigo, desconhecido, morreu: false);
			checa("...e so na ERUPCAO: o segundo nocaute, com a raiva acesa, so renova o prazo -- e nao grita de novo", DoHost().Count == 0,
				  string.Join(" | ", DoHost()));
			Calar();
			AoPerderALuta(amigo, desconhecido, morreu: true);
			List<string> furia = DoHost();
			checa("**...e a MORTE grita o NOME DO AMIGO com o NAO** (*\"na furia quando o amigo morre ele fala algo 'KURIRIN NAO!'\"*) -- na hora, e so "
				  + "ele (o amigo sem jogador na tela, como o Kuririn do robo)",
				  furia.Count == 1 && ONome(NivelDeRaiva.Extrema, furia[0]), string.Join(" | ", furia));
			Esperar(intervalo * 0.6);
			List<string> noMeio = DoHost();
			checa($"...a frase da furia ESPERA o intervalo: {intervalo * 0.6:0.0#} s depois, ainda so o nome",
				  noMeio.Count == 1 && ONome(NivelDeRaiva.Extrema, noMeio[0]), string.Join(" | ", noMeio));
			Esperar(intervalo * 0.5);
			List<string> emSeguida = DoHost();
			checa("**...E LOGO EM SEGUIDA, A FRASE DA FURIA, aos berros** (*\"'KURIRIN NAO!' e logo em seguida 'SEU DESGRACADO!'\"*): passado o "
				  + $"intervalo ({intervalo:0.0#} s) o host diz uma das falas da furia -- as duas da boca dele, na ordem",
				  emSeguida.Count == 2 && ONome(NivelDeRaiva.Extrema, emSeguida[0]) && RaivaEmDegraus.GritosDaFuria.Contains(emSeguida[1]),
				  string.Join(" | ", emSeguida));
			RaivaMudaDeTeste = true;
			try
			{
				EmPaz();
				Calar();
				AoPerderALuta(amigo, desconhecido, morreu: false);
			}
			finally { RaivaMudaDeTeste = false; }
			checa("   DEFEITO INJETADO (a raiva muda, o jogo de antes): o nocaute nao faz o host dizer nada -- a regua de cima reprovaria",
				  DoHost().Count == 0 && NivelDaRaivaDe(pl) != NivelDeRaiva.Nenhuma, string.Join(" | ", DoHost()));

			// ---------------------------------------------------------------- (2026-10-18) d. o nome, muitas vezes
			// A MOEDA SOLTA: duzentos nocautes, cada um uma erupcao nova pelo funil de producao. Com a chance de meio a meio, sair da folga de 20
			// pontos em volta dela e coisa de uma rodada em dezenas de milhoes -- o que sai dela e a chance que mudou.
			const int Nocautes = 200, Mortes = 20;
			int nomes = 0, frases = 0, outras = 0;
			var jeitos = new HashSet<string>();
			for (int i = 0; i < Nocautes; i++)
			{
				EmPaz();
				Calar();
				AoPerderALuta(amigo, desconhecido, morreu: false);
				List<string> g = DoHost();
				if (g.Count == 1 && ONome(NivelDeRaiva.Comum, g[0])) { nomes++; jeitos.Add(g[0]); }
				else if (g.Count == 1 && RaivaEmDegraus.GritosDaRaivaComum.Contains(g[0])) frases++;
				else outras++;
			}
			double fracao = nomes / (double)Nocautes;
			checa($"**A RAIVA COMUM GRITA O NOME DO AMIGO NUMA CHANCE** (*\"uma chance do personagem gritar o nome do amigo derrubado tipo 'kuririn!' e "
				  + $"coisas do tipo\"*): em {Nocautes} nocautes o nome sai perto da chance declarada ({RaivaEmDegraus.ChanceDoNomeNaRaivaComum * 100:0}%, "
				  + "folga de 20 pontos) e em mais de um jeito de gritar, a frase no resto -- e nada fora disso",
				  outras == 0 && frases > 0 && jeitos.Count >= 2 && Math.Abs(fracao - RaivaEmDegraus.ChanceDoNomeNaRaivaComum) <= 0.2,
				  $"{nomes} nome(s) ({fracao * 100:0}%) em {jeitos.Count} jeito(s): {string.Join(" / ", jeitos)}; {frases} frase(s); {outras} outra(s)");

			int sequencias = 0;
			var jeitosDaFuria = new HashSet<string>();
			string errada = "";
			for (int i = 0; i < Mortes; i++)
			{
				EmPaz();
				Calar();
				AoPerderALuta(amigo, desconhecido, morreu: true);
				int naHora = DoHost().Count;
				Esperar(intervalo + 0.2);
				List<string> g = DoHost();
				if (naHora == 1 && g.Count == 2 && ONome(NivelDeRaiva.Extrema, g[0]) && RaivaEmDegraus.GritosDaFuria.Contains(g[1]))
				{
					sequencias++;
					jeitosDaFuria.Add(g[0]);
				}
				else if (errada.Length == 0) errada = $"; uma errada: {naHora} na hora, depois {string.Join(" | ", g)}";
			}
			checa($"...e em {Mortes} mortes a furia grita SEMPRE as duas, na ordem -- o nome com o NAO na hora e a frase depois do intervalo --, em "
				  + "mais de um jeito",
				  sequencias == Mortes && jeitosDaFuria.Count >= 2,
				  $"{sequencias} de {Mortes}; {jeitosDaFuria.Count} jeito(s): {string.Join(" / ", jeitosDaFuria)}{errada}");

			// O AMIGO JOGADOR: o mesmo nome -- o que o host conhece --, agora com dono na tela. A moeda da raiva comum e cravada no nome (o palco
			// `RaivaComumSempreONomeDeTeste`): o que se mede aqui e QUAL nome sai, e nao a chance, que ja foi medida acima.
			bool eraJogador;
			List<string> doJogador, daMorteDoJogador;
			RaivaEmDegraus.RaivaComumSempreONomeDeTeste = true;
			amigo.Peer = pl.Peer;   // EMPRESTADO: sem `Peer` ele nao e jogador. Devolvido no `finally`.
			try
			{
				eraJogador = EhJogador(amigo);
				EmPaz();
				Calar();
				AoPerderALuta(amigo, desconhecido, morreu: false);
				doJogador = DoHost();
				Calar();
				AoPerderALuta(amigo, desconhecido, morreu: true);
				daMorteDoJogador = DoHost();
			}
			finally
			{
				amigo.Peer = null;
				RaivaEmDegraus.RaivaComumSempreONomeDeTeste = false;
			}
			checa("...e com o amigo JOGADOR (dono na tela) o nome gritado e o mesmo -- o que o host conhece dele --, no nocaute e na morte",
				  eraJogador && doJogador.Count == 1 && ONome(NivelDeRaiva.Comum, doJogador[0])
				  && daMorteDoJogador.Count == 1 && ONome(NivelDeRaiva.Extrema, daMorteDoJogador[0]),
				  $"jogador={eraJogador}; nocaute: {string.Join(" | ", doJogador)}; morte: {string.Join(" | ", daMorteDoJogador)}");

			// O DEFEITO DO NOME: o jogo da manha. Quarenta nocautes, e uma morte com o intervalo inteiro esperado.
			int nomesSemONome = 0;
			List<string> furiaSemONome;
			RaivaEmDegraus.GritoSemONomeDeTeste = true;
			try
			{
				for (int i = 0; i < 40; i++)
				{
					EmPaz();
					Calar();
					AoPerderALuta(amigo, desconhecido, morreu: false);
					if (DoHost().Exists(g => ONome(NivelDeRaiva.Comum, g))) nomesSemONome++;
				}
				EmPaz();
				Calar();
				AoPerderALuta(amigo, desconhecido, morreu: true);
				Esperar(intervalo + 0.2);
				furiaSemONome = DoHost();
			}
			finally { RaivaEmDegraus.GritoSemONomeDeTeste = false; }
			checa("   DEFEITO INJETADO (o grito sem o nome, o jogo da manha): em 40 nocautes o nome nao sai nenhuma vez, e a morte grita uma fala so, a "
				  + "frase -- as reguas do nome reprovariam",
				  nomesSemONome == 0 && furiaSemONome.Count == 1 && RaivaEmDegraus.GritosDaFuria.Contains(furiaSemONome[0]),
				  $"{nomesSemONome} nome(s); a morte: {string.Join(" | ", furiaSemONome)}");

			// ---------------------------------------------------------------- (2026-10-18) e. cai e morre no mesmo golpe
			List<string> NoMesmoGolpe()
			{
				EmPaz();
				Calar();
				AoPerderALuta(amigo, desconhecido, morreu: false);
				AoPerderALuta(amigo, desconhecido, morreu: true);
				return DoHost();
			}
			List<string> mesmoGolpe = NoMesmoGolpe();
			checa("**O AMIGO CAI E MORRE NO MESMO GOLPE, E O NOME COM O NAO NAO SE PERDE**: o host grita o do nocaute E o da furia -- o teto de 400 ms "
				  + "entre falas e contra o cliente tagarela, e nao cala o grito do servidor",
				  mesmoGolpe.Count == 2 && ONome(NivelDeRaiva.Extrema, mesmoGolpe[1]), string.Join(" | ", mesmoGolpe));
			List<string> noTeto;
			GritoNoTetoDaFalaDeTeste = true;
			try { noTeto = NoMesmoGolpe(); }
			finally { GritoNoTetoDaFalaDeTeste = false; }
			checa("   DEFEITO INJETADO (o grito no teto de fala do cliente, o jogo de antes): o nome com o NAO da morte se perde -- a regua de cima reprovaria",
				  noTeto.Count == 1, string.Join(" | ", noTeto));
		}
		finally
		{
			EscutaDeFalas = falasDeFora;
			RaivaEmDegraus.GritoSemONomeDeTeste = false;
			RaivaEmDegraus.RaivaComumSempreONomeDeTeste = false;
			GritoNoTetoDaFalaDeTeste = false;
			amigo.Peer = null;
			Calar();   // (o nome e a frase que ficaram na agenda nao saem depois da bancada)
			EmPaz();
		}
	}
}
